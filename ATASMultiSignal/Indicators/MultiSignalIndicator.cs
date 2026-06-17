using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using ATAS.Indicators;
using ATAS.Indicators.DrawingItems;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;
using ATASMultiSignal.Alerts;
using ATASMultiSignal.Models;
using ATASMultiSignal.Rendering;
using ATASMultiSignal.Scoring;
using ATASMultiSignal.Signals;

namespace ATASMultiSignal.Indicators
{
    [DisplayName("Multi Signal Score")]
    [Category("Custom")]
    public sealed class MultiSignalIndicator : Indicator
    {
        // ══════════════════════════════════════════════════════════════════════════
        // Parameters
        // ══════════════════════════════════════════════════════════════════════════

        [Parameter]
        [DisplayName("Buy Threshold")]
        public float BuyThreshold { get; set; } = 0.55f;

        [Parameter]
        [DisplayName("Sell Threshold")]
        public float SellThreshold { get; set; } = 0.55f;

        [Parameter]
        [DisplayName("MACD Fast Period")]
        public int MacdFast { get; set; } = 12;

        [Parameter]
        [DisplayName("MACD Slow Period")]
        public int MacdSlow { get; set; } = 26;

        [Parameter]
        [DisplayName("MACD Signal Period")]
        public int MacdSignal { get; set; } = 9;

        [Parameter]
        [DisplayName("EMA Fast Period")]
        public int EmaFast { get; set; } = 9;

        [Parameter]
        [DisplayName("EMA Slow Period")]
        public int EmaSlow { get; set; } = 21;

        [Parameter]
        [DisplayName("Delta Lookback Bars")]
        public int DeltaLookback { get; set; } = 20;

        [Parameter]
        [DisplayName("Imbalance Threshold")]
        public float ImbalanceThreshold { get; set; } = 0.7f;

        [Parameter]
        [DisplayName("Large Order Multiplier")]
        public float LargeOrderMultiplier { get; set; } = 3.0f;

        [Parameter]
        [DisplayName("MACD Weight")]
        public float MacdWeight { get; set; } = 1.5f;

        [Parameter]
        [DisplayName("EMA Cross Weight")]
        public float EmaWeight { get; set; } = 1.5f;

        [Parameter]
        [DisplayName("Delta Weight")]
        public float DeltaWeight { get; set; } = 2.0f;

        [Parameter]
        [DisplayName("Footprint Weight")]
        public float FootprintWeight { get; set; } = 2.0f;

        [Parameter]
        [DisplayName("Sound Alerts")]
        public bool SoundAlerts { get; set; } = true;

        [Parameter]
        [DisplayName("Popup Alerts")]
        public bool PopupAlerts { get; set; } = true;

        // ══════════════════════════════════════════════════════════════════════════
        // Data series
        // ══════════════════════════════════════════════════════════════════════════

        private readonly ValueDataSeries _buyArrows;
        private readonly ValueDataSeries _sellArrows;

        // ══════════════════════════════════════════════════════════════════════════
        // Sub-components
        // ══════════════════════════════════════════════════════════════════════════

        private MacdCalculator? _macdCalc;
        private EmaCrossCalculator? _emaCrossCalc;
        private VolumeDeltaCalculator? _deltaCalc;
        private FootprintImbalanceCalculator? _fpCalc;
        private readonly ScoringEngine _scoringEngine = new();
        private readonly ScorePanelRenderer _panelRenderer = new();
        private AlertManager? _alertManager;
        private ArrowRenderer? _arrowRenderer;

        // ══════════════════════════════════════════════════════════════════════════
        // Result cache
        // ══════════════════════════════════════════════════════════════════════════

        private readonly Dictionary<int, CompositeSignalResult> _cache = new();
        private CompositeSignalResult? _latestResult;
        private SignalDirection _lastDirection = SignalDirection.None;

        // Arrow offset as a fraction of ATR
        private const decimal ArrowOffsetFraction = 0.3m;

        // ══════════════════════════════════════════════════════════════════════════
        // Constructor
        // ══════════════════════════════════════════════════════════════════════════

        public MultiSignalIndicator()
        {
            // Buy arrows series
            _buyArrows = new ValueDataSeries("BuyArrows")
            {
                Name = "Buy Signal",
                VisualType = VisualMode.UpArrow,
                ShowZeroLine = false,
                Width = 2
            };

            // Sell arrows series
            _sellArrows = new ValueDataSeries("SellArrows")
            {
                Name = "Sell Signal",
                VisualType = VisualMode.DownArrow,
                ShowZeroLine = false,
                Width = 2
            };

            DataSeries.Add(_buyArrows);
            DataSeries.Add(_sellArrows);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Lifecycle
        // ══════════════════════════════════════════════════════════════════════════

        protected override void OnInitialize()
        {
            // Build calculators with current parameter values
            _macdCalc = new MacdCalculator((ValueDataSeries)DataSeries[0])
            {
                FastPeriod = MacdFast,
                SlowPeriod = MacdSlow,
                SignalPeriod = MacdSignal,
                Weight = MacdWeight
            };

            _emaCrossCalc = new EmaCrossCalculator(
                (ValueDataSeries)DataSeries[0],
                (ValueDataSeries)DataSeries[1],
                (ValueDataSeries)DataSeries[2])
            {
                FastPeriod = EmaFast,
                SlowPeriod = EmaSlow,
                Weight = EmaWeight
            };

            _deltaCalc = new VolumeDeltaCalculator(GetCandle)
            {
                LookbackBars = DeltaLookback,
                Weight = DeltaWeight
            };

            _fpCalc = new FootprintImbalanceCalculator(GetCandle)
            {
                ImbalanceThreshold = ImbalanceThreshold,
                LargeOrderMultiplier = LargeOrderMultiplier,
                Weight = FootprintWeight
            };

            _alertManager = new AlertManager
            {
                SoundEnabled = SoundAlerts,
                PopupEnabled = PopupAlerts
            };

            _arrowRenderer = new ArrowRenderer(_buyArrows, _sellArrows);

            _cache.Clear();
            _lastDirection = SignalDirection.None;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Calculation
        // ══════════════════════════════════════════════════════════════════════════

        protected override void OnCalculate(int bar, decimal value)
        {
            // Reset arrow values to NaN (no signal)
            _buyArrows[bar] = decimal.MinValue;  // ATAS treats MinValue as NaN for arrow series
            _sellArrows[bar] = decimal.MinValue;

            // Return cached result for historical bars to avoid redundant work
            if (_cache.TryGetValue(bar, out var cached))
            {
                ApplyResult(bar, cached);
                return;
            }

            // Collect sub-signal scores
            var scores = new List<SignalScore>
            {
                _macdCalc!.Calculate(bar),
                _emaCrossCalc!.Calculate(bar),
                _deltaCalc!.Calculate(bar),
                _fpCalc!.Calculate(bar)
            };

            // Evaluate composite score
            var result = _scoringEngine.Evaluate(bar, scores, BuyThreshold, SellThreshold);

            // Cache and apply
            _cache[bar] = result;
            ApplyResult(bar, result);

            // Store latest for rendering
            _latestResult = result;

            // Alert on direction change
            _alertManager!.TryAlert(bar, result.Direction, _lastDirection);
            _lastDirection = result.Direction;
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Rendering
        // ══════════════════════════════════════════════════════════════════════════

        public override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (layout != DrawingLayouts.Final) return;

            var chartBounds = context.ClipRectangle;
            _panelRenderer.Draw(context, _latestResult, chartBounds);
        }

        // ══════════════════════════════════════════════════════════════════════════
        // Private helpers
        // ══════════════════════════════════════════════════════════════════════════

        private void ApplyResult(int bar, CompositeSignalResult result)
        {
            var candle = GetCandle(bar);
            if (candle == null) return;

            decimal offset = (candle.High - candle.Low) * ArrowOffsetFraction;
            if (offset <= 0m) offset = 0.001m;

            switch (result.Direction)
            {
                case SignalDirection.Buy:
                    _buyArrows[bar] = candle.Low - offset;
                    _sellArrows[bar] = decimal.MinValue;
                    break;

                case SignalDirection.Sell:
                    _sellArrows[bar] = candle.High + offset;
                    _buyArrows[bar] = decimal.MinValue;
                    break;

                default:
                    _buyArrows[bar] = decimal.MinValue;
                    _sellArrows[bar] = decimal.MinValue;
                    break;
            }
        }
    }
}
