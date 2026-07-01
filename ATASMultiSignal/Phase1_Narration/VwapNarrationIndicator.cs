using System;
using System.ComponentModel;
using System.Drawing;
using ATAS.Indicators;
using ATASMultiSignal.Communication;
using OFT.Rendering.Context;
using OFT.Rendering.Enums;

namespace ATASMultiSignal.Phase1_Narration
{
    // Méthodologie Easy Money Invest (EMI) — VWAP + 5 SD
    // Séquences : Équilibrée / Déséquilibrée / Reversale / Breakout
    // Rythmes   : Slow / Normal / Fast / Speed Trend
    // Vote Phase 1 : Bull/Bear selon séquence + position SD + rythme

    public enum VwapSequence { Equilibree, Desequilibree, Reversale, Breakout, Inconnue }
    public enum VwapRythme   { Slow, Normal, Fast, Speed }

    [DisplayName("P1 - VWAP Narration (EMI)")]
    [Category("ATASMultiSignal - Phase 1")]
    [Description("VWAP journalière + 5 SD (méthode Easy Money Invest). Détecte séquence et rythme de marché.")]
    public sealed class VwapNarrationIndicator : Indicator
    {
        private const string IndicatorId = "P1_VWAP";
        private const int MinBars = 30;

        // ── Paramètres ──────────────────────────────────────────────────────────

        [Parameter] [DisplayName("Reset quotidien")]  public bool DailyReset   { get; set; } = true;
        [Parameter] [DisplayName("Nb barres slope")]  public int  SlopeLookback { get; set; } = 10;
        [Parameter] [DisplayName("Nb barres rythme")] public int  RythmeLookback { get; set; } = 5;
        [Parameter] [DisplayName("Afficher SD1")]     public bool ShowSd1 { get; set; } = true;
        [Parameter] [DisplayName("Afficher SD2")]     public bool ShowSd2 { get; set; } = true;
        [Parameter] [DisplayName("Afficher SD3")]     public bool ShowSd3 { get; set; } = true;

        // ── Séries ──────────────────────────────────────────────────────────────

        private readonly ValueDataSeries _vwap;
        private readonly ValueDataSeries _sd1Up, _sd1Dn;
        private readonly ValueDataSeries _sd2Up, _sd2Dn;
        private readonly ValueDataSeries _sd3Up, _sd3Dn;

        // SD4 et SD5 en lignes fines (zones climax)
        private readonly ValueDataSeries _sd4Up, _sd4Dn;
        private readonly ValueDataSeries _sd5Up, _sd5Dn;

        // ── État interne ────────────────────────────────────────────────────────

        private VwapSequence _lastSequence = VwapSequence.Inconnue;
        private VwapRythme   _lastRythme   = VwapRythme.Normal;
        private bool         _haussier     = true;

        public VwapNarrationIndicator()
        {
            _vwap   = MakeLine("VWAP",  Color.Yellow,    2, VisualMode.Line);
            _sd1Up  = MakeLine("SD+1",  Color.Cyan,      1, VisualMode.Line);
            _sd1Dn  = MakeLine("SD-1",  Color.Cyan,      1, VisualMode.Line);
            _sd2Up  = MakeLine("SD+2",  Color.Orange,    1, VisualMode.Line);
            _sd2Dn  = MakeLine("SD-2",  Color.Orange,    1, VisualMode.Line);
            _sd3Up  = MakeLine("SD+3",  Color.OrangeRed, 1, VisualMode.Line);
            _sd3Dn  = MakeLine("SD-3",  Color.OrangeRed, 1, VisualMode.Line);
            _sd4Up  = MakeLine("SD+4",  Color.Red,       1, VisualMode.Line);
            _sd4Dn  = MakeLine("SD-4",  Color.Red,       1, VisualMode.Line);
            _sd5Up  = MakeLine("SD+5",  Color.DarkRed,   1, VisualMode.Line);
            _sd5Dn  = MakeLine("SD-5",  Color.DarkRed,   1, VisualMode.Line);

            DataSeries.Add(_vwap);
            DataSeries.Add(_sd1Up); DataSeries.Add(_sd1Dn);
            DataSeries.Add(_sd2Up); DataSeries.Add(_sd2Dn);
            DataSeries.Add(_sd3Up); DataSeries.Add(_sd3Dn);
            DataSeries.Add(_sd4Up); DataSeries.Add(_sd4Dn);
            DataSeries.Add(_sd5Up); DataSeries.Add(_sd5Dn);
        }

        private static ValueDataSeries MakeLine(string name, Color color, int width, VisualMode mode)
            => new(name) { Color = color, Width = width, VisualType = mode, ShowZeroLine = false };

        protected override void OnRecalculate()
        {
            _lastSequence = VwapSequence.Inconnue;
            _lastRythme   = VwapRythme.Normal;
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            var candle = GetCandle(bar);
            if (candle == null) return;

            // ── 1. Calcul VWAP + Variance pour SD ───────────────────────────────

            var (vwap, stdDev) = CalculateVwapAndStdDev(bar);

            _vwap[bar]   = vwap;
            _sd1Up[bar]  = ShowSd1 ? vwap + 1m * stdDev : double.NaN;
            _sd1Dn[bar]  = ShowSd1 ? vwap - 1m * stdDev : double.NaN;
            _sd2Up[bar]  = ShowSd2 ? vwap + 2m * stdDev : double.NaN;
            _sd2Dn[bar]  = ShowSd2 ? vwap - 2m * stdDev : double.NaN;
            _sd3Up[bar]  = ShowSd3 ? vwap + 3m * stdDev : double.NaN : double.NaN;
            _sd4Up[bar]  = vwap + 4m * stdDev;
            _sd4Dn[bar]  = vwap - 4m * stdDev;
            _sd5Up[bar]  = vwap + 5m * stdDev;
            _sd5Dn[bar]  = vwap - 5m * stdDev;

            if (bar < MinBars || bar == CurrentBar - 1) return;

            // ── 2. Position du prix dans les SD ──────────────────────────────────

            decimal close = candle.Close;
            decimal sd1U = vwap + stdDev;
            decimal sd1D = vwap - stdDev;
            decimal sd2U = vwap + 2m * stdDev;
            decimal sd2D = vwap - 2m * stdDev;

            bool dansLaDva  = close > sd1D && close < sd1U;
            bool horsDva    = !dansLaDva;

            // ── 3. Slope VWAP (plate vs pentifiée) ──────────────────────────────

            decimal vwapPrev = bar >= SlopeLookback ? (decimal)_vwap[bar - SlopeLookback] : vwap;
            decimal slopeRaw = vwap - vwapPrev;
            decimal slopeAbs = Math.Abs(slopeRaw);

            // Seuil de slope : 0.5 point sur NQ par barre en moyenne
            decimal slopeThreshold = 0.5m * SlopeLookback;
            bool vwapPlatee   = slopeAbs < slopeThreshold;
            bool vwapPentifiee = !vwapPlatee;
            _haussier = slopeRaw >= 0;

            // ── 4. Qualifier le rythme EMI ───────────────────────────────────────
            //  Slow   : prix entre VWAP et SD0.5 (≈ SD1/2)
            //  Normal : SD0.5 à SD1
            //  Fast   : SD1 à SD1.5
            //  Speed  : au-delà de SD1.5

            decimal distVwap = Math.Abs(close - vwap);
            decimal sd05 = stdDev * 0.5m;
            decimal sd15 = stdDev * 1.5m;

            VwapRythme rythme;
            if      (distVwap < sd05)   rythme = VwapRythme.Slow;
            else if (distVwap < stdDev) rythme = VwapRythme.Normal;
            else if (distVwap < sd15)   rythme = VwapRythme.Fast;
            else                        rythme = VwapRythme.Speed;
            _lastRythme = rythme;

            // ── 5. Identifier la séquence EMI ────────────────────────────────────
            //  Équilibrée   : VWAP plate + prix dans DVA
            //  Déséquilibrée: VWAP pentifiée + prix hors DVA
            //  Breakout     : VWAP plate + prix hors DVA (cassure en cours)
            //  Reversale    : VWAP pentifiée + prix revient dans DVA (retour vers équilibre)

            VwapSequence seq;
            if      (vwapPlatee   && dansLaDva) seq = VwapSequence.Equilibree;
            else if (vwapPentifiee && horsDva)   seq = VwapSequence.Desequilibree;
            else if (vwapPlatee   && horsDva)    seq = VwapSequence.Breakout;
            else                                  seq = VwapSequence.Reversale;
            _lastSequence = seq;

            // ── 6. Vote Phase 1 selon matrice EMI ───────────────────────────────

            Vote vote;
            float strength;
            string reason;

            switch (seq)
            {
                case VwapSequence.Desequilibree:
                    // Séquence continuation — vote fort dans le sens de la VWAP
                    vote     = _haussier ? Vote.Bull : Vote.Bear;
                    strength = rythme switch
                    {
                        VwapRythme.Speed  => 1.0f,
                        VwapRythme.Fast   => 0.85f,
                        VwapRythme.Normal => 0.70f,
                        _                 => 0.55f
                    };
                    reason = $"Déséquilibrée {(_haussier ? "↑" : "↓")} | Rythme:{rythme} | SD:{SdLabel(close, vwap, stdDev)}";
                    break;

                case VwapSequence.Breakout:
                    // Cassure DVA en cours — vote modéré dans sens du breakout
                    vote     = close > vwap ? Vote.Bull : Vote.Bear;
                    strength = 0.65f;
                    reason   = $"Breakout {(close > vwap ? "↑" : "↓")} | SD:{SdLabel(close, vwap, stdDev)}";
                    break;

                case VwapSequence.Equilibree:
                    // Rotation — signal neutre (Extreme Fade possible mais narration neutre)
                    vote     = Vote.Neutral;
                    strength = 0.1f;
                    reason   = $"Équilibrée (rotation) | SD:{SdLabel(close, vwap, stdDev)}";
                    break;

                case VwapSequence.Reversale:
                    // Changement de condition — attendre confirmation
                    vote     = Vote.Neutral;
                    strength = 0.2f;
                    reason   = $"Reversale — attendre confirmation | SD:{SdLabel(close, vwap, stdDev)}";
                    break;

                default:
                    vote     = Vote.Neutral;
                    strength = 0f;
                    reason   = "Données insuffisantes";
                    break;
            }

            SharedStateWriter.Write(new IndicatorState
            {
                IndicatorId = IndicatorId,
                Vote        = vote,
                Strength    = strength,
                LastUpdate  = DateTime.UtcNow,
                Reason      = reason
            });
        }

        // ── Calcul VWAP + Écart-type ────────────────────────────────────────────
        // Réinitialise chaque jour si DailyReset = true, sinon mensuel

        private (decimal vwap, decimal stdDev) CalculateVwapAndStdDev(int bar)
        {
            var candle = GetCandle(bar);
            var barTime = candle.Time;
            DateTime resetTime = DailyReset
                ? barTime.Date
                : new DateTime(barTime.Year, barTime.Month, 1);

            decimal sumPV = 0m, sumV = 0m, sumPV2 = 0m;

            for (int i = bar; i >= 0; i--)
            {
                var c = GetCandle(i);
                if (c == null || c.Time < resetTime) break;

                decimal tp = (c.High + c.Low + c.Close) / 3m;
                sumPV  += tp * c.Volume;
                sumV   += c.Volume;
                sumPV2 += tp * tp * c.Volume;
            }

            if (sumV <= 0) return (candle.Close, 0m);

            decimal vwap   = sumPV / sumV;
            decimal varTP  = (sumPV2 / sumV) - (vwap * vwap);
            decimal stdDev = varTP > 0 ? (decimal)Math.Sqrt((double)varTP) : 0m;

            // Minimum SD pour éviter les divisions par zéro en marché très stable
            if (stdDev < 0.25m) stdDev = 0.25m;

            return (vwap, stdDev);
        }

        private static string SdLabel(decimal close, decimal vwap, decimal stdDev)
        {
            if (stdDev <= 0) return "0";
            decimal dist = Math.Abs(close - vwap) / stdDev;
            string side = close >= vwap ? "+" : "-";
            return $"SD{side}{dist:F1}";
        }

        // ── Panel de statut ─────────────────────────────────────────────────────

        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (layout != DrawingLayouts.Final) return;

            var bounds = ChartInfo.PaneBounds;
            int x = bounds.Left + 8;
            int y = bounds.Top + 8;

            var font = new RenderFont("Consolas", 8.5f);
            var pen  = new RenderPen(Color.FromArgb(180, 60, 60, 90));

            string seqStr = _lastSequence switch
            {
                VwapSequence.Equilibree    => "ÉQUILIBRÉE  → Extreme Fade",
                VwapSequence.Desequilibree => "DÉSÉQUILIBRÉE → Imbalance PB",
                VwapSequence.Breakout      => "BREAKOUT → Pullback DVA",
                VwapSequence.Reversale     => "REVERSALE → attendre",
                _                          => "—"
            };
            string rythStr = $"Rythme : {_lastRythme}";

            Color seqColor = _lastSequence switch
            {
                VwapSequence.Desequilibree => _haussier ? Color.LimeGreen : Color.Tomato,
                VwapSequence.Breakout      => Color.Orange,
                VwapSequence.Equilibree    => Color.DodgerBlue,
                VwapSequence.Reversale     => Color.Gold,
                _                          => Color.Gray
            };

            context.DrawString($"Séquence : {seqStr}", font, seqColor,  new Rectangle(x, y,      300, 18));
            context.DrawString(rythStr,                 font, Color.Silver, new Rectangle(x, y + 18, 300, 18));
        }
    }
}
