using System;
using System.IO;
using System.Text.Json;

namespace ATASMultiSignal.Communication
{
    public static class SharedStateReader
    {
        private static readonly string StatePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ATASMultiSignal", "state.json");

        public static IndicatorState? ReadState()
        {
            try
            {
                if (!File.Exists(StatePath)) return null;
                var json = File.ReadAllText(StatePath);
                return JsonSerializer.Deserialize<IndicatorState>(json);
            }
            catch { return null; }
        }

        public static IndicatorVote? GetVote(string indicatorId)
        {
            var state = ReadState();
            if (state == null) return null;
            state.Indicators.TryGetValue(indicatorId, out var vote);
            return vote;
        }
    }
}
