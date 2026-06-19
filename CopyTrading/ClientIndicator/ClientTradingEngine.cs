using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CopyTrading.Protocol;

namespace CopyTrading.ClientIndicator
{
    public enum SignalPriority { P0_Close = 0, P1_Entry = 1 }

    public class PrioritizedSignal
    {
        public SignalPriority Priority   { get; set; }
        public TradingMessage Message    { get; set; }
        public long           ReceivedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        public string         SignalId   { get; set; } // anti-doublon
    }

    public class ClientTradingConfig
    {
        public string RelayHost         { get; set; } = "127.0.0.1";
        public int    RelayTradingPort  { get; set; } = 8765;
        public string SecretKey         { get; set; } = "changeme";
        public string ClientId          { get; set; } = "client1";
        public string MasterIdToFollow  { get; set; } = "";

        public int    MaxSignalAgeMs    { get; set; } = 500;
        public int    MaxSpreadTicks    { get; set; } = 4;
        public int    MaxLotsTotal      { get; set; } = 2;
        public int    MaxLotsPerTrade   { get; set; } = 10;
        public double VolumeMultiplier  { get; set; } = 1.0;

        public int    ReconnectMs           { get; set; } = 2000;
        public int    HeartbeatTimeoutS     { get; set; } = 15;
        public int    CloseDebounceMs       { get; set; } = 450;  // debounce CLOSE_ALL (Veloxas)
        public int    DoneExecIdsMaxSize    { get; set; } = 500;  // taille max du cache anti-doublon
    }

    public class ClientTradingEngine
    {
        private readonly ClientTradingConfig _cfg;
        private readonly byte[]              _secretKey;
        private readonly RiskManager         _risk;

        // File de priorité P0 (CLOSE) / P1 (ENTRY)
        private readonly ConcurrentQueue<PrioritizedSignal> _p0Queue    = new();
        private readonly ConcurrentQueue<PrioritizedSignal> _p1Queue    = new();
        private readonly SemaphoreSlim                      _queueSignal = new(0, int.MaxValue);

        // ── AMÉLIORATION 1 : Anti-doublon (inspiré Veloxas _doneExecIds) ──
        // HashSet des SignalId déjà exécutés + Queue FIFO pour purge LIFO
        private readonly HashSet<string>  _doneExecIds   = new(StringComparer.Ordinal);
        private readonly Queue<string>    _doneExecOrder = new();
        private readonly object           _doneExecGate  = new();

        // ── AMÉLIORATION 2 : Flatten atomique (inspiré Veloxas _flattenInProgress) ──
        // int32 géré via Interlocked.CompareExchange — évite les doubles flatten simultanés
        private int  _flattenInProgress = 0; // 0=libre, 1=en cours

        // ── AMÉLIORATION 3 : Debounce CLOSE_ALL ──
        private long _lastCloseReceivedTicks = 0;

        private TcpClient     _tcp;
        private NetworkStream _stream;
        private Thread        _dispatchThread;
        private volatile bool _connected;
        private volatile bool _running;
        private long          _lastHeartbeatMs;
        private readonly CancellationTokenSource _cts = new();

        public Action<TradingMessage> OnExecuteEntry    { get; set; }
        public Action<TradingMessage> OnExecuteClose    { get; set; }
        public Action<string>         OnConnectionLost  { get; set; }
        public Action<long>           OnLatencyMeasured { get; set; }

        public bool IsConnected    => _connected;
        public int  PendingSignals => _p0Queue.Count + _p1Queue.Count;

        public ClientTradingEngine(ClientTradingConfig cfg, RiskManager risk)
        {
            _cfg       = cfg;
            _secretKey = Encoding.UTF8.GetBytes(cfg.SecretKey);
            _risk      = risk;
        }

        public void Start()
        {
            _running = true;

            _dispatchThread = new Thread(DispatchLoop)
            {
                IsBackground = true,
                Priority     = ThreadPriority.AboveNormal,
                Name         = "Trading-Dispatch"
            };
            _dispatchThread.Start();

            Task.Run(ConnectLoop, _cts.Token);
            Task.Run(HeartbeatWatchdog, _cts.Token);
        }

        public void Stop()
        {
            _running = false;
            _cts.Cancel();
            _queueSignal.Release();
            _tcp?.Close();
        }

        // ────────────────────────────────────────────────────────────────
        // CONNEXION & RECONNEXION
        // ────────────────────────────────────────────────────────────────

        private async Task ConnectLoop()
        {
            while (_running && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    _tcp = new TcpClient
                    {
                        NoDelay           = true,
                        SendBufferSize    = 8192,
                        ReceiveBufferSize = 8192
                    };

                    await _tcp.ConnectAsync(_cfg.RelayHost, _cfg.RelayTradingPort);
                    _stream = _tcp.GetStream();

                    var header = Encoding.ASCII.GetBytes($"CLIENT:{_cfg.ClientId}\n");
                    await _stream.WriteAsync(header, 0, header.Length);

                    _connected       = true;
                    _lastHeartbeatMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    Console.WriteLine($"[TRADING-ENGINE] Connecté à {_cfg.RelayHost}:{_cfg.RelayTradingPort}");
                    await ReceiveLoop();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TRADING-ENGINE] Déconnexion : {ex.Message}");
                }
                finally
                {
                    _connected = false;
                    _tcp?.Close();
                    TriggerConnectionLost("Relay trading perdu");
                }

                if (_running)
                {
                    await Task.Delay(_cfg.ReconnectMs, _cts.Token).ConfigureAwait(false);
                    Console.WriteLine("[TRADING-ENGINE] Tentative de reconnexion...");
                }
            }
        }

        private async Task ReceiveLoop()
        {
            var buf = new byte[TradingMessage.TotalSize];

            while (_running && _tcp.Connected)
            {
                int total = 0;
                while (total < TradingMessage.TotalSize)
                {
                    int n = await _stream.ReadAsync(buf, total, TradingMessage.TotalSize - total);
                    if (n == 0) return;
                    total += n;
                }

                _lastHeartbeatMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (!TradingMessage.TryDeserialize(buf, _secretKey, out var msg))
                {
                    Console.WriteLine("[TRADING-ENGINE] HMAC invalide — message rejeté");
                    continue;
                }

                if (!string.IsNullOrEmpty(_cfg.MasterIdToFollow) &&
                    msg.GetMasterId() != _cfg.MasterIdToFollow)
                    continue;

                EnqueueSignal(msg);
            }
        }

        private void EnqueueSignal(TradingMessage msg)
        {
            if (msg.Type == MessageType.Heartbeat) return;

            // Génère un ID unique par signal : masterId + timestamp + type
            string signalId = $"{msg.GetMasterId()}_{msg.TimestampMs}_{msg.Type}";

            var signal = new PrioritizedSignal
            {
                Message  = msg,
                SignalId = signalId,
            };

            if (msg.Type == MessageType.CloseAll || msg.Type == MessageType.ClosePartial)
            {
                // ── AMÉLIORATION 3 : Debounce CLOSE_ALL ──
                // Si un CLOSE_ALL a déjà été reçu il y a moins de CloseDebounceMs, on ignore
                long now      = DateTime.UtcNow.Ticks;
                long lastTick = Interlocked.Read(ref _lastCloseReceivedTicks);

                if (lastTick > 0)
                {
                    long elapsedMs = (now - lastTick) / TimeSpan.TicksPerMillisecond;
                    if (elapsedMs < _cfg.CloseDebounceMs)
                    {
                        Console.WriteLine($"[DISPATCH] CLOSE_ALL debounce ({elapsedMs}ms < {_cfg.CloseDebounceMs}ms) — ignoré");
                        return;
                    }
                }

                Interlocked.Exchange(ref _lastCloseReceivedTicks, now);
                signal.Priority = SignalPriority.P0_Close;
                _p0Queue.Enqueue(signal);
            }
            else
            {
                signal.Priority = SignalPriority.P1_Entry;
                _p1Queue.Enqueue(signal);
            }

            _queueSignal.Release();
        }

        // ────────────────────────────────────────────────────────────────
        // DISPATCH — thread AboveNormal, draine P0 avant P1
        // ────────────────────────────────────────────────────────────────

        private void DispatchLoop()
        {
            while (_running)
            {
                _queueSignal.Wait();

                if (_p0Queue.TryDequeue(out var close))
                {
                    ExecuteClose(close);
                    continue;
                }

                if (_p1Queue.TryDequeue(out var entry))
                    TryExecuteEntry(entry);
            }
        }

        private void ExecuteClose(PrioritizedSignal signal)
        {
            // ── AMÉLIORATION 2 : Flatten atomique ──
            // CompareExchange garantit qu'un seul thread entre dans le flatten
            if (Interlocked.CompareExchange(ref _flattenInProgress, 1, 0) != 0)
            {
                Console.WriteLine("[DISPATCH] Flatten déjà en cours — CLOSE_ALL ignoré (doublon)");
                return;
            }

            try
            {
                Console.WriteLine("[DISPATCH] P0 CLOSE_ALL — exécution immédiate");
                OnExecuteClose?.Invoke(signal.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DISPATCH] Erreur CLOSE : {ex.Message}");
            }
            finally
            {
                // Libère le flag après exécution (avec délai court pour éviter rebond)
                Task.Delay(200).ContinueWith(_ =>
                    Interlocked.Exchange(ref _flattenInProgress, 0));
            }
        }

        private void TryExecuteEntry(PrioritizedSignal signal)
        {
            var msg = signal.Message;

            // ── AMÉLIORATION 1 : Anti-doublon ──
            if (IsAlreadyExecuted(signal.SignalId))
            {
                Console.WriteLine($"[DISPATCH] Signal {signal.SignalId} déjà exécuté — ignoré");
                return;
            }

            // Filtre âge
            long ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - signal.ReceivedAt;
            if (ageMs > _cfg.MaxSignalAgeMs)
            {
                Console.WriteLine($"[DISPATCH] Signal trop vieux ({ageMs}ms) — ignoré");
                return;
            }

            // Risk manager
            if (!_risk.PeutEntrer)
            {
                Console.WriteLine($"[DISPATCH] Risk manager bloqué ({_risk.State}) — refusé");
                return;
            }

            // Marque comme exécuté AVANT l'envoi (évite doublon si callback lent)
            MarkExecuted(signal.SignalId);

            int volume = (int)Math.Max(1, Math.Round(msg.Volume * _cfg.VolumeMultiplier));
            volume     = Math.Min(volume, _cfg.MaxLotsPerTrade);

            Console.WriteLine($"[DISPATCH] P1 ENTRY {msg.Direction} {msg.GetSymbol()} vol={volume} @ {msg.Price}");
            try { OnExecuteEntry?.Invoke(msg); }
            catch (Exception ex) { Console.WriteLine($"[DISPATCH] Erreur ENTRY : {ex.Message}"); }
        }

        // ────────────────────────────────────────────────────────────────
        // ANTI-DOUBLON — HashSet borné + Queue FIFO pour purge
        // ────────────────────────────────────────────────────────────────

        private bool IsAlreadyExecuted(string signalId)
        {
            lock (_doneExecGate)
                return _doneExecIds.Contains(signalId);
        }

        private void MarkExecuted(string signalId)
        {
            lock (_doneExecGate)
            {
                if (_doneExecIds.Add(signalId))
                {
                    _doneExecOrder.Enqueue(signalId);

                    // Purge FIFO si dépasse la taille max
                    while (_doneExecOrder.Count > _cfg.DoneExecIdsMaxSize)
                    {
                        var old = _doneExecOrder.Dequeue();
                        _doneExecIds.Remove(old);
                    }
                }
            }
        }

        // ────────────────────────────────────────────────────────────────
        // CONNEXION PERDUE — déclenché une seule fois via Interlocked
        // ────────────────────────────────────────────────────────────────

        private int _connectionLostFired = 0;

        private void TriggerConnectionLost(string reason)
        {
            // Garantit que OnConnectionLost n'est déclenché qu'une fois par déconnexion
            if (Interlocked.CompareExchange(ref _connectionLostFired, 1, 0) == 0)
            {
                OnConnectionLost?.Invoke(reason);
                // Reset après 3s pour permettre le prochain cycle
                Task.Delay(3000).ContinueWith(_ =>
                    Interlocked.Exchange(ref _connectionLostFired, 0));
            }
        }

        // ────────────────────────────────────────────────────────────────
        // HEARTBEAT WATCHDOG
        // ────────────────────────────────────────────────────────────────

        private async Task HeartbeatWatchdog()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(5000, _cts.Token).ConfigureAwait(false);
                if (!_connected) continue;

                var silenceMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _lastHeartbeatMs;
                if (silenceMs > _cfg.HeartbeatTimeoutS * 1000L)
                {
                    Console.WriteLine($"[WATCHDOG] Silence {silenceMs}ms — flatten + reconnexion");
                    TriggerConnectionLost($"Timeout relay ({silenceMs}ms sans activité)");
                    _tcp?.Close();
                }
            }
        }
    }
}
