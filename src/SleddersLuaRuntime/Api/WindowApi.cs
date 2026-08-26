using System;
using System.Globalization;
using System.Reflection;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class WindowApi
    {
        private static Type? _screenType;
        private static Type? _applicationType;
        private static PropertyInfo? _width;
        private static PropertyInfo? _height;
        private static PropertyInfo? _focused;

        public static Table Build(LuaModInstance mod)
        {
            Ensure();
            var table = new Table(mod.Script);
            table.Set("getWidth", DynValue.NewCallback((ctx, args) => DynValue.NewNumber(GetWidth())));
            table.Set("getHeight", DynValue.NewCallback((ctx, args) => DynValue.NewNumber(GetHeight())));
            table.Set("isFocused", DynValue.NewCallback((ctx, args) =>
            {
                bool? focused = GetFocused();
                return focused.HasValue ? DynValue.NewBoolean(focused.Value) : DynValue.Nil;
            }));
            table.Set("getResolution", DynValue.NewCallback((ctx, args) =>
            {
                double width = GetWidth();
                double height = GetHeight();
                var result = new Table(mod.Script);
                result.Set("width", DynValue.NewNumber(width));
                result.Set("height", DynValue.NewNumber(height));
                result.Set("aspect", DynValue.NewNumber(height > 0.0 ? width / height : 0.0));
                return DynValue.NewTable(result);
            }));
            return table;
        }

        public static int GetWidth()
        {
            try
            {
                Ensure();
                object? raw = _width?.GetValue(null);
                return raw == null ? 0 : Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            }
            catch { return 0; }
        }

        public static int GetHeight()
        {
            try
            {
                Ensure();
                object? raw = _height?.GetValue(null);
                return raw == null ? 0 : Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            }
            catch { return 0; }
        }

        public static bool? GetFocused()
        {
            try
            {
                Ensure();
                object? raw = _focused?.GetValue(null);
                return raw is bool ? (bool)raw : (bool?)null;
            }
            catch { return null; }
        }

        private static void Ensure()
        {
            if (_screenType != null) return;
            _screenType = ReflectionBridge.FindTypeExact("UnityEngine.Screen");
            _applicationType = ReflectionBridge.FindTypeExact("UnityEngine.Application");
            _width = _screenType?.GetProperty("width", BindingFlags.Public | BindingFlags.Static);
            _height = _screenType?.GetProperty("height", BindingFlags.Public | BindingFlags.Static);
            _focused = _applicationType?.GetProperty("isFocused", BindingFlags.Public | BindingFlags.Static);
        }
    }
}
