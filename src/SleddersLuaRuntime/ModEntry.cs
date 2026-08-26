using MelonLoader;
using SleddersLuaRuntime.Core;

[assembly: MelonInfo(typeof(SleddersLuaRuntime.ModEntry), "Sledders Lua Runtime", SleddersLuaRuntime.Core.BuildInfo.RuntimeVersion, "Don")]
[assembly: MelonGame("Hanki Games", "Sledders")]

namespace SleddersLuaRuntime
{
    public sealed class ModEntry : MelonMod
    {
        private RuntimeHost? _runtime;

        public override void OnInitializeMelon()
        {
            _runtime = new RuntimeHost();
            _runtime.Initialize();
        }

        public override void OnUpdate()
        {
            _runtime?.Update();
        }

        public override void OnLateUpdate()
        {
            _runtime?.LateUpdate();
        }

        public override void OnGUI()
        {
            _runtime?.Draw();
        }

        public override void OnFixedUpdate()
        {
            _runtime?.FixedUpdate();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _runtime?.SceneLoaded(buildIndex, sceneName);
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            _runtime?.SceneInitialized(buildIndex, sceneName);
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            _runtime?.SceneUnloaded(buildIndex, sceneName);
        }

        public override void OnDeinitializeMelon()
        {
            _runtime?.Shutdown();
            _runtime = null;
        }
    }
}
