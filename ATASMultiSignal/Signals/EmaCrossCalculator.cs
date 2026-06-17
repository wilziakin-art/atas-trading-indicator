using System;
using ATAS.Indicators;
using ATASMultiSignal.Models;

namespace ATASMultiSignal.Signals
{
    /// <summary>
    /// EMA-crossover sub-signal calculator.
    /// Score: +1.0 when fast EMA is above slow EMA and the gap is widening,
    ///        -1.0 when below and widening in the opposite direction.
    /// The raw gap is divided by an ATR approximation and clamped to [-1, +1].
    /// </summary>
    public sealed class EmaCrossCalculator : ISignalCalculator
    {
        // ── Parameters ──────────────────────────────────────────────────────────
        public int FastPeriod { get; set; } = 9;
        public int SlowPeriod { get; set; } = 21;
        public float Weight { get; set; } = 1.5f;

        // ── Internal state ───────────────────────────────────────────────────────
        private readonly ValueDataSeries _closeSeries;
        private readonly ValueDataSeries _highSeries;
        private readonly ValueDataSeries _lowSeries;

        private float[]? _fastEma;
        private float[]? _slowEma;
        private float _alphaFast;
        private float _alphaSlow;
        private bool _initialized;

        private const int AtrPeriod = 14;

        public EmaCrossCalculator(ValueDataSeries closeSeries, ValueDataSeries highSeries, ValueDataSeries lowSeries)
        {
            _closeSeries = closeSeries ?? throw new ArgumentNullException(nameof(closeSeries));
            _highSeries = highSeries ?? throw new ArgumentNullException(nameof(highSeries));
            _lowSeries = lowSeries ?? throw new ArgumentNullException(nameof(lowSeries));
        }

        public SignalScore Calculate(int bar)
        {
            int minBars = SlowPeriod + AtrPeriod;
            if (bar < minBars)
                return new SignalScore(0f, Weight, "EMA Cross", $"Warming up ({bar}/{minBars})");

            EnsureArrays(bar + 1);

            if (!_initialized)
            {
                InitializeEmas(bar);
                _initialized = true;
            }
            else
            {
                UpdateEmas(bar);
            }

            float fast = _fastEma![bar];
            float slow = _slowEma![bar];
            float fastPrev = _fastEma[bar - 1];
            float slowPrev = _slowEma[bar - 1];

            float gap = fast - slow;
            float prevGap = fastPrev - slowPrev;
            float atr = ComputeAtr(bar);

            float rawScore = atr > 0f ? gap / atr : 0f;
            // Boost toward ±1 when gap is widening
            if (gap > 0 && gap > prevGap) rawScore = Math.Min(rawScore * 1.2f, 1f);
            else if (gap < 0 && gap < prevGap) rawScore = Math.Max(rawScore * 1.2f, -1f);

            float score = Math.Clamp(rawScore, -1f, 1f);
            string reason = $"fast={fast:F4} slow={slow:F4} gap={gap:F4} atr={atr:F4}";
            return new SignalScore(score, Weight, "EMA Cross", reason);
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private void EnsureArrays(int length)
        {
            if (_fastEma == null || _fastEma.Length < length)
            {
                int size = Math.Max(length + 64, 256);
                _fastEma = new float[size];
                _slowEma = new float[size];
                _initialized = false;
            }
            _alphaFast = 2f / (FastPeriod + 1f);
            _alphaSlow = 2f / (SlowPeriod + 1f);
        }

        private float Close(int bar) => (float)_closeSeries[bar];
        private float High(int bar) => (float)_highSeries[bar];
        private float Low(int bar) => (float)_lowSeries[bar];

        private void InitializeEmas(int upToBar)
        {
            // Seed from SMA
            float sumFast = 0f, sumSlow = 0f;
            int fastStart = upToBar - FastPeriod + 1;
            int slowStart = upToBar - SlowPeriod + 1;

            for (int i = fastStart; i <= upToBar; i++) sumFast += Close(i);
            for (int i = slowStart; i <= upToBar; i++) sumSlow += Close(i);

            _fastEma![upToBar] = sumFast / FastPeriod;
            _slowEma![upToBar] = sumSlow / SlowPeriod;
        }

        private void UpdateEmas(int bar)
        {
            float close = Close(bar);
            _fastEma![bar] = _fastEma[bar - 1] + _alphaFast * (close - _fastEma[bar - 1]);
            _slowEma![bar] = _slowEma[bar - 1] + _alphaSlow * (close - _slowEma[bar - 1]);
        }

        private float ComputeAtr(int bar)
        {
            float sum = 0f;
            for (int i = bar - AtrPeriod + 1; i <= bar; i++)
            {
                float h = High(i);
                float l = Low(i);
                float prevClose = i > 0 ? Close(i - 1) : Close(i);
                float tr = Math.Max(h - l, Math.Max(Math.Abs(h - prevClose), Math.Abs(l - prevClose)));
                sum += tr;
            }
            return sum / AtrPeriod;
        }
    }
}
