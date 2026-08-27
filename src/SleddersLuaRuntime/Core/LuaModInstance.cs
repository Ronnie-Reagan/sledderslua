using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Api;

namespace SleddersLuaRuntime.Core
{
    internal sealed class LuaModInstance
    {
        private sealed class KeyBinding
        {
            public int Handle { get; set; }
            public string Key { get; set; } = string.Empty;
            public DynValue Callback { get; set; } = DynValue.Nil;
        }

        private readonly RuntimeHost _host;
        private readonly LuaModSource _source;
        private readonly List<KeyBinding> _keyBindings = new List<KeyBinding>();
        private readonly Dictionary<string, DynValue> _moduleCache = new Dictionary<string, DynValue>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DynValue> _scriptObjectCache = new Dictionary<string, DynValue>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _sourceTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private int _nextHandle = 1;
        private readonly HashSet<string> _suspendedCallbacks = new HashSet<string>(StringComparer.Ordinal);
        private Script? _script;
        private StorageApi? _storage;

        public LuaModInstance(RuntimeHost host, LuaModSource source)
        {
            _host = host;
            _source = source;
            Manifest = ModManifest.Load(source.ManifestPath, source.FallbackId, source.FallbackName);
            Handles = new ObjectHandleRegistry();
            ResetDrawColor();
        }

        public ModManifest Manifest { get; }
        public bool Enabled { get; private set; }
        public Script Script => _script ?? throw new InvalidOperationException("Script not loaded.");
        public ObjectHandleRegistry Handles { get; }
        public RuntimeHost Host => _host;
        public StorageApi Storage => _storage ?? throw new InvalidOperationException("Storage not initialized.");
        public string SourceKey => _source.Key;

        public double DrawR { get; private set; }
        public double DrawG { get; private set; }
        public double DrawB { get; private set; }
        public double DrawA { get; private set; }

        public bool WantsRawKeyEvents
        {
            get
            {
                if (!Enabled || _script == null) return false;
                return IsFunction(_script.Globals.Get("onKey"));
            }
        }

        public void SetDrawColor(double r, double g, double b, double a)
        {
            DrawR = Clamp01(r);
            DrawG = Clamp01(g);
            DrawB = Clamp01(b);
            DrawA = Clamp01(a);
        }

        public void ResetDrawColor()
        {
            DrawR = 1.0;
            DrawG = 1.0;
            DrawB = 1.0;
            DrawA = 1.0;
        }

        public bool TryGetCachedObject(string kind, int handle, out DynValue value)
        {
            if (_scriptObjectCache.TryGetValue(kind + ":" + handle.ToString(), out DynValue? found) && found != null)
            {
                value = found;
                return true;
            }
            value = DynValue.Nil;
            return false;
        }

        public void CacheObject(string kind, int handle, DynValue value)
        {
            _scriptObjectCache[kind + ":" + handle.ToString()] = value;
        }

        public void ValidateSourceSyntax()
        {
            var validator = new Script(CoreModules.Preset_SoftSandbox);
            foreach (string file in EnumerateSourceFiles())
            {
                if (_source.ManifestPath != null && string.Equals(Path.GetFullPath(file), Path.GetFullPath(_source.ManifestPath), StringComparison.OrdinalIgnoreCase))
                    continue;
                validator.LoadString(File.ReadAllText(file), codeFriendlyName: file);
            }
        }

        public void UpdateStorage(double dt)
        {
            _storage?.Update(dt);
        }

        public void Load()
        {
            PrepareLoad();
            Activate();
        }

        public void PrepareLoad()
        {
            if (_script != null)
                throw new InvalidOperationException("Lua mod instance has already been prepared.");
            if (!IsApiCompatible(Manifest.Api))
                throw new InvalidOperationException($"Mod requires API '{Manifest.Api}', runtime provides '{RuntimeHost.ApiVersion}'.");

            _script = new Script(CoreModules.Preset_SoftSandbox);
            _script.Options.DebugPrint = message => RuntimeLog.Info($"[{Manifest.Id}] {message}");
            _storage = new StorageApi(_host, Manifest.Id, _script);
            Table sledders = ApiBuilder.Build(this);
            _script.Globals["sledders"] = sledders;
            _script.Globals["require"] = DynValue.NewCallback(RequireModule);

            string code = File.ReadAllText(_source.MainPath);
            _script.DoString(code, codeFriendlyName: _source.MainPath);

            SnapshotSourceTimes();
        }

        public void Activate()
        {
            if (_script == null || _storage == null)
                throw new InvalidOperationException("Lua mod must be prepared before activation.");
            if (Enabled)
                return;

            Enabled = true;
            _suspendedCallbacks.Clear();
            CallCanonical("onLoad", Array.Empty<object?>());
        }

        public void Unload(string reason)
        {
            if (_script == null) return;

            if (Enabled)
            {
                try
                {
                    CallCanonical("onUnload", new object?[] { reason });
                }
                catch
                {
                    // CallSafe already records script failures.
                }
            }

            try { _storage?.Save(); }
            catch (Exception ex) { RuntimeLog.Warn($"[{Manifest.Id}] Storage save during unload failed: {ex.Message}"); }

            SleddersGameBindings.ReleaseOverrides(Manifest.Id);

            Enabled = false;
            _keyBindings.Clear();
            _moduleCache.Clear();
            _scriptObjectCache.Clear();
            _suspendedCallbacks.Clear();
            Handles.Clear();
            _script = null;
            _storage = null;
        }

        public void Dispatch(string eventName, params object?[] args)
        {
            if (!Enabled || _script == null) return;

            string? canonical = CanonicalCallbackFor(eventName);
            if (!string.IsNullOrWhiteSpace(canonical))
                CallCanonical(canonical!, args);

        }

        public void DispatchKey(string key)
        {
            if (!Enabled || _script == null) return;
            CallCanonical("onKey", new object?[] { key });
        }

        public void Draw()
        {
            if (!Enabled || _script == null) return;
            ResetDrawColor();
            CallCanonical("onDraw", Array.Empty<object?>());
        }

        public int RegisterKeyBinding(string key, DynValue callback)
        {
            EnsureFunction(callback, "input.onPressed callback");
            int handle = _nextHandle++;
            _keyBindings.Add(new KeyBinding { Handle = handle, Key = key.Trim(), Callback = callback });
            return handle;
        }

        public bool RemoveKeyBinding(int handle) => _keyBindings.RemoveAll(x => x.Handle == handle) > 0;

        public void ProcessKeyBindings()
        {
            if (!Enabled || _script == null || _keyBindings.Count == 0) return;
            foreach (KeyBinding binding in _keyBindings.ToArray())
            {
                if (UnityBridge.GetKeyDown(binding.Key))
                    CallSafe($"key binding {binding.Key}", $"key:{binding.Handle}", binding.Callback, binding.Key);
            }
        }

        public bool HasSourceChanged()
        {
            var current = EnumerateSourceFiles().ToDictionary(path => path, path => SafeWriteTime(path), StringComparer.OrdinalIgnoreCase);
            if (current.Count != _sourceTimes.Count) return true;
            foreach (var pair in current)
            {
                if (!_sourceTimes.TryGetValue(pair.Key, out DateTime previous) || previous != pair.Value) return true;
            }
            return false;
        }

        public void DemandPermission(string permission)
        {
            if (!Manifest.HasPermission(permission))
                throw new ScriptRuntimeException($"Mod '{Manifest.Id}' does not have permission '{permission}'. Add it to manifest.lua permissions.");
        }

        public DynValue RequireModule(ScriptExecutionContext context, CallbackArguments args)
        {
            if (string.Equals(_source.Key, _source.MainPath, StringComparison.OrdinalIgnoreCase))
                throw new ScriptRuntimeException("require() is available to folder mods only. Move this single-file mod into its own folder.");
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("require(module) expects a module name string.");

            string moduleName = args[0].String.Trim();
            if (moduleName.Length == 0 || moduleName.IndexOf("..", StringComparison.Ordinal) >= 0)
                throw new ScriptRuntimeException("Invalid module name.");
            if (_moduleCache.TryGetValue(moduleName, out DynValue cached)) return cached;

            string bareName = moduleName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
                ? moduleName.Substring(0, moduleName.Length - 4)
                : moduleName;
            string relative = bareName.Replace('.', Path.DirectorySeparatorChar) + ".lua";
            string root = Path.GetFullPath(_source.ModuleRoot);
            string path = Path.GetFullPath(Path.Combine(root, relative));
            string rootedPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(rootedPrefix, StringComparison.OrdinalIgnoreCase)) throw new ScriptRuntimeException("Module path escapes the mod directory.");
            if (!File.Exists(path)) throw new ScriptRuntimeException($"Module '{moduleName}' not found at '{relative}'.");

            DynValue result = Script.DoString(File.ReadAllText(path), codeFriendlyName: path);
            if (result.IsNil()) result = DynValue.True;
            _moduleCache[moduleName] = result;
            return result;
        }

        public DynValue CallSafe(string context, DynValue callback, params object?[] args)
        {
            return CallSafe(context, "direct:" + context, callback, args);
        }

        private DynValue CallSafe(string context, string faultKey, DynValue callback, params object?[] args)
        {
            if (_script == null || !Enabled || _suspendedCallbacks.Contains(faultKey))
                return DynValue.Nil;

            try
            {
                return _script.Call(callback, args);
            }
            catch (InterpreterException ex)
            {
                string details = _host.Config.LogScriptStackTraces && !string.IsNullOrWhiteSpace(ex.DecoratedMessage)
                    ? ex.DecoratedMessage
                    : ex.Message;
                SuspendCallback(faultKey);
                RuntimeLog.Error($"[{Manifest.Id}] Lua fault in {context}: {details}");
                RuntimeLog.Warn($"[{Manifest.Id}] Suspended {context} until this mod hot reloads; other callbacks remain active.");
                return DynValue.Nil;
            }
            catch (Exception ex)
            {
                SuspendCallback(faultKey);
                RuntimeLog.Exception($"[{Manifest.Id}] Host fault in {context}", ex);
                RuntimeLog.Warn($"[{Manifest.Id}] Suspended {context} until this mod hot reloads; other callbacks remain active.");
                return DynValue.Nil;
            }
        }

        private void SuspendCallback(string faultKey)
        {
            _suspendedCallbacks.Add(faultKey);
        }

        private void CallCanonical(string callbackName, object?[] args)
        {
            if (_script == null || !Enabled)
                return;

            DynValue callback = _script.Globals.Get(callbackName);
            if (IsFunction(callback))
                CallSafe(callbackName, "canonical:" + callbackName, callback, args);
        }

        private static string? CanonicalCallbackFor(string eventName)
        {
            switch ((eventName ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "update": return "onTick";
                case "late_update": return "onLateTick";
                case "fixed_update": return "onFixedTick";
                case "scene_loaded": return "onSceneLoaded";
                case "scene_initialized": return "onSceneInitialized";
                case "scene_unloaded": return "onSceneUnloaded";
                case "sled_ready": return "onSledReady";
                case "sled_changed": return "onSledChanged";
                case "sled_lost": return "onSledLost";
                default: return null;
            }
        }

        private void SnapshotSourceTimes()
        {
            _sourceTimes.Clear();
            foreach (string file in EnumerateSourceFiles()) _sourceTimes[file] = SafeWriteTime(file);
        }

        private IEnumerable<string> EnumerateSourceFiles()
        {
            if (string.Equals(_source.Key, _source.MainPath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(_source.MainPath)) yield return Path.GetFullPath(_source.MainPath);
                yield break;
            }
            if (Directory.Exists(_source.ModuleRoot))
            {
                foreach (string file in Directory.EnumerateFiles(_source.ModuleRoot, "*.lua", SearchOption.AllDirectories))
                    yield return Path.GetFullPath(file);
            }
        }

        private static DateTime SafeWriteTime(string path)
        {
            try { return File.GetLastWriteTimeUtc(path); }
            catch { return DateTime.MinValue; }
        }

        private static void EnsureFunction(DynValue callback, string description)
        {
            if (!IsFunction(callback)) throw new ScriptRuntimeException($"{description} must be a function.");
        }

        private static bool IsFunction(DynValue value)
        {
            return value.Type == DataType.Function || value.Type == DataType.ClrFunction;
        }

        private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));

        private static bool IsApiCompatible(string requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return true;

            if (!TryParseApiVersion(requested, out int requestedMajor, out int requestedMinor) ||
                !TryParseApiVersion(RuntimeHost.ApiVersion, out int runtimeMajor, out int runtimeMinor))
                return false;

            return requestedMajor == runtimeMajor && requestedMinor <= runtimeMinor;
        }

        private static bool TryParseApiVersion(string value, out int major, out int minor)
        {
            major = 0;
            minor = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string[] parts = value.Trim().Split('.');
            if (parts.Length < 1 || parts.Length > 3 || !int.TryParse(parts[0], out major) || major < 0)
                return false;
            if (parts.Length >= 2 && (!int.TryParse(parts[1], out minor) || minor < 0))
                return false;
            if (parts.Length == 3 && (!int.TryParse(parts[2], out int patch) || patch < 0))
                return false;
            return true;
        }
    }
}
