using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class NativeInputApi
    {
        public static void Enhance(LuaModInstance mod, Table input)
        {
            var native = new Table(mod.Script);
            native.Set("actions", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, native);
                int max = args.Count > offset
                    ? FrameworkApiUtil.RequireInt(args, offset, "input.native.actions(max)", 1, 512)
                    : 256;
                return ActionArray(mod, EnumerateActions(max));
            }));
            native.Set("action", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, native);
                string name = FrameworkApiUtil.RequireString(args, offset, "input.native.action(name)");
                object? action = FindAction(name);
                return action == null ? DynValue.Nil : WrapAction(mod, action);
            }));
            native.Set("getCurrentController", DynValue.NewCallback((ctx, args) =>
            {
                object? controller = GetController();
                if (controller != null && SleddersGameBindings.TryGetAny(controller, out object? raw, "currentController") && raw != null)
                    return DynValue.NewString(raw.ToString() ?? string.Empty);
                return DynValue.Nil;
            }));
            native.Set("setSystemEnabled", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, native);
                bool enabled = FrameworkApiUtil.RequireBool(args, offset, "input.native.setSystemEnabled(enabled)");
                object? controller = GetController();
                return DynValue.NewBoolean(controller != null && SleddersGameBindings.TryCallAny(
                    controller, new[] { "SetSystemState" }, new object?[] { enabled }, out _));
            }));
            native.Set("setUiEnabled", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, native);
                bool enabled = FrameworkApiUtil.RequireBool(args, offset, "input.native.setUiEnabled(enabled)");
                object? controller = GetController();
                return DynValue.NewBoolean(controller != null && SleddersGameBindings.TryCallAny(
                    controller, new[] { "SetUIInputSystemState" }, new object?[] { enabled }, out _));
            }));
            input.Set("native", DynValue.NewTable(native));
        }

        public static DynValue WrapAction(LuaModInstance mod, object action)
        {
            int handle = mod.Handles.Add(action);
            if (mod.TryGetCachedObject("inputAction", handle, out DynValue cached))
                return cached;

            var table = new Table(mod.Script);
            table.Set("__handle", DynValue.NewNumber(handle));
            table.Set("__type", DynValue.NewString("inputAction"));
            table.Set("isValid", DynValue.NewCallback((ctx, args) =>
                DynValue.NewBoolean(FrameworkApiUtil.Resolve(mod, handle) != null)));
            table.Set("getName", DynValue.NewCallback((ctx, args) => StringProperty(mod, handle, "name")));
            table.Set("getEnabled", DynValue.NewCallback((ctx, args) => BoolProperty(mod, handle, "enabled")));
            table.Set("getPhase", DynValue.NewCallback((ctx, args) => StringProperty(mod, handle, "phase")));
            table.Set("getExpectedControlType", DynValue.NewCallback((ctx, args) => StringProperty(mod, handle, "expectedControlType")));
            table.Set("getInteractions", DynValue.NewCallback((ctx, args) => StringProperty(mod, handle, "interactions")));
            table.Set("getProcessors", DynValue.NewCallback((ctx, args) => StringProperty(mod, handle, "processors")));
            table.Set("getValue", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "input action");
                return ReflectionBridge.TryCall(live, "ReadValueAsObject", Array.Empty<object?>(), out object? raw)
                    ? ValueConverter.ToDynValue(mod, raw)
                    : DynValue.Nil;
            }));
            table.Set("getActiveControl", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "input action");
                if (!SleddersGameBindings.TryGetAnyOrGetter(live, out object? control, "activeControl") || control == null)
                    return DynValue.Nil;
                foreach (string member in new[] { "displayName", "name", "path" })
                    if (SleddersGameBindings.TryGetAnyOrGetter(control, out object? value, member) && value != null)
                        return DynValue.NewString(value.ToString() ?? string.Empty);
                return DynValue.NewString(control.ToString() ?? string.Empty);
            }));
            table.Set("isPressed", DynValue.NewCallback((ctx, args) => CallBool(mod, handle, "IsPressed")));
            table.Set("wasPressed", DynValue.NewCallback((ctx, args) => CallBool(mod, handle, "WasPressedThisFrame")));
            table.Set("wasReleased", DynValue.NewCallback((ctx, args) => CallBool(mod, handle, "WasReleasedThisFrame")));
            table.Set("enable", DynValue.NewCallback((ctx, args) => CallVoid(mod, handle, "Enable")));
            table.Set("disable", DynValue.NewCallback((ctx, args) => CallVoid(mod, handle, "Disable")));

            DynValue wrapped = DynValue.NewTable(table);
            mod.CacheObject("inputAction", handle, wrapped);
            return wrapped;
        }

        private static object? GetController()
        {
            Type? type = ReflectionBridge.FindTypeExact("InputSystemController");
            if (type == null)
                return null;
            try { return ReflectionBridge.GetStaticMember(type, "Instance"); }
            catch { return ReflectionBridge.FindObjectsOfType(type, 8).FirstOrDefault(); }
        }

        private static object? GetActions()
        {
            object? controller = GetController();
            return controller != null && SleddersGameBindings.TryGetAny(controller, out object? actions, "inputActions")
                ? actions
                : null;
        }

        private static object? FindAction(string name)
        {
            object? actions = GetActions();
            if (actions == null)
                return null;
            try { return ReflectionBridge.Call(actions, "FindAction", new object?[] { name, false }); }
            catch { return null; }
        }

        private static IReadOnlyList<object> EnumerateActions(int max)
        {
            object? actions = GetActions();
            if (actions is not IEnumerable enumerable)
                return Array.Empty<object>();
            var result = new List<object>();
            foreach (object? action in enumerable)
            {
                if (action == null)
                    continue;
                result.Add(action);
                if (result.Count >= max)
                    break;
            }
            return result;
        }

        private static DynValue ActionArray(LuaModInstance mod, IEnumerable<object> actions)
        {
            var table = new Table(mod.Script);
            int i = 1;
            foreach (object action in actions)
                table.Set(i++, WrapAction(mod, action));
            return DynValue.NewTable(table);
        }

        private static DynValue StringProperty(LuaModInstance mod, int handle, string name)
        {
            object live = FrameworkApiUtil.RequireObject(mod, handle, "input action");
            return SleddersGameBindings.TryGetAnyOrGetter(live, out object? raw, name) && raw != null
                ? DynValue.NewString(raw.ToString() ?? string.Empty)
                : DynValue.Nil;
        }

        private static DynValue BoolProperty(LuaModInstance mod, int handle, string name)
        {
            object live = FrameworkApiUtil.RequireObject(mod, handle, "input action");
            return SleddersGameBindings.TryGetAnyOrGetter(live, out object? raw, name) && raw is bool value
                ? DynValue.NewBoolean(value)
                : DynValue.Nil;
        }

        private static DynValue CallBool(LuaModInstance mod, int handle, string method)
        {
            object live = FrameworkApiUtil.RequireObject(mod, handle, "input action");
            return ReflectionBridge.TryCall(live, method, Array.Empty<object?>(), out object? raw) && raw is bool value
                ? DynValue.NewBoolean(value)
                : DynValue.Nil;
        }

        private static DynValue CallVoid(LuaModInstance mod, int handle, string method)
        {
            object live = FrameworkApiUtil.RequireObject(mod, handle, "input action");
            return DynValue.NewBoolean(ReflectionBridge.TryCall(live, method, Array.Empty<object?>(), out _));
        }
    }
}
