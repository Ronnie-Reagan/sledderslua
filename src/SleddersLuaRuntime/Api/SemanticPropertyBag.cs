using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal sealed class SemanticProperty
    {
        public SemanticProperty(string key, bool writable, params string[] names)
        {
            Key = key;
            Writable = writable;
            Names = names;
        }

        public string Key { get; }
        public bool Writable { get; }
        public string[] Names { get; }
    }

    internal static class SemanticPropertyBag
    {
        public static DynValue Wrap(LuaModInstance mod, object target, string kind, IReadOnlyList<SemanticProperty> properties)
        {
            int handle = mod.Handles.Add(target);
            if (mod.TryGetCachedObject("bag:" + kind, handle, out DynValue cached))
                return cached;

            var table = new Table(mod.Script);
            table.Set("__handle", DynValue.NewNumber(handle));
            table.Set("__type", DynValue.NewString(kind));

            table.Set("isValid", DynValue.NewCallback((ctx, args) =>
                DynValue.NewBoolean(FrameworkApiUtil.Resolve(mod, handle) != null)));

            table.Set("keys", DynValue.NewCallback((ctx, args) =>
            {
                var result = new Table(mod.Script);
                int i = 1;
                foreach (SemanticProperty property in properties)
                    result.Set(i++, DynValue.NewString(property.Key));
                return DynValue.NewTable(result);
            }));

            table.Set("isWritable", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string key = FrameworkApiUtil.RequireString(args, offset, kind + ".isWritable(key)");
                SemanticProperty? property = Find(properties, key);
                return property == null ? DynValue.Nil : DynValue.NewBoolean(property.Writable);
            }));

            table.Set("get", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, kind);
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string key = FrameworkApiUtil.RequireString(args, offset, kind + ".get(key)");
                SemanticProperty? property = Find(properties, key);
                if (property == null) return DynValue.Nil;
                return SleddersGameBindings.TryGetAnyOrGetter(live, out object? value, property.Names)
                    ? ValueConverter.ToDynValue(mod, value)
                    : DynValue.Nil;
            }));

            table.Set("set", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, kind);
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string key = FrameworkApiUtil.RequireString(args, offset, kind + ".set(key, value)");
                if (args.Count <= offset + 1) throw new ScriptRuntimeException(kind + ".set(key, value) requires a value.");
                SemanticProperty? property = Find(properties, key);
                if (property == null || !property.Writable) return DynValue.False;
                return DynValue.NewBoolean(TrySet(mod, live, property, args[offset + 1]));
            }));

            DynValue wrapped = DynValue.NewTable(table);
            mod.CacheObject("bag:" + kind, handle, wrapped);
            return wrapped;
        }

        public static void AddNamedAccessors(Table table, LuaModInstance mod, DynValue bagValue, string propertyKey, string methodStem)
        {
            Table bag = bagValue.Table;
            table.Set("get" + methodStem, DynValue.NewCallback((ctx, args) =>
                mod.Script.Call(bag.Get("get"), DynValue.NewTable(bag), DynValue.NewString(propertyKey))));
            table.Set("set" + methodStem, DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                if (args.Count <= offset) throw new ScriptRuntimeException("set" + methodStem + "(value) requires a value.");
                return mod.Script.Call(bag.Get("set"), DynValue.NewTable(bag), DynValue.NewString(propertyKey), args[offset]);
            }));
        }

        private static SemanticProperty? Find(IReadOnlyList<SemanticProperty> properties, string key)
        {
            for (int i = 0; i < properties.Count; i++)
                if (string.Equals(properties[i].Key, key, StringComparison.OrdinalIgnoreCase)) return properties[i];
            return null;
        }

        private static bool TrySet(LuaModInstance mod, object target, SemanticProperty property, DynValue value)
        {
            foreach (string name in property.Names)
            {
                Type? memberType = SleddersGameBindings.ResolveMemberType(target, name);
                if (memberType == null) continue;
                object? converted;
                try { converted = ValueConverter.FromDynValue(mod, value, memberType); }
                catch (ScriptRuntimeException) { throw; }
                catch { continue; }
                if (ReflectionBridge.TrySetMember(target, name, converted)) return true;
            }
            return false;
        }
    }
}
