using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class AssetApi
    {
        private const long MaxBundleBytes = 1024L * 1024L * 1024L;

        public static Table Build(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("loadBundle", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string relative = FrameworkApiUtil.RequireString(args, offset, "assets.loadBundle(relativePath)");
                object? bundle = LoadBundle(mod, relative);
                return bundle == null ? DynValue.Nil : WrapBundle(mod, bundle);
            }));
            table.Set("findLoaded", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string typeName = FrameworkApiUtil.RequireString(args, offset, "assets.findLoaded(typeName [, max])");
                int max = args.Count > offset + 1
                    ? FrameworkApiUtil.RequireInt(args, offset + 1, "assets.findLoaded(typeName [, max])", 1, 512)
                    : 64;
                Type? type = ReflectionBridge.FindTypeExact(typeName);
                var result = new Table(mod.Script);
                if (type == null) return DynValue.NewTable(result);
                int i = 1;
                foreach (object value in ReflectionBridge.FindObjectsOfType(type, max))
                    result.Set(i++, WrapAsset(mod, value));
                return DynValue.NewTable(result);
            }));
            table.Set("instantiate", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                if (args.Count <= offset || args[offset].Type != DataType.Table)
                    throw new ScriptRuntimeException("assets.instantiate(asset [, position, rotation]) expects an asset wrapper.");
                object? asset = ObjectProxyBuilder.DynToRaw(mod, args[offset]);
                return asset == null ? DynValue.Nil : Instantiate(mod, asset, args, offset + 1);
            }));
            return table;
        }

        private static object? LoadBundle(LuaModInstance mod, string relative)
        {
            string root = Path.GetFullPath(mod.ModRoot);
            string path = Path.GetFullPath(Path.Combine(root, relative));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new ScriptRuntimeException("assets.loadBundle(path) only reads files inside the mod directory.");
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaxBundleBytes)
                throw new ScriptRuntimeException("AssetBundle is empty or exceeds the 1 GiB safety limit.");
            Type? bundleType = ReflectionBridge.FindTypeExact("UnityEngine.AssetBundle");
            if (bundleType == null) return null;
            object? bundle;
            try { bundle = ReflectionBridge.CallStatic(bundleType, "LoadFromFile", new object?[] { path }); }
            catch { bundle = null; }
            if (bundle != null)
                RuntimeResourceRegistry.Register(mod.StateOwnerToken, () =>
                {
                    try { ReflectionBridge.Call(bundle, "Unload", new object?[] { false }); } catch { }
                });
            return bundle;
        }

        private static DynValue WrapBundle(LuaModInstance mod, object bundle)
        {
            int handle = mod.Handles.Add(bundle);
            if (mod.TryGetCachedObject("assetBundle", handle, out DynValue cached)) return cached;
            var table = new Table(mod.Script);
            table.Set("__handle", DynValue.NewNumber(handle));
            table.Set("__type", DynValue.NewString("assetBundle"));
            table.Set("isValid", DynValue.NewCallback((ctx, args) => DynValue.NewBoolean(FrameworkApiUtil.Resolve(mod, handle) != null)));
            table.Set("getName", DynValue.NewCallback((ctx, args) => DynValue.NewString(SleddersGameBindings.GetFriendlyName(FrameworkApiUtil.RequireObject(mod, handle, "asset bundle")))));
            table.Set("getAssetNames", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "asset bundle");
                var result = new Table(mod.Script);
                if (ReflectionBridge.TryCall(live, "GetAllAssetNames", Array.Empty<object?>(), out object? raw) && raw is IEnumerable names)
                {
                    int i = 1;
                    foreach (object? name in names) if (name != null) result.Set(i++, DynValue.NewString(name.ToString() ?? string.Empty));
                }
                return DynValue.NewTable(result);
            }));
            table.Set("load", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "asset bundle");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string name = FrameworkApiUtil.RequireString(args, offset, "assetBundle.load(name)");
                return ReflectionBridge.TryCall(live, "LoadAsset", new object?[] { name }, out object? asset) && asset != null
                    ? WrapAsset(mod, asset)
                    : DynValue.Nil;
            }));
            table.Set("unload", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "asset bundle");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                bool all = args.Count > offset && !args[offset].IsNil()
                    ? FrameworkApiUtil.RequireBool(args, offset, "assetBundle.unload(unloadLoadedObjects)")
                    : false;
                return DynValue.NewBoolean(ReflectionBridge.TryCall(live, "Unload", new object?[] { all }, out _));
            }));
            DynValue wrapped = DynValue.NewTable(table);
            mod.CacheObject("assetBundle", handle, wrapped);
            return wrapped;
        }

        private static DynValue WrapAsset(LuaModInstance mod, object asset)
        {
            int handle = mod.Handles.Add(asset);
            if (mod.TryGetCachedObject("asset", handle, out DynValue cached)) return cached;
            var table = new Table(mod.Script);
            table.Set("__handle", DynValue.NewNumber(handle));
            table.Set("__type", DynValue.NewString("asset"));
            table.Set("isValid", DynValue.NewCallback((ctx, args) => DynValue.NewBoolean(FrameworkApiUtil.Resolve(mod, handle) != null)));
            table.Set("getName", DynValue.NewCallback((ctx, args) => DynValue.NewString(SleddersGameBindings.GetFriendlyName(FrameworkApiUtil.RequireObject(mod, handle, "asset")))));
            table.Set("getType", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "asset");
                return DynValue.NewString(live.GetType().FullName ?? live.GetType().Name);
            }));
            table.Set("instantiate", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "asset");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                return Instantiate(mod, live, args, offset);
            }));
            table.Set("getRenderers", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "asset");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                int max = args.Count > offset
                    ? FrameworkApiUtil.RequireInt(args, offset, "asset.getRenderers(max)", 1, 512)
                    : 128;
                return VisualApi.GetRenderers(mod, live, max);
            }));
            DynValue wrapped = DynValue.NewTable(table);
            mod.CacheObject("asset", handle, wrapped);
            return wrapped;
        }

        private static DynValue Instantiate(LuaModInstance mod, object asset, CallbackArguments args, int offset)
        {
            Type? objectType = ReflectionBridge.FindTypeExact("UnityEngine.Object");
            if (objectType == null) return DynValue.Nil;
            object? clone;
            try
            {
                if (args.Count > offset && !args[offset].IsNil())
                {
                    object? position = FrameworkApiUtil.ReadVector3(mod, args, offset, "instantiate(asset [, position, rotation])");
                    int rotationIndex = args[offset].Type == DataType.Table ? offset + 1 : offset + 3;
                    object? rotation = args.Count > rotationIndex && !args[rotationIndex].IsNil()
                        ? FrameworkApiUtil.ReadEulerQuaternion(mod, args, rotationIndex, "instantiate(asset [, position, rotation])")
                        : CreateIdentityQuaternion();
                    clone = position != null && rotation != null
                        ? ReflectionBridge.CallStatic(objectType, "Instantiate", new object?[] { asset, position, rotation })
                        : null;
                }
                else
                {
                    clone = ReflectionBridge.CallStatic(objectType, "Instantiate", new object?[] { asset });
                }
            }
            catch { clone = null; }
            if (clone == null) return DynValue.Nil;
            RuntimeResourceRegistry.Register(mod.StateOwnerToken, () => DestroyUnity(clone));
            return SceneApi.Wrap(mod, clone);
        }

        private static object? CreateIdentityQuaternion()
        {
            Type? type = ReflectionBridge.FindTypeExact("UnityEngine.Quaternion");
            if (type == null) return null;
            try { return ReflectionBridge.GetStaticMember(type, "identity"); }
            catch { return Activator.CreateInstance(type); }
        }

        private static void DestroyUnity(object value)
        {
            Type? objectType = ReflectionBridge.FindTypeExact("UnityEngine.Object");
            if (objectType == null) return;
            try { ReflectionBridge.CallStatic(objectType, "Destroy", new object?[] { value }); } catch { }
        }
    }
}
