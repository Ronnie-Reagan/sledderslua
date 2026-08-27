using System;
using System.Globalization;
using System.Reflection;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class AudioApi
    {
        private static Type? _listenerType;
        private static PropertyInfo? _volume;

        public static Table Build(LuaModInstance mod)
        {
            Ensure();
            var table = new Table(mod.Script);
            table.Set("getVolume", DynValue.NewCallback((ctx, args) =>
            {
                double? volume = GetVolume();
                return volume.HasValue ? DynValue.NewNumber(volume.Value) : DynValue.Nil;
            }));
            table.Set("setVolume", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                double value = FrameworkApiUtil.RequireFiniteNumber(args, offset, "audio.setVolume(value)");
                return DynValue.NewBoolean(SetVolume(value));
            }));
            AudioRuntimeApi.Enhance(mod, table);
            return table;
        }

        private static double? GetVolume()
        {
            try
            {
                Ensure();
                object? raw = _volume?.GetValue(null);
                return raw == null ? (double?)null : Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            }
            catch { return null; }
        }

        private static bool SetVolume(double value)
        {
            try
            {
                Ensure();
                if (_volume == null || !_volume.CanWrite) return false;
                value = Math.Max(0.0, Math.Min(1.0, value));
                _volume.SetValue(null, Convert.ToSingle(value, CultureInfo.InvariantCulture));
                return true;
            }
            catch { return false; }
        }

        private static void Ensure()
        {
            if (_listenerType != null) return;
            _listenerType = ReflectionBridge.FindTypeExact("UnityEngine.AudioListener");
            _volume = _listenerType?.GetProperty("volume", BindingFlags.Public | BindingFlags.Static);
        }

        private static int MethodOffset(CallbackArguments args, Table table)
        {
            return args.Count > 0 && args[0].Type == DataType.Table && ReferenceEquals(args[0].Table, table) ? 1 : 0;
        }
    }
}
