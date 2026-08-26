using System;
using System.Collections.Generic;
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
        private bool _dirty;
        private double _dirtySeconds;

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
            object? value;
            if (_values.TryGetValue(key, out value))
                return ValueConverter.PlainToDyn(_script, value);
            return fallback;
        }

        public void Set(string key, DynValue value)
        {
            _values[key] = ValueConverter.DynToPlain(value);
            MarkDirty();
        }

        public bool Delete(string key)
        {
            bool removed = _values.Remove(key);
            if (removed)
                MarkDirty();
            return removed;
        }

        public void Update(double dt)
        {
            if (!_dirty)
                return;

            _dirtySeconds += Math.Max(0.0, dt);
            if (_dirtySeconds >= AutoSaveDelaySeconds)
                Save();
        }

        public void Save()
        {
            if (!_dirty && File.Exists(_path))
                return;

            string json = SimpleJson.Serialize(_values, true);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, json);

            try
            {
                if (File.Exists(_path))
                {
                    try
                    {
                        File.Replace(temp, _path, _backupPath, true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        FallbackReplace(temp);
                    }
                    catch (NotSupportedException)
                    {
                        FallbackReplace(temp);
                    }
                    catch (IOException)
                    {
                        FallbackReplace(temp);
                    }
                }
                else
                {
                    File.Move(temp, _path);
                }

                _dirty = false;
                _dirtySeconds = 0.0;
            }
            finally
            {
                if (File.Exists(temp))
                {
                    try { File.Delete(temp); }
                    catch { }
                }
            }
        }

        private void MarkDirty()
        {
            if (!_dirty)
                _dirtySeconds = 0.0;
            _dirty = true;
        }

        private void FallbackReplace(string temp)
        {
            if (File.Exists(_path))
            {
                try { File.Copy(_path, _backupPath, true); }
                catch { }
                File.Delete(_path);
            }
            File.Move(temp, _path);
        }

        private void Load()
        {
            if (TryLoadPath(_path))
                return;

            if (File.Exists(_backupPath) && TryLoadPath(_backupPath))
                RuntimeLog.Warn("Recovered Lua storage from backup: " + _backupPath);
        }

        private bool TryLoadPath(string path)
        {
            if (!File.Exists(path))
                return false;

            try
            {
                var data = SimpleJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object?>;
                if (data == null)
                    return false;

                _values.Clear();
                foreach (var pair in data)
                    _values[pair.Key] = pair.Value;
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
