using System;
using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class SceneApi
    {
        public static Table Build(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("find", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string typeName = FrameworkApiUtil.RequireString(args, offset, "scene.find(typeName [, max])");
                int max = args.Count > offset + 1
                    ? FrameworkApiUtil.RequireInt(args, offset + 1, "scene.find(typeName [, max])", 1, 512)
                    : 64;
                return ObjectArray(mod, Find(typeName, null, max));
            }));
            table.Set("findNamed", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string typeName = FrameworkApiUtil.RequireString(args, offset, "scene.findNamed(typeName, name [, max])");
                string name = FrameworkApiUtil.RequireString(args, offset + 1, "scene.findNamed(typeName, name [, max])");
                int max = args.Count > offset + 2
                    ? FrameworkApiUtil.RequireInt(args, offset + 2, "scene.findNamed(typeName, name [, max])", 1, 512)
                    : 64;
                return ObjectArray(mod, Find(typeName, name, max));
            }));
            table.Set("getLocalSled", DynValue.NewCallback((ctx, args) =>
            {
                object? sled = SleddersGameBindings.FindLocalSled();
                return sled == null ? DynValue.Nil : Wrap(mod, sled);
            }));
            table.Set("getLocalPlayer", DynValue.NewCallback((ctx, args) =>
            {
                object? player = SleddersGameBindings.FindPlayerObject();
                return player == null ? DynValue.Nil : Wrap(mod, player);
            }));
            return table;
        }

        public static DynValue Wrap(LuaModInstance mod, object value)
        {
            int handle = mod.Handles.Add(value);
            if (mod.TryGetCachedObject("sceneObject", handle, out DynValue cached))
                return cached;

            var table = new Table(mod.Script);
            table.Set("__handle", DynValue.NewNumber(handle));
            table.Set("__type", DynValue.NewString("sceneObject"));
            table.Set("isValid", DynValue.NewCallback((ctx, args) =>
                DynValue.NewBoolean(FrameworkApiUtil.Resolve(mod, handle) != null)));
            table.Set("getName", DynValue.NewCallback((ctx, args) =>
                DynValue.NewString(SleddersGameBindings.GetFriendlyName(
                    FrameworkApiUtil.RequireObject(mod, handle, "scene object")))));
            table.Set("getType", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "scene object");
                return DynValue.NewString(live.GetType().FullName ?? live.GetType().Name);
            }));
            table.Set("getActive", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "scene object");
                bool? active = GetActive(live);
                return active.HasValue ? DynValue.NewBoolean(active.Value) : DynValue.Nil;
            }));
            table.Set("setActive", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "scene object");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                return DynValue.NewBoolean(SetActive(
                    live,
                    FrameworkApiUtil.RequireBool(args, offset, "sceneObject.setActive(active)")));
            }));
            table.Set("getTransform", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "scene object");
                object? transform = SleddersGameBindings.GetTransform(live);
                return transform == null ? DynValue.Nil : TransformApi.Wrap(mod, transform);
            }));
            table.Set("getPos", DynValue.NewCallback((ctx, args) =>
                ValueConverter.ToDynValue(mod, SleddersGameBindings.GetPosition(
                    FrameworkApiUtil.RequireObject(mod, handle, "scene object")))));
            table.Set("setPos", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "scene object");
                object? transform = SleddersGameBindings.GetTransform(live);
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? position = FrameworkApiUtil.ReadVector3(mod, args, offset, "sceneObject.setPos(vector3)");
                return DynValue.NewBoolean(transform != null && position != null &&
                    SleddersGameBindings.TrySetAny(transform, position, "position"));
            }));
            table.Set("getRot", DynValue.NewCallback((ctx, args) =>
                ValueConverter.ToDynValue(mod, SleddersGameBindings.GetRotation(
                    FrameworkApiUtil.RequireObject(mod, handle, "scene object")))));
            table.Set("setRot", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "scene object");
                object? transform = SleddersGameBindings.GetTransform(live);
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? rotation = FrameworkApiUtil.ReadEulerQuaternion(mod, args, offset, "sceneObject.setRot(eulerVector3)");
                return DynValue.NewBoolean(transform != null && rotation != null &&
                    SleddersGameBindings.TrySetAny(transform, rotation, "rotation"));
            }));
            table.Set("getRenderers", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "scene object");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                int max = args.Count > offset
                    ? FrameworkApiUtil.RequireInt(args, offset, "sceneObject.getRenderers(max)", 1, 512)
                    : 128;
                return VisualApi.GetRenderers(mod, live, max);
            }));
            table.Set("getAudioSources", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "scene object");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                int max = args.Count > offset
                    ? FrameworkApiUtil.RequireInt(args, offset, "sceneObject.getAudioSources(max)", 1, 256)
                    : 64;
                return AudioSources(mod, live, max);
            }));
            table.Set("getComponents", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "scene object");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string typeName = FrameworkApiUtil.RequireString(args, offset, "sceneObject.getComponents(typeName [, max])");
                int max = args.Count > offset + 1
                    ? FrameworkApiUtil.RequireInt(args, offset + 1, "sceneObject.getComponents(typeName [, max])", 1, 256)
                    : 64;
                Type? componentType = ReflectionBridge.FindTypeExact(typeName);
                if (componentType == null)
                    return DynValue.NewTable(new Table(mod.Script));
                IReadOnlyList<object> values = ReflectionBridge.GetComponentsInChildren(live, componentType, true, max);
                return ObjectArray(mod, values);
            }));

            DynValue wrapped = DynValue.NewTable(table);
            mod.CacheObject("sceneObject", handle, wrapped);
            return wrapped;
        }

        private static IReadOnlyList<object> Find(string typeName, string? name, int max)
        {
            Type? type = ReflectionBridge.FindTypeExact(typeName);
            if (type == null)
                return Array.Empty<object>();
            var result = new List<object>();
            foreach (object value in ReflectionBridge.FindObjectsOfType(type, Math.Min(2048, max * 8)))
            {
                if (name != null && !string.Equals(
                        SleddersGameBindings.GetFriendlyName(value),
                        name,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(value);
                if (result.Count >= max)
                    break;
            }
            return result;
        }

        private static DynValue ObjectArray(LuaModInstance mod, IEnumerable<object> values)
        {
            var table = new Table(mod.Script);
            int i = 1;
            foreach (object value in values)
                table.Set(i++, Wrap(mod, value));
            return DynValue.NewTable(table);
        }

        private static DynValue AudioSources(LuaModInstance mod, object target, int max)
        {
            Type? sourceType = ReflectionBridge.FindTypeExact("UnityEngine.AudioSource");
            var table = new Table(mod.Script);
            if (sourceType == null)
                return DynValue.NewTable(table);
            int i = 1;
            foreach (object source in ReflectionBridge.GetComponentsInChildren(target, sourceType, true, max))
                table.Set(i++, AudioRuntimeApi.WrapSource(mod, source));
            return DynValue.NewTable(table);
        }

        private static bool? GetActive(object target)
        {
            object? go = target.GetType().FullName == "UnityEngine.GameObject"
                ? target
                : SleddersGameBindings.GetGameObject(target);
            if (go != null && SleddersGameBindings.TryGetAny(go, out object? raw, "activeSelf") && raw is bool active)
                return active;
            return null;
        }

        private static bool SetActive(object target, bool active)
        {
            object? go = target.GetType().FullName == "UnityEngine.GameObject"
                ? target
                : SleddersGameBindings.GetGameObject(target);
            return go != null && SleddersGameBindings.TryCallAny(
                go, new[] { "SetActive" }, new object?[] { active }, out _);
        }
    }
}
