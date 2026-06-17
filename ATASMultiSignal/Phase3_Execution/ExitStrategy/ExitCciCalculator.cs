using System;
using ATAS.Indicators;
using ATASMultiSignal.Models;

namespace ATASMultiSignal.Phase3_Execution.ExitStrategy
{
    public sealed class ExitCciCalculator
    {
        public int Period { get; set; } = 20;

        public float Calculate(int bar, Func<int, IndicatorCandle> getCandle)
        {
            if (bar < Period - 1) return 0f;

            decimal sumTp = 0m;
            for (int i = 0; i < Period; i++)
            {
                var c = getCandle(bar - i);
                if (c == null) return 0f;
                sumTp += (c.High + c.Low + c.Close) / 3m;
            }
            decimal smaTp = sumTp / Period;

            var cur = getCandle(bar);
            if (cur == null) return 0f;
            decimal typicalPrice = (cur.High + cur.Low + cur.Close) / 3m;

            decimal sumDev = 0m;
            for (int i = 0; i < Period; i++)
            {
                var c = getCandle(bar - i);
                if (c == null) break;
                decimal tp = (c.High + c.Low + c.Close) / 3m;
                sumDev += Math.Abs(tp - smaTp);
            }

            decimal meanDev = sumDev / Period;
            if (meanDev == 0m) return 0f;

            return (float)((typicalPrice - smaTp) / (0.015m * meanDev));
        }

        public bool ShouldExit(int bar, SignalDirection position, Func<int, IndicatorCandle> getCandle)
        {
            if (bar < 1) return false;

            float cciCurrent = Calculate(bar, getCandle);
            float cciPrev = Calculate(bar - 1, getCandle);

            if (position == SignalDirection.Buy)
                return cciPrev >= 100f && cciCurrent < 100f;

            if (position == SignalDirection.Sell)
                return cciPrev <= -100f && cciCurrent > -100f;

            return false;
        }
    }
}
