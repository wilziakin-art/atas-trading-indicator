using System;
using ATAS.Indicators;
using ATASMultiSignal.Models;

namespace ATASMultiSignal.Phase3_Execution.ExitStrategy
{
    public static class DomSweepDetector
    {
        private const int AvgVolumeLookback = 10;
        private const decimal RangeFraction = 0.5m;

        /// <summary>
        /// Detects DOM sweeps: rapid removal of large bids/asks.
        /// Returns true if a sweep is detected against the current position direction.
        /// </summary>
        public static bool DetectSweep(int bar, Func<int, IndicatorCandle> getCandle, SignalDirection position)
        {
            if (bar < AvgVolumeLookback + 1) return false;

            var current = getCandle(bar);
            if (current == null) return false;

            // Calculate average volume over last 10 bars
            decimal avgVolume = 0m;
            for (int i = 1; i <= AvgVolumeLookback; i++)
            {
                var c = getCandle(bar - i);
                if (c != null) avgVolume += c.Volume;
            }
            avgVolume /= AvgVolumeLookback;

            // Calculate ATR over last 10 bars
            decimal atr = 0m;
            int atrCount = 0;
            for (int i = 1; i <= AvgVolumeLookback; i++)
            {
                var c = getCandle(bar - i);
                var p = getCandle(bar - i - 1);
                if (c == null || p == null) continue;
                decimal tr = Math.Max(c.High - c.Low, Math.Max(Math.Abs(c.High - p.Close), Math.Abs(c.Low - p.Close)));
                atr += tr;
                atrCount++;
            }
            if (atrCount > 0) atr /= atrCount;

            bool highVolume = current.Volume > avgVolume * 2m;
            bool smallRange = atr > 0 && (current.High - current.Low) < RangeFraction * atr;
            bool volumeSweep = highVolume && smallRange;

            // Delta sign change check
            bool deltaFlip = false;
            if (bar >= 2)
            {
                var prev = getCandle(bar - 1);
                if (prev != null)
                    deltaFlip = (current.Delta > 0 && prev.Delta < 0) || (current.Delta < 0 && prev.Delta > 0);
            }

            bool sweepDetected = volumeSweep || deltaFlip;

            if (!sweepDetected) return false;

            // Check if sweep is against our position
            if (position == SignalDirection.Buy && current.Delta < 0) return true;
            if (position == SignalDirection.Sell && current.Delta > 0) return true;

            return false;
        }
    }
}
