using System;
using ATAS.Indicators;
using ATASMultiSignal.Models;

namespace ATASMultiSignal.Phase3_Execution.ExitStrategy
{
    public enum ExitReason { None, DomSweep, MacdReversal, CciExtreme, TwoFactors }

    public sealed class ExitSignalEvaluator
    {
        
        private readonly ExitMacdCalculator _macd = new();
        private readonly ExitCciCalculator _cci = new();

        public ExitReason Evaluate(int bar, SignalDirection position, Func<int, IndicatorCandle> getCandle)
        {
            if (position == SignalDirection.None) return ExitReason.None;

            bool domSweep = DomSweepDetector.DetectSweep(bar, getCandle, position);
            bool macdExit = _macd.ShouldExit(bar, position, getCandle);
            bool cciExit  = _cci.ShouldExit(bar, position, getCandle);

            int count = (domSweep ? 1 : 0) + (macdExit ? 1 : 0) + (cciExit ? 1 : 0);

            if (count >= 2) return ExitReason.TwoFactors;
            if (domSweep) return ExitReason.DomSweep;
            if (macdExit) return ExitReason.MacdReversal;
            if (cciExit)  return ExitReason.CciExtreme;
            return ExitReason.None;
        }

        public (bool dom, bool macd, bool cci) GetActiveFactors(int bar, SignalDirection position, Func<int, IndicatorCandle> getCandle)
        {
            if (position == SignalDirection.None) return (false, false, false);
            return (
                DomSweepDetector.DetectSweep(bar, getCandle, position),
                _macd.ShouldExit(bar, position, getCandle),
                _cci.ShouldExit(bar, position, getCandle)
            );
        }
    }
}
