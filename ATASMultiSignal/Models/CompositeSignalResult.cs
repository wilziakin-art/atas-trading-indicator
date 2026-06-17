using System.Collections.Generic;

namespace ATASMultiSignal.Models
{
    /// <summary>
    /// Aggregated result for a single bar after all sub-signals have been scored.
    /// </summary>
    public sealed class CompositeSignalResult
    {
        /// <summary>Bar index this result belongs to.</summary>
        public int Bar { get; }

        /// <summary>Final trading direction decision.</summary>
        public SignalDirection Direction { get; }

        /// <summary>Weighted-average score in [-1, +1].</summary>
        public float NormalizedScore { get; }

        /// <summary>Individual sub-signal scores that produced this result.</summary>
        public List<SignalScore> Scores { get; }

        public CompositeSignalResult(int bar, SignalDirection direction, float normalizedScore, List<SignalScore> scores)
        {
            Bar = bar;
            Direction = direction;
            NormalizedScore = normalizedScore;
            Scores = scores ?? new List<SignalScore>();
        }
    }
}
