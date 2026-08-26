using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class TypeProxyBuilder
    {
        public static DynValue Wrap(LuaModInstance mod, Type type)
        {
            if (!ReflectionBridge.IsDeveloperTypeAllowed(type))
                throw new ScriptRuntimeException($"Developer reflection is blocked for framework/runtime type '{type.FullName ?? type.Name}'.");

            var table = new Table(mod.Script);
            table.Set("name", DynValue.NewString(type.Name));
            table.Set("fullName", DynValue.NewString(type.FullName ?? type.Name));
            table.Set("assembly", DynValue.NewString(type.Assembly.GetName().Name ?? string.Empty));

            table.Set("members", DynValue.NewCallback((ctx, args) =>
            {
                mod.DemandPermission("dev");
                int offset = MethodOffset(args, table);
                string filter = args.Count > offset && args[offset].Type == DataType.String ? args[offset].String : string.Empty;
                int max = args.Count > offset + 1 && args[offset + 1].Type == DataType.Number
                    ? Compat.Clamp((int)args[offset + 1].Number, 1, 1024)
                    : 256;
                return ObjectProxyBuilder.ToStringArray(mod.Script, ReflectionBridge.DescribeTypeMembers(type, filter, max));
            }));

            table.Set("get", DynValue.NewCallback((ctx, args) =>
            {
                mod.DemandPermission("dev");
                int offset = MethodOffset(args, table);
                string member = RequireString(args, offset, "type:get(member)");
                return ValueConverter.ToDynValue(mod, ReflectionBridge.GetStaticMember(type, member));
            }));

            table.Set("set", DynValue.NewCallback((ctx, args) =>
            {
                mod.DemandPermission("dev");
                int offset = MethodOffset(args, table);
                string member = RequireString(args, offset, "type:set(member, value)");
                if (args.Count <= offset + 1)
                    throw new ScriptRuntimeException("type:set(member, value) requires a value.");

                Type memberType = ResolveStaticMemberType(type, member);
                object? converted = ValueConverter.FromDynValue(mod, args[offset + 1], memberType);
                ReflectionBridge.SetStaticMember(type, member, converted);
                return DynValue.True;
            }));

            table.Set("call", DynValue.NewCallback((ctx, args) =>
            {
                mod.DemandPermission("dev");
                int offset = MethodOffset(args, table);
                string method = RequireString(args, offset, "type:call(method, ...)");
                var rawArgs = new List<object?>();
                for (int i = offset + 1; i < args.Count; i++)
                    rawArgs.Add(ObjectProxyBuilder.DynToRaw(mod, args[i]));
                return ValueConverter.ToDynValue(mod, ReflectionBridge.CallStatic(type, method, rawArgs));
            }));

            return DynValue.NewTable(table);
        }


        internal static DynValue ToTypeArray(LuaModInstance mod, IEnumerable<Type> types)
        {
            var table = new Table(mod.Script);
            int i = 1;
            foreach (Type type in types)
                table.Set(i++, Wrap(mod, type));
            return DynValue.NewTable(table);
        }

        private static Type ResolveStaticMemberType(Type type, string member)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo? property = type.GetProperties(flags).FirstOrDefault(p => string.Equals(p.Name, member, StringComparison.OrdinalIgnoreCase));
            if (property != null) return property.PropertyType;
            FieldInfo? field = type.GetFields(flags).FirstOrDefault(f => string.Equals(f.Name, member, StringComparison.OrdinalIgnoreCase));
            if (field != null) return field.FieldType;
            throw new MissingMemberException(type.FullName, member);
        }

        private static int MethodOffset(CallbackArguments args, Table table)
        {
            if (args.Count > 0 && args[0].Type == DataType.Table && ReferenceEquals(args[0].Table, table))
                return 1;
            return 0;
        }

        private static string RequireString(CallbackArguments args, int index, string usage)
        {
            if (args.Count <= index || args[index].Type != DataType.String || string.IsNullOrWhiteSpace(args[index].String))
                throw new ScriptRuntimeException(usage + " expects a non-empty string.");
            return args[index].String.Trim();
        }
    }
}
