using System;
using System.Collections.Generic;
using System.Drawing;
using ATAS.Indicators.DrawingItems;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;
using ATASMultiSignal.Models;

namespace ATASMultiSignal.Rendering
{
    /// <summary>
    /// Draws a fixed-position overlay panel in the top-right corner of the chart
    /// showing each sub-signal's score as a mini bar and the composite score/direction.
    /// </summary>
    public sealed class ScorePanelRenderer
    {
        // ── Layout constants ─────────────────────────────────────────────────────
        private const int PanelWidth = 200;
        private const int PanelHeight = 160;
        private const int Margin = 8;
        private const int RowHeight = 18;
        private const int BarMaxWidth = 60;
        private const int LabelWidth = 80;

        // ── Colours ──────────────────────────────────────────────────────────────
        private static readonly Color BackgroundColor = Color.FromArgb(200, 20, 20, 30);
        private static readonly Color BorderColor = Color.FromArgb(255, 80, 80, 100);
        private static readonly Color TextColor = Color.FromArgb(255, 220, 220, 240);
        private static readonly Color BullishColor = Color.FromArgb(200, 0, 200, 83);
        private static readonly Color BearishColor = Color.FromArgb(200, 229, 57, 53);
        private static readonly Color NeutralColor = Color.FromArgb(200, 120, 120, 140);
        private static readonly Color BuyLabelColor = Color.FromArgb(255, 0, 230, 118);
        private static readonly Color SellLabelColor = Color.FromArgb(255, 255, 82, 82);
        private static readonly Color NeutralLabelColor = Color.FromArgb(255, 180, 180, 200);

        private readonly RenderFont _labelFont = new RenderFont("Consolas", 8f);
        private readonly RenderFont _scoreFont = new RenderFont("Consolas", 9f);
        private readonly RenderFont _directionFont = new RenderFont("Consolas", 11f);

        /// <summary>
        /// Renders the score panel onto <paramref name="context"/>.
        /// Safe to call with a null <paramref name="result"/> — draws an "No Data" placeholder.
        /// </summary>
        public void Draw(RenderContext context, CompositeSignalResult? result, Rectangle chartBounds)
        {
            if (context == null) return;

            // Position: top-right corner
            int x = chartBounds.Right - PanelWidth - Margin;
            int y = chartBounds.Top + Margin;
            var panelRect = new Rectangle(x, y, PanelWidth, PanelHeight);

            // Background
            context.FillRectangle(BackgroundColor, panelRect);
            context.DrawRectangle(new RenderPen(BorderColor), panelRect);

            if (result == null)
            {
                context.DrawString("Multi Signal — No Data", _labelFont, TextColor,
                    new Rectangle(x + Margin, y + Margin, PanelWidth - 2 * Margin, RowHeight));
                return;
            }

            // Header
            int cy = y + Margin;
            context.DrawString("Multi Signal Score", _labelFont, TextColor,
                new Rectangle(x + Margin, cy, PanelWidth - 2 * Margin, RowHeight));
            cy += RowHeight + 2;

            // Separator line
            context.DrawLine(new RenderPen(BorderColor), x + Margin, cy, x + PanelWidth - Margin, cy);
            cy += 4;

            // Individual signal rows
            foreach (var score in result.Scores)
            {
                DrawSignalRow(context, score, x, cy);
                cy += RowHeight;
            }

            // Separator
            cy += 2;
            context.DrawLine(new RenderPen(BorderColor), x + Margin, cy, x + PanelWidth - Margin, cy);
            cy += 4;

            // Composite score bar
            DrawCompositeScore(context, result, x, cy);
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private void DrawSignalRow(RenderContext context, SignalScore score, int panelX, int rowY)
        {
            // Label
            context.DrawString(score.Label, _labelFont, TextColor,
                new Rectangle(panelX + Margin, rowY, LabelWidth, RowHeight));

            // Mini score bar (centred at mid-point)
            int barX = panelX + Margin + LabelWidth;
            int midX = barX + BarMaxWidth / 2;
            int barY = rowY + RowHeight / 2 - 4;
            int barH = 8;

            // Background track
            context.FillRectangle(Color.FromArgb(80, 80, 80, 100),
                new Rectangle(barX, barY, BarMaxWidth, barH));

            // Filled portion
            float clampedVal = Math.Clamp(score.Value, -1f, 1f);
            int fillWidth = (int)(Math.Abs(clampedVal) * (BarMaxWidth / 2f));
            Color fillColor = clampedVal >= 0 ? BullishColor : BearishColor;

            if (clampedVal >= 0)
                context.FillRectangle(fillColor, new Rectangle(midX, barY, fillWidth, barH));
            else
                context.FillRectangle(fillColor, new Rectangle(midX - fillWidth, barY, fillWidth, barH));

            // Centre mark
            context.DrawLine(new RenderPen(BorderColor), midX, barY, midX, barY + barH);

            // Numeric value
            int valX = barX + BarMaxWidth + 4;
            context.DrawString($"{score.Value:+0.00;-0.00}", _scoreFont, TextColor,
                new Rectangle(valX, rowY, 40, RowHeight));
        }

        private void DrawCompositeScore(RenderContext context, CompositeSignalResult result, int panelX, int rowY)
        {
            string dirLabel = result.Direction switch
            {
                SignalDirection.Buy => "▲ BUY",
                SignalDirection.Sell => "▼ SELL",
                _ => "● NEUTRAL"
            };
            Color dirColor = result.Direction switch
            {
                SignalDirection.Buy => BuyLabelColor,
                SignalDirection.Sell => SellLabelColor,
                _ => NeutralLabelColor
            };

            context.DrawString($"Score: {result.NormalizedScore:+0.000;-0.000}", _scoreFont, TextColor,
                new Rectangle(panelX + Margin, rowY, 120, RowHeight));
            rowY += RowHeight;
            context.DrawString(dirLabel, _directionFont, dirColor,
                new Rectangle(panelX + Margin, rowY, PanelWidth - 2 * Margin, RowHeight + 4));
        }
    }
}
