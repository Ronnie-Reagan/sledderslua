using System;
using System.Globalization;
using System.Reflection;

namespace SleddersLuaRuntime.Api
{
    internal static class UnityBridge
    {
        private static Type? _timeType;
        private static PropertyInfo? _deltaTime;
        private static PropertyInfo? _fixedDeltaTime;
        private static Type? _inputType;
        private static Type? _keyCodeType;
        private static MethodInfo? _getKeyDown;
        private static MethodInfo? _getKey;
        private static MethodInfo? _getAxis;
        private static PropertyInfo? _anyKeyDown;
        private static object[]? _keyCodeValues;
        private static Type? _cameraType;
        private static PropertyInfo? _cameraMain;
        private static Type? _sceneManagerType;
        private static MethodInfo? _getActiveScene;
        private static Type? _applicationType;
        private static PropertyInfo? _applicationVersion;

        public static void Initialize()
        {
            RefreshTypes();
        }

        public static double DeltaTime => ReadFloat(_deltaTime, 0.0);
        public static double FixedDeltaTime => ReadFloat(_fixedDeltaTime, 0.02);

        public static bool GetKeyDown(string key)
        {
            try
            {
                string[] parts = SplitChord(key);
                if (parts.Length == 0)
                    return false;

                if (!ModifiersMatchExactly(parts))
                    return false;

                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (!GetSingleKey(parts[i], false))
                        return false;
                }

                return GetSingleKey(parts[parts.Length - 1], true);
            }
            catch
            {
                return false;
            }
        }

        public static bool GetKey(string key)
        {
            try
            {
                string[] parts = SplitChord(key);
                if (parts.Length == 0)
                    return false;

                for (int i = 0; i < parts.Length; i++)
                {
                    if (!GetSingleKey(parts[i], false))
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }


        public static string[] GetPressedKeys()
        {
            try
            {
                EnsureTypes();
                if (_getKeyDown == null || _keyCodeType == null)
                    return new string[0];

                object? any = _anyKeyDown?.GetValue(null);
                if (any is bool && !(bool)any)
                    return new string[0];

                if (_keyCodeValues == null)
                {
                    Array values = Enum.GetValues(_keyCodeType);
                    _keyCodeValues = new object[values.Length];
                    for (int i = 0; i < values.Length; i++)
                        _keyCodeValues[i] = values.GetValue(i)!;
                }

                var result = new System.Collections.Generic.List<string>();
                var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (object keyCode in _keyCodeValues)
                {
                    object? raw = _getKeyDown.Invoke(null, new[] { keyCode });
                    if (!(raw is bool) || !(bool)raw)
                        continue;

                    string name = FormatKeyName(keyCode.ToString() ?? string.Empty);
                    if (name.Length > 0 && seen.Add(name))
                        result.Add(name);
                }
                return result.ToArray();
            }
            catch
            {
                return new string[0];
            }
        }

        public static double GetAxis(string axis)
        {
            try
            {
                EnsureTypes();
                if (_getAxis == null)
                    return 0.0;
                object? value = _getAxis.Invoke(null, new object[] { axis });
                return value == null ? 0.0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0.0;
            }
        }

        public static object? MainCamera
        {
            get
            {
                try
                {
                    EnsureTypes();
                    return _cameraMain?.GetValue(null);
                }
                catch
                {
                    return null;
                }
            }
        }

        public static string ActiveSceneName
        {
            get
            {
                try
                {
                    EnsureTypes();
                    object? scene = _getActiveScene?.Invoke(null, null);
                    if (scene != null && ReflectionBridge.TryGetMember(scene, "name", out object? name) && name != null)
                        return name.ToString() ?? string.Empty;
                }
                catch
                {
                }
                return string.Empty;
            }
        }

        public static string GameVersion
        {
            get
            {
                try
                {
                    EnsureTypes();
                    object? value = _applicationVersion?.GetValue(null);
                    return value?.ToString() ?? "unknown";
                }
                catch
                {
                    return "unknown";
                }
            }
        }

        private static void EnsureTypes()
        {
            if (_timeType == null || _inputType == null || _cameraType == null)
                RefreshTypes();
        }

        private static void RefreshTypes()
        {
            _timeType = ReflectionBridge.FindTypeExact("UnityEngine.Time");
            _inputType = ReflectionBridge.FindTypeExact("UnityEngine.Input");
            _keyCodeType = ReflectionBridge.FindTypeExact("UnityEngine.KeyCode");
            _cameraType = ReflectionBridge.FindTypeExact("UnityEngine.Camera");
            _sceneManagerType = ReflectionBridge.FindTypeExact("UnityEngine.SceneManagement.SceneManager");
            _applicationType = ReflectionBridge.FindTypeExact("UnityEngine.Application");

            _deltaTime = _timeType?.GetProperty("deltaTime", BindingFlags.Public | BindingFlags.Static);
            _fixedDeltaTime = _timeType?.GetProperty("fixedDeltaTime", BindingFlags.Public | BindingFlags.Static);

            if (_inputType != null && _keyCodeType != null)
            {
                _getKeyDown = _inputType.GetMethod("GetKeyDown", BindingFlags.Public | BindingFlags.Static, null, new[] { _keyCodeType }, null);
                _getKey = _inputType.GetMethod("GetKey", BindingFlags.Public | BindingFlags.Static, null, new[] { _keyCodeType }, null);
                _getAxis = _inputType.GetMethod("GetAxis", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                _anyKeyDown = _inputType.GetProperty("anyKeyDown", BindingFlags.Public | BindingFlags.Static);
                _keyCodeValues = null;
            }

            _cameraMain = _cameraType?.GetProperty("main", BindingFlags.Public | BindingFlags.Static);
            _getActiveScene = _sceneManagerType?.GetMethod("GetActiveScene", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            _applicationVersion = _applicationType?.GetProperty("version", BindingFlags.Public | BindingFlags.Static);
        }

        private static double ReadFloat(PropertyInfo? property, double fallback)
        {
            try
            {
                object? value = property?.GetValue(null);
                return value == null ? fallback : Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }



        private static bool ModifiersMatchExactly(string[] parts)
        {
            bool wantsCtrl = ContainsModifier(parts, "CTRL", "CONTROL", "LEFTCONTROL", "RIGHTCONTROL");
            bool wantsShift = ContainsModifier(parts, "SHIFT", "LEFTSHIFT", "RIGHTSHIFT");
            bool wantsAlt = ContainsModifier(parts, "ALT", "LEFTALT", "RIGHTALT");

            bool hasCtrl = GetSingleKey("CTRL", false);
            bool hasShift = GetSingleKey("SHIFT", false);
            bool hasAlt = GetSingleKey("ALT", false);

            return wantsCtrl == hasCtrl && wantsShift == hasShift && wantsAlt == hasAlt;
        }

        private static bool ContainsModifier(string[] parts, params string[] aliases)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                for (int j = 0; j < aliases.Length; j++)
                {
                    if (part.Equals(aliases[j], StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        private static bool GetSingleKey(string key, bool downOnly)
        {
            EnsureTypes();
            if (_keyCodeType == null)
                return false;

            MethodInfo? method = downOnly ? _getKeyDown : _getKey;
            if (method == null)
                return false;

            string trimmed = key.Trim();
            if (trimmed.Equals("CTRL", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("CONTROL", StringComparison.OrdinalIgnoreCase))
                return InvokeKey(method, "LeftControl") || InvokeKey(method, "RightControl");
            if (trimmed.Equals("SHIFT", StringComparison.OrdinalIgnoreCase))
                return InvokeKey(method, "LeftShift") || InvokeKey(method, "RightShift");
            if (trimmed.Equals("ALT", StringComparison.OrdinalIgnoreCase))
                return InvokeKey(method, "LeftAlt") || InvokeKey(method, "RightAlt");

            return InvokeKey(method, NormalizeKeyName(trimmed));
        }

        private static bool InvokeKey(MethodInfo method, string keyName)
        {
            object parsed = Enum.Parse(_keyCodeType, keyName, true);
            object raw = method.Invoke(null, new[] { parsed });
            return raw is bool && (bool)raw;
        }

        private static string[] SplitChord(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return new string[0];

            string[] raw = key.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            var parts = new System.Collections.Generic.List<string>();
            for (int i = 0; i < raw.Length; i++)
            {
                string part = raw[i].Trim();
                if (part.Length > 0)
                    parts.Add(part);
            }
            return parts.ToArray();
        }

        private static string NormalizeKeyName(string key)
        {
            string trimmed = key.Trim();
            if (trimmed.Equals("CTRL", StringComparison.OrdinalIgnoreCase)) return "LeftControl";
            if (trimmed.Equals("SHIFT", StringComparison.OrdinalIgnoreCase)) return "LeftShift";
            if (trimmed.Equals("ALT", StringComparison.OrdinalIgnoreCase)) return "LeftAlt";
            if (trimmed.Equals("ENTER", StringComparison.OrdinalIgnoreCase)) return "Return";
            if (trimmed.Equals("ESC", StringComparison.OrdinalIgnoreCase)) return "Escape";
            if (trimmed.Equals("SPACE", StringComparison.OrdinalIgnoreCase)) return "Space";
            if (trimmed.Length == 1 && trimmed[0] >= '0' && trimmed[0] <= '9') return "Alpha" + trimmed;
            return trimmed;
        }

        private static string FormatKeyName(string keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return string.Empty;
            if (keyName.StartsWith("Alpha", StringComparison.OrdinalIgnoreCase) && keyName.Length == 6)
                return keyName.Substring(5, 1);
            if (keyName.Length == 1) return keyName.ToLowerInvariant();
            if (keyName.Equals("Return", StringComparison.OrdinalIgnoreCase)) return "enter";
            if (keyName.Equals("Escape", StringComparison.OrdinalIgnoreCase)) return "escape";
            if (keyName.Equals("Space", StringComparison.OrdinalIgnoreCase)) return "space";
            return keyName.ToLowerInvariant();
        }
    }
}
