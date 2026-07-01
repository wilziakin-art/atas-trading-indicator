using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ATAS.Indicators;
using System.Drawing;

namespace ATASMultiSignal.Phase2_Confirmation
{
    [DisplayName("EMI - Cluster Confirm Imbalances (Phase 2)")]
    [Description("Détection imbalances bid/ask et stack imbalances sur Cluster 500T")]
    public class ClusterConfirmIndicator : Indicator
    {
        [Parameter][Display(Name = "Ratio Imbalance (x)", GroupName = "Paramètres")] public double ImbalanceRatio { get; set; } = 3.0;
        [Parameter][Display(Name = "Stack min (niveaux)", GroupName = "Paramètres")] public int StackMinLevels { get; set; } = 2;
        [Parameter][Display(Name = "Indicateur ID", GroupName = "Communication")] public string IndicatorId { get; set; } = "ClusterConfirm";

        private readonly ValueDataSeries _imbalanceScore = new("ImbalanceScore") { Color = System.Drawing.Color.Yellow, Width = 2 };
        private readonly ValueDataSeries _delta = new("Delta") { Color = System.Drawing.Color.Cyan, Width = 1 };

        public ClusterConfirmIndicator() : base(true)
        {
            DataSeries[0] = _imbalanceScore;
            DataSeries.Add(_delta);
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            var candle = GetCandle(bar);
            double ask = (double)candle.Ask;
            double bid = (double)candle.Bid;
            double delta = ask - bid;

            _delta[bar] = (decimal)delta;

            // Détection imbalance sur la bougie courante
            // Ask ≥ 3x Bid → imbalance acheteuse | Bid ≥ 3x Ask → imbalance vendeuse
            bool buyImbalance = bid > 0 && ask >= bid * ImbalanceRatio;
            bool sellImbalance = ask > 0 && bid >= ask * ImbalanceRatio;

            double score = 0;
            if (buyImbalance) score = 1.0;
            else if (sellImbalance) score = -1.0;

            _imbalanceScore[bar] = (decimal)score;

            if (bar == CurrentBar - 1)
            {
                // Vérifier stack sur les N dernières barres
                int stackBuy = 0, stackSell = 0;
                for (int i = bar; i >= Math.Max(0, bar - 5); i--)
                {
                    double v = (double)_imbalanceScore[i];
                    if (v > 0) stackBuy++;
                    else if (v < 0) stackSell++;
                }

                bool stackBuySignal = stackBuy >= StackMinLevels;
                bool stackSellSignal = stackSell >= StackMinLevels;

                string direction;
                double strength;

                if (stackBuySignal && !stackSellSignal)
                {
                    direction = "Bull";
                    strength = Math.Min(stackBuy / 3.0, 1.0);
                }
                else if (stackSellSignal && !stackBuySignal)
                {
                    direction = "Bear";
                    strength = Math.Min(stackSell / 3.0, 1.0);
                }
                else if (buyImbalance)
                {
                    direction = "Bull";
                    strength = 0.5;
                }
                else if (sellImbalance)
                {
                    direction = "Bear";
                    strength = 0.5;
                }
                else
                {
                    direction = "Neutral";
                    strength = 0.1;
                }

                string details = $"Buy Stack={stackBuy} Sell Stack={stackSell} Delta={delta:F0}";
                Communication.SharedStateWriter.WriteVote(IndicatorId, direction, strength, details);
            }
        }
    }
}
