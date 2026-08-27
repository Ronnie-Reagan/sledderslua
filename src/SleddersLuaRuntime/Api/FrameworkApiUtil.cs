using System;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class FrameworkApiUtil
    {
        public static int MethodOffset(CallbackArguments args, Table table)
        {
            if (args.Count <= 0 || args[0].Type != DataType.Table) return 0;
            if (ReferenceEquals(args[0].Table, table)) return 1;
            DynValue a = args[0].Table.Get("__handle");
            DynValue b = table.Get("__handle");
            if (a.Type != DataType.Number || b.Type != DataType.Number) return 0;
            if (!IsIntegralHandle(a.Number) || !IsIntegralHandle(b.Number)) return 0;
            return (int)a.Number == (int)b.Number ? 1 : 0;
        }

        private static bool IsIntegralHandle(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 1.0 && value <= int.MaxValue &&
                   Math.Abs(value - Math.Round(value)) <= 0.0000001;
        }

        public static object? Resolve(LuaModInstance mod, int handle) => mod.Handles.Get(handle);

        public static object RequireObject(LuaModInstance mod, int handle, string label)
        {
            object? value = Resolve(mod, handle);
            if (value == null) throw new ScriptRuntimeException(label + " is no longer valid; its scene or owner may have been unloaded.");
            return value;
        }

        public static double RequireFiniteNumber(CallbackArguments args, int index, string usage)
        {
            if (args.Count <= index || args[index].Type != DataType.Number)
                throw new ScriptRuntimeException(usage + " expects a number.");
            double value = args[index].Number;
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ScriptRuntimeException(usage + " expects a finite number.");
            return value;
        }

        public static int RequireInt(CallbackArguments args, int index, string usage, int min, int max)
        {
            double raw = RequireFiniteNumber(args, index, usage);
            if (Math.Abs(raw - Math.Round(raw)) > 0.0000001 || raw < min || raw > max)
                throw new ScriptRuntimeException(usage + $" expects an integer from {min} to {max}.");
            return (int)raw;
        }

        public static bool RequireBool(CallbackArguments args, int index, string usage)
        {
            if (args.Count <= index || args[index].Type != DataType.Boolean)
                throw new ScriptRuntimeException(usage + " expects true or false.");
            return args[index].Boolean;
        }

        public static string RequireString(CallbackArguments args, int index, string usage)
        {
            if (args.Count <= index || args[index].Type != DataType.String || string.IsNullOrWhiteSpace(args[index].String))
                throw new ScriptRuntimeException(usage + " expects a non-empty string.");
            return args[index].String.Trim();
        }

        public static DynValue ToArray(LuaModInstance mod, System.Collections.Generic.IEnumerable<object> values, Func<LuaModInstance, object, DynValue> wrap)
        {
            var table = new Table(mod.Script);
            int i = 1;
            foreach (object value in values) table.Set(i++, wrap(mod, value));
            return DynValue.NewTable(table);
        }

        public static object? ReadVector3(LuaModInstance mod, CallbackArguments args, int index, string usage)
        {
            Type? vectorType = ReflectionBridge.FindTypeExact("UnityEngine.Vector3");
            if (vectorType == null) return null;
            if (args.Count <= index) throw new ScriptRuntimeException(usage + " requires a vector3 or x,y,z.");
            if (args[index].Type == DataType.Table)
                return ValueConverter.FromDynValue(mod, args[index], vectorType);
            if (args.Count >= index + 3)
            {
                double x = RequireFiniteNumber(args, index, usage);
                double y = RequireFiniteNumber(args, index + 1, usage);
                double z = RequireFiniteNumber(args, index + 2, usage);
                var t = GameplayApi.MakeVector3(mod.Script, x, y, z);
                return ValueConverter.FromDynValue(mod, DynValue.NewTable(t), vectorType);
            }
            throw new ScriptRuntimeException(usage + " requires a vector3 or x,y,z.");
        }

        public static object? ReadEulerQuaternion(LuaModInstance mod, CallbackArguments args, int index, string usage)
        {
            object? euler = ReadVector3(mod, args, index, usage);
            Type? quaternionType = ReflectionBridge.FindTypeExact("UnityEngine.Quaternion");
            if (euler == null || quaternionType == null) return null;
            Type? vectorType = ReflectionBridge.FindTypeExact("UnityEngine.Vector3");
            if (vectorType == null) return null;
            var method = quaternionType.GetMethod("Euler", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { vectorType }, null);
            try { return method?.Invoke(null, new[] { euler }); }
            catch { return null; }
        }
    }
}
