using System;
using System.ComponentModel;
using ATAS.Indicators;
using ATASMultiSignal.Communication;

namespace ATASMultiSignal.Phase2_Confirmation
{
    [DisplayName("P2 - Heikin Ashi Confirm")]
    [Category("ATASMultiSignal")]
    public sealed class HeikinAshiConfirmIndicator : Indicator
    {
        private const string IndicatorId = "P2_HeikinAshi";
        private const int MacdFast = 12;
        private const int MacdSlow = 26;
        private const int MacdSignal = 9;
        private const int SchaffCycleLength = 23;
        private const int SchaffFastPeriod = 50;
        private const float SchaffFactor = 0.5f;
        private const int MinBars = MacdSlow + MacdSignal + 5;

        private readonly ValueDataSeries _haClose;
        private readonly ValueDataSeries _haOpen;
        private readonly ValueDataSeries _macdHistogram;
        private readonly ValueDataSeries _schaffLine;
        private readonly ValueDataSeries _cvdSeries;

        public HeikinAshiConfirmIndicator()
        {
            _haClose = new ValueDataSeries("HAClose") { VisualType = VisualMode.Hide };
            _haOpen = new ValueDataSeries("HAOpen") { VisualType = VisualMode.Hide };
            _macdHistogram = new ValueDataSeries("MACDHist")
            {
                Name = "MACD Histogram",
                VisualType = VisualMode.Histogram,
                ShowZeroLine = true
            };
            _schaffLine = new ValueDataSeries("Schaff")
            {
                Name = "Schaff Trend Cycle",
                VisualType = VisualMode.Hide
            };
            _cvdSeries = new ValueDataSeries("CVD")
            {
                Name = "CVD",
                VisualType = VisualMode.Hide
            };

            DataSeries.Add(_macdHistogram);
            DataSeries.Add(_schaffLine);
            DataSeries.Add(_cvdSeries);
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            var candle = GetCandle(bar);
            if (candle == null) return;

            // Heikin Ashi calculation
            decimal haClose = (candle.Open + candle.High + candle.Low + candle.Close) / 4m;
            decimal haOpen = bar == 0
                ? (candle.Open + candle.Close) / 2m
                : (_haOpen[bar - 1] + _haClose[bar - 1]) / 2m;

            _haClose[bar] = haClose;
            _haOpen[bar] = haOpen;

            // MACD on HA close
            decimal macdLine = CalculateEma(bar, MacdFast, _haClose) - CalculateEmaFromSeries(bar, MacdSlow, _haClose);
            decimal signalLine = CalculateSignalEma(bar, MacdSignal, macdLine);
            decimal histogram = macdLine - signalLine;
            _macdHistogram[bar] = histogram;

            // CVD (cumulative volume delta)
            decimal delta = candle.Delta;
            _cvdSeries[bar] = bar == 0 ? delta : _cvdSeries[bar - 1] + delta;

            // Schaff Trend Cycle (simplified: Stoch of MACD)
            decimal schaff = CalculateSchaff(bar);
            _schaffLine[bar] = schaff;

            if (bar == CurrentBar - 1) return;
            if (bar < MinBars) return;

            bool macdBull = histogram > 0;
            bool schaffBull = schaff > 25;
            bool cvdUp = bar > 0 && _cvdSeries[bar] > _cvdSeries[bar - 1];

            bool macdBear = histogram < 0;
            bool schaffBear = schaff < 75;
            bool cvdDown = bar > 0 && _cvdSeries[bar] < _cvdSeries[bar - 1];

            Vote vote;
            float strength;

            if (macdBull && schaffBull && cvdUp)
            {
                vote = Vote.Bull;
                strength = 1f;
            }
            else if (macdBear && schaffBear && cvdDown)
            {
                vote = Vote.Bear;
                strength = 1f;
            }
            else
            {
                vote = Vote.Neutral;
                int bullCount = (macdBull ? 1 : 0) + (schaffBull ? 1 : 0) + (cvdUp ? 1 : 0);
                int bearCount = (macdBear ? 1 : 0) + (schaffBear ? 1 : 0) + (cvdDown ? 1 : 0);
                strength = Math.Max(bullCount, bearCount) / 3f;
                if (bullCount > bearCount) vote = Vote.Bull;
                else if (bearCount > bullCount) vote = Vote.Bear;
            }

            SharedStateWriter.Write(new IndicatorState
            {
                IndicatorId = IndicatorId,
                Vote = vote,
                Strength = strength,
                LastUpdate = DateTime.UtcNow,
                Reason = $"MACDHist={histogram:F4} Schaff={schaff:F1} CVD={_cvdSeries[bar]:F0}"
            });
        }

        private decimal CalculateEmaFromSeries(int bar, int period, ValueDataSeries src)
        {
            if (bar == 0) return src[0];
            // Using a secondary internal buffer approach with multiplier
            decimal k = 2m / (period + 1);
            decimal prev = bar > 0 ? CalculateEmaFromSeries(bar - 1, period, src) : src[0];
            return src[bar] * k + prev * (1m - k);
        }

        // EMA helpers using simple recursive approach cached in-place
        private readonly ValueDataSeries _emaFastBuf = new ValueDataSeries("_ef") { VisualType = VisualMode.Hide };
        private readonly ValueDataSeries _emaSlowBuf = new ValueDataSeries("_es") { VisualType = VisualMode.Hide };
        private readonly ValueDataSeries _sigBuf = new ValueDataSeries("_sig") { VisualType = VisualMode.Hide };

        private decimal CalculateEma(int bar, int period, ValueDataSeries src)
        {
            if (bar == 0) return src[0];
            var buf = period == MacdFast ? _emaFastBuf : _emaSlowBuf;
            if (buf[bar] != 0m) return buf[bar];
            decimal prev = buf[bar - 1] != 0m ? buf[bar - 1] : src[bar - 1];
            decimal k = 2m / (period + 1);
            buf[bar] = src[bar] * k + prev * (1m - k);
            return buf[bar];
        }

        private decimal CalculateSignalEma(int bar, int period, decimal macdValue)
        {
            if (bar == 0) { _sigBuf[bar] = macdValue; return macdValue; }
            decimal prev = _sigBuf[bar - 1];
            decimal k = 2m / (period + 1);
            _sigBuf[bar] = macdValue * k + prev * (1m - k);
            return _sigBuf[bar];
        }

        private decimal CalculateSchaff(int bar)
        {
            if (bar < SchaffCycleLength) return 50m;

            decimal minMacd = decimal.MaxValue;
            decimal maxMacd = decimal.MinValue;
            for (int i = bar - SchaffCycleLength + 1; i <= bar; i++)
            {
                if (_macdHistogram[i] < minMacd) minMacd = _macdHistogram[i];
                if (_macdHistogram[i] > maxMacd) maxMacd = _macdHistogram[i];
            }

            decimal range = maxMacd - minMacd;
            if (range == 0m) return 50m;

            decimal stoch = (_macdHistogram[bar] - minMacd) / range * 100m;
            return stoch;
        }
    }
}
