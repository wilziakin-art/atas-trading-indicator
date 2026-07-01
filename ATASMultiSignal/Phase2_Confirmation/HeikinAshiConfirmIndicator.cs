using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ATAS.Indicators;
using System.Drawing;

namespace ATASMultiSignal.Phase2_Confirmation
{
    [DisplayName("EMI - Heikin Ashi Confirm (Phase 2)")]
    [Description("MACD + STC + CVD sur Heikin Ashi 500T — Confirmation direction EMI")]
    public class HeikinAshiConfirmIndicator : Indicator
    {
        // MACD params
        [Parameter][Display(Name = "MACD Fast", GroupName = "MACD")] public int MacdFast { get; set; } = 12;
        [Parameter][Display(Name = "MACD Slow", GroupName = "MACD")] public int MacdSlow { get; set; } = 26;
        [Parameter][Display(Name = "MACD Signal", GroupName = "MACD")] public int MacdSignal { get; set; } = 9;
        [Parameter][Display(Name = "ZS Rouge (vente)", GroupName = "MACD")] public double ZsRed { get; set; } = -0.20;
        [Parameter][Display(Name = "ZS Vert (achat)", GroupName = "MACD")] public double ZsGreen { get; set; } = 0.20;

        // STC params
        [Parameter][Display(Name = "STC Fast", GroupName = "STC")] public int StcFast { get; set; } = 23;
        [Parameter][Display(Name = "STC Slow", GroupName = "STC")] public int StcSlow { get; set; } = 50;
        [Parameter][Display(Name = "STC Cycle", GroupName = "STC")] public int StcCycle { get; set; } = 10;

        [Parameter][Display(Name = "Indicateur ID", GroupName = "Communication")] public string IndicatorId { get; set; } = "HeikinAshiConfirm";

        // Series
        private readonly ValueDataSeries _macdLine = new("MACD") { Color = Colors.Cyan, Width = 1 };
        private readonly ValueDataSeries _signalLine = new("Signal") { Color = Colors.Red, Width = 1 };
        private readonly ValueDataSeries _histogram = new("Histogram") { Color = Colors.DodgerBlue, Width = 2 };
        private readonly ValueDataSeries _stc = new("STC") { Color = Colors.Yellow, Width = 2 };
        private readonly ValueDataSeries _cvd = new("CVD") { Color = Colors.Lime, Width = 1 };

        // EMA state
        private double _emaFast, _emaSlow, _emaSignal;
        private bool _initialized;
        private double _cumDelta;

        // STC state
        private double _stcEmaFast, _stcEmaSlow;
        private double[] _stcBuffer = new double[200];
        private int _stcBufIdx;
        private double _stcValue;
        private double _k1, _d1, _k2, _d2;
        private bool _stcInit;

        public HeikinAshiConfirmIndicator() : base(true)
        {
            DataSeries[0] = _macdLine;
            DataSeries.Add(_signalLine);
            DataSeries.Add(_histogram);
            DataSeries.Add(_stc);
            DataSeries.Add(_cvd);
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            var candle = GetCandle(bar);
            double close = (double)candle.Close;
            double delta = (double)(candle.Ask - candle.Bid);

            if (!_initialized)
            {
                _emaFast = close;
                _emaSlow = close;
                _emaSignal = 0;
                _stcEmaFast = close;
                _stcEmaSlow = close;
                _initialized = true;
            }

            // MACD
            double kf = 2.0 / (MacdFast + 1);
            double ks = 2.0 / (MacdSlow + 1);
            double ksg = 2.0 / (MacdSignal + 1);
            _emaFast = close * kf + _emaFast * (1 - kf);
            _emaSlow = close * ks + _emaSlow * (1 - ks);
            double macd = _emaFast - _emaSlow;
            _emaSignal = macd * ksg + _emaSignal * (1 - ksg);
            double hist = macd - _emaSignal;

            _macdLine[bar] = (decimal)macd;
            _signalLine[bar] = (decimal)_emaSignal;
            _histogram[bar] = (decimal)hist;

            // CVD cumulatif de session
            if (bar == 0 || IsNewSession(bar)) _cumDelta = 0;
            _cumDelta += delta;
            _cvd[bar] = (decimal)_cumDelta;

            // STC simplifié (MACD stochastique)
            double stcRaw = CalculateSTC(bar, macd);
            _stc[bar] = (decimal)stcRaw;

            if (bar == CurrentBar - 1)
            {
                bool macdBull = hist > ZsGreen;
                bool macdBear = hist < ZsRed;
                bool stcBull = stcRaw < 25;   // traverse 25 = signal achat
                bool stcBear = stcRaw > 75;   // passe sous 75 = signal vente
                bool cvdBull = _cumDelta > 0;
                bool cvdBear = _cumDelta < 0;

                int bullScore = (macdBull ? 1 : 0) + (stcBull ? 1 : 0) + (cvdBull ? 1 : 0);
                int bearScore = (macdBear ? 1 : 0) + (stcBear ? 1 : 0) + (cvdBear ? 1 : 0);

                string direction = bullScore > bearScore ? "Bull" : (bearScore > bullScore ? "Bear" : "Neutral");
                double strength = Math.Max(bullScore, bearScore) / 3.0;
                string details = $"MACD={hist:F3} STC={stcRaw:F1} CVD={_cumDelta:F0}";

                Communication.SharedStateWriter.WriteVote(IndicatorId, direction, strength, details);
            }
        }

        private double CalculateSTC(int bar, double macdValue)
        {
            // Stockage circulaire des valeurs MACD pour stochastique
            _stcBuffer[_stcBufIdx % _stcBuffer.Length] = macdValue;
            _stcBufIdx++;

            int len = Math.Min(_stcBufIdx, StcCycle);
            double hi = double.MinValue, lo = double.MaxValue;
            for (int i = 0; i < len; i++)
            {
                double v = _stcBuffer[(_stcBufIdx - 1 - i + _stcBuffer.Length) % _stcBuffer.Length];
                if (v > hi) hi = v;
                if (v < lo) lo = v;
            }

            double range = hi - lo;
            double k = range > 0 ? (macdValue - lo) / range * 100.0 : 50.0;

            // Lissage
            double kSmooth = 2.0 / (StcCycle + 1);
            _k1 = k * kSmooth + _k1 * (1 - kSmooth);
            _d1 = _k1 * kSmooth + _d1 * (1 - kSmooth);

            return Math.Clamp(_d1, 0, 100);
        }

        private bool IsNewSession(int bar)
        {
            if (bar == 0) return false;
            return GetCandle(bar).Time.Date != GetCandle(bar - 1).Time.Date;
        }
    }
}
