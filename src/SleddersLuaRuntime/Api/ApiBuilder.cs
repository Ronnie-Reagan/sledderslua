using System;
using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class ApiBuilder
    {
        public static Table Build(LuaModInstance mod)
        {
            var root = new Table(mod.Script);

            var api = new Table(mod.Script);
            api.Set("version", DynValue.NewString(RuntimeHost.ApiVersion));
            api.Set("runtimeVersion", DynValue.NewString(RuntimeHost.RuntimeVersion));
            api.Set("has", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, api);
                string name = RequireString(args, offset, "api.has(capability)");
                return DynValue.NewBoolean(HasCapability(mod, name));
            }));
            root.Set("api", DynValue.NewTable(api));

            var modInfo = new Table(mod.Script);
            modInfo.Set("id", DynValue.NewString(mod.Manifest.Id));
            modInfo.Set("name", DynValue.NewString(mod.Manifest.Name));
            modInfo.Set("version", DynValue.NewString(mod.Manifest.Version));
            modInfo.Set("author", DynValue.NewString(mod.Manifest.Author));
            root.Set("mod", DynValue.NewTable(modInfo));

            root.Set("log", DynValue.NewTable(BuildLog(mod)));
            root.Set("input", DynValue.NewTable(BuildInput(mod)));
            root.Set("game", DynValue.NewTable(BuildGame(mod)));
            root.Set("time", DynValue.NewTable(BuildTime(mod)));
            root.Set("player", DynValue.NewTable(GameplayApi.BuildPlayer(mod)));
            root.Set("sled", DynValue.NewTable(GameplayApi.BuildSledService(mod)));
            root.Set("camera", DynValue.NewTable(GameplayApi.BuildCamera(mod)));
            root.Set("window", DynValue.NewTable(WindowApi.Build(mod)));
            root.Set("screen", DynValue.NewTable(ScreenApi.Build(mod)));
            root.Set("audio", DynValue.NewTable(AudioApi.Build(mod)));
            root.Set("hud", DynValue.NewTable(HudApi.Build(mod)));
            root.Set("world", DynValue.NewTable(WorldApi.Build(mod)));
            root.Set("visual", DynValue.NewTable(VisualApi.BuildService(mod)));
            root.Set("scene", DynValue.NewTable(SceneApi.Build(mod)));
            root.Set("physics", DynValue.NewTable(PhysicsWorldApi.Build(mod)));
            root.Set("assets", DynValue.NewTable(AssetApi.Build(mod)));

            root.Set("storage", DynValue.NewTable(BuildStorage(mod)));
            root.Set("dev", DynValue.NewTable(BuildDev(mod)));

            root.Set("vector3", DynValue.NewCallback((ctx, args) =>
            {
                double x = NumberOr(args, 0, 0.0);
                double y = NumberOr(args, 1, 0.0);
                double z = NumberOr(args, 2, 0.0);
                return DynValue.NewTable(GameplayApi.MakeVector3(mod.Script, x, y, z));
            }));

            root.Set("color", DynValue.NewCallback((ctx, args) =>
            {
                double r = NumberOr(args, 0, 1.0);
                double g = NumberOr(args, 1, 1.0);
                double b = NumberOr(args, 2, 1.0);
                double a = NumberOr(args, 3, 1.0);
                return DynValue.NewTable(ScreenApi.MakeColor(mod.Script, r, g, b, a));
            }));

            return root;
        }

        private static Table BuildLog(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("info", DynValue.NewCallback((ctx, args) =>
            {
                RuntimeLog.Info($"[{mod.Manifest.Id}] {JoinArgs(args, MethodOffset(args, table))}");
                return DynValue.Nil;
            }));
            table.Set("warn", DynValue.NewCallback((ctx, args) =>
            {
                RuntimeLog.Warn($"[{mod.Manifest.Id}] {JoinArgs(args, MethodOffset(args, table))}");
                return DynValue.Nil;
            }));
            table.Set("error", DynValue.NewCallback((ctx, args) =>
            {
                RuntimeLog.Error($"[{mod.Manifest.Id}] {JoinArgs(args, MethodOffset(args, table))}");
                return DynValue.Nil;
            }));
            return table;
        }

        private static Table BuildInput(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("wasPressed", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                return DynValue.NewBoolean(UnityBridge.GetKeyDown(RequireString(args, offset, "input.wasPressed(key)")));
            }));
            table.Set("isDown", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                return DynValue.NewBoolean(UnityBridge.GetKey(RequireString(args, offset, "input.isDown(key)")));
            }));
            table.Set("getAxis", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                return DynValue.NewNumber(UnityBridge.GetAxis(RequireString(args, offset, "input.getAxis(name)")));
            }));
            table.Set("onPressed", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                string key = RequireString(args, offset, "input.onPressed(key, callback)");
                if (args.Count <= offset + 1)
                    throw new ScriptRuntimeException("input.onPressed(key, callback) requires a callback.");
                return DynValue.NewNumber(mod.RegisterKeyBinding(key, args[offset + 1]));
            }));
            table.Set("off", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                return DynValue.NewBoolean(mod.RemoveKeyBinding(RequireInt(args, offset, "input.off(handle)")));
            }));

            NativeInputApi.Enhance(mod, table);
            return table;
        }

        private static Table BuildGame(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("getVersion", DynValue.NewCallback((ctx, args) => DynValue.NewString(UnityBridge.GameVersion)));
            table.Set("getScene", DynValue.NewCallback((ctx, args) => DynValue.NewString(UnityBridge.ActiveSceneName)));
            return table;
        }

        private static Table BuildTime(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("getDelta", DynValue.NewCallback((ctx, args) => DynValue.NewNumber(UnityBridge.DeltaTime)));
            table.Set("getFixedDelta", DynValue.NewCallback((ctx, args) => DynValue.NewNumber(UnityBridge.FixedDeltaTime)));
            table.Set("getDeltaMs", DynValue.NewCallback((ctx, args) => DynValue.NewNumber(UnityBridge.DeltaTime * 1000.0)));
            table.Set("getFixedDeltaMs", DynValue.NewCallback((ctx, args) => DynValue.NewNumber(UnityBridge.FixedDeltaTime * 1000.0)));
            DynValue getFps = DynValue.NewCallback((ctx, args) =>
            {
                double dt = UnityBridge.DeltaTime;
                return dt > 0.0000001 ? DynValue.NewNumber(1.0 / dt) : DynValue.Nil;
            });
            table.Set("getFps", getFps);
            table.Set("getFPS", getFps);
            table.Set("getUptime", DynValue.NewCallback((ctx, args) => DynValue.NewNumber(mod.Host.UptimeSeconds)));
            return table;
        }

        private static Table BuildStorage(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("get", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                string key = RequireString(args, offset, "storage.get(key [, default])");
                DynValue fallback = args.Count > offset + 1 ? args[offset + 1] : DynValue.Nil;
                return mod.Storage.Get(key, fallback);
            }));
            table.Set("set", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                string key = RequireString(args, offset, "storage.set(key, value)");
                if (args.Count <= offset + 1) throw new ScriptRuntimeException("storage.set(key, value) requires a value.");
                mod.Storage.Set(key, args[offset + 1]);
                return DynValue.True;
            }));
            table.Set("delete", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                return DynValue.NewBoolean(mod.Storage.Delete(RequireString(args, offset, "storage.delete(key)")));
            }));
            table.Set("save", DynValue.NewCallback((ctx, args) =>
            {
                mod.Storage.Save();
                return DynValue.True;
            }));
            return table;
        }

        private static Table BuildDev(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("type", DynValue.NewCallback((ctx, args) =>
            {
                mod.DemandPermission("dev");
                int offset = MethodOffset(args, table);
                string query = RequireString(args, offset, "dev.type(nameOrQuery)");
                Type? exact = ReflectionBridge.FindTypeExact(query);
                Type? type = exact != null && ReflectionBridge.IsDeveloperTypeAllowed(exact)
                    ? exact
                    : ReflectionBridge.FindDeveloperTypes(query, 1).FirstOrDefault();
                return type == null ? DynValue.Nil : TypeProxyBuilder.Wrap(mod, type);
            }));
            table.Set("types", DynValue.NewCallback((ctx, args) =>
            {
                mod.DemandPermission("dev");
                int offset = MethodOffset(args, table);
                string query = RequireString(args, offset, "dev.types(query [, max])");
                int max = args.Count > offset + 1 && args[offset + 1].Type == DataType.Number
                    ? Compat.Clamp((int)args[offset + 1].Number, 1, 512)
                    : mod.Host.Config.MaxDiscoveryResults;
                return TypeProxyBuilder.ToTypeArray(mod, ReflectionBridge.FindDeveloperTypes(query, max));
            }));
            table.Set("objects", DynValue.NewCallback((ctx, args) =>
            {
                mod.DemandPermission("dev");
                int offset = MethodOffset(args, table);
                string query = RequireString(args, offset, "dev.objects(query [, max])");
                int max = args.Count > offset + 1 && args[offset + 1].Type == DataType.Number
                    ? Compat.Clamp((int)args[offset + 1].Number, 1, 256)
                    : mod.Host.Config.MaxDiscoveryResults;
                return ObjectProxyBuilder.ToObjectArray(mod, ReflectionBridge.FindObjects(query, max));
            }));
            return table;
        }

        private static bool HasCapability(LuaModInstance mod, string name)
        {
            switch ((name ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "sled":
                case "sled.physics":
                case "sled.vehicle":
                case "sled.tuning":
                case "sled.visuals":
                case "player":
                case "camera":
                case "camera.projection":
                case "camera.free":
                case "camera.photo":
                case "hud":
                case "audio":
                case "audio.sources":
                case "audio.wav":
                case "audio.native_sfx":
                case "audio.presets":
                case "visual.materials":
                case "scene":
                case "scene.objects":
                case "physics":
                case "physics.queries":
                case "assets":
                case "assets.bundles":
                case "input.native":
                case "world":
                case "world.snow":
                case "world.weather":
                case "world.time":
                case "world.fuel":
                case "storage":
                    return true;
                case "dev":
                    return mod.Host.Config.EnableDevApi && mod.Manifest.HasPermission("dev");
                default:
                    return false;
            }
        }

        private static string RequireString(CallbackArguments args, int index, string usage)
        {
            if (args.Count <= index || args[index].Type != DataType.String || string.IsNullOrWhiteSpace(args[index].String))
                throw new ScriptRuntimeException(usage + " expects a non-empty string.");
            return args[index].String.Trim();
        }

        private static int RequireInt(CallbackArguments args, int index, string usage)
        {
            if (args.Count <= index || args[index].Type != DataType.Number)
                throw new ScriptRuntimeException(usage + " expects an integer.");
            double raw = args[index].Number;
            if (double.IsNaN(raw) || double.IsInfinity(raw) || raw < int.MinValue || raw > int.MaxValue || Math.Abs(raw - Math.Round(raw)) > 0.0000001)
                throw new ScriptRuntimeException(usage + " expects a finite integer.");
            return (int)raw;
        }

        private static int MethodOffset(CallbackArguments args, Table table)
        {
            return args.Count > 0 && args[0].Type == DataType.Table && ReferenceEquals(args[0].Table, table) ? 1 : 0;
        }

        private static string JoinArgs(CallbackArguments args, int offset = 0)
        {
            var pieces = new List<string>();
            for (int i = offset; i < args.Count; i++) pieces.Add(args[i].ToPrintString());
            return string.Join(" ", pieces);
        }

        private static double NumberOr(CallbackArguments args, int index, double fallback)
        {
            if (args.Count <= index || args[index].IsNil()) return fallback;
            if (args[index].Type != DataType.Number || double.IsNaN(args[index].Number) || double.IsInfinity(args[index].Number))
                throw new ScriptRuntimeException("Expected a finite number.");
            return args[index].Number;
        }

    }
}
