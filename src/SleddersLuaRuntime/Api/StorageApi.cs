using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal sealed class StorageApi
    {
        private const double AutoSaveDelaySeconds = 1.0;

        private readonly string _path;
        private readonly string _backupPath;
        private readonly Script _script;
        private readonly Dictionary<string, object?> _values = new Dictionary<string, object?>(StringComparer.Ordinal);
        private readonly HashSet<string> _changedKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private bool _dirty;
        private double _dirtySinceSeconds;
        private bool _writesSuppressed = true;
        private bool _saveRequested;

        public StorageApi(RuntimeHost host, string modId, Script script)
        {
            _script = script;
            string directory = Path.Combine(host.DataRoot, "mods", modId);
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "storage.json");
            _backupPath = _path + ".bak";
            Load();
        }

        public DynValue Get(string key, DynValue fallback)
        {
            return _values.TryGetValue(key, out object? value) ? ValueConverter.PlainToDyn(_script, value) : fallback;
        }

        public void Set(string key, DynValue value)
        {
            _values[key] = ValueConverter.DynToPlain(value);
            _changedKeys.Add(key);
            MarkDirty();
        }

        public bool Delete(string key)
        {
            bool removed = _values.Remove(key);
            _changedKeys.Add(key);
            if (removed) MarkDirty();
            return removed;
        }

        public void Update(double dt)
        {
            if (!_dirty || _writesSuppressed) return;
            if (_clock.Elapsed.TotalSeconds - _dirtySinceSeconds >= AutoSaveDelaySeconds) Save();
        }

        public void Activate()
        {
            _writesSuppressed = false;
            if (_saveRequested)
            {
                _saveRequested = false;
                try { Save(); }
                catch (Exception ex) { RuntimeLog.Warn("Deferred Lua storage save failed: " + ex.Message); }
            }
            _changedKeys.Clear();
        }

        public Dictionary<string, object?> ExportSnapshot()
        {
            return new Dictionary<string, object?>(_values, StringComparer.Ordinal);
        }

        public void MergeBaseSnapshot(Dictionary<string, object?> snapshot, bool markDirty)
        {
            var currentKeys = new List<string>(_values.Keys);
            foreach (string key in currentKeys)
            {
                if (!_changedKeys.Contains(key) && !snapshot.ContainsKey(key)) _values.Remove(key);
            }
            foreach (var pair in snapshot)
            {
                if (!_changedKeys.Contains(pair.Key)) _values[pair.Key] = pair.Value;
            }
            if (markDirty) MarkDirty();
        }

        public bool TrySave(out string? error)
        {
            try { Save(); error = null; return true; }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        public void Save()
        {
            if (_writesSuppressed)
            {
                _saveRequested = true;
                return;
            }
            if (!_dirty && File.Exists(_path)) return;

            string json = SimpleJson.Serialize(_values, true);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            try
            {
                if (File.Exists(_path))
                {
                    try { File.Replace(temp, _path, _backupPath, true); }
                    catch (PlatformNotSupportedException) { FallbackReplace(temp); }
                    catch (NotSupportedException) { FallbackReplace(temp); }
                    catch (IOException) { FallbackReplace(temp); }
                }
                else File.Move(temp, _path);
                _dirty = false;
                _dirtySinceSeconds = 0.0;
            }
            finally
            {
                if (File.Exists(temp)) { try { File.Delete(temp); } catch { } }
            }
        }

        private void MarkDirty()
        {
            if (!_dirty) _dirtySinceSeconds = _clock.Elapsed.TotalSeconds;
            _dirty = true;
        }

        private void FallbackReplace(string temp)
        {
            if (File.Exists(_path))
            {
                try { File.Copy(_path, _backupPath, true); } catch { }
                File.Delete(_path);
            }
            File.Move(temp, _path);
        }

        private void Load()
        {
            if (TryLoadPath(_path)) return;
            if (File.Exists(_backupPath) && TryLoadPath(_backupPath))
                RuntimeLog.Warn("Recovered Lua storage from backup: " + _backupPath);
        }

        private bool TryLoadPath(string path)
        {
            if (!File.Exists(path)) return false;
            try
            {
                var data = SimpleJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object?>;
                if (data == null) return false;
                _values.Clear();
                foreach (var pair in data) _values[pair.Key] = pair.Value;
                return true;
            }
            catch (Exception ex)
            {
                RuntimeLog.Warn("Storage file '" + path + "' could not be loaded: " + ex.Message);
                return false;
            }
        }
    }
}
