using System;
using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class ObjectProxyBuilder
    {
        public static DynValue Wrap(LuaModInstance mod, object value)
        {
            int handle = mod.Handles.Add(value);
            string typeName = value.GetType().FullName ?? value.GetType().Name;
            var table = new Table(mod.Script);
            table.Set("__handle", DynValue.NewNumber(handle));
            table.Set("__type", DynValue.NewString(typeName));

            table.Set("typeName", DynValue.NewCallback((ctx, args) => DynValue.NewString(typeName)));
            table.Set("isValid", DynValue.NewCallback((ctx, args) => DynValue.NewBoolean(mod.Handles.Get(handle) != null)));
            table.Set("name", DynValue.NewCallback((ctx, args) =>
            {
                object? target = mod.Handles.Get(handle);
                if (target == null) return DynValue.Nil;
                string? name = ReflectionBridge.TryGetObjectName(target);
                return name == null ? DynValue.Nil : DynValue.NewString(name);
            }));
            table.Set("get", DynValue.NewCallback((ctx, args) =>
            {
                mod.DemandPermission("dev"); object target = RequireObject(mod, handle);
                int offset = MethodOffset(args, table); string member = RequireString(args, offset, "object:get(member)");
                return ValueConverter.ToDynValue(mod, ReflectionBridge.GetMember(target, member));
            }));
            table.Set("set", DynValue.NewCallback((ctx, args) =>
            {
                mod.DemandPermission("dev"); object target = RequireObject(mod, handle);
                int offset = MethodOffset(args, table); string member = RequireString(args, offset, "object:set(member, value)");
                if (args.Count <= offset + 1) throw new ScriptRuntimeException("object:set(member, value) requires a value.");
                Type memberType = ResolveMemberType(target, member);
                ReflectionBridge.SetMember(target, member, ValueConverter.FromDynValue(mod, args[offset + 1], memberType));
                return DynValue.True;
            }));
            table.Set("call", DynValue.NewCallback((ctx, args) =>
            {
                mod.DemandPermission("dev"); object target = RequireObject(mod, handle);
                int offset = MethodOffset(args, table); string method = RequireString(args, offset, "object:call(method, ...)");
                var rawArgs = new List<object?>();
                for (int i = offset + 1; i < args.Count; i++) rawArgs.Add(DynToRaw(mod, args[i]));
                return ValueConverter.ToDynValue(mod, ReflectionBridge.Call(target, method, rawArgs));
            }));
            table.Set("members", DynValue.NewCallback((ctx, args) =>
            {
                mod.DemandPermission("dev"); object target = RequireObject(mod, handle);
                int offset = MethodOffset(args, table); string filter = args.Count > offset && args[offset].Type == DataType.String ? args[offset].String : string.Empty;
                return ToStringArray(mod.Script, ReflectionBridge.DescribeMembers(target, filter));
            }));
            table.Set("components", DynValue.NewCallback((ctx, args) =>
            {
                mod.DemandPermission("dev"); object target = RequireObject(mod, handle);
                int offset = MethodOffset(args, table); string query = args.Count > offset && args[offset].Type == DataType.String ? args[offset].String : string.Empty;
                return ToObjectArray(mod, ReflectionBridge.GetComponents(target, query, mod.Host.Config.MaxDiscoveryResults));
            }));
            table.Set("dump", DynValue.NewCallback((ctx, args) =>
            {
                mod.DemandPermission("dev"); object target = RequireObject(mod, handle);
                int offset = MethodOffset(args, table); string filter = args.Count > offset && args[offset].Type == DataType.String ? args[offset].String : string.Empty;
                return DynValue.NewString(ReflectionBridge.Dump(target, filter));
            }));
            return DynValue.NewTable(table);
        }

        private static object RequireObject(LuaModInstance mod, int handle)
        {
            object? value = mod.Handles.Get(handle);
            if (value == null) throw new ScriptRuntimeException("Object handle is no longer valid. The object may have been unloaded with its scene.");
            return value;
        }

        internal static object? DynToRaw(LuaModInstance mod, DynValue value)
        {
            if (value.IsNil()) return null;
            if (value.Type == DataType.String) return value.String;
            if (value.Type == DataType.Number) return value.Number;
            if (value.Type == DataType.Boolean) return value.Boolean;
            if (value.Type == DataType.Table)
            {
                DynValue handle = value.Table.Get("__handle");
                if (handle.Type == DataType.Number && !double.IsNaN(handle.Number) && !double.IsInfinity(handle.Number) &&
                    handle.Number >= 1.0 && handle.Number <= int.MaxValue && Math.Abs(handle.Number - Math.Round(handle.Number)) <= 0.0000001)
                    return mod.Handles.Get((int)handle.Number);
            }
            return ValueConverter.DynToPlain(value);
        }

        private static Type ResolveMemberType(object value, string member)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            Type type = value.GetType();
            var property = type.GetProperties(flags).FirstOrDefault(p => string.Equals(p.Name, member, StringComparison.OrdinalIgnoreCase));
            if (property != null) return property.PropertyType;
            var field = type.GetFields(flags).FirstOrDefault(f => string.Equals(f.Name, member, StringComparison.OrdinalIgnoreCase));
            if (field != null) return field.FieldType;
            throw new MissingMemberException(type.FullName, member);
        }

        private static int MethodOffset(CallbackArguments args, Table table)
        {
            if (args.Count <= 0 || args[0].Type != DataType.Table)
                return 0;

            if (ReferenceEquals(args[0].Table, table))
                return 1;

            // Lua wrappers for the same object may share a handle.
            DynValue expected = table.Get("__handle");
            DynValue actual = args[0].Table.Get("__handle");
            if (expected.Type == DataType.Number && actual.Type == DataType.Number &&
                (int)expected.Number == (int)actual.Number)
                return 1;

            return 0;
        }

        private static string RequireString(CallbackArguments args, int index, string usage)
        {
            if (args.Count <= index || args[index].Type != DataType.String || string.IsNullOrWhiteSpace(args[index].String))
                throw new ScriptRuntimeException(usage + " expects a non-empty string.");
            return args[index].String.Trim();
        }

        internal static DynValue ToStringArray(Script script, IEnumerable<string> values)
        {
            var table = new Table(script);
            int i = 1;
            foreach (string value in values)
                table.Set(i++, DynValue.NewString(value));
            return DynValue.NewTable(table);
        }

        internal static DynValue ToObjectArray(LuaModInstance mod, IEnumerable<object> values)
        {
            var table = new Table(mod.Script);
            int i = 1;
            foreach (object value in values)
                table.Set(i++, Wrap(mod, value));
            return DynValue.NewTable(table);
        }
    }
}
