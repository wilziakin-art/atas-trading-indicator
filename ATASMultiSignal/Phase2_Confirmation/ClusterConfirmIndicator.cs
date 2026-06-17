using System;
using System.ComponentModel;
using ATAS.Indicators;
using ATASMultiSignal.Communication;

namespace ATASMultiSignal.Phase2_Confirmation
{
    [DisplayName("P2 - Cluster Confirm")]
    [Category("ATASMultiSignal")]
    public sealed class ClusterConfirmIndicator : Indicator
    {
        private const string IndicatorId = "P2_Cluster";
        private const int LookbackBars = 3;
        private const int AvgVolumePeriod = 20;
        private const int MinBars = AvgVolumePeriod + LookbackBars;

        public ClusterConfirmIndicator()
        {
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            if (bar == CurrentBar - 1) return;
            if (bar < MinBars) return;

            // Cumulative delta over last 3 bars
            decimal sumDelta = 0m;
            bool largeBuyCluster = false;
            bool largeSellCluster = false;

            for (int i = 0; i < LookbackBars; i++)
            {
                var c = GetCandle(bar - i);
                if (c == null) continue;
                sumDelta += c.Delta;

                // Detect large buy cluster: ask volume > bid * 2 (proxy using delta and volume)
                decimal totalVol = c.Volume;
                decimal delta = c.Delta;
                // Approximate: askVol = (totalVol + delta) / 2, bidVol = (totalVol - delta) / 2
                if (totalVol > 0)
                {
                    decimal askVol = (totalVol + delta) / 2m;
                    decimal bidVol = (totalVol - delta) / 2m;
                    if (bidVol > 0 && askVol > bidVol * 2m) largeBuyCluster = true;
                    if (askVol > 0 && bidVol > askVol * 2m) largeSellCluster = true;
                }
            }

            // Average volume over AvgVolumePeriod
            decimal avgVol = 0m;
            for (int i = 0; i < AvgVolumePeriod; i++)
            {
                var c = GetCandle(bar - i);
                if (c != null) avgVol += c.Volume;
            }
            avgVol /= AvgVolumePeriod;

            Vote vote;
            float strength;

            if (sumDelta > 0 && largeBuyCluster)
            {
                vote = Vote.Bull;
            }
            else if (sumDelta < 0 && largeSellCluster)
            {
                vote = Vote.Bear;
            }
            else
            {
                vote = Vote.Neutral;
            }

            float rawStrength = avgVol > 0
                ? Math.Min(1f, (float)(Math.Abs(sumDelta) / (avgVol * LookbackBars)))
                : 0f;
            strength = vote == Vote.Neutral ? 0f : rawStrength;

            SharedStateWriter.Write(new IndicatorState
            {
                IndicatorId = IndicatorId,
                Vote = vote,
                Strength = strength,
                LastUpdate = DateTime.UtcNow,
                Reason = $"SumDelta3={sumDelta:F0} LargeBuy={largeBuyCluster} LargeSell={largeSellCluster}"
            });
        }
    }
}
