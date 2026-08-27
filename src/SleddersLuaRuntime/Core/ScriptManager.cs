using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SleddersLuaRuntime.Api;

namespace SleddersLuaRuntime.Core
{
    internal sealed class ScriptManager
    {
        private readonly RuntimeHost _host;
        private readonly Dictionary<string, LuaModInstance> _mods = new Dictionary<string, LuaModInstance>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _failedSourceFingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Stopwatch _scanClock = Stopwatch.StartNew();
        private double _nextScanSeconds;

        public ScriptManager(RuntimeHost host)
        {
            _host = host;
        }

        public void InitialLoad()
        {
            ReconcileMods(forceReloadChanged: false);
            RuntimeLog.Info($"Loaded {_mods.Count} Lua mod(s).");
        }

        public void Update(double dt)
        {
            if (_host.Config.HotReload && _scanClock.Elapsed.TotalSeconds >= _nextScanSeconds)
            {
                _nextScanSeconds = _scanClock.Elapsed.TotalSeconds + Math.Max(0.1, _host.Config.ScanIntervalSeconds);
                ReconcileMods(forceReloadChanged: true);
            }

            LuaModInstance[] mods = OrderedMods();
            string[] rawKeys = mods.Any(m => m.WantsRawKeyEvents)
                ? UnityBridge.GetPressedKeys()
                : Array.Empty<string>();

            foreach (LuaModInstance mod in mods)
            {
                if (!mod.Enabled) continue;
                mod.ProcessKeyBindings();
                foreach (string key in rawKeys) mod.DispatchKey(key);
                mod.Dispatch("update", dt);
                mod.UpdateStorage(dt);
            }
        }

        public void Draw()
        {
            if (!ScreenApi.ShouldDispatchDraw) return;
            foreach (LuaModInstance mod in OrderedMods())
            {
                if (mod.Enabled) mod.Draw();
            }
        }

        public void Dispatch(string eventName, params object?[] args)
        {
            foreach (LuaModInstance mod in OrderedMods())
            {
                if (mod.Enabled) mod.Dispatch(eventName, args);
            }
        }

        public void Shutdown()
        {
            foreach (LuaModInstance mod in OrderedMods()) mod.Unload("runtime shutdown");
            _mods.Clear();
            _failedSourceFingerprints.Clear();
        }

        private LuaModInstance[] OrderedMods()
        {
            return _mods.Values.OrderBy(m => m.SourceKey, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private void ReconcileMods(bool forceReloadChanged)
        {
            List<LuaModSource> sources;
            try { sources = DiscoverSources().OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase).ToList(); }
            catch (Exception ex)
            {
                RuntimeLog.Exception("Failed scanning LuaMods", ex);
                return;
            }

            var liveKeys = new HashSet<string>(sources.Select(s => s.Key), StringComparer.OrdinalIgnoreCase);
            foreach (string existingKey in _mods.Keys.ToArray())
            {
                if (!liveKeys.Contains(existingKey))
                {
                    _mods[existingKey].Unload("files removed");
                    _mods.Remove(existingKey);
                    _failedSourceFingerprints.Remove(existingKey);
                    RuntimeLog.Info($"Unloaded removed Lua mod source: {existingKey}");
                }
            }
            foreach (string failedKey in _failedSourceFingerprints.Keys.ToArray())
            {
                if (!liveKeys.Contains(failedKey))
                    _failedSourceFingerprints.Remove(failedKey);
            }

            foreach (LuaModSource source in sources)
            {
                string fingerprint = GetSourceFingerprint(source);

                if (!_mods.TryGetValue(source.Key, out LuaModInstance? existing))
                {
                    if (_failedSourceFingerprints.TryGetValue(source.Key, out string? failed) && failed == fingerprint)
                        continue;
                    TryLoad(source, fingerprint);
                    continue;
                }

                if (!forceReloadChanged || !existing.HasSourceChanged())
                    continue;

                if (_failedSourceFingerprints.TryGetValue(source.Key, out string? failedFingerprint) && failedFingerprint == fingerprint)
                    continue;

                LuaModInstance candidate;
                try
                {
                    candidate = new LuaModInstance(_host, source);
                    candidate.ValidateSourceSyntax();
                    candidate.PrepareLoad();
                }
                catch (Exception ex)
                {
                    _failedSourceFingerprints[source.Key] = fingerprint;
                    RuntimeLog.Exception($"Hot reload rejected for '{source.MainPath}'. The last working copy is still running", ex);
                    continue;
                }

                if (_mods.Values.Any(other =>
                    !ReferenceEquals(other, existing) &&
                    string.Equals(other.Manifest.Id, candidate.Manifest.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    _failedSourceFingerprints[source.Key] = fingerprint;
                    RuntimeLog.Error($"Hot reload rejected for '{source.MainPath}': duplicate Lua mod id '{candidate.Manifest.Id}'. The last working copy is still running.");
                    continue;
                }

                RuntimeLog.Info($"Hot reloading {existing.Manifest.Name}...");
                existing.Unload("hot reload");
                _mods.Remove(source.Key);

                try
                {
                    candidate.Activate();
                    _mods[source.Key] = candidate;
                    _failedSourceFingerprints.Remove(source.Key);
                    RuntimeLog.Info($"Loaded Lua mod: {candidate.Manifest.Name} {candidate.Manifest.Version} by {candidate.Manifest.Author} [{candidate.Manifest.Id}]");
                }
                catch (Exception ex)
                {
                    _failedSourceFingerprints[source.Key] = fingerprint;
                    RuntimeLog.Exception($"Prepared hot reload for '{source.MainPath}' failed during activation", ex);
                }
            }
        }

        private IEnumerable<LuaModSource> DiscoverSources()
        {
            Directory.CreateDirectory(_host.LuaModsRoot);

            foreach (string directory in Directory.EnumerateDirectories(_host.LuaModsRoot))
            {
                if (Path.GetFileName(directory).StartsWith("_", StringComparison.Ordinal))
                    continue;
                string main = Path.Combine(directory, "main.lua");
                if (File.Exists(main)) yield return LuaModSource.FromDirectory(directory);
            }

            foreach (string file in Directory.EnumerateFiles(_host.LuaModsRoot, "*.lua", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith("_", StringComparison.Ordinal)) continue;
                yield return LuaModSource.FromSingleFile(file);
            }
        }

        private void TryLoad(LuaModSource source, string fingerprint)
        {
            try
            {
                var mod = new LuaModInstance(_host, source);
                mod.ValidateSourceSyntax();
                if (_mods.Values.Any(existing => string.Equals(existing.Manifest.Id, mod.Manifest.Id, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Duplicate Lua mod id '{mod.Manifest.Id}'. Mod ids must be unique.");
                mod.PrepareLoad();
                mod.Activate();
                _mods[source.Key] = mod;
                _failedSourceFingerprints.Remove(source.Key);
                RuntimeLog.Info($"Loaded Lua mod: {mod.Manifest.Name} {mod.Manifest.Version} by {mod.Manifest.Author} [{mod.Manifest.Id}]");
            }
            catch (Exception ex)
            {
                _failedSourceFingerprints[source.Key] = fingerprint;
                RuntimeLog.Exception($"Failed loading Lua mod source '{source.MainPath}'", ex);
            }
        }

        private static string GetSourceFingerprint(LuaModSource source)
        {
            try
            {
                var files = new List<string>();
                if (File.Exists(source.MainPath)) files.Add(Path.GetFullPath(source.MainPath));
                if (!string.IsNullOrWhiteSpace(source.ManifestPath) && File.Exists(source.ManifestPath))
                    files.Add(Path.GetFullPath(source.ManifestPath));
                if (!string.Equals(source.Key, source.MainPath, StringComparison.OrdinalIgnoreCase) && Directory.Exists(source.ModuleRoot))
                {
                    foreach (string file in Directory.EnumerateFiles(source.ModuleRoot, "*.lua", SearchOption.AllDirectories))
                    {
                        string full = Path.GetFullPath(file);
                        if (!files.Contains(full, StringComparer.OrdinalIgnoreCase)) files.Add(full);
                    }
                }

                var builder = new StringBuilder();
                foreach (string path in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var info = new FileInfo(path);
                    builder.Append(path).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append(';');
                }
                return builder.ToString();
            }
            catch
            {
                return Guid.NewGuid().ToString("N");
            }
        }
    }
}
