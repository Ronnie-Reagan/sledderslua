using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SleddersLuaRuntime.Api;

namespace SleddersLuaRuntime.Core
{
    internal sealed class ScriptManager
    {
        private readonly RuntimeHost _host;
        private readonly Dictionary<string, LuaModInstance> _mods = new Dictionary<string, LuaModInstance>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _failedSourceFingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _transientWarnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Stopwatch _scanClock = Stopwatch.StartNew();
        private double _nextScanSeconds;

        public ScriptManager(RuntimeHost host) { _host = host; }

        public void InitialLoad()
        {
            ReconcileMods(false);
            RuntimeLog.Info($"Loaded {_mods.Count} Lua mod(s).");
        }

        public void Update(double dt)
        {
            if (_host.Config.HotReload && _scanClock.Elapsed.TotalSeconds >= _nextScanSeconds)
            {
                _nextScanSeconds = _scanClock.Elapsed.TotalSeconds + Math.Max(0.1, _host.Config.ScanIntervalSeconds);
                ReconcileMods(true);
            }

            LuaModInstance[] mods = OrderedMods();
            string[] rawKeys = mods.Any(m => m.WantsRawKeyEvents) ? UnityBridge.GetPressedKeys() : Array.Empty<string>();
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
            foreach (LuaModInstance mod in OrderedMods()) if (mod.Enabled) mod.Draw();
        }

        public void Dispatch(string eventName, params object?[] args)
        {
            foreach (LuaModInstance mod in OrderedMods()) if (mod.Enabled) mod.Dispatch(eventName, args);
        }

        public void InvalidateSceneObjects()
        {
            foreach (LuaModInstance mod in OrderedMods()) mod.InvalidateSceneObjects();
        }

        public void Shutdown()
        {
            foreach (LuaModInstance mod in OrderedMods()) mod.Unload("runtime shutdown");
            _mods.Clear();
            _failedSourceFingerprints.Clear();
            _transientWarnings.Clear();
        }

        private LuaModInstance[] OrderedMods() => _mods.Values.OrderBy(m => m.SourceKey, StringComparer.OrdinalIgnoreCase).ToArray();

        private void ReconcileMods(bool forceReloadChanged)
        {
            List<LuaModSource> sources;
            try { sources = DiscoverSources().OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase).ToList(); }
            catch (Exception ex) { RuntimeLog.Exception("Failed scanning LuaMods", ex); return; }

            var liveKeys = new HashSet<string>(sources.Select(s => s.Key), StringComparer.OrdinalIgnoreCase);
            foreach (string existingKey in _mods.Keys.ToArray())
            {
                if (liveKeys.Contains(existingKey)) continue;
                _mods[existingKey].Unload("files removed");
                _mods.Remove(existingKey);
                _failedSourceFingerprints.Remove(existingKey);
                _transientWarnings.Remove(existingKey);
                RuntimeLog.Info($"Unloaded removed Lua mod source: {existingKey}");
            }
            foreach (string failedKey in _failedSourceFingerprints.Keys.ToArray()) if (!liveKeys.Contains(failedKey)) _failedSourceFingerprints.Remove(failedKey);

            foreach (LuaModSource source in sources)
            {
                string fingerprint;
                try
                {
                    fingerprint = GetSourceFingerprint(source);
                    _transientWarnings.Remove(source.Key);
                }
                catch (Exception ex) when (IsTransientIo(ex))
                {
                    WarnTransientOnce(source.Key, "Could not read Lua source while scanning; will retry: " + ex.Message);
                    continue;
                }

                if (!_mods.TryGetValue(source.Key, out LuaModInstance? existing))
                {
                    if (_failedSourceFingerprints.TryGetValue(source.Key, out string? failed) && failed == fingerprint) continue;
                    TryLoad(source, fingerprint);
                    continue;
                }

                if (!forceReloadChanged || string.Equals(existing.SourceFingerprint, fingerprint, StringComparison.Ordinal)) continue;
                if (_failedSourceFingerprints.TryGetValue(source.Key, out string? failedFingerprint) && failedFingerprint == fingerprint) continue;

                if (!existing.FlushStorage())
                {
                    WarnTransientOnce(source.Key, $"Hot reload delayed for '{source.MainPath}' because current storage could not be flushed. Will retry.");
                    continue;
                }

                LuaModInstance? candidate = null;
                try
                {
                    candidate = new LuaModInstance(_host, source);
                    if (_mods.Values.Any(other => !ReferenceEquals(other, existing) && string.Equals(other.Manifest.Id, candidate.Manifest.Id, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException($"Duplicate Lua mod id '{candidate.Manifest.Id}'. Mod ids must be unique.");
                    candidate.ValidateSourceSyntax();
                    candidate.PrepareLoad(fingerprint);
                }
                catch (Exception ex)
                {
                    candidate?.AbortPrepared();
                    if (IsTransientIo(ex))
                    {
                        WarnTransientOnce(source.Key, $"Hot reload delayed for '{source.MainPath}' by a temporary file I/O error: {ex.Message}");
                    }
                    else
                    {
                        _failedSourceFingerprints[source.Key] = fingerprint;
                        RuntimeLog.Exception($"Hot reload rejected for '{source.MainPath}'. The last working copy is still running", ex);
                    }
                    continue;
                }

                RuntimeLog.Info($"Hot reloading {existing.Manifest.Name}...");
                Dictionary<string, object?> finalStorage = existing.UnloadForReload("hot reload", out bool saved);
                _mods.Remove(source.Key);
                candidate.MergeStorageBase(finalStorage, !saved);
                try
                {
                    candidate.Activate();
                    _mods[source.Key] = candidate;
                    _failedSourceFingerprints.Remove(source.Key);
                    _transientWarnings.Remove(source.Key);
                    RuntimeLog.Info($"Loaded Lua mod: {candidate.Manifest.Name} {candidate.Manifest.Version} by {candidate.Manifest.Author} [{candidate.Manifest.Id}]");
                }
                catch (Exception ex)
                {
                    candidate.Unload("activation failed");
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
                if (Path.GetFileName(directory).StartsWith("_", StringComparison.Ordinal)) continue;
                string main = Path.Combine(directory, "main.lua");
                if (File.Exists(main)) yield return LuaModSource.FromDirectory(directory);
            }
            foreach (string file in Directory.EnumerateFiles(_host.LuaModsRoot, "*.lua", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(file);
                if (!name.StartsWith("_", StringComparison.Ordinal)) yield return LuaModSource.FromSingleFile(file);
            }
        }

        private void TryLoad(LuaModSource source, string fingerprint)
        {
            LuaModInstance? mod = null;
            try
            {
                mod = new LuaModInstance(_host, source);
                if (_mods.Values.Any(existing => string.Equals(existing.Manifest.Id, mod.Manifest.Id, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Duplicate Lua mod id '{mod.Manifest.Id}'. Mod ids must be unique.");
                mod.ValidateSourceSyntax();
                mod.PrepareLoad(fingerprint);
                mod.Activate();
                _mods[source.Key] = mod;
                _failedSourceFingerprints.Remove(source.Key);
                RuntimeLog.Info($"Loaded Lua mod: {mod.Manifest.Name} {mod.Manifest.Version} by {mod.Manifest.Author} [{mod.Manifest.Id}]");
            }
            catch (Exception ex)
            {
                mod?.AbortPrepared();
                if (IsTransientIo(ex))
                {
                    WarnTransientOnce(source.Key, $"Lua mod source '{source.MainPath}' could not be read; will retry: {ex.Message}");
                    return;
                }
                _failedSourceFingerprints[source.Key] = fingerprint;
                RuntimeLog.Exception($"Failed loading Lua mod source '{source.MainPath}'", ex);
            }
        }

        private static string GetSourceFingerprint(LuaModSource source)
        {
            var builder = new StringBuilder();
            using (SHA256 sha = SHA256.Create())
            {
                foreach (string path in EnumerateSourceFiles(source).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    byte[] hash = sha.ComputeHash(bytes);
                    builder.Append(path).Append('|').Append(Convert.ToBase64String(hash)).Append(';');
                }
            }
            return builder.ToString();
        }

        private static IEnumerable<string> EnumerateSourceFiles(LuaModSource source)
        {
            if (File.Exists(source.MainPath)) yield return Path.GetFullPath(source.MainPath);
            if (!string.IsNullOrWhiteSpace(source.ManifestPath) && File.Exists(source.ManifestPath)) yield return Path.GetFullPath(source.ManifestPath);
            if (!string.Equals(source.Key, source.MainPath, StringComparison.OrdinalIgnoreCase) && Directory.Exists(source.ModuleRoot))
            {
                foreach (string file in Directory.EnumerateFiles(source.ModuleRoot, "*.lua", SearchOption.AllDirectories))
                {
                    string full = Path.GetFullPath(file);
                    if (source.ManifestPath != null && string.Equals(full, Path.GetFullPath(source.ManifestPath), StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(full, Path.GetFullPath(source.MainPath), StringComparison.OrdinalIgnoreCase)) continue;
                    yield return full;
                }
            }
        }

        private static bool IsTransientIo(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException;
        }

        private void WarnTransientOnce(string key, string message)
        {
            if (_transientWarnings.Add(key)) RuntimeLog.Warn(message);
        }
    }
}
