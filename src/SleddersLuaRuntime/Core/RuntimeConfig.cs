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
        public bool EnableDevApi { get; set; } = false;

        public static RuntimeConfig LoadOrCreate(string path)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                try { Directory.CreateDirectory(directory); }
                catch (Exception ex) { RuntimeLog.Warn("Could not create configuration directory: " + ex.Message); }
            }

            if (File.Exists(path))
            {
                try
                {
                    var loaded = SimpleJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
                    if (loaded != null)
                    {
                        RuntimeConfig loadedConfig = FromDictionary(loaded);
                        TryWriteNormalized(path, loadedConfig);
                        return loadedConfig;
                    }
                }
                catch (Exception ex)
                {
                    RuntimeLog.Warn("Could not load configuration; using defaults. " + ex.Message);
                    return new RuntimeConfig();
                }
            }

            var defaultConfig = new RuntimeConfig();
            TryWriteNormalized(path, defaultConfig);
            return defaultConfig;
        }

        private static void TryWriteNormalized(string path, RuntimeConfig config)
        {
            try
            {
                string normalized = SimpleJson.Serialize(config.ToDictionary(), true);
                if (!File.Exists(path) || !string.Equals(File.ReadAllText(path), normalized, StringComparison.Ordinal))
                    File.WriteAllText(path, normalized);
            }
            catch (Exception ex)
            {
                RuntimeLog.Warn("Could not write normalized configuration: " + ex.Message);
            }
        }

        private Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "HotReload", HotReload },
                { "ScanIntervalSeconds", ScanIntervalSeconds },
                { "MaxDiscoveryResults", MaxDiscoveryResults },
                { "LogScriptStackTraces", LogScriptStackTraces },
                { "EnableDevApi", EnableDevApi }
            };
        }

        private static RuntimeConfig FromDictionary(Dictionary<string, object> values)
        {
            var config = new RuntimeConfig();
            object value;
            if (values.TryGetValue("HotReload", out value)) TryApply(() => config.HotReload = Convert.ToBoolean(value));
            if (values.TryGetValue("ScanIntervalSeconds", out value)) TryApply(() => config.ScanIntervalSeconds = Convert.ToDouble(value));
            if (values.TryGetValue("MaxDiscoveryResults", out value)) TryApply(() => config.MaxDiscoveryResults = Convert.ToInt32(value));
            if (values.TryGetValue("LogScriptStackTraces", out value)) TryApply(() => config.LogScriptStackTraces = Convert.ToBoolean(value));
            if (values.TryGetValue("EnableDevApi", out value)) TryApply(() => config.EnableDevApi = Convert.ToBoolean(value));

            if (double.IsNaN(config.ScanIntervalSeconds) || double.IsInfinity(config.ScanIntervalSeconds))
                config.ScanIntervalSeconds = 0.75;
            config.ScanIntervalSeconds = Math.Max(0.1, config.ScanIntervalSeconds);
            config.MaxDiscoveryResults = Compat.Clamp(config.MaxDiscoveryResults, 1, 1024);
            return config;
        }

        private static void TryApply(Action action)
        {
            try { action(); }
            catch { }
        }
    }
}
