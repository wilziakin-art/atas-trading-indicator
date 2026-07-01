using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ATAS.Indicators;
using ATAS.Indicators.Drawing;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;
using System.Drawing;

namespace ATASMultiSignal.Phase3_Execution
{
    [DisplayName("EMI - Execution Master (Phase 3)")]
    [Description("Indicateur maître EMI : agrège votes Phase 1 + Phase 2, affiche signal final")]
    public class ExecutionIndicator : Indicator
    {
        // IDs des votes à lire
        [Parameter][Display(Name = "ID VWAP", GroupName = "Phase 1 IDs")] public string IdVwap { get; set; } = "VwapNarration";
        [Parameter][Display(Name = "ID MA NQ", GroupName = "Phase 1 IDs")] public string IdMaNq { get; set; } = "MaNarration_NQ";
        [Parameter][Display(Name = "ID Market Profile", GroupName = "Phase 1 IDs")] public string IdMp { get; set; } = "MarketProfile";
        [Parameter][Display(Name = "ID HA Confirm", GroupName = "Phase 2 IDs")] public string IdHa { get; set; } = "HeikinAshiConfirm";
        [Parameter][Display(Name = "ID Cluster", GroupName = "Phase 2 IDs")] public string IdCluster { get; set; } = "ClusterConfirm";
        [Parameter][Display(Name = "ID MACD CCI", GroupName = "Phase 2 IDs")] public string IdMacdCci { get; set; } = "MacdCciConfirm";
        [Parameter][Display(Name = "ID Footprint", GroupName = "Phase 2 IDs")] public string IdFootprint { get; set; } = "FootprintConfirm";
        [Parameter][Display(Name = "Score min Phase 1 (/3)", GroupName = "Filtres")] public int MinPhase1Score { get; set; } = 2;
        [Parameter][Display(Name = "Score min Phase 2 (/4)", GroupName = "Filtres")] public int MinPhase2Score { get; set; } = 3;

        // Flèches de signal
        private readonly ValueDataSeries _signalUp = new("Signal UP") { Color = System.Drawing.Color.Lime, Width = 3, ShowZeroValue = false };
        private readonly ValueDataSeries _signalDown = new("Signal DOWN") { Color = System.Drawing.Color.Red, Width = 3, ShowZeroValue = false };
        private readonly ValueDataSeries _scoreDisplay = new("Score") { Color = System.Drawing.Color.Yellow, Width = 1 };

        private string _lastSignal = "Neutral";
        private double _lastScore = 0;
        private string _lastDetails = "";

        public ExecutionIndicator() : base(true)
        {
            DataSeries[0] = _signalUp;
            DataSeries.Add(_signalDown);
            DataSeries.Add(_scoreDisplay);
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            _signalUp[bar] = decimal.MinValue;
            _signalDown[bar] = decimal.MinValue;
            _scoreDisplay[bar] = 0;

            if (bar != CurrentBar - 1) return;

            // Lecture des votes
            var state = Communication.SharedStateReader.ReadState();
            if (state == null) return;

            // Phase 1
            int p1Bull = 0, p1Bear = 0;
            double p1Strength = 0;
            string[] p1Ids = { IdVwap, IdMaNq, IdMp };
            foreach (var id in p1Ids)
            {
                if (state.Indicators.TryGetValue(id, out var vote))
                {
                    if (vote.Direction == "Bull") { p1Bull++; p1Strength += vote.Strength; }
                    else if (vote.Direction == "Bear") { p1Bear++; p1Strength += vote.Strength; }
                }
            }

            // Phase 2
            int p2Bull = 0, p2Bear = 0;
            double p2Strength = 0;
            string[] p2Ids = { IdHa, IdCluster, IdMacdCci, IdFootprint };
            foreach (var id in p2Ids)
            {
                if (state.Indicators.TryGetValue(id, out var vote))
                {
                    if (vote.Direction == "Bull") { p2Bull++; p2Strength += vote.Strength; }
                    else if (vote.Direction == "Bear") { p2Bear++; p2Strength += vote.Strength; }
                }
            }

            var candle = GetCandle(bar);
            double price = (double)candle.Close;

            // Signal EMI : Phase 1 ET Phase 2 alignées
            bool bullSignal = p1Bull >= MinPhase1Score && p2Bull >= MinPhase2Score;
            bool bearSignal = p1Bear >= MinPhase1Score && p2Bear >= MinPhase2Score;

            double totalScore = 0;
            if (bullSignal)
            {
                totalScore = (p1Bull + p2Bull) / 7.0;
                _signalUp[bar] = (decimal)(price - (double)(candle.High - candle.Low) * 0.5);
                _lastSignal = "Bull";
                _lastScore = totalScore;
                _lastDetails = $"P1={p1Bull}/3 P2={p2Bull}/4 Force={totalScore:P0}";
            }
            else if (bearSignal)
            {
                totalScore = (p1Bear + p2Bear) / 7.0;
                _signalDown[bar] = (decimal)(price + (double)(candle.High - candle.Low) * 0.5);
                _lastSignal = "Bear";
                _lastScore = totalScore;
                _lastDetails = $"P1={p1Bear}/3 P2={p2Bear}/4 Force={totalScore:P0}";
            }
            else
            {
                _lastSignal = "Neutral";
                _lastScore = 0;
                _lastDetails = $"P1 Bull={p1Bull} Bear={p1Bear} | P2 Bull={p2Bull} Bear={p2Bear}";
            }

            _scoreDisplay[bar] = (decimal)totalScore;
        }

        public override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (layout != DrawingLayouts.Final) return;

            // Panneau de score en haut à gauche
            var font = new RenderFont("Arial", 11);
            var colorSignal = _lastSignal == "Bull" ? System.Drawing.Color.Lime : (_lastSignal == "Bear" ? System.Drawing.Color.Red : System.Drawing.Color.Gray);
            var bg = Color.FromArgb(180, 0, 0, 0);

            int x = 10, y = 10;
            context.FillRectangle(bg, new System.Drawing.Rectangle(x - 5, y - 5, 300, 70));
            context.DrawString($"EMI Signal : {_lastSignal}", font, colorSignal, x, y);
            context.DrawString($"Score : {_lastScore:P0}", font, System.Drawing.Color.White, x, y + 18);
            context.DrawString(_lastDetails, new RenderFont("Arial", 9), System.Drawing.Color.LightGray, x, y + 36);
        }
    }
}
