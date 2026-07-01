using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace ATASMultiSignal.Communication
{
    public static class SharedStateWriter
    {
        private static readonly string StateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ATASMultiSignal");
        private static readonly string StatePath = Path.Combine(StateDir, "state.json");
        private static readonly object _lock = new();

        public static void WriteVote(string indicatorId, string direction, double strength, string details = "")
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(StateDir);
                    IndicatorState state;
                    if (File.Exists(StatePath))
                    {
                        var json = File.ReadAllText(StatePath);
                        state = JsonSerializer.Deserialize<IndicatorState>(json) ?? new IndicatorState();
                    }
                    else
                    {
                        state = new IndicatorState();
                    }

                    state.Indicators[indicatorId] = new IndicatorVote
                    {
                        Direction = direction,
                        Strength = Math.Clamp(strength, 0.0, 1.0),
                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        Details = details
                    };

                    File.WriteAllText(StatePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { /* silencieux */ }
            }
        }
    }
}
