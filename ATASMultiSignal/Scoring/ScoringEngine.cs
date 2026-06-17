using System.Collections.Generic;
using System.Linq;
using ATASMultiSignal.Models;

namespace ATASMultiSignal.Scoring
{
    /// <summary>
    /// Combines individual sub-signal scores into a single <see cref="CompositeSignalResult"/>.
    /// </summary>
    public sealed class ScoringEngine
    {
        /// <summary>
        /// Evaluates a collection of <see cref="SignalScore"/> objects and produces a
        /// <see cref="CompositeSignalResult"/> for the given bar.
        /// </summary>
        /// <param name="bar">Bar index.</param>
        /// <param name="scores">Sub-signal scores to aggregate.</param>
        /// <param name="buyThreshold">Normalised score must be &gt;= this to trigger BUY.</param>
        /// <param name="sellThreshold">Normalised score must be &lt;= -this to trigger SELL.</param>
        public CompositeSignalResult Evaluate(
            int bar,
            IEnumerable<SignalScore> scores,
            float buyThreshold,
            float sellThreshold)
        {
            var scoreList = scores?.ToList() ?? new List<SignalScore>();

            float weightedSum = 0f;
            float totalWeight = 0f;

            foreach (var s in scoreList)
            {
                weightedSum += s.Value * s.Weight;
                totalWeight += s.Weight;
            }

            float normalized = totalWeight > 0f ? weightedSum / totalWeight : 0f;

            SignalDirection direction;
            if (normalized >= buyThreshold)
                direction = SignalDirection.Buy;
            else if (normalized <= -sellThreshold)
                direction = SignalDirection.Sell;
            else
                direction = SignalDirection.None;

            return new CompositeSignalResult(bar, direction, normalized, scoreList);
        }
    }
}
