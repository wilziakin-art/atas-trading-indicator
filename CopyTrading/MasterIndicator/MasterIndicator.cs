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
    // Le MasterIndicator est un INDICATEUR (pas une stratégie) :
    // il lit les trades du maître via OnMyTrade et les diffuse,
    // sans jamais passer d'ordre lui-même.

    [DisplayName("CopyTrading — Maître v2")]
    [Category("CopyTrading")]
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

        // File d'attente si déconnecté (max 100, purge auto > 500ms)
        private readonly ConcurrentQueue<(TradingMessage msg, long ts)> _pendingQueue = new();
        private const int MaxPending  = 100;
        private const int MaxAgeMs    = 500;

        // Cache stats clients alimenté par le canal COMM — zéro requête réseau au rendu
        private readonly ConcurrentDictionary<string, ClientStatsPayload> _clientStats = new();

        // Dernier trade diffusé (pour l'overlay)
        private string _lastTradeInfo = "—";

        private volatile bool _running;
        private readonly CancellationTokenSource _cts = new();

        // ────────────────────────────────────────────────────────────────
        // CYCLE DE VIE
        // ────────────────────────────────────────────────────────────────

        protected override void OnInitialize()
        {
            _secretKey = Encoding.UTF8.GetBytes(SecretKey);
            _running   = true;

            // Thread trading priorité haute — connexion vers le relay
            var t = new Thread(ConnectLoop)
            {
                IsBackground = true,
                Priority     = ThreadPriority.AboveNormal,
                Name         = "Master-TradingConnect"
            };
            t.Start();

            // Canal COMM — réception stats clients en arrière-plan
            Task.Run(CommReceiveLoop, _cts.Token);

            // Heartbeat toutes les 30s
            Task.Run(HeartbeatLoop, _cts.Token);

            // Purge périodique de la file en cas de déconnexion longue
            Task.Run(PendingPurgeLoop, _cts.Token);

            LogInfo("[MASTER] Démarré — en attente du relay");
        }

        protected override void OnStopped()
        {
            _running = false;
            _cts.Cancel();
            _tradingTcp?.Close();
            LogInfo("[MASTER] Arrêté");
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            // Le MasterIndicator ne calcule rien — rendu seul dans OnRender
        }

        // ────────────────────────────────────────────────────────────────
        // CAPTURE DES TRADES MAÎTRE via OnMyTrade
        // ────────────────────────────────────────────────────────────────

        protected override void OnMyTrade(MyTrade myTrade)
        {
            var t = myTrade.Trade;

            // Détermine si c'est une entrée ou une fermeture
            if (t.Operation == TradeOperations.Buy || t.Operation == TradeOperations.Sell)
            {
                // Applique le ratio de copie au volume
                int volume = (int)Math.Max(1, Math.Round(t.Volume * RatioCopie / 100.0));

                var dir     = t.Operation == TradeOperations.Buy ? TradeDirection.Buy : TradeDirection.Sell;
                var msgType = MessageType.EntryMarket;

                var msg = TradingMessage.CreateEntry(
                    msgType, dir,
                    t.Symbol ?? Symbol,
                    volume,
                    (double)t.Price,
                    MasterId);

                _lastTradeInfo = $"{dir} {volume} @ {t.Price:F2}  {DateTime.Now:HH:mm:ss}";
                LogInfo($"[MASTER] Trade capturé : {_lastTradeInfo}");

                SendOrQueue(msg);
            }
            else
            {
                // Toute autre opération (fermeture partielle, flat...) → CLOSE_ALL
                var msg = TradingMessage.CreateCloseAll(t.Symbol ?? Symbol, MasterId);
                _lastTradeInfo = $"CLOSE_ALL @ {t.Price:F2}  {DateTime.Now:HH:mm:ss}";
                LogWarn($"[MASTER] CLOSE_ALL émis : {t.Symbol}");
                SendOrQueue(msg);
            }
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

                while (_pendingQueue.Count > MaxPending)
                    _pendingQueue.TryDequeue(out _);

                LogWarn($"[MASTER] Relay déconnecté — trade en file ({_pendingQueue.Count} en attente)");
            }
        }

        private void TrySend(TradingMessage msg)
        {
            try
            {
                var data = msg.Serialize(_secretKey);
                _tradingStream.Write(data, 0, data.Length);
                _tradingStream.Flush();
            }
            catch (Exception ex)
            {
                LogError($"[MASTER] Erreur envoi : {ex.Message}");
                _tradingConnected = false;
                _tradingTcp?.Close();
            }
        }

        // Vide la file après reconnexion — purge les messages périmés
        private void FlushPending()
        {
            int sent = 0, purged = 0;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var fresh = new List<(TradingMessage, long)>();

            while (_pendingQueue.TryDequeue(out var item))
            {
                if (now - item.ts > MaxAgeMs)
                    purged++;
                else
                    fresh.Add(item);
            }

            foreach (var item in fresh)
            {
                TrySend(item.msg);
                sent++;
            }

            if (sent > 0 || purged > 0)
                LogInfo($"[MASTER] File vidée : {sent} envoyés, {purged} expirés purgés");
        }

        // Purge périodique si déconnexion dure (évite accumulation de vieux trades)
        private async Task PendingPurgeLoop()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000, _cts.Token).ConfigureAwait(false);
                if (_pendingQueue.IsEmpty || _tradingConnected) continue;

                var now   = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var fresh = new List<(TradingMessage, long)>();

                while (_pendingQueue.TryDequeue(out var item))
                    if (now - item.ts <= MaxAgeMs)
                        fresh.Add(item);

                foreach (var item in fresh)
                    _pendingQueue.Enqueue(item);
            }
        }

        // ────────────────────────────────────────────────────────────────
        // CONNEXION / RECONNEXION TRADING — TCP pur, TCP_NODELAY
        // ────────────────────────────────────────────────────────────────

        private void ConnectLoop()
        {
            while (_running)
            {
                try
                {
                    _tradingTcp = new TcpClient
                    {
                        NoDelay           = true,
                        SendBufferSize    = 8192,
                        ReceiveBufferSize = 8192
                    };

                    // SO_KEEPALIVE agressif
                    _tradingTcp.Client.SetSocketOption(
                        SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                    _tradingTcp.Connect(RelayHost, TradingPort);
                    _tradingStream = _tradingTcp.GetStream();

                    // Handshake : annonce le rôle MASTER
                    var header = Encoding.ASCII.GetBytes($"MASTER:{MasterId}\n");
                    _tradingStream.Write(header, 0, header.Length);
                    _tradingStream.Flush();

                    _tradingConnected = true;
                    LogInfo($"[MASTER] Connecté au relay trading {RelayHost}:{TradingPort}");

                    // Rejoue les trades en file après reconnexion
                    FlushPending();

                    // Maintient la connexion — lit les éventuels ACK ou pings du relay
                    var buf = new byte[TradingMessage.TotalSize];
                    while (_running && _tradingTcp.Connected)
                    {
                        int n = _tradingStream.Read(buf, 0, buf.Length);
                        if (n == 0) break; // déconnexion propre
                    }
                }
                catch (Exception ex)
                {
                    LogWarn($"[MASTER] Déconnexion relay : {ex.Message}");
                }
                finally
                {
                    _tradingConnected = false;
                    _tradingTcp?.Close();
                }

                if (_running)
                {
                    Thread.Sleep(2000); // reconnexion agressive 2s
                    LogInfo("[MASTER] Tentative reconnexion relay...");
                }
            }
        }

        // ────────────────────────────────────────────────────────────────
        // HEARTBEAT — toutes les 30s pour maintenir la connexion active
        // ────────────────────────────────────────────────────────────────

        private async Task HeartbeatLoop()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(30_000, _cts.Token).ConfigureAwait(false);

                if (!_tradingConnected) continue;

                var hb = TradingMessage.CreateHeartbeat(MasterId);
                TrySend(hb);
            }
        }

        // ────────────────────────────────────────────────────────────────
        // CANAL COMM — réception stats clients pour l'overlay
        // Thread séparé, priorité Normal, isolation totale du canal trading
        // ────────────────────────────────────────────────────────────────

        private async Task CommReceiveLoop()
        {
            while (_running && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    using var ws = new System.Net.WebSockets.ClientWebSocket();
                    ws.Options.SetRequestHeader("X-Client-Id",  MasterId);
                    ws.Options.SetRequestHeader("X-Secret-Key", SecretKey);

                    var uri = new Uri($"ws://{RelayHost}:{CommPort}/");
                    await ws.ConnectAsync(uri, _cts.Token);
                    LogInfo($"[MASTER] Canal COMM connecté : {uri}");

                    var buf = new byte[8192];
                    while (ws.State == System.Net.WebSockets.WebSocketState.Open
                           && !_cts.Token.IsCancellationRequested)
                    {
                        var result = await ws.ReceiveAsync(buf, _cts.Token);
                        if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                            break;

                        var json = Encoding.UTF8.GetString(buf, 0, result.Count);
                        ProcessCommMessage(json);
                    }
                }
                catch (Exception ex) when (!_cts.Token.IsCancellationRequested)
                {
                    LogWarn($"[MASTER] Canal COMM perdu : {ex.Message}");
                    await Task.Delay(3000, _cts.Token).ConfigureAwait(false);
                }
            }
        }

        private void ProcessCommMessage(string json)
        {
            try
            {
                var msg = System.Text.Json.JsonSerializer.Deserialize<CommMessage>(json);
                if (msg == null) return;

                if (msg.Type == CommMessageType.ClientStatus
                    && msg.Payload is System.Text.Json.JsonElement el)
                {
                    var stats = el.Deserialize<ClientStatsPayload>();
                    if (stats?.ClientId != null)
                        _clientStats[stats.ClientId] = stats;
                }
            }
            catch { /* parsing silencieux — ne doit pas bloquer le canal */ }
        }

        // ────────────────────────────────────────────────────────────────
        // RENDU OVERLAY — 100% depuis cache mémoire, zéro requête réseau
        // ────────────────────────────────────────────────────────────────

        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (!PanneauVisible || layout != DrawingLayouts.Final) return;

            var bounds = ChartInfo.PaneBounds;
            int x  = bounds.Left + 10;
            int y  = bounds.Top  + 10;
            int lh = TaillePolice + 5;

            var font     = new RenderFont("Courier New", TaillePolice - 1);
            var fontBold = new RenderFont("Courier New", TaillePolice, System.Drawing.FontStyle.Bold);
            var fontSm   = new RenderFont("Courier New", TaillePolice - 2);

            // ── En-tête ──
            var relayColor = _tradingConnected
                ? System.Drawing.Color.LimeGreen
                : System.Drawing.Color.Red;

            context.DrawString(
                $"[MASTER] {MasterId}  Relay:{(_tradingConnected ? "OK" : "OFF")}  File:{_pendingQueue.Count}",
                fontBold, relayColor, x, y);
            y += lh + 2;

            context.DrawString(
                $"Clients actifs : {_clientStats.Count}",
                font, System.Drawing.Color.White, x, y);
            y += lh;

            context.DrawString(
                $"Dernier trade : {_lastTradeInfo}",
                font, System.Drawing.Color.Cyan, x, y);
            y += lh + 4;

            // Séparateur
            context.DrawLine(
                new RenderPen(System.Drawing.Color.FromArgb(80, 255, 255, 255)),
                x, y, x + 480, y);
            y += 6;

            // ── Tableau clients (depuis cache) ──
            if (_clientStats.IsEmpty)
            {
                context.DrawString("  Aucun client connecté", fontSm,
                    System.Drawing.Color.Gray, x, y);
                return;
            }

            // En-têtes colonnes
            context.DrawString(
                $"  {"Client",-12} {"Statut",-18} {"PnL Jour",10} {"Trades",7} {"CL",4} {"Lat",7}",
                fontSm, System.Drawing.Color.FromArgb(180, 180, 180), x, y);
            y += lh;

            foreach (var kv in _clientStats)
            {
                var s = kv.Value;

                var stateColor = (s.Status ?? "") switch
                {
                    "Actif"             => System.Drawing.Color.LimeGreen,
                    "Breakeven"         => System.Drawing.Color.DeepSkyBlue,
                    "PauseConsecLosses" => System.Drawing.Color.Orange,
                    _                   => System.Drawing.Color.Red,     // Stop*
                };

                string pnlStr = $"{(s.PnlDay >= 0 ? "+" : "")}{s.PnlDay:F2}$";
                var    pnlCol = s.PnlDay >= 0
                    ? System.Drawing.Color.LimeGreen
                    : System.Drawing.Color.OrangeRed;

                // Ligne principale client
                context.DrawString(
                    $"  {s.ClientId,-12} {(s.Status ?? "?"),-18}",
                    fontSm, stateColor, x, y);

                context.DrawString(
                    $"{pnlStr,10} {s.TradesDay,7} {s.ConsecLosses,4} {s.LatencyMs}ms",
                    fontSm, pnlCol,
                    x + 220, y);

                y += lh;

                // Dernier trade sur ligne secondaire si disponible
                if (!string.IsNullOrEmpty(s.LastTradeDir))
                {
                    string lastStr = $"    → {s.LastTradeDir} {s.LastTradeSymbol}" +
                                     $"  {(s.LastTradePnl >= 0 ? "+" : "")}{s.LastTradePnl:F2}$";
                    context.DrawString(lastStr, fontSm,
                        System.Drawing.Color.FromArgb(160, 160, 160), x, y);
                    y += lh;
                }
            }
        }
    }
}
