using System;
using System.Collections.Generic;
using System.IO;

namespace SleddersLuaRuntime.Core
{
    internal sealed class RuntimeConfig
    {
        public bool HotReload { get; set; } = true;
        public double ScanIntervalSeconds { get; set; } = 0.75;
        public int MaxDiscoveryResults { get; set; } = 64;
        public bool LogScriptStackTraces { get; set; } = true;

        public static RuntimeConfig LoadOrCreate(string path)
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                if (File.Exists(path))
                {
                    var loaded = SimpleJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
                    if (loaded != null)
                        return FromDictionary(loaded);
                }

                var config = new RuntimeConfig();
                File.WriteAllText(path, SimpleJson.Serialize(config.ToDictionary(), true));
                return config;
            }
            catch (Exception ex)
            {
                RuntimeLog.Warn("Could not load configuration; using defaults. " + ex.Message);
                return new RuntimeConfig();
            }
        }

        private Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "HotReload", HotReload },
                { "ScanIntervalSeconds", ScanIntervalSeconds },
                { "MaxDiscoveryResults", MaxDiscoveryResults },
                { "LogScriptStackTraces", LogScriptStackTraces }
            };
        }

        private static RuntimeConfig FromDictionary(Dictionary<string, object> values)
        {
            var config = new RuntimeConfig();
            object value;
            if (values.TryGetValue("HotReload", out value)) config.HotReload = Convert.ToBoolean(value);
            if (values.TryGetValue("ScanIntervalSeconds", out value)) config.ScanIntervalSeconds = Convert.ToDouble(value);
            if (values.TryGetValue("MaxDiscoveryResults", out value)) config.MaxDiscoveryResults = Convert.ToInt32(value);
            if (values.TryGetValue("LogScriptStackTraces", out value)) config.LogScriptStackTraces = Convert.ToBoolean(value);

            config.ScanIntervalSeconds = Math.Max(0.1, config.ScanIntervalSeconds);
            config.MaxDiscoveryResults = Compat.Clamp(config.MaxDiscoveryResults, 1, 1024);
            return config;
        }
    }
}
