using System;
using System.Threading;

namespace CopyTrading.ClientIndicator
{
    public enum RiskState
    {
        Actif,
        Breakeven,
        PauseConsecLosses,
        StopTarget,
        StopLoss,
    }

    public class RiskManagerConfig
    {
        public double TargetJourUsd         { get; set; } = 500;
        public double PerteMaxJourUsd       { get; set; } = 500;
        public double SeuilBreakevenUsd     { get; set; } = 500;  // gain pour déclencher breakeven
        public double MontantVerrouilleUsd  { get; set; } = 300;  // gain verrouillé en breakeven
        public int    MaxConsecLosses       { get; set; } = 3;
        public TimeSpan HeureReset          { get; set; } = TimeSpan.FromHours(17); // 17h UTC
    }

    public class RiskManager
    {
        private readonly RiskManagerConfig _cfg;
        private readonly object            _lock = new();

        private volatile int    _stateInt      = (int)RiskState.Actif;
        private double          _pnlJour;
        private int             _consecLosses;
        private int             _tradesJour;
        private int             _winsJour;
        private DateTime        _lastReset     = DateTime.UtcNow.Date;

        public RiskState State => (RiskState)_stateInt;
        public double    PnlJour       => _pnlJour;
        public int       TradesJour    => _tradesJour;
        public int       WinsJour      => _winsJour;
        public int       ConsecLosses  => _consecLosses;

        // Appelé par le ClientTradingEngine pour savoir si on peut prendre un trade
        public bool PeutEntrer => State == RiskState.Actif || State == RiskState.Breakeven;

        public RiskManager(RiskManagerConfig cfg)
        {
            _cfg = cfg;
        }

        // Appelé après chaque trade clôturé
        public void OnTradeClosed(double pnlUsd)
        {
            lock (_lock)
            {
                CheckReset();

                _pnlJour  += pnlUsd;
                _tradesJour++;

                if (pnlUsd > 0)
                {
                    _winsJour++;
                    _consecLosses = 0;
                }
                else
                {
                    _consecLosses++;
                }

                UpdateState();
            }
        }

        // Appelé à chaque tick de PnL pour vérifications continues
        public void OnPnlUpdate(double currentPnlJour)
        {
            lock (_lock)
            {
                CheckReset();
                _pnlJour = currentPnlJour;
                UpdateState();
            }
        }

        // CLOSE_ALL reste toujours actif — appelé même en PAUSE/STOP
        public bool CloseAllAutorise => true;

        private void UpdateState()
        {
            var current = State;

            // Stop Loss journalier
            if (_pnlJour <= -_cfg.PerteMaxJourUsd)
            {
                SetState(RiskState.StopLoss);
                return;
            }

            // Stop Target journalier
            if (_cfg.TargetJourUsd > 0 && _pnlJour >= _cfg.TargetJourUsd)
            {
                SetState(RiskState.StopTarget);
                return;
            }

            // Breakeven : gain atteint le seuil
            if (current == RiskState.Actif && _pnlJour >= _cfg.SeuilBreakevenUsd)
            {
                SetState(RiskState.Breakeven);
                return;
            }

            // En Breakeven : vérifier que le gain ne descend pas sous le montant verrouillé
            if (current == RiskState.Breakeven)
            {
                double gainMin = _cfg.SeuilBreakevenUsd - (_cfg.SeuilBreakevenUsd - _cfg.MontantVerrouilleUsd);
                if (_pnlJour < gainMin)
                {
                    SetState(RiskState.StopLoss);
                    return;
                }
            }

            // Pause pertes consécutives
            if (_consecLosses >= _cfg.MaxConsecLosses && current == RiskState.Actif)
            {
                SetState(RiskState.PauseConsecLosses);
                return;
            }
        }

        private void SetState(RiskState newState)
        {
            var old = State;
            if (old == newState) return;
            Interlocked.Exchange(ref _stateInt, (int)newState);
            Console.WriteLine($"[RISK] État : {old} → {newState} | PnL={_pnlJour:F2} ConsecLoss={_consecLosses}");
        }

        // Reset journalier automatique
        private void CheckReset()
        {
            var now       = DateTime.UtcNow;
            var resetTime = _lastReset.Date + _cfg.HeureReset;

            if (now >= resetTime && _lastReset < resetTime)
            {
                _pnlJour      = 0;
                _consecLosses = 0;
                _tradesJour   = 0;
                _winsJour     = 0;
                _lastReset    = now;
                SetState(RiskState.Actif);
                Console.WriteLine($"[RISK] Reset journalier effectué à {now:HH:mm:ss} UTC");
            }
        }

        // Résumé pour l'overlay ATAS
        public RiskSummary GetSummary() => new RiskSummary
        {
            State        = State,
            PnlJour      = _pnlJour,
            TradesJour   = _tradesJour,
            WinsJour     = _winsJour,
            ConsecLosses = _consecLosses,
            PeutEntrer   = PeutEntrer,
        };
    }

    public class RiskSummary
    {
        public RiskState State        { get; set; }
        public double    PnlJour      { get; set; }
        public int       TradesJour   { get; set; }
        public int       WinsJour     { get; set; }
        public int       ConsecLosses { get; set; }
        public bool      PeutEntrer   { get; set; }
    }
}
