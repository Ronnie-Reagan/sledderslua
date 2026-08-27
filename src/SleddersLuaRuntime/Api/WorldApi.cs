using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class WorldApi
    {
        private static readonly string[] SnowConditions = { "SoftPowder", "Powder", "Soft", "SemiSoft", "Firm" };

        public static Table Build(LuaModInstance mod)
        {
            var root = new Table(mod.Script);
            root.Set("snow", DynValue.NewTable(BuildSnow(mod)));
            root.Set("time", DynValue.NewTable(BuildWorldTime(mod)));
            root.Set("weather", DynValue.NewTable(BuildWeather(mod)));
            root.Set("fuel", DynValue.NewTable(BuildFuel(mod)));
            return root;
        }

        private static Table BuildSnow(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("conditions", DynValue.NewCallback((ctx, args) => StringArray(mod, SnowConditions)));
            table.Set("getCondition", DynValue.NewCallback((ctx, args) =>
            {
                object? snow = GetSingleton("SnowConditionController");
                if (snow != null && SleddersGameBindings.TryCallAny(
                        snow, new[] { "GetSnowCondition" }, Array.Empty<object?>(), out object? value) && value != null)
                    return DynValue.NewString(value.ToString() ?? string.Empty);
                return DynValue.Nil;
            }));
            table.Set("setCondition", DynValue.NewCallback((ctx, args) =>
            {
                object? snow = GetSingleton("SnowConditionController");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string condition = FrameworkApiUtil.RequireString(args, offset, "world.snow.setCondition(condition)");
                return DynValue.NewBoolean(snow != null && SleddersGameBindings.TryCallAny(
                    snow, new[] { "SetSnowCondition" }, new object?[] { condition }, out _));
            }));
            table.Set("getHardness", DynValue.NewCallback((ctx, args) =>
            {
                object? snow = GetSingleton("SnowConditionController");
                if (snow == null || !SleddersGameBindings.TryGetAny(snow, out object? value, "snowHardness"))
                    return DynValue.Nil;
                double? number = SleddersGameBindings.ToDouble(value);
                return number.HasValue ? DynValue.NewNumber(number.Value) : DynValue.Nil;
            }));
            table.Set("setHardness", DynValue.NewCallback((ctx, args) =>
            {
                object? snow = GetSingleton("SnowConditionController");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                double value = FrameworkApiUtil.RequireFiniteNumber(args, offset, "world.snow.setHardness(value)");
                return DynValue.NewBoolean(snow != null && SleddersGameBindings.TrySetAny(snow, value, "snowHardness"));
            }));
            return table;
        }

        private static Table BuildWorldTime(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("getTimeOfDay", DynValue.NewCallback((ctx, args) =>
            {
                object? time = GetSingleton("TimeController");
                if (time != null && SleddersGameBindings.TryCallAny(
                        time, new[] { "GetTimeOfDay" }, Array.Empty<object?>(), out object? raw))
                {
                    double? number = SleddersGameBindings.ToDouble(raw);
                    if (number.HasValue)
                        return DynValue.NewNumber(number.Value);
                }
                return DynValue.Nil;
            }));
            table.Set("setTimeOfDay", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                double value = FrameworkApiUtil.RequireFiniteNumber(args, offset, "world.time.setTimeOfDay(value)");
                return DynValue.NewBoolean(SetTimeOfDay(value));
            }));
            table.Set("getDateTime", DynValue.NewCallback((ctx, args) => CallString("TimeController", "GetGameDateTime")));
            table.Set("getDate", DynValue.NewCallback((ctx, args) => CallString("TimeController", "GetDateYMD")));
            table.Set("getClock", DynValue.NewCallback((ctx, args) => CallString("TimeController", "GetTimeHHMM")));
            table.Set("refresh", DynValue.NewCallback((ctx, args) =>
            {
                object? time = GetSingleton("TimeController");
                return DynValue.NewBoolean(time != null && SleddersGameBindings.TryCallAny(
                    time, new[] { "RefreshEnvironment" }, Array.Empty<object?>(), out _));
            }));
            return table;
        }

        private static Table BuildWeather(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("getName", DynValue.NewCallback((ctx, args) =>
            {
                object? current = GetCurrentWeather();
                return current == null ? DynValue.Nil : DynValue.NewString(GetWeatherName(current));
            }));
            table.Set("getIndex", DynValue.NewCallback((ctx, args) =>
            {
                object? manager = GetSingleton("WeatherManager");
                if (manager == null || !SleddersGameBindings.TryCallAny(
                        manager, new[] { "get_WeatherCurrentIndex" }, Array.Empty<object?>(), out object? raw))
                    return DynValue.Nil;
                double? number = SleddersGameBindings.ToDouble(raw);
                return number.HasValue ? DynValue.NewNumber(number.Value) : DynValue.Nil;
            }));
            table.Set("names", DynValue.NewCallback((ctx, args) =>
            {
                object? manager = GetSingleton("WeatherManager");
                var names = new List<string>();
                if (manager != null && SleddersGameBindings.TryGetAny(manager, out object? raw, "setupsInitial") && raw is IEnumerable values)
                {
                    foreach (object? setup in values)
                        if (setup != null)
                            names.Add(GetWeatherName(setup));
                }
                return StringArray(mod, names);
            }));
            table.Set("get", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string name = FrameworkApiUtil.RequireString(args, offset, "world.weather.get(name)");
                object? setup = FindWeather(name);
                return setup == null ? DynValue.Nil : WrapWeatherSetup(mod, setup);
            }));
            table.Set("current", DynValue.NewCallback((ctx, args) =>
            {
                object? setup = GetCurrentWeather();
                return setup == null ? DynValue.Nil : WrapWeatherSetup(mod, setup);
            }));
            table.Set("set", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string name = FrameworkApiUtil.RequireString(args, offset, "world.weather.set(name)");
                return DynValue.NewBoolean(SetWeather(name));
            }));
            table.Set("findIndex", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string name = FrameworkApiUtil.RequireString(args, offset, "world.weather.findIndex(name)");
                object? manager = GetSingleton("WeatherManager");
                object? setup = FindWeather(name);
                if (manager == null || setup == null || !SleddersGameBindings.TryCallAny(
                        manager, new[] { "FindWeatherIndex" }, new object?[] { setup }, out object? raw))
                    return DynValue.Nil;
                double? number = SleddersGameBindings.ToDouble(raw);
                return number.HasValue ? DynValue.NewNumber(number.Value) : DynValue.Nil;
            }));
            return table;
        }

        private static DynValue WrapWeatherSetup(LuaModInstance mod, object setup)
        {
            IReadOnlyList<SemanticProperty> properties = new[]
            {
                new SemanticProperty("name", false, "displayName"),
                new SemanticProperty("azurePreset", false, "azureWeatherPresetName"),
                new SemanticProperty("chance", true, "chance"),
                new SemanticProperty("stayHoursMin", true, "stayGameHoursMin"),
                new SemanticProperty("stayHoursMax", true, "stayGameHoursMax"),
                new SemanticProperty("wwiseSwitch", true, "wwiseWeatherSwitchState")
            };
            DynValue bag = SemanticPropertyBag.Wrap(mod, setup, "weatherSetup", properties);
            Table table = bag.Table;
            SemanticPropertyBag.AddNamedAccessors(table, mod, bag, "name", "Name");
            SemanticPropertyBag.AddNamedAccessors(table, mod, bag, "chance", "Chance");
            SemanticPropertyBag.AddNamedAccessors(table, mod, bag, "stayHoursMin", "StayHoursMin");
            SemanticPropertyBag.AddNamedAccessors(table, mod, bag, "stayHoursMax", "StayHoursMax");
            return bag;
        }

        private static Table BuildFuel(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("getUsageEnabled", DynValue.NewCallback((ctx, args) => FuelBool("FuelUsageEnabledEffective")));
            table.Set("setUsageEnabled", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? manager = GetSingleton("FuelManager");
                return DynValue.NewBoolean(manager != null && SleddersGameBindings.TryCallAny(
                    manager,
                    new[] { "SetHostFuelUsageEnabled" },
                    new object?[] { FrameworkApiUtil.RequireBool(args, offset, "world.fuel.setUsageEnabled(enabled)") },
                    out _));
            }));
            table.Set("canRescue", DynValue.NewCallback((ctx, args) => FuelBool("CanFuelRescue")));
            table.Set("rescue", DynValue.NewCallback((ctx, args) =>
            {
                object? manager = GetSingleton("FuelManager");
                return DynValue.NewBoolean(manager != null && SleddersGameBindings.TryCallAny(
                    manager, new[] { "PerformFuelRescue" }, Array.Empty<object?>(), out _));
            }));
            table.Set("hasStation", DynValue.NewCallback((ctx, args) => FuelBool("SceneHasFuelStation")));
            table.Set("getNearestStation", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? position;
                if (args.Count > offset)
                    position = FrameworkApiUtil.ReadVector3(mod, args, offset, "world.fuel.getNearestStation(position)");
                else
                {
                    object? sled = SleddersGameBindings.FindLocalSled();
                    position = sled == null ? null : SleddersGameBindings.GetPosition(sled);
                }

                Type? type = ReflectionBridge.FindTypeExact("FuelStation");
                if (type == null || position == null)
                    return DynValue.Nil;
                try
                {
                    object? station = ReflectionBridge.CallStatic(type, "FindNearestActivated", new object?[] { position });
                    return station == null ? DynValue.Nil : WrapFuelStation(mod, station);
                }
                catch { return DynValue.Nil; }
            }));
            return table;
        }

        private static DynValue WrapFuelStation(LuaModInstance mod, object station)
        {
            int handle = mod.Handles.Add(station);
            if (mod.TryGetCachedObject("fuelStation", handle, out DynValue cached))
                return cached;

            var table = new Table(mod.Script);
            table.Set("__handle", DynValue.NewNumber(handle));
            table.Set("__type", DynValue.NewString("fuelStation"));
            table.Set("isValid", DynValue.NewCallback((ctx, args) =>
                DynValue.NewBoolean(FrameworkApiUtil.Resolve(mod, handle) != null)));
            table.Set("getPos", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "fuel station");
                if (SleddersGameBindings.TryGetAnyOrGetter(live, out object? point, "DiscoveryPoint") && point != null)
                    return ValueConverter.ToDynValue(mod, SleddersGameBindings.GetPosition(point));
                return ValueConverter.ToDynValue(mod, SleddersGameBindings.GetPosition(live));
            }));
            table.Set("getRefuelEnabled", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "fuel station");
                if (SleddersGameBindings.TryGetAnyOrGetter(live, out object? raw, "RefuelEnabled") && raw is bool enabled)
                    return DynValue.NewBoolean(enabled);
                return DynValue.Nil;
            }));
            table.Set("setRefuelEnabled", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "fuel station");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                bool enabled = FrameworkApiUtil.RequireBool(args, offset, "fuelStation.setRefuelEnabled(enabled)");
                return DynValue.NewBoolean(SleddersGameBindings.TrySetAny(live, enabled, "refuelEnabled"));
            }));

            DynValue wrapped = DynValue.NewTable(table);
            mod.CacheObject("fuelStation", handle, wrapped);
            return wrapped;
        }

        private static bool SetTimeOfDay(double value)
        {
            object? time = GetSingleton("TimeController");
            if (time == null || !SleddersGameBindings.TryGetAny(time, out object? atom, "TimeOfDay") || atom == null)
                return false;

            // Current build stores time-of-day in LBFEPEDFKGG<float>. ALDECJNAILK(T)
            // is its value setter; using the atom keeps the normal environment subscriptions alive.
            bool changed = SleddersGameBindings.TryCallAny(
                atom, new[] { "ALDECJNAILK" }, new object?[] { (float)value }, out _);
            if (changed)
                SleddersGameBindings.TryCallAny(time, new[] { "RefreshEnvironment" }, Array.Empty<object?>(), out _);
            return changed;
        }

        private static bool SetWeather(string name)
        {
            object? manager = GetSingleton("WeatherManager");
            object? setup = FindWeather(name);
            if (manager == null || setup == null)
                return false;

            if (!SleddersGameBindings.TryCallAny(
                    manager, new[] { "CreateFixedForecast" }, new object?[] { setup }, out object? forecast) || forecast == null)
                return false;
            if (!SleddersGameBindings.TryGetAny(manager, out object? forecastController, "Forecast") || forecastController == null)
                return false;

            // Current build's NCFELFGMEBC<T>.FGJBGONLDDG(T) applies the supplied forecast.
            return SleddersGameBindings.TryCallAny(
                forecastController, new[] { "FGJBGONLDDG" }, new object?[] { forecast }, out _);
        }

        private static object? FindWeather(string name)
        {
            object? manager = GetSingleton("WeatherManager");
            if (manager == null)
                return null;
            return SleddersGameBindings.TryCallAny(
                manager, new[] { "GetWeatherFromName" }, new object?[] { name }, out object? setup)
                ? setup
                : null;
        }

        private static object? GetCurrentWeather()
        {
            object? manager = GetSingleton("WeatherManager");
            if (manager == null)
                return null;
            return SleddersGameBindings.TryCallAny(
                manager, new[] { "get_WeatherCurrent" }, Array.Empty<object?>(), out object? current)
                ? current
                : null;
        }

        private static string GetWeatherName(object setup)
        {
            if (SleddersGameBindings.TryGetAny(setup, out object? raw, "displayName") && raw is string displayName && !string.IsNullOrWhiteSpace(displayName))
                return displayName;
            return ReflectionBridge.TryGetObjectName(setup) ?? setup.GetType().Name;
        }

        private static DynValue FuelBool(string getterStem)
        {
            object? manager = GetSingleton("FuelManager");
            if (manager != null && SleddersGameBindings.TryGetAnyOrGetter(manager, out object? raw, getterStem) && raw is bool value)
                return DynValue.NewBoolean(value);
            return DynValue.Nil;
        }

        private static DynValue CallString(string typeName, string method)
        {
            object? instance = GetSingleton(typeName);
            if (instance != null && SleddersGameBindings.TryCallAny(instance, new[] { method }, Array.Empty<object?>(), out object? raw) && raw != null)
                return DynValue.NewString(raw.ToString() ?? string.Empty);
            return DynValue.Nil;
        }

        private static DynValue StringArray(LuaModInstance mod, IEnumerable<string> values)
        {
            var table = new Table(mod.Script);
            int i = 1;
            foreach (string value in values)
                table.Set(i++, DynValue.NewString(value));
            return DynValue.NewTable(table);
        }

        private static object? GetSingleton(string typeName)
        {
            Type? type = ReflectionBridge.FindTypeExact(typeName);
            if (type == null)
                return null;
            try { return ReflectionBridge.GetStaticMember(type, "Instance"); }
            catch { return ReflectionBridge.FindObjectsOfType(type, 16).FirstOrDefault(); }
        }
    }
}
