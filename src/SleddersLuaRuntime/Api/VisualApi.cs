using System;
using System.Collections;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class VisualApi
    {
        private static readonly Dictionary<string, string[]> PartGroups = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "hood", new[] { "hoodParts" } },
            { "skis", new[] { "skiParts" } },
            { "handlebars", new[] { "handlebarParts", "handlebarCustomParts" } },
            { "seat", new[] { "seatParts" } },
            { "tunnel", new[] { "tunnelParts" } },
            { "rails", new[] { "railParts", "railCustomParts" } },
            { "logos", new[] { "logoParts" } },
            { "metal", new[] { "metalParts" } },
            { "spindles", new[] { "spindleCustomParts" } },
            { "lightFrame", new[] { "lightFrameParts" } },
            { "bumper", new[] { "bumberParts", "bumperParts" } },
            { "underside", new[] { "underParts", "underSideParts", "undersideParts" } },
            { "headlights", new[] { "headLights" } }
        };

        public static Table BuildService(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("renderers", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                object target = ResolveTarget(mod, args, offset, "visual.renderers(target [, max])", out int consumed);
                int maxIndex = offset + consumed;
                int max = args.Count > maxIndex && args[maxIndex].Type == DataType.Number
                    ? FrameworkApiUtil.RequireInt(args, maxIndex, "visual.renderers(target,max)", 1, 512)
                    : 128;
                return RendererArray(mod, FindRenderers(target, max));
            }));
            table.Set("material", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                if (args.Count <= offset || args[offset].Type != DataType.Table) return DynValue.Nil;
                object? raw = ObjectProxyBuilder.DynToRaw(mod, args[offset]);
                return raw == null ? DynValue.Nil : WrapMaterial(mod, raw);
            }));
            return table;
        }


        public static DynValue GetPartGroupNames(LuaModInstance mod)
        {
            var result = new Table(mod.Script);
            int i = 1;
            foreach (string name in PartGroups.Keys) result.Set(i++, DynValue.NewString(name));
            return DynValue.NewTable(result);
        }
        public static DynValue GetRenderers(LuaModInstance mod, object target, int max)
        {
            return RendererArray(mod, FindRenderers(target, max));
        }

        public static DynValue GetSledRenderers(LuaModInstance mod, object sled, string group)
        {
            if (string.Equals(group, "all", StringComparison.OrdinalIgnoreCase))
                return RendererArray(mod, FindRenderers(sled, 256));

            object? structure = SleddersGameBindings.GetStructure(sled);
            if (structure == null || !PartGroups.TryGetValue(group, out string[]? fields))
                return DynValue.NewTable(new Table(mod.Script));

            var found = new List<object>();
            foreach (string field in fields)
            {
                if (!SleddersGameBindings.TryGetAny(structure, out object? raw, field) || raw == null) continue;
                AddRenderers(raw, found, 256);
            }
            return RendererArray(mod, found);
        }

        public static DynValue WrapRenderer(LuaModInstance mod, object renderer)
        {
            int handle = mod.Handles.Add(renderer);
            if (mod.TryGetCachedObject("renderer", handle, out DynValue cached)) return cached;
            var table = new Table(mod.Script);
            table.Set("__handle", DynValue.NewNumber(handle));
            table.Set("__type", DynValue.NewString("renderer"));
            table.Set("isValid", DynValue.NewCallback((ctx, args) => DynValue.NewBoolean(FrameworkApiUtil.Resolve(mod, handle) != null)));
            table.Set("getName", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "renderer");
                return DynValue.NewString(SleddersGameBindings.GetFriendlyName(live));
            }));
            table.Set("getEnabled", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "renderer");
                return SleddersGameBindings.TryGetAny(live, out object? value, "enabled") && value is bool b ? DynValue.NewBoolean(b) : DynValue.Nil;
            }));
            table.Set("setEnabled", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "renderer");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                return DynValue.NewBoolean(SleddersGameBindings.TrySetAny(live, FrameworkApiUtil.RequireBool(args, offset, "renderer.setEnabled(enabled)"), "enabled"));
            }));
            table.Set("getMaterials", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "renderer");
                if (!SleddersGameBindings.TryGetAny(live, out object? raw, "materials") || raw is not IEnumerable values)
                    return DynValue.NewTable(new Table(mod.Script));
                var result = new Table(mod.Script);
                int i = 1;
                foreach (object? value in values) if (value != null) result.Set(i++, WrapMaterial(mod, value));
                return DynValue.NewTable(result);
            }));
            table.Set("getMaterial", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "renderer");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                int index = args.Count > offset ? FrameworkApiUtil.RequireInt(args, offset, "renderer.getMaterial(index)", 1, 128) : 1;
                if (!SleddersGameBindings.TryGetAny(live, out object? raw, "materials") || raw is not IList list || index > list.Count)
                    return DynValue.Nil;
                object? material = list[index - 1];
                return material == null ? DynValue.Nil : WrapMaterial(mod, material);
            }));
            table.Set("setColor", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "renderer");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                if (args.Count <= offset || args[offset].Type != DataType.Table) throw new ScriptRuntimeException("renderer.setColor(color [, property]) expects a color table.");
                string? property = args.Count > offset + 1 && args[offset + 1].Type == DataType.String ? args[offset + 1].String : null;
                if (!SleddersGameBindings.TryGetAny(live, out object? raw, "materials") || raw is not IEnumerable values) return DynValue.False;
                bool changed = false;
                foreach (object? value in values)
                    if (value != null) changed |= SetMaterialColor(mod, value, args[offset], property);
                return DynValue.NewBoolean(changed);
            }));
            DynValue wrapped = DynValue.NewTable(table);
            mod.CacheObject("renderer", handle, wrapped);
            return wrapped;
        }

        public static DynValue WrapMaterial(LuaModInstance mod, object material)
        {
            int handle = mod.Handles.Add(material);
            if (mod.TryGetCachedObject("material", handle, out DynValue cached)) return cached;
            var table = new Table(mod.Script);
            table.Set("__handle", DynValue.NewNumber(handle));
            table.Set("__type", DynValue.NewString("material"));
            table.Set("isValid", DynValue.NewCallback((ctx, args) => DynValue.NewBoolean(FrameworkApiUtil.Resolve(mod, handle) != null)));
            table.Set("getName", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "material");
                return DynValue.NewString(SleddersGameBindings.GetFriendlyName(live));
            }));
            table.Set("getShader", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "material");
                if (!SleddersGameBindings.TryGetAny(live, out object? shader, "shader") || shader == null) return DynValue.Nil;
                string? name = ReflectionBridge.TryGetObjectName(shader);
                return name == null ? DynValue.Nil : DynValue.NewString(name);
            }));
            table.Set("has", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "material");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string property = FrameworkApiUtil.RequireString(args, offset, "material.has(property)");
                return DynValue.NewBoolean(HasProperty(live, property));
            }));
            table.Set("getColor", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "material");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string? property = args.Count > offset && args[offset].Type == DataType.String ? args[offset].String : null;
                string? chosen = ChooseColorProperty(live, property);
                if (chosen == null || !ReflectionBridge.TryCall(live, "GetColor", new object?[] { chosen }, out object? color)) return DynValue.Nil;
                return ValueConverter.ToDynValue(mod, color);
            }));
            table.Set("setColor", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "material");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                if (args.Count <= offset || args[offset].Type != DataType.Table) throw new ScriptRuntimeException("material.setColor(color [, property]) expects a color table.");
                string? property = args.Count > offset + 1 && args[offset + 1].Type == DataType.String ? args[offset + 1].String : null;
                return DynValue.NewBoolean(SetMaterialColor(mod, live, args[offset], property));
            }));
            table.Set("getFloat", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "material");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string property = FrameworkApiUtil.RequireString(args, offset, "material.getFloat(property)");
                if (!HasProperty(live, property) || !ReflectionBridge.TryCall(live, "GetFloat", new object?[] { property }, out object? value)) return DynValue.Nil;
                double? number = SleddersGameBindings.ToDouble(value);
                return number.HasValue ? DynValue.NewNumber(number.Value) : DynValue.Nil;
            }));
            table.Set("setFloat", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "material");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string property = FrameworkApiUtil.RequireString(args, offset, "material.setFloat(property,value)");
                double value = FrameworkApiUtil.RequireFiniteNumber(args, offset + 1, "material.setFloat(property,value)");
                return DynValue.NewBoolean(HasProperty(live, property) && ReflectionBridge.TryCall(live, "SetFloat", new object?[] { property, (float)value }, out _));
            }));
            table.Set("enableKeyword", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "material");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                return DynValue.NewBoolean(ReflectionBridge.TryCall(live, "EnableKeyword", new object?[] { FrameworkApiUtil.RequireString(args, offset, "material.enableKeyword(keyword)") }, out _));
            }));
            table.Set("disableKeyword", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "material");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                return DynValue.NewBoolean(ReflectionBridge.TryCall(live, "DisableKeyword", new object?[] { FrameworkApiUtil.RequireString(args, offset, "material.disableKeyword(keyword)") }, out _));
            }));
            DynValue wrapped = DynValue.NewTable(table);
            mod.CacheObject("material", handle, wrapped);
            return wrapped;
        }

        private static object ResolveTarget(LuaModInstance mod, CallbackArguments args, int index, string usage, out int consumed)
        {
            consumed = 0;
            if (args.Count > index && args[index].Type == DataType.Table)
            {
                object? raw = ObjectProxyBuilder.DynToRaw(mod, args[index]);
                if (raw != null) { consumed = 1; return raw; }
            }
            object? sled = SleddersGameBindings.FindLocalSled();
            if (sled == null) throw new ScriptRuntimeException(usage + " could not resolve a target or local sled.");
            return sled;
        }

        private static IReadOnlyList<object> FindRenderers(object target, int max)
        {
            Type? rendererType = ReflectionBridge.FindTypeExact("UnityEngine.Renderer");
            return rendererType == null ? Array.Empty<object>() : ReflectionBridge.GetComponentsInChildren(target, rendererType, true, max);
        }

        private static void AddRenderers(object raw, List<object> output, int max)
        {
            if (output.Count >= max) return;
            if (raw is IEnumerable enumerable && raw is not string)
            {
                foreach (object? item in enumerable)
                {
                    if (item == null) continue;
                    AddRenderers(item, output, max);
                    if (output.Count >= max) return;
                }
                return;
            }
            Type? rendererType = ReflectionBridge.FindTypeExact("UnityEngine.Renderer");
            if (rendererType != null && rendererType.IsInstanceOfType(raw)) { output.Add(raw); return; }
            foreach (object renderer in FindRenderers(raw, max - output.Count)) output.Add(renderer);
        }

        private static DynValue RendererArray(LuaModInstance mod, IEnumerable<object> renderers)
        {
            var result = new Table(mod.Script);
            int i = 1;
            foreach (object renderer in renderers) result.Set(i++, WrapRenderer(mod, renderer));
            return DynValue.NewTable(result);
        }

        private static bool HasProperty(object material, string property)
        {
            return ReflectionBridge.TryCall(material, "HasProperty", new object?[] { property }, out object? raw) && raw is bool b && b;
        }

        private static string? ChooseColorProperty(object material, string? requested)
        {
            if (!string.IsNullOrWhiteSpace(requested)) return HasProperty(material, requested!) ? requested : null;
            foreach (string name in new[] { "_BaseColor", "_Color", "baseColor" }) if (HasProperty(material, name)) return name;
            return null;
        }

        private static bool SetMaterialColor(LuaModInstance mod, object material, DynValue colorValue, string? property)
        {
            string? chosen = ChooseColorProperty(material, property);
            Type? colorType = ReflectionBridge.FindTypeExact("UnityEngine.Color");
            if (chosen == null || colorType == null) return false;
            object? color = ValueConverter.FromDynValue(mod, colorValue, colorType);
            return color != null && ReflectionBridge.TryCall(material, "SetColor", new object?[] { chosen, color }, out _);
        }
    }
}
