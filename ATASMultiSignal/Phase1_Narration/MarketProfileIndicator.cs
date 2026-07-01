using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ATAS.Indicators;
using System.Drawing;

namespace ATASMultiSignal.Phase1_Narration
{
    [DisplayName("EMI - Market Profile POC/VAH/VAL (Phase 1)")]
    [Description("Market Profile journalier : POC, VAH, VAL pour narration EMI")]
    public class MarketProfileIndicator : Indicator
    {
        [Parameter][Display(Name = "Value Area %", GroupName = "Paramètres")] public double ValueAreaPercent { get; set; } = 70.0;
        [Parameter][Display(Name = "Indicateur ID", GroupName = "Communication")] public string IndicatorId { get; set; } = "MarketProfile";

        private readonly ValueDataSeries _poc = new("POC") { Color = System.Drawing.Color.Yellow, Width = 2 };
        private readonly ValueDataSeries _vah = new("VAH") { Color = System.Drawing.Color.Cyan, Width = 1 };
        private readonly ValueDataSeries _val = new("VAL") { Color = System.Drawing.Color.Cyan, Width = 1 };

        // État session
        private double _sessionPoc, _sessionVah, _sessionVal;
        private double _sessionHigh, _sessionLow;
        private double _totalVolume;
        private double[] _priceVolume = new double[10000];
        private double _tickSize = 0.25;
        private bool _sessionActive;

        public MarketProfileIndicator() : base(true)
        {
            DataSeries[0] = _poc;
            DataSeries.Add(_vah);
            DataSeries.Add(_val);
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            var candle = GetCandle(bar);
            bool newSession = bar == 0 || IsNewSession(bar);

            if (newSession)
            {
                Array.Clear(_priceVolume, 0, _priceVolume.Length);
                _totalVolume = 0;
                _sessionHigh = (double)candle.High;
                _sessionLow = (double)candle.Low;
            }

            _sessionHigh = Math.Max(_sessionHigh, (double)candle.High);
            _sessionLow = Math.Min(_sessionLow, (double)candle.Low);
            _totalVolume += (double)candle.Volume;

            // Distribution volume par niveau de prix (simplifié : prix typique)
            double tp = (double)(candle.High + candle.Low + candle.Close) / 3.0;
            int idx = (int)((tp - _sessionLow) / _tickSize);
            if (idx >= 0 && idx < _priceVolume.Length)
                _priceVolume[idx] += (double)candle.Volume;

            // Calcul POC, VAH, VAL
            CalculateProfileLevels();

            _poc[bar] = (decimal)_sessionPoc;
            _vah[bar] = (decimal)_sessionVah;
            _val[bar] = (decimal)_sessionVal;

            if (bar == CurrentBar - 1)
            {
                double price = (double)candle.Close;
                string direction;
                double strength;

                if (price > _sessionVah)
                {
                    direction = "Bull";
                    strength = 0.8;
                }
                else if (price < _sessionVal)
                {
                    direction = "Bear";
                    strength = 0.8;
                }
                else if (Math.Abs(price - _sessionPoc) < _tickSize * 2)
                {
                    direction = "Neutral";
                    strength = 0.3;
                }
                else
                {
                    direction = price > _sessionPoc ? "Bull" : "Bear";
                    strength = 0.4;
                }

                string details = $"POC={_sessionPoc:F2} VAH={_sessionVah:F2} VAL={_sessionVal:F2}";
                Communication.SharedStateWriter.WriteVote(IndicatorId, direction, strength, details);
            }
        }

        private void CalculateProfileLevels()
        {
            if (_totalVolume == 0) return;

            int range = (int)((_sessionHigh - _sessionLow) / _tickSize) + 1;
            range = Math.Min(range, _priceVolume.Length);

            // POC = index avec le plus de volume
            int pocIdx = 0;
            double maxVol = 0;
            for (int i = 0; i < range; i++)
            {
                if (_priceVolume[i] > maxVol)
                {
                    maxVol = _priceVolume[i];
                    pocIdx = i;
                }
            }
            _sessionPoc = _sessionLow + pocIdx * _tickSize;

            // VAH/VAL : 70% du volume autour du POC
            double targetVol = _totalVolume * (ValueAreaPercent / 100.0);
            double accVol = _priceVolume[pocIdx];
            int upper = pocIdx, lower = pocIdx;

            while (accVol < targetVol && (upper < range - 1 || lower > 0))
            {
                double upAdd = upper < range - 1 ? _priceVolume[upper + 1] : 0;
                double dnAdd = lower > 0 ? _priceVolume[lower - 1] : 0;

                if (upAdd >= dnAdd && upper < range - 1)
                    accVol += _priceVolume[++upper];
                else if (lower > 0)
                    accVol += _priceVolume[--lower];
                else
                    break;
            }

            _sessionVah = _sessionLow + upper * _tickSize;
            _sessionVal = _sessionLow + lower * _tickSize;
        }

        private bool IsNewSession(int bar)
        {
            if (bar == 0) return false;
            return GetCandle(bar).Time.Date != GetCandle(bar - 1).Time.Date;
        }
    }
}
