using System;
using System.Collections.Concurrent;
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
    [Category("CopyTrading")]
    public class ClientIndicator : ChartStrategy
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
        public bool MaitreFlatClientFlat { get; set; } = true;

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

        private RiskManager         _riskManager;
        private ClientTradingEngine _tradingEngine;
        private ClientCommEngine    _commEngine;

        // Ordres TP/SL en attente de placement (posés dans OnMyTrade)
        private readonly ConcurrentQueue<Action> _pendingOrders = new();

        // Flatten différé si relay perdu
        private long   _connexionPerduTs;

        // Message dashboard bannière
        private string   _dashboardMessage    = "";
        private DateTime _dashboardMessageExp = DateTime.MinValue;

        // Suivi breakeven position (par direction d'entrée)
        private decimal _breakevenActivatedAt;
        private bool    _breakevenDone;

        // ────────────────────────────────────────────────────────────────
        // CYCLE DE VIE ATAS
        // ────────────────────────────────────────────────────────────────

        protected override void OnInitialize()
        {
            _riskManager = new RiskManager(new RiskManagerConfig
            {
                TargetJourUsd        = TargetJourUsd,
                PerteMaxJourUsd      = PerteMaxJourUsd,
                SeuilBreakevenUsd    = SeuilBreakevenUsd,
                MontantVerrouilleUsd = MontantVerrouilleUsd,
                MaxConsecLosses      = MaxConsecLosses,
                HeureReset           = TimeSpan.FromHours(HeureResetUtc),
            });

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

            _tradingEngine.OnExecuteEntry   = OnSignalEntry;
            _tradingEngine.OnExecuteClose   = OnSignalClose;
            _tradingEngine.OnConnectionLost = OnConnectionLost;

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

            _commEngine.GetStats = () => new ClientStatsPayload { ClientId = ClientId };

            if (CopieActive)
            {
                _tradingEngine.Start();
                _commEngine.Start();
            }
        }

        // Appelé par ATAS à l'arrêt propre de la stratégie
        protected override void OnStopping()
        {
            _tradingEngine?.Stop();
            _commEngine?.Stop();

            // Annule tous les ordres en attente et ferme les positions
            CancelOrders();
            ClosePositions();
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            if (!CanProcess || bar != CurrentBar - 1) return;

            // Mise à jour PnL temps réel dans le risk manager
            if (TradingManager != null)
            {
                double pnlJour = (double)(TradingManager.RealizedPnl + TradingManager.UnrealizedPnl);
                _riskManager.OnPnlUpdate(pnlJour);

                // Flatten automatique si risk manager passe en STOP
                var state = _riskManager.State;
                if (FlattenAuStopJour &&
                    (state == RiskState.StopLoss || state == RiskState.StopTarget))
                {
                    FlattenPositions($"Risk manager : {state}");
                }
            }

            // Flatten différé si relay perdu
            if (_connexionPerduTs > 0)
            {
                long silenceMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _connexionPerduTs;
                if (silenceMs >= DelaiFlaitenRelayS * 1000L && FlattenSiRelayPerdu)
                {
                    _connexionPerduTs = 0;
                    FlattenPositions("Relay perdu — flatten auto");
                }
            }

            // Gestion breakeven position sur barre courante
            if (BreakevenPositionTicks > 0 && !_breakevenDone)
                CheckBreakevenPosition();

            // Pose des ordres TP/SL en attente (thread-safe depuis OnMyTrade)
            while (_pendingOrders.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { LogError($"[CLIENT] Erreur pose ordre : {ex.Message}"); }
            }
        }

        // ────────────────────────────────────────────────────────────────
        // SIGNAUX REÇUS DU RELAY (callbacks depuis le trading engine)
        // Ces callbacks arrivent depuis un thread externe — ATAS exige
        // que les ordres soient posés depuis le thread OnCalculate.
        // On utilise donc _pendingOrders pour différer l'exécution.
        // ────────────────────────────────────────────────────────────────

        private void OnSignalEntry(TradingMessage msg)
        {
            if (!CopieActive) return;

            _pendingOrders.Enqueue(() =>
            {
                if (!CanProcess) return;

                // Filtre spread
                decimal tickSize = InstrumentInfo.TickSize;
                decimal spread   = (Ask - Bid) / tickSize;
                if (spread > MaxSpreadTicks)
                {
                    LogInfo($"[CLIENT] Spread trop large ({spread} ticks) — entrée annulée");
                    return;
                }

                // Filtre position max
                int positionActuelle = Math.Abs((int)(TradingManager?.Position ?? 0));
                if (positionActuelle >= MaxLotsTotal)
                {
                    LogInfo($"[CLIENT] Position max atteinte ({positionActuelle}/{MaxLotsTotal}) — entrée annulée");
                    return;
                }

                // Calcul volume
                int volume = VolumeFixe > 0
                    ? VolumeFixe
                    : (int)Math.Max(1, Math.Round(msg.Volume * VolumeMultiplier));
                volume = Math.Min(volume, MaxLotsParTrade);
                volume = Math.Min(volume, MaxLotsTotal - positionActuelle);
                if (volume <= 0) return;

                var direction = msg.Direction == TradeDirection.Buy
                    ? OrderDirections.Buy
                    : OrderDirections.Sell;

                if (EntreeParLimite)
                {
                    // Prix limite : prix maître ± bande
                    decimal limitPrice = (decimal)msg.Price;
                    limitPrice = ShrinkPrice(limitPrice); // alignement sur tick size
                    PlacerLimite(direction, volume, limitPrice, DureeVieOrdreS);
                }
                else
                {
                    PlacerMarket(direction, volume);
                }

                _breakevenDone = false;
                LogInfo($"[CLIENT] Ordre {direction} {volume} contrat(s) soumis");
            });
        }

        private void OnSignalClose(TradingMessage msg)
        {
            // P0 — exécution immédiate, ne passe pas par _pendingOrders
            // On force le flatten directement, thread-safe via ATAS
            _pendingOrders.Enqueue(() =>
            {
                if (!CanProcess) return;
                FlattenPositions("CLOSE_ALL reçu du maître");
            });
        }

        private void OnConnectionLost(string reason)
        {
            LogWarn($"[CLIENT] Connexion relay perdue : {reason}");
            _connexionPerduTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        // ────────────────────────────────────────────────────────────────
        // TP / SL LOCAL — posé sur le prix RÉEL d'exécution du client
        // ────────────────────────────────────────────────────────────────

        protected override void OnMyTrade(MyTrade myTrade)
        {
            // OnMyTrade peut être appelé depuis n'importe quel thread
            // → on diffère la pose des ordres TP/SL dans _pendingOrders

            var trade    = myTrade.Trade;
            bool isEntry = trade.Operation == TradeOperations.Buy
                        || trade.Operation == TradeOperations.Sell;

            if (!isEntry || (TpTicks <= 0 && SlTicks <= 0)) return;

            bool    isLong     = trade.Operation == TradeOperations.Buy;
            decimal entryPrice = trade.Price;
            int     volume     = trade.Volume;

            _pendingOrders.Enqueue(() =>
            {
                if (!CanProcess) return;

                decimal tickSize = InstrumentInfo.TickSize;
                var exitDir = isLong ? OrderDirections.Sell : OrderDirections.Buy;

                // TP principal
                if (TpTicks > 0)
                {
                    decimal tpPrice = isLong
                        ? entryPrice + TpTicks * tickSize
                        : entryPrice - TpTicks * tickSize;
                    tpPrice = ShrinkPrice(tpPrice);

                    if (TpPartielPct > 0 && TpPartielTicks > 0 && volume > 1)
                    {
                        // TP partiel niveau 1
                        decimal tp1Price = isLong
                            ? entryPrice + TpPartielTicks * tickSize
                            : entryPrice - TpPartielTicks * tickSize;
                        tp1Price = ShrinkPrice(tp1Price);

                        int vol1 = Math.Max(1, volume * TpPartielPct / 100);
                        int vol2 = volume - vol1;

                        PlacerLimite(exitDir, vol1, tp1Price);
                        if (vol2 > 0)
                            PlacerLimite(exitDir, vol2, tpPrice);
                    }
                    else
                    {
                        PlacerLimite(exitDir, volume, tpPrice);
                    }
                }

                // SL
                if (SlTicks > 0)
                {
                    decimal slPrice = isLong
                        ? entryPrice - SlTicks * tickSize
                        : entryPrice + SlTicks * tickSize;
                    slPrice = ShrinkPrice(slPrice);

                    if (SlTrailing)
                        PlacerTrailingStop(exitDir, volume, TrailingDistanceTicks * tickSize);
                    else
                        PlacerStop(exitDir, volume, slPrice);
                }

                // Mémoriser le prix d'entrée pour le breakeven position
                _breakevenActivatedAt = entryPrice;
                _breakevenDone        = false;

                // Mise à jour risk manager (trade ouvert)
                var summary = _riskManager.GetSummary();
                LogInfo($"[CLIENT] TP/SL posés — Risk: {summary.State} PnL: {summary.PnlJour:F2}$");
            });
        }

        // Callback quand un trade est fermé (pour mettre à jour le risk manager)
        protected override void OnPositionChanged(PositionEventArgs e)
        {
            if (e.PositionInfo == null) return;

            // Quand la position revient à zéro → trade fermé
            if (e.PositionInfo.Amount == 0 && TradingManager != null)
            {
                double pnl = (double)TradingManager.RealizedPnl;
                // Le risk manager est mis à jour via OnPnlUpdate dans OnCalculate
                // mais on force ici la mise à jour du compteur de trades
                _riskManager.OnPnlUpdate(pnl);
                _breakevenDone = false;
            }
        }

        // ────────────────────────────────────────────────────────────────
        // BREAKEVEN POSITION AUTO
        // ────────────────────────────────────────────────────────────────

        private void CheckBreakevenPosition()
        {
            if (_breakevenActivatedAt == 0) return;

            decimal tickSize = InstrumentInfo.TickSize;
            decimal current  = GetCandle(CurrentBar - 1).Close;
            bool    isLong   = TradingManager?.Position > 0;

            decimal gainTicks = isLong
                ? (current - _breakevenActivatedAt) / tickSize
                : (_breakevenActivatedAt - current)  / tickSize;

            if (gainTicks >= BreakevenPositionTicks)
            {
                // Déplace le SL au prix d'entrée (breakeven)
                decimal bePrice = ShrinkPrice(_breakevenActivatedAt);
                var exitDir     = isLong ? OrderDirections.Sell : OrderDirections.Buy;
                int volume      = Math.Abs((int)(TradingManager?.Position ?? 0));

                CancelOrders(); // annule l'ancien SL
                PlacerStop(exitDir, volume, bePrice);

                _breakevenDone = true;
                LogInfo($"[CLIENT] Breakeven activé à {bePrice} ({gainTicks:F0} ticks de gain)");
            }
        }

        // ────────────────────────────────────────────────────────────────
        // PLACEMENT D'ORDRES — API ATAS réelle
        // ────────────────────────────────────────────────────────────────

        private void PlacerMarket(OrderDirections direction, int volume)
        {
            var order = new Order
            {
                Portfolio      = Portfolio,
                Symbol         = Symbol,
                Direction      = direction,
                Type           = OrderType.Market,
                QuantityToFill = volume,
                Comment        = "CopyTrading"
            };
            OpenOrder(order);
        }

        private void PlacerLimite(OrderDirections direction, int volume, decimal price, int timeoutSec = 0)
        {
            var order = new Order
            {
                Portfolio      = Portfolio,
                Symbol         = Symbol,
                Direction      = direction,
                Type           = OrderType.Limit,
                Price          = price,
                QuantityToFill = volume,
                Comment        = "CopyTrading-TP"
            };

            if (timeoutSec > 0)
            {
                order.TimeInForce    = OrderTimeInForce.GTD;
                order.ExpirationTime = DateTime.UtcNow.AddSeconds(timeoutSec);
            }
            else
            {
                order.TimeInForce = OrderTimeInForce.GTC;
            }

            OpenOrder(order);
        }

        private void PlacerStop(OrderDirections direction, int volume, decimal triggerPrice)
        {
            var order = new Order
            {
                Portfolio      = Portfolio,
                Symbol         = Symbol,
                Direction      = direction,
                Type           = OrderType.Stop,
                TriggerPrice   = triggerPrice,
                QuantityToFill = volume,
                TimeInForce    = OrderTimeInForce.GTC,
                Comment        = "CopyTrading-SL"
            };
            OpenOrder(order);
        }

        private void PlacerTrailingStop(OrderDirections direction, int volume, decimal trailDistance)
        {
            // ATAS supporte le trailing stop via OrderType.TrailingStop
            var order = new Order
            {
                Portfolio      = Portfolio,
                Symbol         = Symbol,
                Direction      = direction,
                Type           = OrderType.TrailingStop,
                TriggerPrice   = trailDistance, // distance en valeur absolue
                QuantityToFill = volume,
                TimeInForce    = OrderTimeInForce.GTC,
                Comment        = "CopyTrading-Trail"
            };
            OpenOrder(order);
        }

        private void FlattenPositions(string reason)
        {
            LogWarn($"[CLIENT] Flatten : {reason}");
            CancelOrders();
            ClosePositions();

            if (FlattenAuStopJour && !RepriseAutoCopie)
                CopieActive = false;
        }

        // Ferme toutes les positions ouvertes (méthode ATAS native)
        private void ClosePositions()
        {
            if (TradingManager == null || TradingManager.Position == 0) return;

            bool    isLong   = TradingManager.Position > 0;
            int     volume   = Math.Abs((int)TradingManager.Position);
            var     exitDir  = isLong ? OrderDirections.Sell : OrderDirections.Buy;

            var order = new Order
            {
                Portfolio           = Portfolio,
                Symbol              = Symbol,
                Direction           = exitDir,
                Type                = OrderType.Market,
                QuantityToFill      = volume,
                ReduceOnly          = true,  // "Réduction uniquement" — ne crée pas de nouvelle position
                Comment             = "CopyTrading-FlatAll"
            };
            OpenOrder(order);
        }

        // Annule tous les ordres actifs (SL, TP en attente)
        private void CancelOrders()
        {
            if (TradingManager == null) return;

            foreach (var order in TradingManager.Orders)
            {
                if (order.OrderState == OrderStates.Active ||
                    order.OrderState == OrderStates.PartiallyFilled)
                {
                    CancelOrder(order);
                }
            }
        }

        // ────────────────────────────────────────────────────────────────
        // ÉVÉNEMENTS ORDRES — gestion des erreurs
        // ────────────────────────────────────────────────────────────────

        protected override void OnOrderChanged(Order order)
        {
            if (order.OrderState == OrderStates.Rejected)
                LogError($"[CLIENT] Ordre rejeté : {order.Comment} | Raison : {order.Comment}");
        }

        protected override void OnOrderRegisterFailed(Order order)
        {
            LogError($"[CLIENT] Échec enregistrement ordre : {order.Direction} {order.Type} vol={order.QuantityToFill}");
        }

        protected override void OnOrderModifyFailed(Order order)
        {
            LogError($"[CLIENT] Échec modification ordre : {order.Id}");
        }

        // ────────────────────────────────────────────────────────────────
        // RENDU OVERLAY ATAS
        // ────────────────────────────────────────────────────────────────

        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (!PanneauVisible || layout != DrawingLayouts.Final) return;

            var summary  = _riskManager?.GetSummary();
            bool trading = _tradingEngine?.IsConnected ?? false;
            bool comm    = _commEngine?.IsConnected    ?? false;
            long latency = _commEngine?.LatencyMs      ?? 0;

            int x  = 20;
            int y  = 40;
            int lh = TaillePolice + 6;

            var font     = new RenderFont("Courier New", TaillePolice);
            var fontBold = new RenderFont("Courier New", TaillePolice, System.Drawing.FontStyle.Bold);

            // Ligne 1 — connexion
            var connColor = trading ? System.Drawing.Color.LimeGreen : System.Drawing.Color.Red;
            context.DrawString(
                $"COPY  Trading:{(trading ? "OK" : "OFF")}  Comm:{(comm ? "OK" : "OFF")}  Lat:{latency}ms",
                font, connColor, x, y);
            y += lh;

            if (summary != null)
            {
                // Ligne 2 — risk manager
                var stateColor = summary.PeutEntrer
                    ? System.Drawing.Color.LimeGreen
                    : System.Drawing.Color.Orange;
                string pnlStr = $"{(summary.PnlJour >= 0 ? "+" : "")}{summary.PnlJour:F2}$";
                context.DrawString(
                    $"Risk:{summary.State,-18} PnL:{pnlStr}",
                    font, stateColor, x, y);
                y += lh;

                // Ligne 3 — stats trades
                context.DrawString(
                    $"Trades:{summary.TradesJour}  Wins:{summary.WinsJour}  ConsecLoss:{summary.ConsecLosses}",
                    font, System.Drawing.Color.White, x, y);
                y += lh;

                // Ligne 4 — position courante
                if (TradingManager != null)
                {
                    decimal pos       = TradingManager.Position;
                    string  posStr    = pos == 0 ? "FLAT" : pos > 0 ? $"LONG {pos}" : $"SHORT {Math.Abs(pos)}";
                    var     posColor  = pos > 0
                        ? System.Drawing.Color.LimeGreen
                        : pos < 0
                            ? System.Drawing.Color.Red
                            : System.Drawing.Color.Gray;
                    context.DrawString($"Position : {posStr}", fontBold, posColor, x, y);
                    y += lh;
                }
            }

            // Bannière message dashboard
            if (AfficherMessagesRelay
                && !string.IsNullOrEmpty(_dashboardMessage)
                && DateTime.UtcNow < _dashboardMessageExp)
            {
                context.DrawString(
                    $"[MSG] {_dashboardMessage}",
                    fontBold, System.Drawing.Color.Yellow, x, y);
            }
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            // OnCalculate de ChartStrategy — requis
        }
    }
}
