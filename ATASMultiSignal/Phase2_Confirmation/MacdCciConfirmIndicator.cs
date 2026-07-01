using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ATAS.Indicators;
using System.Drawing;

namespace ATASMultiSignal.Phase2_Confirmation
{
    [DisplayName("EMI - MACD + CCI 10s Confirm (Phase 2)")]
    [Description("MACD(26/12/9) + CCI(10) sur graphique 10 secondes — Timing d'exécution EMI")]
    public class MacdCciConfirmIndicator : Indicator
    {
        [Parameter][Display(Name = "MACD Fast", GroupName = "MACD")] public int MacdFast { get; set; } = 12;
        [Parameter][Display(Name = "MACD Slow", GroupName = "MACD")] public int MacdSlow { get; set; } = 26;
        [Parameter][Display(Name = "MACD Signal", GroupName = "MACD")] public int MacdSignal { get; set; } = 9;
        [Parameter][Display(Name = "CCI Période", GroupName = "CCI")] public int CciPeriod { get; set; } = 10;
        [Parameter][Display(Name = "Indicateur ID", GroupName = "Communication")] public string IndicatorId { get; set; } = "MacdCciConfirm";

        private readonly ValueDataSeries _macdHist = new("MACD Histo") { Color = Colors.DodgerBlue, Width = 2 };
        private readonly ValueDataSeries _cci = new("CCI") { Color = Colors.Yellow, Width = 1 };
        private readonly ValueDataSeries _macdLine = new("MACD") { Color = Colors.Cyan, Width = 1 };
        private readonly ValueDataSeries _signal = new("Signal") { Color = Colors.Red, Width = 1 };

        private double _emaF, _emaS, _emaSig;
        private bool _init;

        public MacdCciConfirmIndicator() : base(true)
        {
            DataSeries[0] = _macdHist;
            DataSeries.Add(_cci);
            DataSeries.Add(_macdLine);
            DataSeries.Add(_signal);
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            var candle = GetCandle(bar);
            double close = (double)candle.Close;

            if (!_init) { _emaF = close; _emaS = close; _emaSig = 0; _init = true; }

            // MACD
            double kf = 2.0 / (MacdFast + 1);
            double ks = 2.0 / (MacdSlow + 1);
            double kg = 2.0 / (MacdSignal + 1);
            _emaF = close * kf + _emaF * (1 - kf);
            _emaS = close * ks + _emaS * (1 - ks);
            double macd = _emaF - _emaS;
            _emaSig = macd * kg + _emaSig * (1 - kg);
            double hist = macd - _emaSig;

            _macdLine[bar] = (decimal)macd;
            _signal[bar] = (decimal)_emaSig;
            _macdHist[bar] = (decimal)hist;

            // CCI
            double cci = CalculateCCI(bar, CciPeriod);
            _cci[bar] = (decimal)cci;

            if (bar == CurrentBar - 1)
            {
                // Règle EMI 10s : MACD = filtre, CCI = timing
                // Le 10s ne décide JAMAIS du sens seul
                bool macdBull = hist > 0 && macd > _emaSig;
                bool macdBear = hist < 0 && macd < _emaSig;
                bool cciBull = cci > 0;
                bool cciBear = cci < 0;

                string direction;
                double strength;

                if (macdBull && cciBull)
                {
                    direction = "Bull";
                    strength = Math.Min(Math.Abs(cci) / 200.0, 1.0);
                }
                else if (macdBear && cciBear)
                {
                    direction = "Bear";
                    strength = Math.Min(Math.Abs(cci) / 200.0, 1.0);
                }
                else
                {
                    direction = "Neutral";
                    strength = 0.1;
                }

                string details = $"MACD Histo={hist:F4} CCI={cci:F1}";
                Communication.SharedStateWriter.WriteVote(IndicatorId, direction, strength, details);
            }
        }

        private double CalculateCCI(int bar, int period)
        {
            int start = Math.Max(0, bar - period + 1);
            int count = bar - start + 1;
            double sum = 0;
            for (int i = start; i <= bar; i++)
            {
                var c = GetCandle(i);
                sum += (double)(c.High + c.Low + c.Close) / 3.0;
            }
            double meanTp = sum / count;
            double meanDev = 0;
            for (int i = start; i <= bar; i++)
            {
                var c = GetCandle(i);
                double tp = (double)(c.High + c.Low + c.Close) / 3.0;
                meanDev += Math.Abs(tp - meanTp);
            }
            meanDev /= count;
            var curr = GetCandle(bar);
            double currTp = (double)(curr.High + curr.Low + curr.Close) / 3.0;
            return meanDev > 0 ? (currTp - meanTp) / (0.015 * meanDev) : 0;
        }
    }
}
