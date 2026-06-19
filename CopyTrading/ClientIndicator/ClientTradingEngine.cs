using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CopyTrading.Protocol;

namespace CopyTrading.ClientIndicator
{
    // Thread dédié TRADING — priorité AboveNormal, port 8765
    // Responsabilité unique : recevoir les signaux binaires et les mettre en file de priorité
    // Aucun lock partagé avec le canal COMM

    public enum SignalPriority { P0_Close = 0, P1_Entry = 1 }

    public class PrioritizedSignal
    {
        public SignalPriority  Priority { get; set; }
        public TradingMessage  Message  { get; set; }
        public long            ReceivedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public class ClientTradingConfig
    {
        public string RelayHost         { get; set; } = "127.0.0.1";
        public int    RelayTradingPort  { get; set; } = 8765;
        public string SecretKey         { get; set; } = "changeme";
        public string ClientId          { get; set; } = "client1";
        public string MasterIdToFollow  { get; set; } = "";

        // Filtres d'entrée
        public int    MaxSignalAgeMs    { get; set; } = 500;   // signal périmé si > 500ms
        public int    MaxSpreadTicks    { get; set; } = 4;
        public int    MaxLotsTotal      { get; set; } = 2;     // contrats max en position simultanée
        public int    MaxLotsPerTrade   { get; set; } = 10;

        // Multiplicateur volume
        public double VolumeMultiplier  { get; set; } = 1.0;

        // Reconnexion
        public int    ReconnectMs       { get; set; } = 2000;  // 2s entre tentatives
        public int    HeartbeatTimeoutS { get; set; } = 15;    // flatten si silence > 15s
    }

    public class ClientTradingEngine
    {
        private readonly ClientTradingConfig _cfg;
        private readonly byte[]              _secretKey;
        private readonly RiskManager         _risk;

        // File de priorité : P0 (CLOSE) toujours avant P1 (ENTRY)
        // On utilise deux queues distinctes pour éviter tout overhead de tri
        private readonly ConcurrentQueue<PrioritizedSignal> _p0Queue = new();
        private readonly ConcurrentQueue<PrioritizedSignal> _p1Queue = new();
        private readonly SemaphoreSlim _queueSignal = new(0, int.MaxValue);

        private TcpClient       _tcp;
        private NetworkStream   _stream;
        private Thread          _receiveThread;
        private Thread          _dispatchThread;
        private volatile bool   _connected;
        private volatile bool   _running;
        private long            _lastHeartbeatMs;
        private readonly CancellationTokenSource _cts = new();

        // Callback vers ATAS pour exécuter réellement les ordres
        // Implémenté dans ClientIndicator.cs qui a accès à l'API ATAS
        public Action<TradingMessage> OnExecuteEntry   { get; set; }
        public Action<TradingMessage> OnExecuteClose   { get; set; }
        public Action<string>         OnConnectionLost { get; set; }  // déclenche flatten
        public Action<long>           OnLatencyMeasured{ get; set; }  // ping/pong ms

        // Getter pour l'overlay — appelé depuis le thread UI, lecture atomique
        public bool  IsConnected    => _connected;
        public int   PendingSignals => _p0Queue.Count + _p1Queue.Count;

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
                    _tcp    = new TcpClient();
                    _tcp.NoDelay           = true;
                    _tcp.SendBufferSize    = 8192;
                    _tcp.ReceiveBufferSize = 8192;

                    await _tcp.ConnectAsync(_cfg.RelayHost, _cfg.RelayTradingPort);

                    _stream = _tcp.GetStream();

                    // Handshake
                    var header = Encoding.ASCII.GetBytes($"CLIENT:{_cfg.ClientId}\n");
                    await _stream.WriteAsync(header, 0, header.Length);

                    _connected        = true;
                    _lastHeartbeatMs  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

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
                    OnConnectionLost?.Invoke("Relay trading perdu");
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
            if (msg.Type == MessageType.Heartbeat)
                return;

            var signal = new PrioritizedSignal { Message = msg };

            if (msg.Type == MessageType.CloseAll || msg.Type == MessageType.ClosePartial)
            {
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
        // DISPATCH — thread AboveNormal, draine les files dans l'ordre P0→P1
        // ────────────────────────────────────────────────────────────────

        private void DispatchLoop()
        {
            while (_running)
            {
                _queueSignal.Wait();

                // P0 toujours en premier
                if (_p0Queue.TryDequeue(out var close))
                {
                    ExecuteClose(close.Message);
                    continue;
                }

                if (_p1Queue.TryDequeue(out var entry))
                {
                    TryExecuteEntry(entry);
                }
            }
        }

        private void ExecuteClose(TradingMessage msg)
        {
            // CLOSE_ALL bypass absolu — aucune validation, aucun risk manager check
            Console.WriteLine($"[DISPATCH] P0 CLOSE_ALL reçu — exécution immédiate");
            try { OnExecuteClose?.Invoke(msg); }
            catch (Exception ex) { Console.WriteLine($"[DISPATCH] Erreur CLOSE : {ex.Message}"); }
        }

        private void TryExecuteEntry(PrioritizedSignal signal)
        {
            var msg = signal.Message;

            // 1. Filtre âge du signal
            long ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - signal.ReceivedAt;
            if (ageMs > _cfg.MaxSignalAgeMs)
            {
                Console.WriteLine($"[DISPATCH] Signal trop vieux ({ageMs}ms > {_cfg.MaxSignalAgeMs}ms) — ignoré");
                return;
            }

            // 2. Risk manager
            if (!_risk.PeutEntrer)
            {
                Console.WriteLine($"[DISPATCH] Risk manager bloqué ({_risk.State}) — entrée refusée");
                return;
            }

            // 3. Volume calculé
            int volume = (int)Math.Max(1, Math.Round(msg.Volume * _cfg.VolumeMultiplier));
            volume     = Math.Min(volume, _cfg.MaxLotsPerTrade);

            // 4. Exécution — les vérifications spread/position sont faites dans ClientIndicator
            //    qui a accès au contexte ATAS (bid/ask temps réel, positions ouvertes)
            var adjustedMsg = msg;
            // Note : on passe le message tel quel, le callback ATAS ajuste le volume

            Console.WriteLine($"[DISPATCH] P1 ENTRY {msg.Direction} {msg.GetSymbol()} vol={volume} prix={msg.Price}");
            try { OnExecuteEntry?.Invoke(adjustedMsg); }
            catch (Exception ex) { Console.WriteLine($"[DISPATCH] Erreur ENTRY : {ex.Message}"); }
        }

        // ────────────────────────────────────────────────────────────────
        // HEARTBEAT WATCHDOG — flatten si silence > HeartbeatTimeoutS
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
                    Console.WriteLine($"[WATCHDOG] Silence relay {silenceMs}ms — flatten déclenché");
                    OnConnectionLost?.Invoke($"Timeout relay ({silenceMs}ms sans activité)");
                    _tcp?.Close(); // force reconnexion
                }
            }
        }
    }
}
