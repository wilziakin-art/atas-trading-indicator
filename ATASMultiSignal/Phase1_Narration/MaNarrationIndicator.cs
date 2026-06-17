using System;
using System.ComponentModel;
using System.Drawing;
using ATAS.Indicators;
using ATASMultiSignal.Communication;

namespace ATASMultiSignal.Phase1_Narration
{
    [DisplayName("P1 - MA Narration (NQ/SP/DJI)")]
    [Category("ATASMultiSignal")]
    public sealed class MaNarrationIndicator : Indicator
    {
        private const int FastPeriod = 9;
        private const int MidPeriod = 45;
        private const int SlowPeriod = 135;
        private const int MinBars = SlowPeriod + 1;

        [Parameter]
        [DisplayName("Instrument ID")]
        public string InstrumentId { get; set; } = "NQ";

        private readonly ValueDataSeries _fastEma;
        private readonly ValueDataSeries _midEma;
        private readonly ValueDataSeries _slowEma;

        public MaNarrationIndicator()
        {
            _fastEma = new ValueDataSeries("FastEMA")
            {
                Name = "EMA Fast (9)",
                VisualType = VisualMode.Line,
                Color = Color.Cyan.ToArgb(),
                Width = 1,
                ShowZeroLine = false
            };

            _midEma = new ValueDataSeries("MidEMA")
            {
                Name = "EMA Mid (45)",
                VisualType = VisualMode.Line,
                Color = Color.Yellow.ToArgb(),
                Width = 1,
                ShowZeroLine = false
            };

            _slowEma = new ValueDataSeries("SlowEMA")
            {
                Name = "EMA Slow (135)",
                VisualType = VisualMode.Line,
                Color = Color.Orange.ToArgb(),
                Width = 2,
                ShowZeroLine = false
            };

            DataSeries.Add(_fastEma);
            DataSeries.Add(_midEma);
            DataSeries.Add(_slowEma);
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            var candle = GetCandle(bar);
            if (candle == null) return;

            decimal close = candle.Close;

            _fastEma[bar] = CalculateEma(bar, FastPeriod, _fastEma);
            _midEma[bar] = CalculateEma(bar, MidPeriod, _midEma);
            _slowEma[bar] = CalculateEma(bar, SlowPeriod, _slowEma);

            if (bar == CurrentBar - 1) return;
            if (bar < MinBars) return;

            decimal fast = _fastEma[bar];
            decimal mid = _midEma[bar];
            decimal slow = _slowEma[bar];

            Vote vote;
            if (fast > mid && mid > slow)
                vote = Vote.Bull;
            else if (fast < mid && mid < slow)
                vote = Vote.Bear;
            else
                vote = Vote.Neutral;

            decimal atr = CalculateAtr(bar, 14);
            float strength = 0f;
            if (atr > 0)
                strength = Math.Min(1f, (float)(Math.Abs(fast - slow) / atr));

            string indicatorId = "P1_MA_" + InstrumentId;

            SharedStateWriter.Write(new IndicatorState
            {
                IndicatorId = indicatorId,
                Vote = vote,
                Strength = strength,
                LastUpdate = DateTime.UtcNow,
                Reason = $"Fast={fast:F2} Mid={mid:F2} Slow={slow:F2}"
            });
        }

        private decimal CalculateEma(int bar, int period, ValueDataSeries series)
        {
            var candle = GetCandle(bar);
            if (candle == null) return 0m;

            if (bar == 0)
                return candle.Close;

            decimal prevEma = series[bar - 1];
            if (prevEma == 0m)
            {
                // Seed with SMA for first 'period' bars
                if (bar < period)
                    return candle.Close;

                decimal sum = 0m;
                for (int i = 0; i < period; i++)
                {
                    var c = GetCandle(bar - i);
                    if (c != null) sum += c.Close;
                }
                return sum / period;
            }

            decimal k = 2m / (period + 1);
            return candle.Close * k + prevEma * (1m - k);
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
