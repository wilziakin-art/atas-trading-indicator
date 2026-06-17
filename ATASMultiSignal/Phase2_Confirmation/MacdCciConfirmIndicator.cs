using System;
using System.ComponentModel;
using ATAS.Indicators;
using ATASMultiSignal.Communication;

namespace ATASMultiSignal.Phase2_Confirmation
{
    [DisplayName("P2 - MACD+CCI Confirm (10s)")]
    [Category("ATASMultiSignal")]
    public sealed class MacdCciConfirmIndicator : Indicator
    {
        private const string IndicatorId = "P2_MacdCci";
        private const int MacdFast = 12;
        private const int MacdSlow = 26;
        private const int MacdSignalPeriod = 9;
        private const int CciPeriod = 20;
        private const int MinBars = MacdSlow + MacdSignalPeriod + CciPeriod;

        private readonly ValueDataSeries _emaFast;
        private readonly ValueDataSeries _emaSlow;
        private readonly ValueDataSeries _macdLine;
        private readonly ValueDataSeries _signalLine;
        private readonly ValueDataSeries _histogram;
        private readonly ValueDataSeries _cciLine;

        public MacdCciConfirmIndicator()
        {
            _emaFast = new ValueDataSeries("EMAFast") { VisualType = VisualMode.Hide };
            _emaSlow = new ValueDataSeries("EMASlow") { VisualType = VisualMode.Hide };
            _macdLine = new ValueDataSeries("MACDLine") { VisualType = VisualMode.Hide };
            _signalLine = new ValueDataSeries("SignalLine") { VisualType = VisualMode.Hide };
            _histogram = new ValueDataSeries("Histogram")
            {
                Name = "MACD Histogram",
                VisualType = VisualMode.Histogram,
                ShowZeroLine = true
            };
            _cciLine = new ValueDataSeries("CCI")
            {
                Name = "CCI",
                VisualType = VisualMode.Line,
                ShowZeroLine = true
            };

            DataSeries.Add(_histogram);
            DataSeries.Add(_cciLine);
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            var candle = GetCandle(bar);
            if (candle == null) return;

            decimal close = candle.Close;

            // EMA Fast
            if (bar == 0)
            {
                _emaFast[bar] = close;
                _emaSlow[bar] = close;
            }
            else
            {
                decimal kFast = 2m / (MacdFast + 1);
                decimal kSlow = 2m / (MacdSlow + 1);
                _emaFast[bar] = close * kFast + _emaFast[bar - 1] * (1m - kFast);
                _emaSlow[bar] = close * kSlow + _emaSlow[bar - 1] * (1m - kSlow);
            }

            decimal macd = _emaFast[bar] - _emaSlow[bar];
            _macdLine[bar] = macd;

            // Signal EMA
            if (bar == 0)
                _signalLine[bar] = macd;
            else
            {
                decimal kSig = 2m / (MacdSignalPeriod + 1);
                _signalLine[bar] = macd * kSig + _signalLine[bar - 1] * (1m - kSig);
            }

            _histogram[bar] = _macdLine[bar] - _signalLine[bar];

            // CCI calculation
            decimal cci = CalculateCci(bar);
            _cciLine[bar] = cci;

            if (bar == CurrentBar - 1) return;
            if (bar < MinBars) return;

            bool macdBullish = _macdLine[bar] > _signalLine[bar];
            bool macdBearish = _macdLine[bar] < _signalLine[bar];
            bool cciBull = cci > 0 && cci < 100;
            bool cciBear = cci < 0 && cci > -100;

            Vote vote;
            if (macdBullish && cciBull)
                vote = Vote.Bull;
            else if (macdBearish && cciBear)
                vote = Vote.Bear;
            else
                vote = Vote.Neutral;

            decimal histAbs = Math.Abs(_histogram[bar]);
            // Normalize histogram over last 20 bars
            decimal maxHist = 0m;
            for (int i = 1; i <= 20 && i <= bar; i++)
            {
                decimal h = Math.Abs(_histogram[bar - i]);
                if (h > maxHist) maxHist = h;
            }
            float histNorm = maxHist > 0 ? Math.Min(1f, (float)(histAbs / maxHist)) : 0f;
            float cciNorm = Math.Min(1f, (float)(Math.Abs(cci) / 100m));
            float strength = Math.Min(1f, (histNorm + cciNorm) / 2f);

            SharedStateWriter.Write(new IndicatorState
            {
                IndicatorId = IndicatorId,
                Vote = vote,
                Strength = strength,
                LastUpdate = DateTime.UtcNow,
                Reason = $"MACDLine={_macdLine[bar]:F4} Signal={_signalLine[bar]:F4} CCI={cci:F1}"
            });
        }

        private decimal CalculateCci(int bar)
        {
            if (bar < CciPeriod - 1) return 0m;

            decimal sumTp = 0m;
            for (int i = 0; i < CciPeriod; i++)
            {
                var c = GetCandle(bar - i);
                if (c == null) break;
                sumTp += (c.High + c.Low + c.Close) / 3m;
            }
            decimal smaTp = sumTp / CciPeriod;

            decimal sumDev = 0m;
            var cur = GetCandle(bar);
            if (cur == null) return 0m;
            decimal typicalPrice = (cur.High + cur.Low + cur.Close) / 3m;

            for (int i = 0; i < CciPeriod; i++)
            {
                var c = GetCandle(bar - i);
                if (c == null) break;
                decimal tp = (c.High + c.Low + c.Close) / 3m;
                sumDev += Math.Abs(tp - smaTp);
            }

            decimal meanDev = sumDev / CciPeriod;
            if (meanDev == 0m) return 0m;

            return (typicalPrice - smaTp) / (0.015m * meanDev);
        }
    }
}
