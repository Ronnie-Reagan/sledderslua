using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class SledStructureApi
    {
        private static readonly Dictionary<string, string[]> TransformMembers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "leftSki", new[] { "leftSki" } },
            { "rightSki", new[] { "rightSki" } },
            { "throttleLever", new[] { "throttleLever" } },
            { "brakeLever", new[] { "brakeLever" } },
            { "track", new[] { "traxBody", "trackRenderer" } },
            { "leftHandTarget", new[] { "leftHandTarget" } },
            { "rightHandTarget", new[] { "rightHandTarget" } },
            { "leftShoulderTarget", new[] { "leftShoulderTarget" } },
            { "rightShoulderTarget", new[] { "rightShoulderTarget" } }
        };

        public static DynValue Wrap(LuaModInstance mod, object structure, object sled)
        {
            int handle = mod.Handles.Add(structure);
            if (mod.TryGetCachedObject("sledStructure", handle, out DynValue cached)) return cached;
            int sledHandle = mod.Handles.Add(sled);
            var table = new Table(mod.Script);
            table.Set("__handle", DynValue.NewNumber(handle));
            table.Set("__type", DynValue.NewString("sledStructure"));
            table.Set("isValid", DynValue.NewCallback((ctx, args) => DynValue.NewBoolean(FrameworkApiUtil.Resolve(mod, handle) != null)));
            table.Set("groups", DynValue.NewCallback((ctx, args) => VisualApi.GetPartGroupNames(mod)));
            table.Set("getRenderers", DynValue.NewCallback((ctx, args) =>
            {
                object? liveSled = FrameworkApiUtil.Resolve(mod, sledHandle);
                if (liveSled == null) return DynValue.NewTable(new Table(mod.Script));
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string group = args.Count > offset && args[offset].Type == DataType.String ? args[offset].String : "all";
                return VisualApi.GetSledRenderers(mod, liveSled, group);
            }));
            table.Set("getTransform", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "sled structure");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string name = FrameworkApiUtil.RequireString(args, offset, "structure.getTransform(name)");
                if (!TransformMembers.TryGetValue(name, out string[]? members) || !SleddersGameBindings.TryGetAny(live, out object? value, members) || value == null)
                    return DynValue.Nil;
                object? transform = SleddersGameBindings.GetTransform(value) ?? value;
                return transform == null ? DynValue.Nil : TransformApi.Wrap(mod, transform);
            }));
            table.Set("getCustomMaterial", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "sled structure");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string name = FrameworkApiUtil.RequireString(args, offset, "structure.getCustomMaterial(name)").ToLowerInvariant();
                string[] members = name switch
                {
                    "spindle" => new[] { "spindleCustomMaterial" },
                    "rail" or "rails" => new[] { "railCustomMaterial" },
                    "handlebar" or "handlebars" => new[] { "handlebarCustomMaterial" },
                    _ => Array.Empty<string>()
                };
                return members.Length > 0 && SleddersGameBindings.TryGetAny(live, out object? material, members) && material != null
                    ? VisualApi.WrapMaterial(mod, material) : DynValue.Nil;
            }));
            AddFloatAction(table, mod, handle, "setHandlebarAngle", "SetHandleBarAngle");
            AddFloatAction(table, mod, handle, "setThrottleLeverAngle", "SetThrottleLeverAngle");
            AddFloatAction(table, mod, handle, "setBrakeLeverAngle", "SetBrakeLeverAngle");
            AddFloatAction(table, mod, handle, "setHeadlightEmission", "SetHeadlightEmission");
            AddBoolAction(table, mod, handle, "setSkisVisible", "SetSkis");
            AddBoolAction(table, mod, handle, "setTrackVisible", "SetTraxObjects");

            DynValue wrapped = DynValue.NewTable(table);
            mod.CacheObject("sledStructure", handle, wrapped);
            return wrapped;
        }

        private static void AddFloatAction(Table table, LuaModInstance mod, int handle, string luaName, string method)
        {
            table.Set(luaName, DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "sled structure");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                float value = (float)FrameworkApiUtil.RequireFiniteNumber(args, offset, "structure." + luaName + "(value)");
                return DynValue.NewBoolean(SleddersGameBindings.TryCallAny(live, new[] { method }, new object?[] { value }, out _));
            }));
        }

        private static void AddBoolAction(Table table, LuaModInstance mod, int handle, string luaName, string method)
        {
            table.Set(luaName, DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "sled structure");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                bool value = FrameworkApiUtil.RequireBool(args, offset, "structure." + luaName + "(enabled)");
                return DynValue.NewBoolean(SleddersGameBindings.TryCallAny(live, new[] { method }, new object?[] { value }, out _));
            }));
        }
    }
}
