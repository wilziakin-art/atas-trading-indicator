namespace ATASMultiSignal.Models
{
    /// <summary>
    /// Represents a single sub-signal's score in the composite scoring system.
    /// Value is clamped to [-1, +1]: negative = bearish, positive = bullish.
    /// </summary>
    public sealed class SignalScore
    {
        /// <summary>Normalized score in [-1, +1].</summary>
        public float Value { get; }

        /// <summary>Relative importance of this signal in the composite calculation.</summary>
        public float Weight { get; }

        /// <summary>Short human-readable label, e.g. "MACD", "EMA Cross".</summary>
        public string Label { get; }

        /// <summary>Optional human-readable reason / diagnostic string.</summary>
        public string Reason { get; }

        public SignalScore(float value, float weight, string label, string reason = "")
        {
            Value = Math.Clamp(value, -1f, 1f);
            Weight = weight;
            Label = label;
            Reason = reason;
        }
    }
}
