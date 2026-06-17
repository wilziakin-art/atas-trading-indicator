using System;
using System.ComponentModel;
using ATAS.Indicators;
using ATASMultiSignal.Communication;

namespace ATASMultiSignal.Phase2_Confirmation
{
    [DisplayName("P2 - Footprint Confirm (10T Reversal)")]
    [Category("ATASMultiSignal")]
    public sealed class FootprintConfirmIndicator : Indicator
    {
        private const string IndicatorId = "P2_Footprint";
        private const int MinBars = 3;

        private readonly ValueDataSeries _deltaSeries;

        public FootprintConfirmIndicator()
        {
            _deltaSeries = new ValueDataSeries("Delta")
            {
                Name = "Bar Delta",
                VisualType = VisualMode.Histogram,
                ShowZeroLine = true
            };

            DataSeries.Add(_deltaSeries);
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            var candle = GetCandle(bar);
            if (candle == null) return;

            // Try to access FootprintCandle
            var fp = candle as FootprintCandle;

            decimal delta = candle.Delta;
            decimal totalVolume = candle.Volume;

            _deltaSeries[bar] = delta;

            if (bar == CurrentBar - 1) return;
            if (bar < MinBars) return;

            decimal prevDelta = bar > 0 ? GetCandle(bar - 1)?.Delta ?? 0m : 0m;
            decimal deltaChange = delta - prevDelta;

            // Detect large buy/sell orders in book via footprint data
            bool largeBuyOrders = false;
            bool largeSellOrders = false;

            if (fp != null)
            {
                // Examine footprint cluster levels
                foreach (var level in fp.Levels)
                {
                    if (level.Ask > level.Bid * 2m) largeBuyOrders = true;
                    if (level.Bid > level.Ask * 2m) largeSellOrders = true;
                }
            }
            else
            {
                // Proxy: use delta as a heuristic
                if (totalVolume > 0)
                {
                    decimal askVol = (totalVolume + delta) / 2m;
                    decimal bidVol = (totalVolume - delta) / 2m;
                    if (bidVol > 0 && askVol > bidVol * 2m) largeBuyOrders = true;
                    if (askVol > 0 && bidVol > askVol * 2m) largeSellOrders = true;
                }
            }

            Vote vote;
            if (delta > 0 && deltaChange > 0 && largeBuyOrders)
                vote = Vote.Bull;
            else if (delta < 0 && deltaChange < 0 && largeSellOrders)
                vote = Vote.Bear;
            else
                vote = Vote.Neutral;

            float strength = totalVolume > 0
                ? Math.Min(1f, (float)(Math.Abs(delta) / totalVolume))
                : 0f;

            if (vote == Vote.Neutral) strength = 0f;

            SharedStateWriter.Write(new IndicatorState
            {
                IndicatorId = IndicatorId,
                Vote = vote,
                Strength = strength,
                LastUpdate = DateTime.UtcNow,
                Reason = $"Delta={delta:F0} DeltaChange={deltaChange:F0} Vol={totalVolume:F0}"
            });
        }
    }
}
