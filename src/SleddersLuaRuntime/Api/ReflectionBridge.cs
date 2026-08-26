using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class ReflectionBridge
    {
        private static readonly object Gate = new object();
        private static List<Type>? _types;
        private static Type? _resourcesType;
        private static MethodInfo? _findObjectsOfTypeAll;
        private static int _assemblyCount;

        public static void Initialize()
        {
            RebuildTypeCache();
        }

        public static Type? FindTypeExact(string fullName)
        {
            EnsureTypeCache();
            return _types!.FirstOrDefault(t => string.Equals(t.FullName, fullName, StringComparison.Ordinal))
                ?? _types!.FirstOrDefault(t => string.Equals(t.Name, fullName, StringComparison.Ordinal));
        }

        public static IReadOnlyList<Type> FindTypes(string query, int max = 64)
        {
            EnsureTypeCache();
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<Type>();

            string q = query.Trim();
            return _types!
                .Where(t => Compat.Contains((t.FullName ?? t.Name), q, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => ScoreType(t, q))
                .ThenBy(t => t.FullName ?? t.Name, StringComparer.OrdinalIgnoreCase)
                .Take(Compat.Clamp(max, 1, 512))
                .ToArray();
        }

        public static IReadOnlyList<Type> FindDeveloperTypes(string query, int max = 64)
        {
            int requested = Compat.Clamp(max, 1, 512);
            return FindTypes(query, Math.Min(512, Math.Max(requested * 4, requested)))
                .Where(IsDeveloperTypeAllowed)
                .Take(requested)
                .ToArray();
        }

        public static bool IsDeveloperTypeAllowed(Type type)
        {
            string assembly = type.Assembly.GetName().Name ?? string.Empty;
            string ns = type.Namespace ?? string.Empty;

            if (assembly.Equals("mscorlib", StringComparison.OrdinalIgnoreCase) ||
                assembly.Equals("netstandard", StringComparison.OrdinalIgnoreCase) ||
                assembly.Equals("System", StringComparison.OrdinalIgnoreCase) ||
                assembly.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                assembly.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
                assembly.StartsWith("MoonSharp", StringComparison.OrdinalIgnoreCase) ||
                assembly.StartsWith("MelonLoader", StringComparison.OrdinalIgnoreCase) ||
                assembly.StartsWith("Harmony", StringComparison.OrdinalIgnoreCase) ||
                assembly.StartsWith("SleddersLuaRuntime", StringComparison.OrdinalIgnoreCase))
                return false;

            if (ns.Equals("System", StringComparison.Ordinal) ||
                ns.StartsWith("System.", StringComparison.Ordinal) ||
                ns.Equals("Microsoft", StringComparison.Ordinal) ||
                ns.StartsWith("Microsoft.", StringComparison.Ordinal) ||
                ns.StartsWith("MoonSharp", StringComparison.Ordinal) ||
                ns.StartsWith("MelonLoader", StringComparison.Ordinal))
                return false;

            return true;
        }

        public static IReadOnlyList<object> FindObjects(string query, int max = 64)
        {
            var result = new List<object>();
            foreach (Type type in FindDeveloperTypes(query, 32))
            {
                foreach (object obj in FindObjectsOfType(type, max - result.Count))
                {
                    result.Add(obj);
                    if (result.Count >= max)
                        return result;
                }
            }
            return result;
        }

        public static IReadOnlyList<object> FindObjectsOfType(Type type, int max = 64)
        {
            if (max <= 0 || type.IsAbstract || type.IsInterface || type.ContainsGenericParameters)
                return Array.Empty<object>();

            try
            {
                EnsureUnityDiscovery();
                if (_findObjectsOfTypeAll == null)
                    return Array.Empty<object>();

                object? raw = _findObjectsOfTypeAll.Invoke(null, new object?[] { type });
                if (raw is not IEnumerable enumerable)
                    return Array.Empty<object>();

                var list = new List<object>();
                foreach (object? item in enumerable)
                {
                    if (item == null)
                        continue;
                    list.Add(item);
                    if (list.Count >= max)
                        break;
                }
                return list;
            }
            catch
            {
                return Array.Empty<object>();
            }
        }

        public static object? GetMember(object target, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = target.GetType();

            PropertyInfo? property = FindProperty(type, name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(target);

            FieldInfo? field = FindField(type, name, flags);
            if (field != null)
                return field.GetValue(target);

            throw new MissingMemberException(type.FullName, name);
        }

        public static object? GetStaticMember(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo? property = FindProperty(type, name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(null);

            FieldInfo? field = FindField(type, name, flags);
            if (field != null)
                return field.GetValue(null);

            throw new MissingMemberException(type.FullName, name);
        }

        public static void SetMember(object target, string name, object? value)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = target.GetType();

            PropertyInfo? property = FindProperty(type, name, flags);
            if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                property.SetValue(target, value);
                return;
            }

            FieldInfo? field = FindField(type, name, flags);
            if (field != null && !field.IsInitOnly)
            {
                field.SetValue(target, value);
                return;
            }

            throw new MissingMemberException(type.FullName, name);
        }

        public static void SetStaticMember(Type type, string name, object? value)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo? property = FindProperty(type, name, flags);
            if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                property.SetValue(null, value);
                return;
            }

            FieldInfo? field = FindField(type, name, flags);
            if (field != null && !field.IsInitOnly)
            {
                field.SetValue(null, value);
                return;
            }

            throw new MissingMemberException(type.FullName, name);
        }

        public static object? Call(object target, string name, IReadOnlyList<object?> args)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = target.GetType();
            MethodInfo[] methods = type.GetMethods(flags)
                .Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) && !m.ContainsGenericParameters)
                .OrderBy(m => Math.Abs(m.GetParameters().Length - args.Count))
                .ToArray();

            Exception? lastError = null;
            foreach (MethodInfo method in methods)
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != args.Count)
                    continue;

                try
                {
                    object?[] converted = new object?[args.Count];
                    for (int i = 0; i < args.Count; i++)
                        converted[i] = ValueConverter.ChangeType(args[i], parameters[i].ParameterType);
                    return method.Invoke(target, converted);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw new MissingMethodException($"Could not invoke {type.FullName}.{name} with {args.Count} argument(s). Last error: {lastError?.Message}");
        }

        public static object? CallStatic(Type type, string name, IReadOnlyList<object?> args)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo[] methods = type.GetMethods(flags)
                .Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) && !m.ContainsGenericParameters)
                .OrderBy(m => Math.Abs(m.GetParameters().Length - args.Count))
                .ToArray();

            Exception? lastError = null;
            foreach (MethodInfo method in methods)
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != args.Count)
                    continue;

                try
                {
                    object?[] converted = new object?[args.Count];
                    for (int i = 0; i < args.Count; i++)
                        converted[i] = ValueConverter.ChangeType(args[i], parameters[i].ParameterType);
                    return method.Invoke(null, converted);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw new MissingMethodException($"Could not invoke static {type.FullName}.{name} with {args.Count} argument(s). Last error: {lastError?.Message}");
        }

        public static IReadOnlyList<string> DescribeTypeMembers(Type type, string filter = "", int max = 256)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var lines = new List<string>();
            string f = filter ?? string.Empty;

            foreach (PropertyInfo property in type.GetProperties(flags).OrderBy(p => p.Name))
            {
                if (!Matches(property.Name, f)) continue;
                MethodInfo? accessor = property.GetMethod ?? property.SetMethod;
                string scope = accessor?.IsStatic == true ? "static " : string.Empty;
                string access = (property.CanRead ? "get" : "") + (property.CanWrite ? "/set" : "");
                lines.Add($"P {scope}{FriendlyType(property.PropertyType)} {property.Name} [{access}]");
                if (lines.Count >= max) return lines;
            }

            foreach (FieldInfo field in type.GetFields(flags).OrderBy(p => p.Name))
            {
                if (!Matches(field.Name, f)) continue;
                string scope = field.IsStatic ? "static " : string.Empty;
                lines.Add($"F {scope}{FriendlyType(field.FieldType)} {field.Name}");
                if (lines.Count >= max) return lines;
            }

            foreach (MethodInfo method in type.GetMethods(flags).Where(m => !m.IsSpecialName).OrderBy(m => m.Name))
            {
                if (!Matches(method.Name, f)) continue;
                string scope = method.IsStatic ? "static " : string.Empty;
                string parameters = string.Join(", ", method.GetParameters().Select(p => FriendlyType(p.ParameterType) + " " + p.Name));
                lines.Add($"M {scope}{FriendlyType(method.ReturnType)} {method.Name}({parameters})");
                if (lines.Count >= max) return lines;
            }

            return lines;
        }

        public static IReadOnlyList<string> DescribeMembers(object target, string filter = "", int max = 256)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = target.GetType();
            var lines = new List<string>();
            string f = filter ?? string.Empty;

            foreach (PropertyInfo property in type.GetProperties(flags).OrderBy(p => p.Name))
            {
                if (!Matches(property.Name, f)) continue;
                string access = (property.CanRead ? "get" : "") + (property.CanWrite ? "/set" : "");
                lines.Add($"P {FriendlyType(property.PropertyType)} {property.Name} [{access}]");
                if (lines.Count >= max) return lines;
            }

            foreach (FieldInfo field in type.GetFields(flags).OrderBy(p => p.Name))
            {
                if (!Matches(field.Name, f)) continue;
                lines.Add($"F {FriendlyType(field.FieldType)} {field.Name}");
                if (lines.Count >= max) return lines;
            }

            foreach (MethodInfo method in type.GetMethods(flags).Where(m => !m.IsSpecialName).OrderBy(m => m.Name))
            {
                if (!Matches(method.Name, f)) continue;
                string parameters = string.Join(", ", method.GetParameters().Select(p => FriendlyType(p.ParameterType) + " " + p.Name));
                lines.Add($"M {FriendlyType(method.ReturnType)} {method.Name}({parameters})");
                if (lines.Count >= max) return lines;
            }

            return lines;
        }

        public static string Dump(object target, string filter = "", int max = 128)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = target.GetType();
            var lines = new List<string>
            {
                $"Type: {type.FullName}",
                $"Assembly: {type.Assembly.GetName().Name}",
                $"Name: {TryGetObjectName(target) ?? "<none>"}"
            };

            string f = filter ?? string.Empty;
            foreach (PropertyInfo property in type.GetProperties(flags).OrderBy(p => p.Name))
            {
                if (lines.Count >= max + 3) break;
                if (!property.CanRead || property.GetIndexParameters().Length != 0 || !Matches(property.Name, f)) continue;
                try
                {
                    object? value = property.GetValue(target);
                    if (IsSimpleValue(value))
                        lines.Add($"P {property.Name} = {FormatValue(value)}");
                }
                catch
                {
                }
            }

            foreach (FieldInfo field in type.GetFields(flags).OrderBy(p => p.Name))
            {
                if (lines.Count >= max + 3) break;
                if (!Matches(field.Name, f)) continue;
                try
                {
                    object? value = field.GetValue(target);
                    if (IsSimpleValue(value))
                        lines.Add($"F {field.Name} = {FormatValue(value)}");
                }
                catch
                {
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        public static IReadOnlyList<object> GetComponents(object target, string query, int max = 64)
        {
            object? gameObject = target;
            Type type = target.GetType();
            if (!string.Equals(type.FullName, "UnityEngine.GameObject", StringComparison.Ordinal))
            {
                try { gameObject = GetMember(target, "gameObject"); }
                catch { gameObject = null; }
            }

            if (gameObject == null)
                return Array.Empty<object>();

            Type? componentType = FindTypeExact("UnityEngine.Component");
            if (componentType == null)
                return Array.Empty<object>();

            try
            {
                MethodInfo? getComponents = gameObject.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(m =>
                        m.Name == "GetComponents" &&
                        !m.IsGenericMethod &&
                        m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType == typeof(Type));
                if (getComponents == null)
                    return Array.Empty<object>();

                object? raw = getComponents.Invoke(gameObject, new object?[] { componentType });
                if (raw is not IEnumerable enumerable)
                    return Array.Empty<object>();

                var results = new List<object>();
                foreach (object? item in enumerable)
                {
                    if (item == null) continue;
                    string fullName = item.GetType().FullName ?? item.GetType().Name;
                    if (!string.IsNullOrWhiteSpace(query) && !Compat.Contains(fullName, query, StringComparison.OrdinalIgnoreCase))
                        continue;
                    results.Add(item);
                    if (results.Count >= max) break;
                }
                return results;
            }
            catch
            {
                return Array.Empty<object>();
            }
        }

        public static IReadOnlyList<object> GetComponentsInChildren(object target, Type componentType, bool includeInactive = true, int max = 64)
        {
            if (target == null || componentType == null || max <= 0)
                return Array.Empty<object>();

            object? gameObject = target;
            Type targetType = target.GetType();
            if (!string.Equals(targetType.FullName, "UnityEngine.GameObject", StringComparison.Ordinal))
            {
                try { gameObject = GetMember(target, "gameObject"); }
                catch { gameObject = null; }
            }

            if (gameObject == null)
                return Array.Empty<object>();

            try
            {
                MethodInfo[] methods = gameObject.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Where(m => m.Name == "GetComponentsInChildren" && !m.IsGenericMethod)
                    .ToArray();

                MethodInfo? selected = methods.FirstOrDefault(m =>
                    m.GetParameters().Length == 2 &&
                    m.GetParameters()[0].ParameterType == typeof(Type) &&
                    m.GetParameters()[1].ParameterType == typeof(bool));

                object? raw;
                if (selected != null)
                {
                    raw = selected.Invoke(gameObject, new object?[] { componentType, includeInactive });
                }
                else
                {
                    selected = methods.FirstOrDefault(m =>
                        m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType == typeof(Type));
                    if (selected == null)
                        return Array.Empty<object>();

                    raw = selected.Invoke(gameObject, new object?[] { componentType });
                }

                if (raw is not IEnumerable enumerable)
                    return Array.Empty<object>();

                var results = new List<object>();
                foreach (object? item in enumerable)
                {
                    if (item == null)
                        continue;
                    results.Add(item);
                    if (results.Count >= max)
                        break;
                }
                return results;
            }
            catch
            {
                return Array.Empty<object>();
            }
        }

        public static bool TryGetMember(object target, string name, out object? value)
        {
            try
            {
                value = GetMember(target, name);
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        public static bool TrySetMember(object target, string name, object? value)
        {
            try
            {
                SetMember(target, name, value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryCall(object target, string name, IReadOnlyList<object?> args, out object? result)
        {
            try
            {
                result = Call(target, name, args);
                return true;
            }
            catch
            {
                result = null;
                return false;
            }
        }

        public static string? TryGetObjectName(object target)
        {
            try
            {
                object? name = GetMember(target, "name");
                return name?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static void EnsureTypeCache()
        {
            int currentCount = AppDomain.CurrentDomain.GetAssemblies().Length;
            if (_types == null || currentCount != _assemblyCount)
                RebuildTypeCache();
        }

        private static void RebuildTypeCache()
        {
            lock (Gate)
            {
                var types = new List<Type>();
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        types.AddRange(assembly.GetTypes());
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        types.AddRange(ex.Types.OfType<Type>());
                    }
                    catch
                    {
                    }
                }
                _types = types.Distinct().ToList();
                _assemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length;
                _resourcesType = null;
                _findObjectsOfTypeAll = null;
            }
        }

        private static void EnsureUnityDiscovery()
        {
            EnsureTypeCache();
            _resourcesType ??= FindTypeExact("UnityEngine.Resources");
            if (_resourcesType == null || _findObjectsOfTypeAll != null)
                return;

            _findObjectsOfTypeAll = _resourcesType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m =>
                    m.Name == "FindObjectsOfTypeAll" &&
                    !m.IsGenericMethod &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == typeof(Type));
        }

        private static int ScoreType(Type type, string query)
        {
            string name = type.Name;
            string full = type.FullName ?? name;
            int score = 0;
            if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase)) score += 1000;
            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) score += 500;
            if (Compat.Contains(name, query, StringComparison.OrdinalIgnoreCase)) score += 250;
            if (Compat.Contains(full, query, StringComparison.OrdinalIgnoreCase)) score += 100;
            if (type.IsClass && !type.IsAbstract) score += 20;
            if ((type.Namespace ?? string.Empty).StartsWith("UnityEngine", StringComparison.Ordinal)) score -= 10;
            return score;
        }

        private static string FriendlyType(Type type)
        {
            if (type.IsGenericType)
                return type.Name.Split('`')[0] + "<" + string.Join(",", type.GetGenericArguments().Select(FriendlyType)) + ">";
            return type.Name;
        }

        private static bool IsSimpleValue(object? value)
        {
            if (value == null) return true;
            Type type = value.GetType();
            return type.IsPrimitive || type.IsEnum || value is string || value is decimal ||
                   type.FullName is "UnityEngine.Vector2" or "UnityEngine.Vector3" or "UnityEngine.Vector4" or "UnityEngine.Quaternion" or "UnityEngine.Color";
        }

        private static string FormatValue(object? value)
        {
            if (value == null) return "nil";
            try { return value.ToString() ?? "<null-string>"; }
            catch { return $"<{value.GetType().Name}>"; }
        }
    }
}
