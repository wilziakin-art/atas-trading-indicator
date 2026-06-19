using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OFT.Attributes;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;
using ATAS.Indicators;
using ATAS.DataFeedsCore;
using CopyTrading.Protocol;

namespace CopyTrading.MasterIndicator
{
    [DisplayName("CopyTrading — Maître v2")]
    public class MasterIndicator : Indicator
    {
        // ────────────────────────────────────────────────────────────────
        // PARAMÈTRES
        // ────────────────────────────────────────────────────────────────

        [Display(Name = "Relay Host", GroupName = "Connexion", Order = 1)]
        public string RelayHost { get; set; } = "127.0.0.1";

        [Display(Name = "Port Trading", GroupName = "Connexion", Order = 2)]
        public int TradingPort { get; set; } = 8765;

        [Display(Name = "Port Comm", GroupName = "Connexion", Order = 3)]
        public int CommPort { get; set; } = 8766;

        [Display(Name = "Clé secrète", GroupName = "Connexion", Order = 4)]
        public string SecretKey { get; set; } = "changeme";

        [Display(Name = "ID Maître", GroupName = "Connexion", Order = 5)]
        public string MasterId { get; set; } = "master1";

        [Display(Name = "Ratio copie (%)", GroupName = "Exécution", Order = 10)]
        public int RatioCopie { get; set; } = 100;

        [Display(Name = "Panneau ON/OFF", GroupName = "Affichage", Order = 20)]
        public bool PanneauVisible { get; set; } = true;

        [Display(Name = "Taille police", GroupName = "Affichage", Order = 21)]
        public int TaillePolice { get; set; } = 14;

        // ────────────────────────────────────────────────────────────────
        // ÉTAT INTERNE
        // ────────────────────────────────────────────────────────────────

        private TcpClient     _tradingTcp;
        private NetworkStream _tradingStream;
        private volatile bool _tradingConnected;
        private byte[]        _secretKey;

        // File d'attente si déconnecté (max 100 messages, purge par âge)
        private readonly ConcurrentQueue<(TradingMessage msg, long ts)> _pendingQueue = new();
        private const int MaxPending = 100;

        // Cache stats clients (lu depuis canal COMM, zéro requête réseau au rendu)
        private readonly ConcurrentDictionary<string, ClientStatsPayload> _clientStats = new();

        private Thread    _connectThread;
        private volatile bool _running;
        private readonly CancellationTokenSource _cts = new();
        private long      _lastTradeTs;

        protected override void OnInitialize()
        {
            _secretKey = Encoding.UTF8.GetBytes(SecretKey);
            _running   = true;

            _connectThread = new Thread(ConnectLoop)
            {
                IsBackground = true,
                Priority     = ThreadPriority.AboveNormal,
                Name         = "Master-Trading-Connect"
            };
            _connectThread.Start();

            Task.Run(CommReceiveLoop, _cts.Token);
            Task.Run(HeartbeatLoop,   _cts.Token);
            Task.Run(PendingPurgeLoop, _cts.Token);
        }

        protected override void OnStopped()
        {
            _running = false;
            _cts.Cancel();
            _tradingTcp?.Close();
        }

        // ────────────────────────────────────────────────────────────────
        // CAPTURE DES TRADES MAÎTRE
        // ────────────────────────────────────────────────────────────────

        protected override void OnMyTrade(MyTrade trade)
        {
            var t = trade.Trade;

            // Filtre ratio copie
            int volume = (int)Math.Max(1, Math.Round(t.Volume * RatioCopie / 100.0));

            MessageType msgType;
            TradeDirection dir;

            if (t.Operation == TradeOperations.Buy)
            {
                msgType = MessageType.EntryMarket;
                dir     = TradeDirection.Buy;
            }
            else if (t.Operation == TradeOperations.Sell)
            {
                msgType = MessageType.EntryMarket;
                dir     = TradeDirection.Sell;
            }
            else
            {
                // Fermeture de position → CLOSE_ALL
                SendCloseAll(t.Symbol);
                return;
            }

            var msg = TradingMessage.CreateEntry(
                msgType, dir,
                t.Symbol ?? InstrumentInfo.Instrument.Symbol,
                volume, (double)t.Price, MasterId);

            SendOrQueue(msg);
            _lastTradeTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private void SendCloseAll(string symbol)
        {
            var msg = TradingMessage.CreateCloseAll(
                symbol ?? InstrumentInfo.Instrument.Symbol, MasterId);
            SendOrQueue(msg);
        }

        // ────────────────────────────────────────────────────────────────
        // ENVOI / FILE D'ATTENTE
        // ────────────────────────────────────────────────────────────────

        private void SendOrQueue(TradingMessage msg)
        {
            if (_tradingConnected)
            {
                TrySend(msg);
            }
            else
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _pendingQueue.Enqueue((msg, now));

                // Limite taille file
                while (_pendingQueue.Count > MaxPending)
                    _pendingQueue.TryDequeue(out _);

                Console.WriteLine($"[MASTER] Déconnecté — trade mis en file ({_pendingQueue.Count} en attente)");
            }
        }

        private void TrySend(TradingMessage msg)
        {
            try
            {
                var data = msg.Serialize(_secretKey);
                _tradingStream.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MASTER] Erreur envoi : {ex.Message}");
                _tradingConnected = false;
                _tradingTcp?.Close();
            }
        }

        // Vide la file dès reconnexion — purge les messages trop vieux
        private void FlushPending(int maxAgeMs = 500)
        {
            int sent = 0, purged = 0;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            while (_pendingQueue.TryDequeue(out var item))
            {
                if (now - item.ts > maxAgeMs)
                {
                    purged++;
                    continue;
                }
                TrySend(item.msg);
                sent++;
            }

            if (sent > 0 || purged > 0)
                Console.WriteLine($"[MASTER] File vidée : {sent} envoyés, {purged} expirés purgés");
        }

        // Purge périodique des messages trop vieux dans la file (si déconnexion longue)
        private async Task PendingPurgeLoop()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000, _cts.Token).ConfigureAwait(false);

                if (_pendingQueue.IsEmpty) continue;

                var now   = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var fresh = new List<(TradingMessage, long)>();

                while (_pendingQueue.TryDequeue(out var item))
                {
                    if (now - item.ts <= 500)
                        fresh.Add(item);
                }

                foreach (var item in fresh)
                    _pendingQueue.Enqueue(item);
            }
        }

        // ────────────────────────────────────────────────────────────────
        // CONNEXION / RECONNEXION TRADING
        // ────────────────────────────────────────────────────────────────

        private void ConnectLoop()
        {
            while (_running)
            {
                try
                {
                    _tradingTcp = new TcpClient();
                    _tradingTcp.NoDelay           = true;
                    _tradingTcp.SendBufferSize    = 8192;
                    _tradingTcp.ReceiveBufferSize = 8192;

                    _tradingTcp.Connect(RelayHost, TradingPort);
                    _tradingStream = _tradingTcp.GetStream();

                    // Handshake maître
                    var header = Encoding.ASCII.GetBytes($"MASTER:{MasterId}\n");
                    _tradingStream.Write(header, 0, header.Length);

                    _tradingConnected = true;
                    Console.WriteLine($"[MASTER] Connecté au relay trading {RelayHost}:{TradingPort}");

                    FlushPending();

                    // Maintient la connexion ouverte (lecture bloquante)
                    var buf = new byte[1];
                    while (_running && _tradingTcp.Connected)
                    {
                        int n = _tradingStream.Read(buf, 0, 1);
                        if (n == 0) break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MASTER] Déconnexion relay : {ex.Message}");
                }
                finally
                {
                    _tradingConnected = false;
                    _tradingTcp?.Close();
                }

                if (_running)
                {
                    Thread.Sleep(2000); // reconnexion agressive 2s
                    Console.WriteLine("[MASTER] Tentative reconnexion relay...");
                }
            }
        }

        // ────────────────────────────────────────────────────────────────
        // HEARTBEAT — toutes les 30s
        // ────────────────────────────────────────────────────────────────

        private async Task HeartbeatLoop()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(30000, _cts.Token).ConfigureAwait(false);

                if (!_tradingConnected) continue;

                var hb = TradingMessage.CreateHeartbeat(MasterId);
                TrySend(hb);
            }
        }

        // ────────────────────────────────────────────────────────────────
        // CANAL COMM — réception stats clients (thread séparé)
        // ────────────────────────────────────────────────────────────────

        private async Task CommReceiveLoop()
        {
            // Connexion WebSocket au canal COMM pour recevoir les stats clients
            using var ws = new System.Net.WebSockets.ClientWebSocket();
            ws.Options.SetRequestHeader("X-Client-Id",  MasterId);
            ws.Options.SetRequestHeader("X-Secret-Key", SecretKey);

            try
            {
                var uri = new Uri($"ws://{RelayHost}:{CommPort}/");
                await ws.ConnectAsync(uri, _cts.Token);

                var buf = new byte[8192];
                while (ws.State == System.Net.WebSockets.WebSocketState.Open
                       && !_cts.Token.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(buf, _cts.Token);
                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                        break;

                    var json = Encoding.UTF8.GetString(buf, 0, result.Count);
                    try
                    {
                        var msg = System.Text.Json.JsonSerializer.Deserialize<CommMessage>(json);
                        if (msg?.Type == CommMessageType.ClientStatus
                            && msg.Payload is System.Text.Json.JsonElement el)
                        {
                            var stats = el.Deserialize<ClientStatsPayload>();
                            if (stats?.ClientId != null)
                                _clientStats[stats.ClientId] = stats;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MASTER] Canal comm perdu : {ex.Message}");
            }
        }

        // ────────────────────────────────────────────────────────────────
        // OVERLAY ATAS — rendu depuis cache mémoire (zéro requête réseau)
        // ────────────────────────────────────────────────────────────────

        public override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (!PanneauVisible || layout != DrawingLayouts.Final) return;

            int x = 20, y = 40;
            int lh = TaillePolice + 6;

            var relayColor = _tradingConnected ? System.Drawing.Color.LimeGreen : System.Drawing.Color.Red;
            context.DrawString($"[MASTER] Relay: {(_tradingConnected ? "OK" : "OFF")}  Clients: {_clientStats.Count}  File: {_pendingQueue.Count}",
                new System.Drawing.Font("Arial", TaillePolice), relayColor, x, y);
            y += lh * 2;

            foreach (var kv in _clientStats)
            {
                var s = kv.Value;
                var col = s.Status == "Actif" || s.Status == "Breakeven"
                    ? System.Drawing.Color.LimeGreen
                    : System.Drawing.Color.Orange;

                context.DrawString(
                    $"  {s.ClientId,-12} [{s.Status,-18}] PnL={s.PnlDay,8:F2}$ " +
                    $"T={s.TradesDay} W={s.WinsDay} Lat={s.LatencyMs}ms",
                    new System.Drawing.Font("Courier New", TaillePolice - 1), col, x, y);
                y += lh;
            }
        }

        protected override void OnCalculate(int bar, decimal value) { }
    }
}
