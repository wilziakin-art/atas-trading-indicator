using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ATAS.Indicators;
using ATAS.Indicators.Drawing;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;
using System.Drawing;

namespace ATASMultiSignal.Phase1_Narration
{
    [DisplayName("EMI - VWAP Narration (Phase 1)")]
    [Description("VWAP + SD1-SD5 avec détection séquence EMI")]
    public class VwapNarrationIndicator : Indicator
    {
        // Paramètres
        [Parameter][Display(Name = "Afficher SD1", GroupName = "Standard Deviations")] public bool ShowSd1 { get; set; } = true;
        [Parameter][Display(Name = "Afficher SD2", GroupName = "Standard Deviations")] public bool ShowSd2 { get; set; } = true;
        [Parameter][Display(Name = "Afficher SD3", GroupName = "Standard Deviations")] public bool ShowSd3 { get; set; } = true;
        [Parameter][Display(Name = "Afficher SD4", GroupName = "Standard Deviations")] public bool ShowSd4 { get; set; } = false;
        [Parameter][Display(Name = "Afficher SD5", GroupName = "Standard Deviations")] public bool ShowSd5 { get; set; } = false;
        [Parameter][Display(Name = "Indicateur ID", GroupName = "Communication")] public string IndicatorId { get; set; } = "VwapNarration";

        // Series
        private readonly ValueDataSeries _vwap = new("VWAP") { Color = System.Drawing.Color.White, Width = 2 };
        private readonly ValueDataSeries _sd1Up = new("SD+1") { Color = System.Drawing.Color.DodgerBlue, Width = 1 };
        private readonly ValueDataSeries _sd1Dn = new("SD-1") { Color = System.Drawing.Color.DodgerBlue, Width = 1 };
        private readonly ValueDataSeries _sd2Up = new("SD+2") { Color = System.Drawing.Color.Orange, Width = 1 };
        private readonly ValueDataSeries _sd2Dn = new("SD-2") { Color = System.Drawing.Color.Orange, Width = 1 };
        private readonly ValueDataSeries _sd3Up = new("SD+3") { Color = System.Drawing.Color.Red, Width = 1 };
        private readonly ValueDataSeries _sd3Dn = new("SD-3") { Color = System.Drawing.Color.Red, Width = 1 };
        private readonly ValueDataSeries _sd4Up = new("SD+4") { Color = System.Drawing.Color.Magenta, Width = 1 };
        private readonly ValueDataSeries _sd4Dn = new("SD-4") { Color = System.Drawing.Color.Magenta, Width = 1 };
        private readonly ValueDataSeries _sd5Up = new("SD+5") { Color = System.Drawing.Color.DarkRed, Width = 1 };
        private readonly ValueDataSeries _sd5Dn = new("SD-5") { Color = System.Drawing.Color.DarkRed, Width = 1 };

        // Calcul VWAP
        private double _cumVolumePrice;
        private double _cumVolume;
        private double _cumVolumePrice2;
        private int _sessionStart;
        private int _lastBar = -1;

        public VwapNarrationIndicator() : base(true)
        {
            DataSeries[0] = _vwap;
            DataSeries.Add(_sd1Up);
            DataSeries.Add(_sd1Dn);
            DataSeries.Add(_sd2Up);
            DataSeries.Add(_sd2Dn);
            DataSeries.Add(_sd3Up);
            DataSeries.Add(_sd3Dn);
            DataSeries.Add(_sd4Up);
            DataSeries.Add(_sd4Dn);
            DataSeries.Add(_sd5Up);
            DataSeries.Add(_sd5Dn);
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            var candle = GetCandle(bar);

            // Réinitialisation session journalière
            if (bar == 0 || IsNewSession(bar))
            {
                _cumVolumePrice = 0;
                _cumVolume = 0;
                _cumVolumePrice2 = 0;
                _sessionStart = bar;
            }

            double typicalPrice = (double)(candle.High + candle.Low + candle.Close) / 3.0;
            double vol = (double)candle.Volume;

            _cumVolumePrice += typicalPrice * vol;
            _cumVolume += vol;
            _cumVolumePrice2 += typicalPrice * typicalPrice * vol;

            double vwap = _cumVolume > 0 ? _cumVolumePrice / _cumVolume : typicalPrice;
            double variance = _cumVolume > 0 ? (_cumVolumePrice2 / _cumVolume) - (vwap * vwap) : 0;
            double stdDev = variance > 0 ? Math.Sqrt(variance) : 0;

            _vwap[bar] = (decimal)vwap;
            _sd1Up[bar] = ShowSd1 ? (decimal)(vwap + 1 * stdDev) : decimal.MinValue;
            _sd1Dn[bar] = ShowSd1 ? (decimal)(vwap - 1 * stdDev) : decimal.MinValue;
            _sd2Up[bar] = ShowSd2 ? (decimal)(vwap + 2 * stdDev) : decimal.MinValue;
            _sd2Dn[bar] = ShowSd2 ? (decimal)(vwap - 2 * stdDev) : decimal.MinValue;
            _sd3Up[bar] = ShowSd3 ? (decimal)(vwap + 3 * stdDev) : decimal.MinValue;
            _sd3Dn[bar] = ShowSd3 ? (decimal)(vwap - 3 * stdDev) : decimal.MinValue;
            _sd4Up[bar] = ShowSd4 ? (decimal)(vwap + 4 * stdDev) : decimal.MinValue;
            _sd4Dn[bar] = ShowSd4 ? (decimal)(vwap - 4 * stdDev) : decimal.MinValue;
            _sd5Up[bar] = ShowSd5 ? (decimal)(vwap + 5 * stdDev) : decimal.MinValue;
            _sd5Dn[bar] = ShowSd5 ? (decimal)(vwap - 5 * stdDev) : decimal.MinValue;

            // Vote EMI sur la dernière barre
            if (bar == CurrentBar - 1)
            {
                double price = (double)candle.Close;
                string direction;
                double strength;
                string details;

                // Détection séquence EMI
                // Rythme = position dans les SD
                double sdRatio = stdDev > 0 ? Math.Abs(price - vwap) / stdDev : 0;

                if (price > vwap + stdDev) // Au-dessus SD1 → déséquilibre haussier
                {
                    direction = "Bull";
                    strength = Math.Min(sdRatio / 3.0, 1.0);
                    details = $"Prix hors DVA haussier. SD={sdRatio:F2}. VWAP={vwap:F2}";
                }
                else if (price < vwap - stdDev) // En-dessous SD1 → déséquilibre baissier
                {
                    direction = "Bear";
                    strength = Math.Min(sdRatio / 3.0, 1.0);
                    details = $"Prix hors DVA baissier. SD={sdRatio:F2}. VWAP={vwap:F2}";
                }
                else // Dans la DVA → équilibre
                {
                    direction = price > vwap ? "Bull" : "Bear";
                    strength = 0.3;
                    details = $"Prix dans DVA. SD={sdRatio:F2}. VWAP={vwap:F2}";
                }

                Communication.SharedStateWriter.WriteVote(IndicatorId, direction, strength, details);
            }
        }

        private bool IsNewSession(int bar)
        {
            if (bar == 0) return false;
            var prev = GetCandle(bar - 1);
            var curr = GetCandle(bar);
            return curr.Time.Date != prev.Time.Date;
        }
    }
}
