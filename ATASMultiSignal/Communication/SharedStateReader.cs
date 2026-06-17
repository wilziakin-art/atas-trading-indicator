using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ATASMultiSignal.Communication
{
    public static class SharedStateReader
    {
        private static readonly object _lock = new object();
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ATASMultiSignal",
            "state.json");

        public static Dictionary<string, IndicatorState> ReadAll()
        {
            lock (_lock)
            {
                if (!File.Exists(FilePath))
                    return new Dictionary<string, IndicatorState>();

                try
                {
                    var json = File.ReadAllText(FilePath);
                    return JsonSerializer.Deserialize<Dictionary<string, IndicatorState>>(json)
                           ?? new Dictionary<string, IndicatorState>();
                }
                catch
                {
                    return new Dictionary<string, IndicatorState>();
                }
            }
        }
    }
}
