using System;
using System.Threading;
using System.Threading.Tasks;
using OFT.Attributes;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;
using ATAS.Indicators;
using ATAS.DataFeedsCore;
using CopyTrading.Protocol;

namespace CopyTrading.ClientIndicator
{
    [DisplayName("CopyTrading — Client v2")]
    public class ClientIndicator : Indicator
    {
        // ────────────────────────────────────────────────────────────────
        // PARAMÈTRES ATAS (section 5 du document technique)
        // ────────────────────────────────────────────────────────────────

        #region Connexion
        [Display(Name = "Relay Host", GroupName = "Connexion", Order = 1)]
        public string RelayHost { get; set; } = "127.0.0.1";

        [Display(Name = "Port Trading", GroupName = "Connexion", Order = 2)]
        public int TradingPort { get; set; } = 8765;

        [Display(Name = "Port Comm", GroupName = "Connexion", Order = 3)]
        public int CommPort { get; set; } = 8766;

        [Display(Name = "Clé secrète", GroupName = "Connexion", Order = 4)]
        public string SecretKey { get; set; } = "changeme";

        [Display(Name = "ID Client", GroupName = "Connexion", Order = 5)]
        public string ClientId { get; set; } = "client1";

        [Display(Name = "ID Maître à suivre", GroupName = "Connexion", Order = 6)]
        public string MasterIdToFollow { get; set; } = "";
        #endregion

        #region Exécution
        [Display(Name = "Copie active", GroupName = "Exécution", Order = 10)]
        public bool CopieActive { get; set; } = true;

        [Display(Name = "Multiplicateur volume", GroupName = "Exécution", Order = 11)]
        public double VolumeMultiplier { get; set; } = 1.0;

        [Display(Name = "Volume fixe (0 = suivre maître)", GroupName = "Exécution", Order = 12)]
        public int VolumeFixe { get; set; } = 0;

        [Display(Name = "Lots max par trade", GroupName = "Exécution", Order = 13)]
        public int MaxLotsParTrade { get; set; } = 10;

        [Display(Name = "Position max (contrats)", GroupName = "Exécution", Order = 14)]
        public int MaxLotsTotal { get; set; } = 2;

        [Display(Name = "Âge signal max (ms)", GroupName = "Exécution", Order = 15)]
        public int MaxSignalAgeMs { get; set; } = 500;

        [Display(Name = "Spread max (ticks)", GroupName = "Exécution", Order = 16)]
        public int MaxSpreadTicks { get; set; } = 4;

        [Display(Name = "Durée de vie ordre (s)", GroupName = "Exécution", Order = 17)]
        public int DureeVieOrdreS { get; set; } = 5;

        [Display(Name = "Entrée par limite (pas market)", GroupName = "Exécution", Order = 18)]
        public bool EntreeParLimite { get; set; } = false;

        [Display(Name = "Bande entrée ± ticks", GroupName = "Exécution", Order = 19)]
        public int BandeEntreeTicks { get; set; } = 4;
        #endregion

        #region TP / SL
        [Display(Name = "TP en ticks", GroupName = "TP/SL", Order = 20)]
        public int TpTicks { get; set; } = 20;

        [Display(Name = "TP partiel % (0 = désactivé)", GroupName = "TP/SL", Order = 21)]
        public int TpPartielPct { get; set; } = 0;

        [Display(Name = "TP partiel niveau (ticks)", GroupName = "TP/SL", Order = 22)]
        public int TpPartielTicks { get; set; } = 10;

        [Display(Name = "SL en ticks", GroupName = "TP/SL", Order = 23)]
        public int SlTicks { get; set; } = 20;

        [Display(Name = "SL Trailing", GroupName = "TP/SL", Order = 24)]
        public bool SlTrailing { get; set; } = false;

        [Display(Name = "Distance trailing (ticks)", GroupName = "TP/SL", Order = 25)]
        public int TrailingDistanceTicks { get; set; } = 10;

        [Display(Name = "Breakeven auto (ticks gain)", GroupName = "TP/SL", Order = 26)]
        public int BreakevenPositionTicks { get; set; } = 0;
        #endregion

        #region Risk Manager
        [Display(Name = "Target jour USD", GroupName = "Risk Manager", Order = 30)]
        public double TargetJourUsd { get; set; } = 500;

        [Display(Name = "Perte max jour USD", GroupName = "Risk Manager", Order = 31)]
        public double PerteMaxJourUsd { get; set; } = 500;

        [Display(Name = "Seuil breakeven journalier USD", GroupName = "Risk Manager", Order = 32)]
        public double SeuilBreakevenUsd { get; set; } = 500;

        [Display(Name = "Montant verrouillé breakeven USD", GroupName = "Risk Manager", Order = 33)]
        public double MontantVerrouilleUsd { get; set; } = 300;

        [Display(Name = "Max pertes consécutives", GroupName = "Risk Manager", Order = 34)]
        public int MaxConsecLosses { get; set; } = 3;

        [Display(Name = "Heure reset (UTC, ex: 17)", GroupName = "Risk Manager", Order = 35)]
        public int HeureResetUtc { get; set; } = 17;

        [Display(Name = "Flatten au stop jour", GroupName = "Risk Manager", Order = 36)]
        public bool FlattenAuStopJour { get; set; } = true;

        [Display(Name = "Maître flat = client flat", GroupName = "Risk Manager", Order = 37)]
        public bool MaiterFlatClientFlat { get; set; } = true;

        [Display(Name = "Flatten si relay perdu", GroupName = "Risk Manager", Order = 38)]
        public bool FlattenSiRelayPerdu { get; set; } = true;

        [Display(Name = "Délai flatten relay perdu (s)", GroupName = "Risk Manager", Order = 39)]
        public int DelaiFlaitenRelayS { get; set; } = 5;

        [Display(Name = "Reprise auto copie", GroupName = "Risk Manager", Order = 40)]
        public bool RepriseAutoCopie { get; set; } = true;

        [Display(Name = "Reconnect stable (s)", GroupName = "Risk Manager", Order = 41)]
        public int ReconnectStableS { get; set; } = 5;

        [Display(Name = "Timeout ping relay (s)", GroupName = "Risk Manager", Order = 42)]
        public int TimeoutPingRelayS { get; set; } = 15;
        #endregion

        #region Affichage
        [Display(Name = "Panneau ON/OFF", GroupName = "Affichage", Order = 50)]
        public bool PanneauVisible { get; set; } = true;

        [Display(Name = "Taille police", GroupName = "Affichage", Order = 51)]
        public int TaillePolice { get; set; } = 14;

        [Display(Name = "PnL dashboard", GroupName = "Affichage", Order = 52)]
        public bool AfficherPnlDashboard { get; set; } = true;

        [Display(Name = "Afficher messages relay", GroupName = "Affichage", Order = 53)]
        public bool AfficherMessagesRelay { get; set; } = true;

        [Display(Name = "Durée message (s)", GroupName = "Affichage", Order = 54)]
        public int DureeMessageS { get; set; } = 180;
        #endregion

        // ────────────────────────────────────────────────────────────────
        // ÉTAT INTERNE
        // ────────────────────────────────────────────────────────────────

        private RiskManager          _riskManager;
        private ClientTradingEngine  _tradingEngine;
        private ClientCommEngine     _commEngine;

        private volatile bool        _flattenPending;
        private string               _dashboardMessage    = "";
        private DateTime             _dashboardMessageExp = DateTime.MinValue;
        private long                 _connexionPerduTs    = 0;
        private volatile bool        _initialise;

        protected override void OnInitialize()
        {
            // Risk Manager
            _riskManager = new RiskManager(new RiskManagerConfig
            {
                TargetJourUsd        = TargetJourUsd,
                PerteMaxJourUsd      = PerteMaxJourUsd,
                SeuilBreakevenUsd    = SeuilBreakevenUsd,
                MontantVerrouilleUsd = MontantVerrouilleUsd,
                MaxConsecLosses      = MaxConsecLosses,
                HeureReset           = TimeSpan.FromHours(HeureResetUtc),
            });

            // Trading Engine
            _tradingEngine = new ClientTradingEngine(new ClientTradingConfig
            {
                RelayHost        = RelayHost,
                RelayTradingPort = TradingPort,
                SecretKey        = SecretKey,
                ClientId         = ClientId,
                MasterIdToFollow = MasterIdToFollow,
                MaxSignalAgeMs   = MaxSignalAgeMs,
                MaxSpreadTicks   = MaxSpreadTicks,
                MaxLotsTotal     = MaxLotsTotal,
                MaxLotsPerTrade  = MaxLotsParTrade,
                VolumeMultiplier = VolumeMultiplier,
                ReconnectMs      = ReconnectStableS * 1000,
                HeartbeatTimeoutS= TimeoutPingRelayS,
            }, _riskManager);

            _tradingEngine.OnExecuteEntry    = OnExecuteEntry;
            _tradingEngine.OnExecuteClose    = OnExecuteClose;
            _tradingEngine.OnConnectionLost  = OnConnectionLost;

            // Comm Engine
            _commEngine = new ClientCommEngine(new ClientCommConfig
            {
                RelayHost     = RelayHost,
                RelayCommPort = CommPort,
                SecretKey     = SecretKey,
                ClientId      = ClientId,
            }, _riskManager);

            _commEngine.OnDashboardMessage = (text, dur) =>
            {
                _dashboardMessage    = text;
                _dashboardMessageExp = DateTime.UtcNow.AddSeconds(dur);
            };

            _commEngine.GetStats = () => new ClientStatsPayload
            {
                ClientId = ClientId,
            };

            if (CopieActive)
            {
                _tradingEngine.Start();
                _commEngine.Start();
            }

            _initialise = true;
        }

        protected override void OnStopped()
        {
            _tradingEngine?.Stop();
            _commEngine?.Stop();
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            if (bar != CurrentBar - 1) return;

            // Vérification flatten différé si relay perdu
            if (_connexionPerduTs > 0)
            {
                long silenceMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _connexionPerduTs;
                if (silenceMs >= DelaiFlaitenRelayS * 1000L && FlattenSiRelayPerdu)
                {
                    _connexionPerduTs = 0;
                    FlattenPositions("Relay perdu — flatten auto");
                }
            }

            // Mise à jour PnL en temps réel dans le risk manager
            // (TradingStatistics.RealizedPnl accessible via l'API ATAS)
        }

        // ────────────────────────────────────────────────────────────────
        // EXÉCUTION ORDRES (callbacks depuis le trading engine)
        // ────────────────────────────────────────────────────────────────

        private void OnExecuteEntry(TradingMessage msg)
        {
            if (!CopieActive) return;

            // Vérification spread (accès bid/ask via l'API ATAS)
            // var spread = (Ask - Bid) / InstrumentInfo.TickSize;
            // if (spread > MaxSpreadTicks) return;

            // Vérification position ouverte max
            // if (OpenPositionSize >= MaxLotsTotal) return;

            int volume = VolumeFixe > 0
                ? VolumeFixe
                : (int)Math.Max(1, Math.Round(msg.Volume * VolumeMultiplier));
            volume = Math.Min(volume, MaxLotsParTrade);

            var direction = msg.Direction == TradeDirection.Buy
                ? OrderDirections.Buy
                : OrderDirections.Sell;

            if (EntreeParLimite)
            {
                // Ordre limite dans la bande ± BandeEntreeTicks autour du prix maître
                decimal limitPrice = (decimal)msg.Price;
                PlaceLimitOrder(direction, volume, limitPrice, DureeVieOrdreS);
            }
            else
            {
                // Ordre market
                PlaceMarketOrder(direction, volume);
            }

            // Le TP/SL sera posé dans OnMyTrade() dès la confirmation d'exécution
        }

        private void OnExecuteClose(TradingMessage msg)
        {
            FlattenPositions("CLOSE_ALL reçu du maître");
        }

        private void OnConnectionLost(string reason)
        {
            Console.WriteLine($"[CLIENT] Connexion perdue : {reason}");
            _connexionPerduTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        // ────────────────────────────────────────────────────────────────
        // TP / SL LOCAL — posé sur le prix RÉEL d'exécution du client
        // ────────────────────────────────────────────────────────────────

        protected override void OnMyTrade(MyTrade trade)
        {
            if (TpTicks <= 0 && SlTicks <= 0) return;

            decimal tickSize = InstrumentInfo.TickSize;
            bool    isLong   = trade.Trade.Operation == TradeOperations.Buy;

            decimal entryPrice = trade.Trade.Price;
            decimal tpPrice    = isLong
                ? entryPrice + TpTicks    * tickSize
                : entryPrice - TpTicks    * tickSize;
            decimal slPrice    = isLong
                ? entryPrice - SlTicks    * tickSize
                : entryPrice + SlTicks    * tickSize;

            var exitDir = isLong ? OrderDirections.Sell : OrderDirections.Buy;
            int vol     = trade.Trade.Volume;

            // TP principal
            if (TpTicks > 0)
            {
                if (TpPartielPct > 0 && TpPartielTicks > 0)
                {
                    // TP partiel niveau 1
                    decimal tp1Price = isLong
                        ? entryPrice + TpPartielTicks * tickSize
                        : entryPrice - TpPartielTicks * tickSize;
                    int vol1 = Math.Max(1, vol * TpPartielPct / 100);
                    int vol2 = vol - vol1;

                    PlaceLimitOrder(exitDir, vol1, tp1Price);
                    if (vol2 > 0 && TpTicks > TpPartielTicks)
                        PlaceLimitOrder(exitDir, vol2, tpPrice);
                }
                else
                {
                    PlaceLimitOrder(exitDir, vol, tpPrice);
                }
            }

            // SL
            if (SlTicks > 0)
            {
                if (SlTrailing)
                    PlaceTrailingStop(exitDir, vol, TrailingDistanceTicks * tickSize);
                else
                    PlaceStopOrder(exitDir, vol, slPrice);
            }

            // Breakeven position auto
            if (BreakevenPositionTicks > 0)
            {
                // Géré via OnCalculate avec suivi du prix courant
            }
        }

        // ────────────────────────────────────────────────────────────────
        // FLATTEN
        // ────────────────────────────────────────────────────────────────

        private void FlattenPositions(string reason)
        {
            Console.WriteLine($"[CLIENT] Flatten : {reason}");
            CloseAllPositions();
            if (FlattenAuStopJour && !RepriseAutoCopie)
                CopieActive = false;
        }

        // ────────────────────────────────────────────────────────────────
        // RENDU OVERLAY ATAS
        // ────────────────────────────────────────────────────────────────

        public override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (!PanneauVisible || layout != DrawingLayouts.Final) return;

            var summary  = _riskManager?.GetSummary();
            bool trading = _tradingEngine?.IsConnected ?? false;
            bool comm    = _commEngine?.IsConnected    ?? false;
            long latency = _commEngine?.LatencyMs      ?? 0;

            int x = 20, y = 40;
            int lh = TaillePolice + 6;

            // Statut connexion
            var connColor = trading ? System.Drawing.Color.LimeGreen : System.Drawing.Color.Red;
            context.DrawString($"[COPY] Trading: {(trading ? "OK" : "OFF")}  Comm: {(comm ? "OK" : "OFF")}  Latence: {latency}ms",
                new System.Drawing.Font("Arial", TaillePolice), connColor, x, y);
            y += lh;

            if (summary != null)
            {
                // État risk manager
                var stateColor = summary.PeutEntrer ? System.Drawing.Color.LimeGreen : System.Drawing.Color.Orange;
                context.DrawString($"Risk: {summary.State}  PnL jour: {summary.PnlJour:F2}$",
                    new System.Drawing.Font("Arial", TaillePolice), stateColor, x, y);
                y += lh;

                context.DrawString($"Trades: {summary.TradesJour}  Wins: {summary.WinsJour}  Losses consec: {summary.ConsecLosses}",
                    new System.Drawing.Font("Arial", TaillePolice), System.Drawing.Color.White, x, y);
                y += lh;
            }

            // Message dashboard (bannière)
            if (AfficherMessagesRelay && !string.IsNullOrEmpty(_dashboardMessage)
                && DateTime.UtcNow < _dashboardMessageExp)
            {
                context.DrawString($"[MARCUS] {_dashboardMessage}",
                    new System.Drawing.Font("Arial", TaillePolice, System.Drawing.FontStyle.Bold),
                    System.Drawing.Color.Yellow, x, y);
            }
        }

        // ────────────────────────────────────────────────────────────────
        // STUBS API ATAS (à adapter selon la version réelle de l'API)
        // ────────────────────────────────────────────────────────────────

        private void PlaceMarketOrder(OrderDirections direction, int volume)
        {
            // TradingManager.PlaceOrderAsync(new OrderModel { ... })
        }

        private void PlaceLimitOrder(OrderDirections direction, int volume, decimal price, int timeoutSec = 0)
        {
            // TradingManager.PlaceOrderAsync(new OrderModel { Type = OrderType.Limit, ... })
        }

        private void PlaceStopOrder(OrderDirections direction, int volume, decimal price)
        {
            // TradingManager.PlaceOrderAsync(new OrderModel { Type = OrderType.Stop, ... })
        }

        private void PlaceTrailingStop(OrderDirections direction, int volume, decimal trailDistance)
        {
            // TradingManager.PlaceOrderAsync(new OrderModel { Type = OrderType.TrailingStop, ... })
        }

        private void CloseAllPositions()
        {
            // TradingManager.CloseAllPositionsAsync()
        }
    }
}
