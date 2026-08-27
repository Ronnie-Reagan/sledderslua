using System;
using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class HudApi
    {
        private static readonly Dictionary<string, string[]> Elements = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "document", new[] { "hudDocument" } },
            { "race", new[] { "raceStateDocument" } },
            { "speedMeter", new[] { "speedMeter" } },
            { "rpmMeter", new[] { "rpmMeter" } },
            { "meterRoot", new[] { "speedMeterRoot" } },
            { "compass", new[] { "compassRoot" } },
            { "shovel", new[] { "shovelIcon" } },
            { "stance", new[] { "stanceIcons" } },
            { "stanceHigh", new[] { "highStanceIcon" } },
            { "stanceLow", new[] { "lowStanceIcon" } },
            { "switchbackLeft", new[] { "switchbackLeftIcon" } },
            { "switchbackRight", new[] { "switchbackRightIcon" } },
            { "corneringLeft", new[] { "corneringLeftIcon" } },
            { "corneringRight", new[] { "corneringRightIcon" } },
            { "engine", new[] { "engineOnOffContainer" } },
            { "fuelRescue", new[] { "fuelRescueContainer" } },
            { "spawnPopup", new[] { "spawnPopUp" } },
            { "challengeNotify", new[] { "challengeAreaNotify" } },
            { "joinChallenge", new[] { "joinChallengeContainer" } },
            { "holdInput", new[] { "holdInputNotify" } }
        };

        public static Table Build(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("isVisible", DynValue.NewCallback((ctx, args) =>
            {
                bool? state = ReadHudVisible(GetSingleton("HudVisibilityController"));
                return state.HasValue ? DynValue.NewBoolean(state.Value) : DynValue.Nil;
            }));
            table.Set("setVisible", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                return DynValue.NewBoolean(SetHudVisible(
                    mod.StateOwnerToken,
                    FrameworkApiUtil.RequireBool(args, offset, "hud.setVisible(visible)")));
            }));
            table.Set("forceShow", DynValue.NewCallback((ctx, args) => DynValue.NewBoolean(ForceShowHud())));
            table.Set("elements", DynValue.NewCallback((ctx, args) =>
            {
                var result = new Table(mod.Script);
                int i = 1;
                foreach (string name in Elements.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    result.Set(i++, DynValue.NewString(name));
                return DynValue.NewTable(result);
            }));
            table.Set("get", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string name = FrameworkApiUtil.RequireString(args, offset, "hud.get(element)");
                object? element = FindElement(name);
                return element == null ? DynValue.Nil : WrapElement(mod, element, name);
            }));
            table.Set("getVisible", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string name = FrameworkApiUtil.RequireString(args, offset, "hud.getVisible(element)");
                object? element = FindElement(name);
                bool? state = element == null ? null : GetObjectVisible(element);
                return state.HasValue ? DynValue.NewBoolean(state.Value) : DynValue.Nil;
            }));
            table.Set("setElementVisible", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string name = FrameworkApiUtil.RequireString(args, offset, "hud.setElementVisible(element, visible)");
                bool visible = FrameworkApiUtil.RequireBool(args, offset + 1, "hud.setElementVisible(element, visible)");
                object? element = FindElement(name);
                return DynValue.NewBoolean(element != null && SetObjectVisible(element, visible));
            }));
            table.Set("showLetterbox", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                bool visible = FrameworkApiUtil.RequireBool(args, offset, "hud.showLetterbox(enabled)");
                object? hud = GetHudController();
                return DynValue.NewBoolean(hud != null && SleddersGameBindings.TryCallAny(
                    hud, new[] { "ShowLetterbox" }, new object?[] { visible }, out _));
            }));
            table.Set("showBanner", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string title = FrameworkApiUtil.RequireString(args, offset, "hud.showBanner(title, message)");
                string message = FrameworkApiUtil.RequireString(args, offset + 1, "hud.showBanner(title, message)");
                object? hud = GetHudController();
                return DynValue.NewBoolean(hud != null && SleddersGameBindings.TryCallAny(
                    hud, new[] { "ShowBannerNotification" }, new object?[] { title, message }, out _));
            }));
            return table;
        }

        public static DynValue WrapElement(LuaModInstance mod, object element, string semanticName)
        {
            int handle = mod.Handles.Add(element);
            if (mod.TryGetCachedObject("hudElement", handle, out DynValue cached))
                return cached;

            var table = new Table(mod.Script);
            table.Set("__handle", DynValue.NewNumber(handle));
            table.Set("__type", DynValue.NewString("hudElement"));
            table.Set("semanticName", DynValue.NewString(semanticName));
            table.Set("isValid", DynValue.NewCallback((ctx, args) =>
                DynValue.NewBoolean(FrameworkApiUtil.Resolve(mod, handle) != null)));
            table.Set("getVisible", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "HUD element");
                bool? state = GetObjectVisible(live);
                return state.HasValue ? DynValue.NewBoolean(state.Value) : DynValue.Nil;
            }));
            table.Set("setVisible", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "HUD element");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                return DynValue.NewBoolean(SetObjectVisible(
                    live,
                    FrameworkApiUtil.RequireBool(args, offset, "hudElement.setVisible(visible)")));
            }));
            table.Set("setValue", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "HUD element");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                float value = (float)FrameworkApiUtil.RequireFiniteNumber(args, offset, "hudElement.setValue(value)");
                return DynValue.NewBoolean(SleddersGameBindings.TryCallAny(
                    live, new[] { "SetValue" }, new object?[] { value }, out _));
            }));
            table.Set("setUnit", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "HUD element");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string unit = FrameworkApiUtil.RequireString(args, offset, "hudElement.setUnit(unit)");
                return DynValue.NewBoolean(SleddersGameBindings.TryCallAny(
                    live, new[] { "SetUnit" }, new object?[] { unit }, out _));
            }));
            table.Set("setReverseText", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "HUD element");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                bool reverse = FrameworkApiUtil.RequireBool(args, offset, "hudElement.setReverseText(reverse, flash)");
                bool flash = args.Count > offset + 1 && !args[offset + 1].IsNil()
                    ? FrameworkApiUtil.RequireBool(args, offset + 1, "hudElement.setReverseText(reverse, flash)")
                    : false;
                return DynValue.NewBoolean(SleddersGameBindings.TryCallAny(
                    live, new[] { "SetReverseText" }, new object?[] { reverse, flash }, out _));
            }));
            table.Set("setText", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "HUD element");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string text = FrameworkApiUtil.RequireString(args, offset, "hudElement.setText(text)");
                return DynValue.NewBoolean(
                    SleddersGameBindings.TrySetAny(live, text, "text", "value") ||
                    SleddersGameBindings.TryCallAny(live, new[] { "SetText" }, new object?[] { text }, out _));
            }));

            DynValue wrapped = DynValue.NewTable(table);
            mod.CacheObject("hudElement", handle, wrapped);
            return wrapped;
        }

        private static object? FindElement(string name)
        {
            object? hud = GetHudController();
            if (hud == null)
                return null;
            if (!Elements.TryGetValue(name, out string[]? aliases))
                aliases = new[] { name };
            return SleddersGameBindings.TryGetAny(hud, out object? value, aliases) ? value : null;
        }

        private static bool SetHudVisible(string owner, bool visible)
        {
            object? controller = GetSingleton("HudVisibilityController");
            if (controller == null)
                return false;

            string method = visible ? "ReleaseHideRequest" : "RequestHideHud";
            return SleddersGameBindings.TryCallAny(
                controller,
                new[] { method },
                new object?[] { owner },
                out _);
        }

        private static bool ForceShowHud()
        {
            object? controller = GetSingleton("HudVisibilityController");
            return controller != null && SleddersGameBindings.TryCallAny(
                controller, new[] { "ForceShowHud" }, Array.Empty<object?>(), out _);
        }

        private static bool? ReadHudVisible(object? controller)
        {
            if (controller == null)
                return null;
            if (SleddersGameBindings.TryCallAny(
                    controller, new[] { "IsHudVisible" }, Array.Empty<object?>(), out object? raw) && raw is bool visible)
                return visible;
            return null;
        }

        private static bool? GetObjectVisible(object target)
        {
            if (SleddersGameBindings.TryGetAny(target, out object? value, "activeSelf") && value is bool active)
                return active;
            if (SleddersGameBindings.TryGetAny(target, out value, "enabled") && value is bool enabled)
                return enabled;
            if (SleddersGameBindings.TryGetAny(target, out value, "visible") && value is bool visible)
                return visible;
            object? go = SleddersGameBindings.GetGameObject(target);
            if (go != null && SleddersGameBindings.TryGetAny(go, out value, "activeSelf") && value is bool goActive)
                return goActive;
            return null;
        }

        private static bool SetObjectVisible(object target, bool visible)
        {
            if (target.GetType().FullName == "UnityEngine.GameObject" &&
                SleddersGameBindings.TryCallAny(target, new[] { "SetActive" }, new object?[] { visible }, out _))
                return true;

            // UI Toolkit VisualElement exposes a writable visible property; prefer it over
            // disabling its owning UIDocument/GameObject when the element itself is addressable.
            if (SleddersGameBindings.TrySetAny(target, visible, "visible"))
                return true;

            if (SleddersGameBindings.TrySetAny(target, visible, "enabled"))
                return true;

            object? go = SleddersGameBindings.GetGameObject(target);
            return go != null && SleddersGameBindings.TryCallAny(
                go, new[] { "SetActive" }, new object?[] { visible }, out _);
        }

        private static object? GetHudController()
        {
            return GetSingleton("HudController") ?? FindOne("HudController");
        }

        private static object? GetSingleton(string typeName)
        {
            Type? type = ReflectionBridge.FindTypeExact(typeName);
            if (type == null)
                return null;
            try { return ReflectionBridge.GetStaticMember(type, "Instance"); }
            catch { return ReflectionBridge.FindObjectsOfType(type, 16).FirstOrDefault(); }
        }

        private static object? FindOne(string typeName)
        {
            Type? type = ReflectionBridge.FindTypeExact(typeName);
            return type == null ? null : ReflectionBridge.FindObjectsOfType(type, 16).FirstOrDefault();
        }
    }
}
