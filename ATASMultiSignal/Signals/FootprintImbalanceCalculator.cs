using System;
using ATAS.Indicators;
using ATASMultiSignal.Models;

namespace ATASMultiSignal.Signals
{
    /// <summary>
    /// Footprint imbalance sub-signal calculator.
    /// Iterates the price levels of a FootprintCandle and counts bullish vs bearish
    /// imbalanced levels.  Also detects absorption (large passive orders against the
    /// prevailing trend) and applies a configurable boost.
    /// </summary>
    public sealed class FootprintImbalanceCalculator : ISignalCalculator
    {
        // ── Parameters ──────────────────────────────────────────────────────────
        public float ImbalanceThreshold { get; set; } = 0.7f;
        public float LargeOrderMultiplier { get; set; } = 3.0f;
        public float Weight { get; set; } = 2.0f;

        // ── Dependencies ─────────────────────────────────────────────────────────
        private readonly Func<int, ATAS.Indicators.IndicatorCandle> _getCandle;

        public FootprintImbalanceCalculator(Func<int, ATAS.Indicators.IndicatorCandle> getCandle)
        {
            _getCandle = getCandle ?? throw new ArgumentNullException(nameof(getCandle));
        }

        public SignalScore Calculate(int bar)
        {
            var candle = _getCandle(bar);
            if (candle == null)
                return new SignalScore(0f, Weight, "FP Imbalance", "No candle data");

            if (candle is not FootprintCandle fp)
                return new SignalScore(0f, Weight, "FP Imbalance", "No footprint data");

            var priceLevels = fp.PriceLevels;
            if (priceLevels == null)
                return new SignalScore(0f, Weight, "FP Imbalance", "No price levels");

            int totalLevels = 0;
            int bullishImbalanced = 0;
            int bearishImbalanced = 0;
            float absorptionBoost = 0f;

            // Compute mean volume per level for large-order detection
            decimal totalAskVol = 0m, totalBidVol = 0m;
            int levelCount = 0;
            foreach (var level in priceLevels)
            {
                if (level == null) continue;
                totalAskVol += level.Ask;
                totalBidVol += level.Bid;
                levelCount++;
            }
            decimal meanVol = levelCount > 0 ? (totalAskVol + totalBidVol) / levelCount : 0m;

            foreach (var level in priceLevels)
            {
                if (level == null) continue;
                totalLevels++;

                decimal ask = level.Ask;
                decimal bid = level.Bid;
                decimal total = ask + bid;

                if (total <= 0m) continue;

                float askRatio = (float)(ask / total);
                float bidRatio = (float)(bid / total);

                // Imbalance detection
                if (askRatio >= ImbalanceThreshold)
                    bullishImbalanced++;
                else if (bidRatio >= ImbalanceThreshold)
                    bearishImbalanced++;

                // Absorption detection: large passive order against prevailing candle direction
                bool isLargeOrder = total >= meanVol * (decimal)LargeOrderMultiplier;
                if (isLargeOrder && meanVol > 0m)
                {
                    // Bullish candle with large bid (buyers absorbed sellers) → bullish absorption
                    if (fp.Close > fp.Open && bidRatio > 0.6f)
                        absorptionBoost += 0.1f;
                    // Bearish candle with large ask (sellers absorbed buyers) → bearish absorption
                    else if (fp.Close < fp.Open && askRatio > 0.6f)
                        absorptionBoost -= 0.1f;
                }
            }

            if (totalLevels == 0)
                return new SignalScore(0f, Weight, "FP Imbalance", "No valid levels");

            float rawScore = (float)(bullishImbalanced - bearishImbalanced) / totalLevels;
            float score = Math.Clamp(rawScore + Math.Clamp(absorptionBoost, -0.3f, 0.3f), -1f, 1f);

            string reason = $"bull={bullishImbalanced} bear={bearishImbalanced} total={totalLevels} absBoost={absorptionBoost:F2}";
            return new SignalScore(score, Weight, "FP Imbalance", reason);
        }
    }
}
