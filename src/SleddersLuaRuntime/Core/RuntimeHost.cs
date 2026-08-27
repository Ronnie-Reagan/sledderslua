using System;
using System.Diagnostics;
using System.IO;
using MelonLoader.Utils;
using SleddersLuaRuntime.Api;

namespace SleddersLuaRuntime.Core
{
    internal sealed class RuntimeHost
    {
        public const string ApiVersion = BuildInfo.ApiVersion;
        public const string RuntimeVersion = BuildInfo.RuntimeVersion;

        private ScriptManager? _manager;
        private RuntimeConfig? _config;
        private string _gameRoot = string.Empty;
        private string _luaModsRoot = string.Empty;
        private string _dataRoot = string.Empty;
        private readonly Stopwatch _runtimeClock = Stopwatch.StartNew();
        private double _nextSledProbeSeconds;
        private object? _lastSled;
        private bool _sledProbeInitialized;

        public string GameRoot => _gameRoot;
        public string LuaModsRoot => _luaModsRoot;
        public string DataRoot => _dataRoot;
        public RuntimeConfig Config => _config ?? throw new InvalidOperationException("Runtime not initialized.");
        public ScriptManager Manager => _manager ?? throw new InvalidOperationException("Runtime not initialized.");
        public double UptimeSeconds => _runtimeClock.Elapsed.TotalSeconds;

        public void Initialize()
        {
            _gameRoot = ResolveGameRoot();
            _luaModsRoot = Path.Combine(_gameRoot, "LuaMods");
            _dataRoot = Path.Combine(MelonEnvironment.UserDataDirectory, "SleddersLua");

            Directory.CreateDirectory(_luaModsRoot);
            Directory.CreateDirectory(_dataRoot);

            _config = RuntimeConfig.LoadOrCreate(Path.Combine(_dataRoot, "config.json"));
            ReflectionBridge.Initialize();
            SleddersBindingResolver.Initialize();
            UnityBridge.Initialize();

            RuntimeLog.Info($"Runtime {RuntimeVersion}, API {ApiVersion}");
            RuntimeLog.Info($"Game root: {_gameRoot}");
            RuntimeLog.Info($"Lua mods: {_luaModsRoot}");
            RuntimeLog.Info($"Hot reload: {Config.HotReload} ({Config.ScanIntervalSeconds:0.##}s scan)");

            _manager = new ScriptManager(this);
            _manager.InitialLoad();
        }

        public void Update()
        {
            if (_manager == null)
                return;

            PollSledLifecycle();
            _manager.Update(UnityBridge.DeltaTime);
        }

        public void Draw()
        {
            _manager?.Draw();
        }

        public void LateUpdate()
        {
            // Reapply persistent overrides after the game update.
            SleddersGameBindings.ApplyManagedStates();
            _manager?.Dispatch("late_update", UnityBridge.DeltaTime);
        }

        public void FixedUpdate()
        {
            _manager?.Dispatch("fixed_update", UnityBridge.FixedDeltaTime);
        }

        public void SceneLoaded(int buildIndex, string sceneName)
        {
            _manager?.Dispatch("scene_loaded", buildIndex, sceneName);
        }

        public void SceneInitialized(int buildIndex, string sceneName)
        {
            SleddersGameBindings.InvalidateCache();
            _sledProbeInitialized = false;
            _lastSled = null;
            _nextSledProbeSeconds = 0.0;
            _manager?.Dispatch("scene_initialized", buildIndex, sceneName);
        }

        public void SceneUnloaded(int buildIndex, string sceneName)
        {
            SleddersGameBindings.InvalidateCache();
            _manager?.Dispatch("scene_unloaded", buildIndex, sceneName);
        }

        public void Shutdown()
        {
            _manager?.Shutdown();
            _manager = null;
            RuntimeLog.Info("Runtime shut down.");
        }

        private void PollSledLifecycle()
        {
            double now = _runtimeClock.Elapsed.TotalSeconds;
            if (now < _nextSledProbeSeconds)
                return;
            _nextSledProbeSeconds = now + 0.5;

            object? current = SleddersGameBindings.FindLocalSled();
            if (!_sledProbeInitialized)
            {
                _sledProbeInitialized = true;
                _lastSled = current;
                if (current != null)
                    _manager?.Dispatch("sled_ready");
                return;
            }

            if (ReferenceEquals(current, _lastSled))
                return;

            bool hadSled = _lastSled != null;
            bool hasSled = current != null;
            _lastSled = current;

            _manager?.Dispatch("sled_changed");
            if (!hadSled && hasSled)
                _manager?.Dispatch("sled_ready");
            else if (hadSled && !hasSled)
                _manager?.Dispatch("sled_lost");
        }

        private static string ResolveGameRoot()
        {
            string root = MelonEnvironment.GameRootDirectory;
            if (string.IsNullOrWhiteSpace(root))
                throw new InvalidOperationException("MelonLoader did not provide a game root directory.");
            return root;
        }
    }
}
