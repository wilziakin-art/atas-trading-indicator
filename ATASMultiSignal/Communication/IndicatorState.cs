using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ATASMultiSignal.Communication
{
    public class IndicatorState
    {
        [JsonPropertyName("indicators")]
        public Dictionary<string, IndicatorVote> Indicators { get; set; } = new();
    }

    public class IndicatorVote
    {
        [JsonPropertyName("direction")]
        public string Direction { get; set; } = "Neutral"; // "Bull", "Bear", "Neutral"

        [JsonPropertyName("strength")]
        public double Strength { get; set; } = 0.0; // 0.0 à 1.0

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; } = 0;

        [JsonPropertyName("details")]
        public string Details { get; set; } = "";
    }
}
