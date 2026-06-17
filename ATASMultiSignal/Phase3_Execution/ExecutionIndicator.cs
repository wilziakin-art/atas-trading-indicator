using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using ATAS.Indicators;
using ATASMultiSignal.Alerts;
using ATASMultiSignal.Communication;
using ATASMultiSignal.Models;
using ATASMultiSignal.Phase3_Execution.ExitStrategy;
using OFT.Rendering.Context;
using OFT.Rendering.Enums;

namespace ATASMultiSignal.Phase3_Execution
{
    [DisplayName("P3 - Execution Master")]
    [Category("ATASMultiSignal - Phase 3")]
    [Description("Reads Phase 1 and Phase 2 votes and generates BUY/SELL signals on the execution chart. Exit based on DOM sweep + MACD + CCI.")]
    public sealed class ExecutionIndicator : Indicator
    {
        // ── Parameters ────────────────────────────────────────────────────────────

        [Parameter] [DisplayName("Min Phase1 Strength")] public float MinPhase1Strength { get; set; } = 0.5f;
        [Parameter] [DisplayName("Min Phase2 Strength")] public float MinPhase2Strength { get; set; } = 0.5f;
        [Parameter] [DisplayName("Sound Alerts")]        public bool SoundAlerts         { get; set; } = true;

        // ── Series ────────────────────────────────────────────────────────────────

        private readonly ValueDataSeries _entryBuy;
        private readonly ValueDataSeries _entrySell;
        private readonly ValueDataSeries _exitMarker;

        // ── Components ────────────────────────────────────────────────────────────

        private readonly ExitSignalEvaluator _exitEval = new();
        private readonly AlertManager _alerts = new();

        // ── State ─────────────────────────────────────────────────────────────────

        private SignalDirection _position = SignalDirection.None;
        private SignalDirection _lastAlertDir = SignalDirection.None;
        private PhaseResult _latestPhase = new();
        private (bool dom, bool macd, bool cci) _exitFactors;

        public ExecutionIndicator()
        {
            _entryBuy = new ValueDataSeries("Entry BUY")
            {
                Color = Color.FromArgb(255, 0, 200, 83),
                VisualType = VisualMode.UpArrow,
                Width = 3,
                ShowZeroLine = false
            };
            _entrySell = new ValueDataSeries("Entry SELL")
            {
                Color = Color.FromArgb(255, 229, 57, 53),
                VisualType = VisualMode.DownArrow,
                Width = 3,
                ShowZeroLine = false
            };
            _exitMarker = new ValueDataSeries("Exit")
            {
                Color = Color.FromArgb(255, 255, 200, 0),
                VisualType = VisualMode.Dot,
                Width = 2,
                ShowZeroLine = false
            };
            DataSeries.Add(_entryBuy);
            DataSeries.Add(_entrySell);
            DataSeries.Add(_exitMarker);
        }

        protected override void OnRecalculate()
        {
            _position = SignalDirection.None;
            _lastAlertDir = SignalDirection.None;
            _alerts.SoundEnabled = SoundAlerts;
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            _entryBuy[bar] = double.NaN;
            _entrySell[bar] = double.NaN;
            _exitMarker[bar] = double.NaN;

            bool isForming = bar == CurrentBar - 1;

            // Re-read shared state on every bar (cheap file read cached by OS)
            var states = SharedStateReader.ReadAll();
            _latestPhase = PhaseAggregator.Aggregate(states);

            var candle = GetCandle(bar);
            decimal priceOffset = (candle.High - candle.Low) * 0.5m;

            // Check exit first if in a position
            if (_position != SignalDirection.None && !isForming)
            {
                var exitReason = _exitEval.Evaluate(bar, _position, GetCandle);
                _exitFactors = _exitEval.GetActiveFactors(bar, _position, GetCandle);

                if (exitReason != ExitReason.None)
                {
                    _exitMarker[bar] = (double)candle.Close;
                    _alerts.TryAlert(bar, SignalDirection.None, _position);
                    _position = SignalDirection.None;
                    return;
                }
            }

            // Entry logic: both phases must be aligned and strong enough
            if (_position == SignalDirection.None
                && _latestPhase.IsAligned
                && _latestPhase.Phase1Strength >= MinPhase1Strength
                && _latestPhase.Phase2Strength >= MinPhase2Strength
                && !isForming)
            {
                if (_latestPhase.Phase1Vote == Vote.Bull)
                {
                    _position = SignalDirection.Buy;
                    _entryBuy[bar] = (double)(candle.Low - priceOffset);
                }
                else if (_latestPhase.Phase1Vote == Vote.Bear)
                {
                    _position = SignalDirection.Sell;
                    _entrySell[bar] = (double)(candle.High + priceOffset);
                }

                if (_position != SignalDirection.None)
                    _alerts.TryAlert(bar, _position, _lastAlertDir);
                _lastAlertDir = _position;
            }
        }

        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (layout != DrawingLayouts.Final) return;
            DrawPanel(context);
        }

        private void DrawPanel(RenderContext context)
        {
            var bounds = ChartInfo.PaneBounds;
            int x = bounds.Right - 230;
            int y = bounds.Top + 10;
            int w = 220;
            int rowH = 18;
            int rows = 14;
            int h = rows * rowH + 16;

            var bg = Color.FromArgb(210, 15, 15, 25);
            var border = Color.FromArgb(255, 60, 60, 90);
            var textColor = Color.FromArgb(255, 210, 210, 230);
            var bull = Color.FromArgb(255, 0, 200, 83);
            var bear = Color.FromArgb(255, 229, 57, 53);
            var neutral = Color.FromArgb(255, 160, 160, 180);
            var yellow = Color.FromArgb(255, 255, 215, 0);

            context.FillRectangle(bg, new Rectangle(x, y, w, h));
            context.DrawRectangle(new RenderPen(border), new Rectangle(x, y, w, h));

            var font = new RenderFont("Consolas", 8.5f);
            var fontBold = new RenderFont("Consolas", 10f);

            int cy = y + 8;
            context.DrawString("EXECUTION PANEL", fontBold, yellow, new Rectangle(x + 8, cy, w - 16, rowH));
            cy += rowH + 2;
            context.DrawLine(new RenderPen(border), x + 6, cy, x + w - 6, cy);
            cy += 4;

            // Phase 1
            Color p1Color = _latestPhase.Phase1Vote == Vote.Bull ? bull : _latestPhase.Phase1Vote == Vote.Bear ? bear : neutral;
            string p1Text = $"Phase1: {_latestPhase.Phase1Vote} ({_latestPhase.Phase1Strength:P0})";
            context.DrawString(p1Text, font, p1Color, new Rectangle(x + 8, cy, w - 16, rowH));
            cy += rowH;

            // Phase 2
            Color p2Color = _latestPhase.Phase2Vote == Vote.Bull ? bull : _latestPhase.Phase2Vote == Vote.Bear ? bear : neutral;
            string p2Text = $"Phase2: {_latestPhase.Phase2Vote} ({_latestPhase.Phase2Strength:P0})";
            context.DrawString(p2Text, font, p2Color, new Rectangle(x + 8, cy, w - 16, rowH));
            cy += rowH;

            // Alignment
            string alignText = _latestPhase.IsAligned ? "✓ ALIGNED" : "✗ Not aligned";
            Color alignColor = _latestPhase.IsAligned ? bull : neutral;
            context.DrawString(alignText, font, alignColor, new Rectangle(x + 8, cy, w - 16, rowH));
            cy += rowH + 2;

            context.DrawLine(new RenderPen(border), x + 6, cy, x + w - 6, cy);
            cy += 4;

            // Position
            string posText = _position switch
            {
                SignalDirection.Buy  => "▲ LONG",
                SignalDirection.Sell => "▼ SHORT",
                _                   => "— FLAT"
            };
            Color posColor = _position == SignalDirection.Buy ? bull : _position == SignalDirection.Sell ? bear : neutral;
            context.DrawString($"Position: {posText}", fontBold, posColor, new Rectangle(x + 8, cy, w - 16, rowH + 2));
            cy += rowH + 4;

            context.DrawLine(new RenderPen(border), x + 6, cy, x + w - 6, cy);
            cy += 4;

            // Exit factors
            context.DrawString("Exit Signals:", font, textColor, new Rectangle(x + 8, cy, w - 16, rowH));
            cy += rowH;
            DrawFactor(context, font, "  DOM Sweep", _exitFactors.dom, x, cy, w, rowH, bull, bear);  cy += rowH;
            DrawFactor(context, font, "  MACD 30s",  _exitFactors.macd, x, cy, w, rowH, bull, bear); cy += rowH;
            DrawFactor(context, font, "  CCI 30s",   _exitFactors.cci,  x, cy, w, rowH, bull, bear);
        }

        private static void DrawFactor(RenderContext ctx, RenderFont font, string label, bool active,
            int x, int cy, int w, int rowH, Color activeColor, Color inactiveColor)
        {
            string mark = active ? "✓" : "✗";
            Color c = active ? activeColor : Color.FromArgb(180, 160, 160, 160);
            ctx.DrawString($"{label}: {mark}", font, c, new Rectangle(x + 8, cy, w - 16, rowH));
        }
    }
}
