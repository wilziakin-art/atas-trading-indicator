using System;
using System.ComponentModel;
using System.Drawing;
using ATAS.Indicators;
using ATASMultiSignal.Communication;

namespace ATASMultiSignal.Phase1_Narration
{
    [DisplayName("P1 - VWAP Narration")]
    [Category("ATASMultiSignal")]
    public sealed class VwapNarrationIndicator : Indicator
    {
        private const string IndicatorId = "P1_VWAP";
        private const int MinBars = 20;

        private readonly ValueDataSeries _vwapSeries;
        private readonly ValueDataSeries _atrSeries;

        public VwapNarrationIndicator()
        {
            _vwapSeries = new ValueDataSeries("VWAP")
            {
                Name = "VWAP",
                VisualType = VisualMode.Line,
                Color = Color.Green.ToArgb(),
                Width = 2,
                ShowZeroLine = false
            };

            _atrSeries = new ValueDataSeries("ATR")
            {
                Name = "ATR",
                VisualType = VisualMode.Hide,
                ShowZeroLine = false
            };

            DataSeries.Add(_vwapSeries);
            DataSeries.Add(_atrSeries);
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            var candle = GetCandle(bar);
            if (candle == null) return;

            // Calculate monthly VWAP from start of month
            decimal sumPV = 0m;
            decimal sumV = 0m;
            var barTime = candle.Time;
            var monthStart = new DateTime(barTime.Year, barTime.Month, 1);

            for (int i = bar; i >= 0; i--)
            {
                var c = GetCandle(i);
                if (c == null) break;
                if (c.Time < monthStart) break;

                decimal typicalPrice = (c.High + c.Low + c.Close) / 3m;
                sumPV += typicalPrice * c.Volume;
                sumV += c.Volume;
            }

            decimal vwap = sumV > 0 ? sumPV / sumV : candle.Close;
            _vwapSeries[bar] = vwap;

            // ATR (14-period)
            decimal atr = CalculateAtr(bar, 14);
            _atrSeries[bar] = atr;

            // Update VWAP line color based on price position
            bool isBullish = candle.Close > vwap;
            _vwapSeries.Color = isBullish ? Color.Green.ToArgb() : Color.Red.ToArgb();

            // Only write state on finalized (non-last) bars
            if (bar == CurrentBar - 1) return;
            if (bar < MinBars) return;

            Vote vote;
            float strength;

            if (candle.Close > vwap)
            {
                vote = Vote.Bull;
                strength = atr > 0 ? Math.Min(1f, (float)((candle.Close - vwap) / atr)) : 0f;
            }
            else if (candle.Close < vwap)
            {
                vote = Vote.Bear;
                strength = atr > 0 ? Math.Min(1f, (float)((vwap - candle.Close) / atr)) : 0f;
            }
            else
            {
                vote = Vote.Neutral;
                strength = 0f;
            }

            SharedStateWriter.Write(new IndicatorState
            {
                IndicatorId = IndicatorId,
                Vote = vote,
                Strength = strength,
                LastUpdate = DateTime.UtcNow,
                Reason = $"Close={candle.Close:F2} VWAP={vwap:F2} ATR={atr:F2}"
            });
        }

        private decimal CalculateAtr(int bar, int period)
        {
            if (bar < 1) return 0m;
            decimal sum = 0m;
            int count = Math.Min(period, bar);
            for (int i = 0; i < count; i++)
            {
                var c = GetCandle(bar - i);
                var p = GetCandle(bar - i - 1);
                if (c == null || p == null) break;
                decimal tr = Math.Max(c.High - c.Low, Math.Max(Math.Abs(c.High - p.Close), Math.Abs(c.Low - p.Close)));
                sum += tr;
            }
            return count > 0 ? sum / count : 0m;
        }
    }
}
