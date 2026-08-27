using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class ValueConverter
    {
        public static DynValue ToDynValue(LuaModInstance mod, object? value)
        {
            if (value == null)
                return DynValue.Nil;

            if (value is DynValue dyn)
                return dyn;

            Type type = value.GetType();
            if (value is string s) return DynValue.NewString(s);
            if (value is bool b) return DynValue.NewBoolean(b);
            if (value is char c) return DynValue.NewString(c.ToString());
            if (type.IsEnum) return DynValue.NewString(value.ToString() ?? string.Empty);
            if (IsNumber(type)) return DynValue.NewNumber(Convert.ToDouble(value, CultureInfo.InvariantCulture));
            if (value is Type reflectedType) return DynValue.NewString(reflectedType.FullName ?? reflectedType.Name);

            if (TryMakeUnityValueTable(mod.Script, value, out Table? vectorTable))
                return DynValue.NewTable(vectorTable);

            if (value is IEnumerable enumerable && value is not string)
            {
                var table = new Table(mod.Script);
                int index = 1;
                foreach (object? item in enumerable)
                {
                    table.Set(index++, ToDynValue(mod, item));
                    if (index > 257)
                        break;
                }
                return DynValue.NewTable(table);
            }

            if (!ReflectionBridge.IsDeveloperTypeAllowed(type))
                return DynValue.NewString($"<{type.FullName ?? type.Name}>");

            return ObjectProxyBuilder.Wrap(mod, value);
        }

        public static object? FromDynValue(LuaModInstance mod, DynValue value, Type targetType)
        {
            Type effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (value.IsNil())
            {
                if (!effectiveType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
                    return null;
                return Activator.CreateInstance(effectiveType);
            }

            if (value.Type == DataType.Table)
            {
                DynValue handle = value.Table.Get("__handle");
                if (handle.Type == DataType.Number)
                {
                    double rawHandle = handle.Number;
                    if (!double.IsNaN(rawHandle) && !double.IsInfinity(rawHandle) &&
                        rawHandle >= 1.0 && rawHandle <= int.MaxValue &&
                        Math.Abs(rawHandle - Math.Round(rawHandle)) <= 0.0000001)
                    {
                        object? resolved = mod.Handles.Get((int)rawHandle);
                        if (resolved == null)
                            return null;
                        if (effectiveType.IsInstanceOfType(resolved) || effectiveType == typeof(object))
                            return resolved;
                    }
                }

                if (TryCreateUnityValue(value.Table, effectiveType, out object? unityValue))
                    return unityValue;
            }

            object? plain = value.Type switch
            {
                DataType.String => value.String,
                DataType.Boolean => value.Boolean,
                DataType.Number => value.Number,
                _ => null
            };

            return ChangeType(plain, targetType);
        }

        public static object? ChangeType(object? value, Type targetType)
        {
            Type effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (value == null)
            {
                if (!effectiveType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
                    return null;
                return Activator.CreateInstance(effectiveType);
            }

            Type sourceType = value.GetType();
            if (value is double d)
            {
                if (double.IsNaN(d) || double.IsInfinity(d))
                    throw new ScriptRuntimeException("Numeric values must be finite.");
                if (IsIntegralType(effectiveType) || effectiveType.IsEnum)
                {
                    if (Math.Abs(d - Math.Round(d)) > 0.0000001)
                        throw new ScriptRuntimeException("This property expects an integer value.");
                }
                if (effectiveType == typeof(bool))
                    throw new ScriptRuntimeException("Boolean properties expect true or false, not a number.");
            }

            if (effectiveType.IsAssignableFrom(sourceType) || effectiveType == typeof(object))
                return value;

            if (effectiveType.IsEnum)
            {
                if (value is string text)
                    return Enum.Parse(effectiveType, text, ignoreCase: true);
                object numeric = Convert.ChangeType(value, Enum.GetUnderlyingType(effectiveType), CultureInfo.InvariantCulture);
                return Enum.ToObject(effectiveType, numeric);
            }

            if (effectiveType == typeof(string))
                return Convert.ToString(value, CultureInfo.InvariantCulture);

            if (effectiveType == typeof(Guid) && value is string guidText)
                return Guid.Parse(guidText);

            return Convert.ChangeType(value, effectiveType, CultureInfo.InvariantCulture);
        }

        public static object? DynToPlain(DynValue value)
        {
            return DynToPlain(value, new List<Table>(), 0);
        }

        private static object? DynToPlain(DynValue value, List<Table> stack, int depth)
        {
            if (depth > 32)
                throw new ScriptRuntimeException("Lua storage table nesting is too deep.");
            if (value.IsNil()) return null;
            if (value.Type == DataType.String) return value.String;
            if (value.Type == DataType.Boolean) return value.Boolean;
            if (value.Type == DataType.Number)
            {
                if (double.IsNaN(value.Number) || double.IsInfinity(value.Number))
                    throw new ScriptRuntimeException("Cannot persist NaN or infinity in Lua storage.");
                return value.Number;
            }

            if (value.Type == DataType.Table)
            {
                var table = value.Table;
                if (TrySemanticTableToPlain(table, out object? semanticValue))
                    return semanticValue;

                if (stack.Any(existing => ReferenceEquals(existing, table)))
                    throw new ScriptRuntimeException("Cannot persist a Lua table that contains itself.");
                stack.Add(table);
                try
                {
                    const int MaxStorageEntries = 65536;
                    int pairCount = 0;
                    bool allNumeric = true;
                    bool allString = true;
                    int maxIndex = 0;
                    var numericKeys = new HashSet<int>();
                    foreach (var pair in table.Pairs)
                    {
                        pairCount++;
                        if (pairCount > MaxStorageEntries)
                            throw new ScriptRuntimeException("Lua storage table is too large (maximum 65536 entries).");

                        if (pair.Key.Type == DataType.Number)
                        {
                            double raw = pair.Key.Number;
                            if (double.IsNaN(raw) || double.IsInfinity(raw) || raw < 1 || raw > MaxStorageEntries ||
                                Math.Abs(raw - Math.Round(raw)) > 0.00001)
                                throw new ScriptRuntimeException("Lua storage array indices must be contiguous integers from 1 to 65536.");
                            int index = (int)raw;
                            numericKeys.Add(index);
                            maxIndex = Math.Max(maxIndex, index);
                            allString = false;
                        }
                        else if (pair.Key.Type == DataType.String)
                        {
                            allNumeric = false;
                        }
                        else
                        {
                            throw new ScriptRuntimeException("Lua storage object keys must be strings; arrays must use integer indices.");
                        }
                    }

                    if (allNumeric)
                    {
                        if (maxIndex != pairCount || numericKeys.Count != pairCount)
                            throw new ScriptRuntimeException("Lua storage arrays must use contiguous indices starting at 1.");
                        var list = new List<object?>(maxIndex);
                        for (int i = 1; i <= maxIndex; i++)
                            list.Add(DynToPlain(table.Get(i), stack, depth + 1));
                        return list;
                    }

                    if (!allString)
                        throw new ScriptRuntimeException("Lua storage tables cannot mix array indices and string keys.");

                    var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var pair in table.Pairs)
                    {
                        string key = pair.Key.String;
                        if (key == "__handle") continue;
                        dict[key] = DynToPlain(pair.Value, stack, depth + 1);
                    }
                    return dict;
                }
                finally
                {
                    stack.RemoveAt(stack.Count - 1);
                }
            }

            throw new ScriptRuntimeException($"Cannot persist Lua value of type {value.Type}. Storage supports nil, strings, numbers, booleans, and plain tables.");
        }

        private static bool TrySemanticTableToPlain(Table table, out object? value)
        {
            value = null;
            DynValue typeValue = table.Get("__type");
            if (typeValue.Type != DataType.String)
                return false;

            string type = typeValue.String.Trim().ToLowerInvariant();
            string[] members;
            switch (type)
            {
                case "vector2": members = new[] { "x", "y" }; break;
                case "vector3": members = new[] { "x", "y", "z" }; break;
                case "vector4":
                case "quaternion": members = new[] { "x", "y", "z", "w" }; break;
                case "color": members = new[] { "r", "g", "b", "a" }; break;
                default: return false;
            }

            var dict = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                { "__type", type }
            };
            foreach (string member in members)
            {
                DynValue component = table.Get(member);
                if (component.Type == DataType.Number)
                {
                    if (double.IsNaN(component.Number) || double.IsInfinity(component.Number))
                        throw new ScriptRuntimeException("Cannot persist a semantic value containing NaN or infinity.");
                    dict[member] = component.Number;
                }
            }
            value = dict;
            return true;
        }

        public static DynValue PlainToDyn(Script script, object? value)
        {
            if (value == null) return DynValue.Nil;
            if (value is string s) return DynValue.NewString(s);
            if (value is bool b) return DynValue.NewBoolean(b);
            if (IsNumber(value.GetType())) return DynValue.NewNumber(Convert.ToDouble(value, CultureInfo.InvariantCulture));

            if (value is IDictionary<string, object?> dict)
            {
                var table = new Table(script);
                foreach (var pair in dict)
                    table.Set(pair.Key, PlainToDyn(script, pair.Value));

                if (dict.TryGetValue("__type", out object? rawType) && rawType is string semanticType)
                {
                    string[]? members = SemanticMembers(semanticType);
                    if (members != null)
                        DecorateValueTable(script, table, members);
                }
                return DynValue.NewTable(table);
            }

            if (value is IEnumerable enumerable)
            {
                var table = new Table(script);
                int index = 1;
                foreach (object? item in enumerable)
                    table.Set(index++, PlainToDyn(script, item));
                return DynValue.NewTable(table);
            }

            return DynValue.NewString(value.ToString() ?? string.Empty);
        }

        private static string[]? SemanticMembers(string semanticType)
        {
            switch ((semanticType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "vector2": return new[] { "x", "y" };
                case "vector3": return new[] { "x", "y", "z" };
                case "vector4":
                case "quaternion": return new[] { "x", "y", "z", "w" };
                case "color": return new[] { "r", "g", "b", "a" };
                default: return null;
            }
        }

        private static bool TryMakeUnityValueTable(Script script, object value, out Table? table)
        {
            table = null;
            string? fullName = value.GetType().FullName;
            string[] members;
            if (fullName is "UnityEngine.Vector2") members = new[] { "x", "y" };
            else if (fullName is "UnityEngine.Vector3") members = new[] { "x", "y", "z" };
            else if (fullName is "UnityEngine.Vector4" or "UnityEngine.Quaternion") members = new[] { "x", "y", "z", "w" };
            else if (fullName is "UnityEngine.Color") members = new[] { "r", "g", "b", "a" };
            else return false;

            table = new Table(script);
            string semanticType = fullName == "UnityEngine.Vector2" ? "vector2" :
                                  fullName == "UnityEngine.Vector3" ? "vector3" :
                                  fullName == "UnityEngine.Vector4" ? "vector4" :
                                  fullName == "UnityEngine.Quaternion" ? "quaternion" :
                                  fullName == "UnityEngine.Color" ? "color" : "value";
            table.Set("__type", DynValue.NewString(semanticType));
            foreach (string member in members)
            {
                object? component = ReadFieldOrProperty(value, member);
                if (component != null)
                    table.Set(member, DynValue.NewNumber(Convert.ToDouble(component, CultureInfo.InvariantCulture)));
            }
            DecorateValueTable(script, table, members);
            return true;
        }

        public static void DecorateValueTable(Script script, Table table, IReadOnlyList<string> members)
        {
            DynValue toString = DynValue.NewCallback((ctx, args) =>
            {
                var formatted = new List<string>(members.Count);
                foreach (string member in members)
                {
                    DynValue value = table.Get(member);
                    formatted.Add(value.Type == DataType.Number
                        ? value.Number.ToString("0.###", CultureInfo.InvariantCulture)
                        : "?");
                }
                return DynValue.NewString("(" + string.Join(", ", formatted) + ")");
            });

            table.Set("toString", toString);
            var meta = new Table(script);
            meta.Set("__tostring", toString);
            table.MetaTable = meta;
        }

        private static bool TryCreateUnityValue(Table table, Type targetType, out object? value)
        {
            value = null;
            string? fullName = targetType.FullName;
            string[] members;
            if (fullName is "UnityEngine.Vector2") members = new[] { "x", "y" };
            else if (fullName is "UnityEngine.Vector3") members = new[] { "x", "y", "z" };
            else if (fullName is "UnityEngine.Vector4" or "UnityEngine.Quaternion") members = new[] { "x", "y", "z", "w" };
            else if (fullName is "UnityEngine.Color") members = new[] { "r", "g", "b", "a" };
            else return false;

            object boxed = Activator.CreateInstance(targetType)!;
            foreach (string member in members)
            {
                DynValue dyn = table.Get(member);
                if (dyn.Type != DataType.Number)
                    continue;
                if (double.IsNaN(dyn.Number) || double.IsInfinity(dyn.Number))
                    throw new ScriptRuntimeException("Vector/color components must be finite numbers.");
                WriteFieldOrProperty(boxed, member, Convert.ToSingle(dyn.Number, CultureInfo.InvariantCulture));
            }
            value = boxed;
            return true;
        }

        private static object? ReadFieldOrProperty(object target, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type type = target.GetType();
            PropertyInfo? property = type.GetProperty(name, flags);
            if (property != null) return property.GetValue(target);
            FieldInfo? field = type.GetField(name, flags);
            return field?.GetValue(target);
        }

        private static void WriteFieldOrProperty(object target, string name, object value)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type type = target.GetType();
            PropertyInfo? property = type.GetProperty(name, flags);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, ChangeType(value, property.PropertyType));
                return;
            }
            FieldInfo? field = type.GetField(name, flags);
            if (field != null && !field.IsInitOnly)
                field.SetValue(target, ChangeType(value, field.FieldType));
        }

        private static bool IsIntegralType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
                   type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);
        }

        private static bool IsNumber(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
                   type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
                   type == typeof(float) || type == typeof(double) || type == typeof(decimal);
        }
    }
}
