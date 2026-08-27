using System;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class TransformApi
    {
        public static DynValue Wrap(LuaModInstance mod, object transform)
        {
            int handle = mod.Handles.Add(transform);
            if (mod.TryGetCachedObject("transform", handle, out DynValue cached)) return cached;
            var table = new Table(mod.Script);
            table.Set("__handle", DynValue.NewNumber(handle));
            table.Set("__type", DynValue.NewString("transform"));
            table.Set("isValid", DynValue.NewCallback((ctx, args) => DynValue.NewBoolean(FrameworkApiUtil.Resolve(mod, handle) != null)));
            table.Set("getName", DynValue.NewCallback((ctx, args) => DynValue.NewString(SleddersGameBindings.GetFriendlyName(FrameworkApiUtil.RequireObject(mod, handle, "transform")))));

            AddVector(table, mod, handle, "Pos", "position", false);
            AddVector(table, mod, handle, "LocalPos", "localPosition", false);
            AddVector(table, mod, handle, "Scale", "localScale", false);
            AddRotation(table, mod, handle, "Rot", "rotation");
            AddRotation(table, mod, handle, "LocalRot", "localRotation");

            table.Set("getForward", DynValue.NewCallback((ctx, args) => GetVector(mod, handle, "forward")));
            table.Set("getUp", DynValue.NewCallback((ctx, args) => GetVector(mod, handle, "up")));
            table.Set("getRight", DynValue.NewCallback((ctx, args) => GetVector(mod, handle, "right")));
            table.Set("getChildCount", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "transform");
                return SleddersGameBindings.TryGetAnyOrGetter(live, out object? raw, "childCount") && SleddersGameBindings.ToDouble(raw) is double n
                    ? DynValue.NewNumber(n) : DynValue.Nil;
            }));
            table.Set("getChild", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "transform");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                int index = FrameworkApiUtil.RequireInt(args, offset, "transform.getChild(index)", 1, 4096) - 1;
                return ReflectionBridge.TryCall(live, "GetChild", new object?[] { index }, out object? child) && child != null
                    ? Wrap(mod, child) : DynValue.Nil;
            }));
            table.Set("transformPoint", DynValue.NewCallback((ctx, args) => CallVectorMethod(mod, table, handle, args, "TransformPoint")));
            table.Set("inverseTransformPoint", DynValue.NewCallback((ctx, args) => CallVectorMethod(mod, table, handle, args, "InverseTransformPoint")));
            table.Set("transformDirection", DynValue.NewCallback((ctx, args) => CallVectorMethod(mod, table, handle, args, "TransformDirection")));
            table.Set("inverseTransformDirection", DynValue.NewCallback((ctx, args) => CallVectorMethod(mod, table, handle, args, "InverseTransformDirection")));

            DynValue wrapped = DynValue.NewTable(table);
            mod.CacheObject("transform", handle, wrapped);
            return wrapped;
        }

        private static void AddVector(Table table, LuaModInstance mod, int handle, string stem, string member, bool readOnly)
        {
            table.Set("get" + stem, DynValue.NewCallback((ctx, args) => GetVector(mod, handle, member)));
            if (readOnly) return;
            table.Set("set" + stem, DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "transform");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? vector = FrameworkApiUtil.ReadVector3(mod, args, offset, "transform.set" + stem + "(vector3)");
                return DynValue.NewBoolean(vector != null && SleddersGameBindings.TrySetAny(live, vector, member));
            }));
        }

        private static void AddRotation(Table table, LuaModInstance mod, int handle, string stem, string member)
        {
            table.Set("get" + stem, DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "transform");
                if (!SleddersGameBindings.TryGetAny(live, out object? q, member) || q == null) return DynValue.Nil;
                return SleddersGameBindings.TryGetAny(q, out object? euler, "eulerAngles") ? ValueConverter.ToDynValue(mod, euler) : ValueConverter.ToDynValue(mod, q);
            }));
            table.Set("set" + stem, DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "transform");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? q = FrameworkApiUtil.ReadEulerQuaternion(mod, args, offset, "transform.set" + stem + "(eulerVector3)");
                return DynValue.NewBoolean(q != null && SleddersGameBindings.TrySetAny(live, q, member));
            }));
        }

        private static DynValue GetVector(LuaModInstance mod, int handle, string member)
        {
            object live = FrameworkApiUtil.RequireObject(mod, handle, "transform");
            return SleddersGameBindings.TryGetAny(live, out object? value, member) ? ValueConverter.ToDynValue(mod, value) : DynValue.Nil;
        }

        private static DynValue CallVectorMethod(LuaModInstance mod, Table table, int handle, CallbackArguments args, string method)
        {
            object live = FrameworkApiUtil.RequireObject(mod, handle, "transform");
            int offset = FrameworkApiUtil.MethodOffset(args, table);
            object? value = FrameworkApiUtil.ReadVector3(mod, args, offset, "transform." + char.ToLowerInvariant(method[0]) + method.Substring(1) + "(vector3)");
            return value != null && ReflectionBridge.TryCall(live, method, new object?[] { value }, out object? result) && result != null
                ? ValueConverter.ToDynValue(mod, result) : DynValue.Nil;
        }
    }
}
