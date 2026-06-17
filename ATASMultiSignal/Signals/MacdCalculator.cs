using System;
using ATAS.Indicators;
using ATASMultiSignal.Models;

namespace ATASMultiSignal.Signals
{
    /// <summary>
    /// MACD-based sub-signal calculator.
    /// Uses a running EMA (Wilder / exponential smoothing) computed manually
    /// so the calculator is self-contained and does not depend on built-in ATAS series.
    /// Score = tanh(histogram / normFactor), giving a smooth value in (-1, +1).
    /// </summary>
    public sealed class MacdCalculator : ISignalCalculator
    {
        // ── Parameters ──────────────────────────────────────────────────────────
        public int FastPeriod { get; set; } = 12;
        public int SlowPeriod { get; set; } = 26;
        public int SignalPeriod { get; set; } = 9;
        public float Weight { get; set; } = 1.5f;

        // ── Internal state ───────────────────────────────────────────────────────
        private readonly ValueDataSeries _closeSeries;

        // We allocate lazily on first Calculate call once we know the series length.
        private float[]? _fastEma;
        private float[]? _slowEma;
        private float[]? _signalEma;
        private float[]? _histogram;
        private bool _initialized;

        private float _alphaFast;
        private float _alphaSlow;
        private float _alphaSignal;

        // Normalisation factor — roughly the average histogram magnitude expected.
        private const float NormFactor = 0.003f;

        public MacdCalculator(ValueDataSeries closeSeries)
        {
            _closeSeries = closeSeries ?? throw new ArgumentNullException(nameof(closeSeries));
        }

        public SignalScore Calculate(int bar)
        {
            int minBars = SlowPeriod + SignalPeriod;
            if (bar < minBars)
                return new SignalScore(0f, Weight, "MACD", $"Warming up ({bar}/{minBars})");

            EnsureArrays(bar + 1);

            // Recompute from scratch when not yet initialized up to this bar.
            // In a real indicator OnCalculate is called sequentially, so we only
            // need to update the current bar.
            if (!_initialized || bar == SlowPeriod + SignalPeriod)
            {
                InitializeEmas(bar);
                _initialized = true;
            }
            else
            {
                UpdateEmas(bar);
            }

            float hist = _histogram![bar];
            float score = (float)Math.Tanh(hist / NormFactor);
            string reason = $"hist={hist:F5} fast={_fastEma![bar]:F4} slow={_slowEma![bar]:F4}";
            return new SignalScore(score, Weight, "MACD", reason);
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private void EnsureArrays(int length)
        {
            if (_fastEma == null || _fastEma.Length < length)
            {
                int size = Math.Max(length + 64, 256);
                _fastEma = new float[size];
                _slowEma = new float[size];
                _signalEma = new float[size];
                _histogram = new float[size];
                _initialized = false;
            }

            _alphaFast = 2f / (FastPeriod + 1f);
            _alphaSlow = 2f / (SlowPeriod + 1f);
            _alphaSignal = 2f / (SignalPeriod + 1f);
        }

        private float Close(int bar) => (float)_closeSeries[bar];

        private void InitializeEmas(int upToBar)
        {
            // Seed fast EMA
            float sumFast = 0f;
            for (int i = 0; i < FastPeriod; i++) sumFast += Close(upToBar - SlowPeriod - SignalPeriod + i);
            _fastEma![upToBar - SlowPeriod - SignalPeriod + FastPeriod - 1] = sumFast / FastPeriod;

            // Seed slow EMA
            float sumSlow = 0f;
            for (int i = 0; i < SlowPeriod; i++) sumSlow += Close(upToBar - SignalPeriod - (SlowPeriod - 1) + i);
            _slowEma![upToBar - SignalPeriod] = sumSlow / SlowPeriod;

            // Build forward from seeds
            int startFast = upToBar - SlowPeriod - SignalPeriod + FastPeriod;
            int startSlow = upToBar - SignalPeriod + 1;

            for (int i = startFast; i <= upToBar - SignalPeriod; i++)
                _fastEma[i] = _fastEma[i - 1] + _alphaFast * (Close(i) - _fastEma[i - 1]);

            for (int i = startSlow; i <= upToBar; i++)
            {
                _slowEma![i] = _slowEma[i - 1] + _alphaSlow * (Close(i) - _slowEma[i - 1]);
                _fastEma[i] = _fastEma[i - 1] + _alphaFast * (Close(i) - _fastEma[i - 1]);
            }

            // Seed signal EMA from MACD line
            float sumSig = 0f;
            for (int i = 0; i < SignalPeriod; i++)
            {
                int idx = upToBar - SignalPeriod + 1 + i;
                sumSig += _fastEma[idx] - _slowEma![idx];
            }
            _signalEma![upToBar] = sumSig / SignalPeriod;

            float macdLine = _fastEma[upToBar] - _slowEma![upToBar];
            _histogram![upToBar] = macdLine - _signalEma[upToBar];
        }

        private void UpdateEmas(int bar)
        {
            float close = Close(bar);
            _fastEma![bar] = _fastEma[bar - 1] + _alphaFast * (close - _fastEma[bar - 1]);
            _slowEma![bar] = _slowEma[bar - 1] + _alphaSlow * (close - _slowEma[bar - 1]);

            float macdLine = _fastEma[bar] - _slowEma[bar];
            _signalEma![bar] = _signalEma[bar - 1] + _alphaSignal * (macdLine - _signalEma[bar - 1]);
            _histogram![bar] = macdLine - _signalEma[bar];
        }
    }
}
