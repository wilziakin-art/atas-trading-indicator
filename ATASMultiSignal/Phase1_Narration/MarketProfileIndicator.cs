using System;
using System.ComponentModel;
using System.Drawing;
using ATAS.Indicators;
using ATAS.Indicators.DrawingItems;
using ATASMultiSignal.Communication;

namespace ATASMultiSignal.Phase1_Narration
{
    [DisplayName("P1 - Market Profile Levels")]
    [Category("ATASMultiSignal")]
    public sealed class MarketProfileIndicator : Indicator
    {
        private const string IndicatorId = "P1_MarketProfile";

        [Parameter]
        [DisplayName("Previous POC")]
        public decimal PreviousPOC { get; set; } = 0m;

        [Parameter]
        [DisplayName("Previous VAH")]
        public decimal PreviousVAH { get; set; } = 0m;

        [Parameter]
        [DisplayName("Previous VAL")]
        public decimal PreviousVAL { get; set; } = 0m;

        private readonly ValueDataSeries _pocLine;
        private readonly ValueDataSeries _vahLine;
        private readonly ValueDataSeries _valLine;

        public MarketProfileIndicator()
        {
            _pocLine = new ValueDataSeries("POC")
            {
                Name = "Previous POC",
                VisualType = VisualMode.Line,
                Color = Color.Yellow.ToArgb(),
                Width = 2,
                ShowZeroLine = false
            };

            _vahLine = new ValueDataSeries("VAH")
            {
                Name = "Previous VAH",
                VisualType = VisualMode.Dashes,
                Color = Color.Green.ToArgb(),
                Width = 1,
                ShowZeroLine = false
            };

            _valLine = new ValueDataSeries("VAL")
            {
                Name = "Previous VAL",
                VisualType = VisualMode.Dashes,
                Color = Color.Red.ToArgb(),
                Width = 1,
                ShowZeroLine = false
            };

            DataSeries.Add(_pocLine);
            DataSeries.Add(_vahLine);
            DataSeries.Add(_valLine);
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            var candle = GetCandle(bar);
            if (candle == null) return;

            // Draw horizontal level lines
            if (PreviousPOC > 0) _pocLine[bar] = PreviousPOC;
            if (PreviousVAH > 0) _vahLine[bar] = PreviousVAH;
            if (PreviousVAL > 0) _valLine[bar] = PreviousVAL;

            if (bar == CurrentBar - 1) return;
            if (PreviousPOC == 0) return;

            decimal close = candle.Close;
            Vote vote;
            float strength;

            bool abovePOC = close > PreviousPOC;
            bool aboveVAH = close > PreviousVAH && PreviousVAH > 0;
            bool belowPOC = close < PreviousPOC;
            bool belowVAL = close < PreviousVAL && PreviousVAL > 0;

            if (abovePOC && aboveVAH)
            {
                vote = Vote.Bull;
                strength = 0.7f;
            }
            else if (belowPOC && belowVAL)
            {
                vote = Vote.Bear;
                strength = 0.7f;
            }
            else if (abovePOC && !aboveVAH)
            {
                // Near POC, slightly bullish
                vote = Vote.Bull;
                strength = 0.3f;
            }
            else if (belowPOC && !belowVAL)
            {
                // Near POC, slightly bearish
                vote = Vote.Bear;
                strength = 0.3f;
            }
            else
            {
                // Deep inside value area
                vote = Vote.Neutral;
                strength = 0.1f;
            }

            string reason = $"Close={close:F2} POC={PreviousPOC:F2} VAH={PreviousVAH:F2} VAL={PreviousVAL:F2}";

            SharedStateWriter.Write(new IndicatorState
            {
                IndicatorId = IndicatorId,
                Vote = vote,
                Strength = strength,
                LastUpdate = DateTime.UtcNow,
                Reason = reason
            });
        }
    }
}
