using System;

namespace ATASMultiSignal.Communication
{
    public enum Vote { Bull, Bear, Neutral }

    public class IndicatorState
    {
        public string IndicatorId { get; set; } = "";
        public Vote Vote { get; set; } = Vote.Neutral;
        public float Strength { get; set; } = 0f;
        public DateTime LastUpdate { get; set; }
        public string Reason { get; set; } = "";
    }
}
