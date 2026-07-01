using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ATAS.Indicators;
using System.Drawing;

namespace ATASMultiSignal.Phase1_Narration
{
    [DisplayName("EMI - MA Narration 3 EMA (Phase 1)")]
    [Description("3 EMA (9/45/135) pour narration tendance — NQ/SP/DJI")]
    public class MaNarrationIndicator : Indicator
    {
        [Parameter][Display(Name = "EMA Rapide", GroupName = "Paramètres")] public int FastPeriod { get; set; } = 9;
        [Parameter][Display(Name = "EMA Moyenne", GroupName = "Paramètres")] public int MidPeriod { get; set; } = 45;
        [Parameter][Display(Name = "EMA Lente", GroupName = "Paramètres")] public int SlowPeriod { get; set; } = 135;
        [Parameter][Display(Name = "Indicateur ID", GroupName = "Communication")] public string IndicatorId { get; set; } = "MaNarration_NQ";

        private readonly ValueDataSeries _emaFast = new("EMA9") { Color = Colors.Lime, Width = 1 };
        private readonly ValueDataSeries _emaMid = new("EMA45") { Color = Colors.Yellow, Width = 2 };
        private readonly ValueDataSeries _emaSlow = new("EMA135") { Color = Colors.Red, Width = 2 };

        private double _eFast, _eMid, _eSlow;
        private bool _initialized;

        public MaNarrationIndicator() : base(true)
        {
            DataSeries[0] = _emaFast;
            DataSeries.Add(_emaMid);
            DataSeries.Add(_emaSlow);
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            var close = (double)GetCandle(bar).Close;

            if (!_initialized)
            {
                _eFast = close;
                _eMid = close;
                _eSlow = close;
                _initialized = true;
            }

            double kFast = 2.0 / (FastPeriod + 1);
            double kMid = 2.0 / (MidPeriod + 1);
            double kSlow = 2.0 / (SlowPeriod + 1);

            _eFast = close * kFast + _eFast * (1 - kFast);
            _eMid = close * kMid + _eMid * (1 - kMid);
            _eSlow = close * kSlow + _eSlow * (1 - kSlow);

            _emaFast[bar] = (decimal)_eFast;
            _emaMid[bar] = (decimal)_eMid;
            _emaSlow[bar] = (decimal)_eSlow;

            if (bar == CurrentBar - 1)
            {
                // Vote EMI : les 3 EMA doivent être alignées
                bool bullish = _eFast > _eMid && _eMid > _eSlow;
                bool bearish = _eFast < _eMid && _eMid < _eSlow;
                double spread = Math.Abs(_eFast - _eSlow);
                double strength = Math.Min(spread / (close * 0.001), 1.0);

                string direction = bullish ? "Bull" : (bearish ? "Bear" : "Neutral");
                string details = $"Fast={_eFast:F2} Mid={_eMid:F2} Slow={_eSlow:F2}";
                Communication.SharedStateWriter.WriteVote(IndicatorId, direction, bullish || bearish ? strength : 0.1, details);
            }
        }
    }
}
