using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ATAS.Indicators;
using System.Drawing;

namespace ATASMultiSignal.Phase2_Confirmation
{
    [DisplayName("EMI - Footprint Confirm Delta (Phase 2)")]
    [Description("Delta + Delta Change sur Footprint 10 Tick Reversal — Déclenchement EMI")]
    public class FootprintConfirmIndicator : Indicator
    {
        [Parameter][Display(Name = "Imbalance Ratio (x)", GroupName = "Footprint")] public double ImbalanceRatio { get; set; } = 3.0;
        [Parameter][Display(Name = "Indicateur ID", GroupName = "Communication")] public string IndicatorId { get; set; } = "FootprintConfirm";

        private readonly ValueDataSeries _delta = new("Delta") { Color = Colors.Cyan, Width = 2 };
        private readonly ValueDataSeries _deltaChange = new("Delta Change") { Color = Colors.Yellow, Width = 1 };
        private readonly ValueDataSeries _volume = new("Volume") { Color = Colors.Gray, Width = 1 };

        private double _prevDelta;

        public FootprintConfirmIndicator() : base(true)
        {
            DataSeries[0] = _delta;
            DataSeries.Add(_deltaChange);
            DataSeries.Add(_volume);
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            var candle = GetCandle(bar);
            double ask = (double)candle.Ask;
            double bid = (double)candle.Bid;
            double delta = ask - bid;
            double deltaChange = delta - _prevDelta;
            double vol = (double)candle.Volume;

            _delta[bar] = (decimal)delta;
            _deltaChange[bar] = (decimal)deltaChange;
            _volume[bar] = (decimal)vol;
            _prevDelta = delta;

            if (bar == CurrentBar - 1)
            {
                // Règle EMI Footprint :
                // Bougie agressive = Zero côté opposé + Delta vif
                bool buyAgressive = bid == 0 && delta > 0;  // Zero côté vendeur
                bool sellAgressive = ask == 0 && delta < 0; // Zero côté acheteur
                bool deltaAccelBull = delta > 0 && deltaChange > 0;
                bool deltaAccelBear = delta < 0 && deltaChange < 0;

                // Détection absorption : delta fort mais price ne progresse pas
                // (approximation : delta change diminue alors que delta est fort)
                bool absorptionBull = delta < 0 && deltaChange > 0;  // vendeurs absorbés
                bool absorptionBear = delta > 0 && deltaChange < 0;  // acheteurs absorbés

                string direction;
                double strength;
                string details;

                if (buyAgressive && deltaAccelBull)
                {
                    direction = "Bull";
                    strength = Math.Min(Math.Abs(delta) / (vol > 0 ? vol : 1), 1.0);
                    details = $"Agressive acheteuse. Delta={delta:F0} DeltaChange={deltaChange:F0}";
                }
                else if (sellAgressive && deltaAccelBear)
                {
                    direction = "Bear";
                    strength = Math.Min(Math.Abs(delta) / (vol > 0 ? vol : 1), 1.0);
                    details = $"Agressive vendeuse. Delta={delta:F0} DeltaChange={deltaChange:F0}";
                }
                else if (absorptionBull)
                {
                    direction = "Bull";
                    strength = 0.6;
                    details = $"Absorption vendeuse. Delta={delta:F0}";
                }
                else if (absorptionBear)
                {
                    direction = "Bear";
                    strength = 0.6;
                    details = $"Absorption acheteuse. Delta={delta:F0}";
                }
                else
                {
                    direction = delta > 0 ? "Bull" : (delta < 0 ? "Bear" : "Neutral");
                    strength = 0.3;
                    details = $"Delta={delta:F0} DeltaChange={deltaChange:F0}";
                }

                Communication.SharedStateWriter.WriteVote(IndicatorId, direction, strength, details);
            }
        }
    }
}
