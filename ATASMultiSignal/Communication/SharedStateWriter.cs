using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ATASMultiSignal.Communication
{
    public static class SharedStateWriter
    {
        private static readonly object _lock = new object();
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ATASMultiSignal",
            "state.json");

        public static void Write(IndicatorState state)
        {
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(FilePath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                Dictionary<string, IndicatorState> dict;
                if (File.Exists(FilePath))
                {
                    try
                    {
                        var json = File.ReadAllText(FilePath);
                        dict = JsonSerializer.Deserialize<Dictionary<string, IndicatorState>>(json)
                               ?? new Dictionary<string, IndicatorState>();
                    }
                    catch
                    {
                        dict = new Dictionary<string, IndicatorState>();
                    }
                }
                else
                {
                    dict = new Dictionary<string, IndicatorState>();
                }

                dict[state.IndicatorId] = state;

                var output = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, output);
            }
        }
    }
}
