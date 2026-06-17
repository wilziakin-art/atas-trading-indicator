using System;
using System.Collections.Generic;
using ATAS.Indicators;
using ATASMultiSignal.Models;

namespace ATASMultiSignal.Signals
{
    /// <summary>
    /// Volume-delta sub-signal calculator.
    /// Score is based on the normalised delta (askVol - bidVol) relative to total volume,
    /// compared against a lookback window to determine relative strength.
    /// Falls back to tick-volume directional heuristic when footprint data is unavailable.
    /// </summary>
    public sealed class VolumeDeltaCalculator : ISignalCalculator
    {
        // ── Parameters ──────────────────────────────────────────────────────────
        public int LookbackBars { get; set; } = 20;
        public float Weight { get; set; } = 2.0f;

        // ── Dependencies ─────────────────────────────────────────────────────────
        private readonly Func<int, ATAS.Indicators.IndicatorCandle> _getCandle;

        public VolumeDeltaCalculator(Func<int, ATAS.Indicators.IndicatorCandle> getCandle)
        {
            _getCandle = getCandle ?? throw new ArgumentNullException(nameof(getCandle));
        }

        public SignalScore Calculate(int bar)
        {
            if (bar < LookbackBars)
                return new SignalScore(0f, Weight, "Vol Delta", $"Warming up ({bar}/{LookbackBars})");

            // Gather delta ratios for the lookback window
            var ratios = new List<float>(LookbackBars);
            for (int i = bar - LookbackBars; i <= bar; i++)
            {
                float ratio = GetDeltaRatio(i);
                ratios.Add(ratio);
            }

            float currentRatio = ratios[ratios.Count - 1];

            // Quartile-based scoring: how extreme is the current delta vs history?
            ratios.Sort();
            int count = ratios.Count;
            float q25 = ratios[count / 4];
            float q75 = ratios[3 * count / 4];
            float iqr = q75 - q25;

            float score;
            if (iqr < 1e-6f)
            {
                score = 0f;
            }
            else
            {
                // Normalise: median maps to 0, extremes map to ±1
                float median = ratios[count / 2];
                score = Math.Clamp((currentRatio - median) / (iqr * 1.5f), -1f, 1f);
            }

            string reason = $"deltaRatio={currentRatio:F4} q25={q25:F4} q75={q75:F4}";
            return new SignalScore(score, Weight, "Vol Delta", reason);
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private float GetDeltaRatio(int bar)
        {
            var candle = _getCandle(bar);
            if (candle == null)
                return 0f;

            // Try footprint delta first
            if (candle is ATAS.Indicators.FootprintCandle fp)
            {
                decimal totalVol = fp.Volume;
                if (totalVol <= 0m)
                    return 0f;
                decimal delta = fp.Delta;
                return (float)(delta / totalVol);
            }

            // Fallback: use close vs open direction with tick volume
            decimal vol = candle.Volume;
            if (vol <= 0m)
                return 0f;

            // Positive delta heuristic: bullish candle → positive, bearish → negative
            float direction = candle.Close >= candle.Open ? 1f : -1f;
            return direction * (float)Math.Min((double)vol / 1000.0, 1.0);
        }
    }
}
