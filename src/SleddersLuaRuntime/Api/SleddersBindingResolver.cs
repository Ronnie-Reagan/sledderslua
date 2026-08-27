using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class SleddersBindingResolver
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static bool _initialized;
        private static bool _reportedFallback;

        private static Type? _controllerType;
        private static Type? _snowmobileType;
        private static MethodInfo? _controllerGetInstance;
        private static MethodInfo? _controllerGetSnowmobile;
        private static MethodInfo? _getFuel;
        private static MethodInfo? _getFuelCapacity;
        private static MethodInfo? _setFuel;
        private static MethodInfo? _addFuel;
        private static MethodInfo? _getRpm;
        private static MethodInfo? _setEngine;
        private static MethodInfo? _getVehicle;
        private static FieldInfo? _controllerBase;
        private static FieldInfo? _mainBody;
        private static FieldInfo? _respawnable;
        private static FieldInfo? _headlightController;
        private static FieldInfo? _structure;
        private static FieldInfo? _character;
        private static FieldInfo? _headlightState;
        private static FieldInfo? _engineState;
        private static FieldInfo? _inputState;
        private static FieldInfo? _throttleState;
        private static FieldInfo? _driverTransform;
        private static FieldInfo? _suspension;

        public static bool HasExactLocalSledBinding
        {
            get
            {
                EnsureInitialized();
                return _controllerGetInstance != null && _controllerGetSnowmobile != null;
            }
        }

        public static bool HasExactLocalPlayerBinding
        {
            get
            {
                EnsureInitialized();
                return HasExactLocalSledBinding && _controllerBase != null && _character != null;
            }
        }

        public static bool HasExactPlayerTransformBinding
        {
            get
            {
                EnsureInitialized();
                return _driverTransform != null;
            }
        }

        public static void Initialize() => Resolve();

        public static object? FindLocalSled()
        {
            EnsureInitialized();
            object? controller = InvokeStatic(_controllerGetInstance);
            return controller == null ? null : Invoke(_controllerGetSnowmobile, controller);
        }

        public static object? FindLocalPlayer()
        {
            object? sled = FindLocalSled();
            if (sled == null || !TryGetControllerBase(sled, out object? controllerBase) || controllerBase == null)
                return null;
            return GetField(_character, controllerBase);
        }

        public static object? GetPlayerTransform(object player)
        {
            EnsureInitialized();
            return GetField(_driverTransform, player);
        }

        public static bool TryGetControllerBase(object sled, out object? value)
        {
            value = GetField(_controllerBase, sled);
            return value != null;
        }

        public static bool TryGetMainBody(object sled, out object? value)
        {
            value = null;
            if (!TryGetControllerBase(sled, out object? controllerBase) || controllerBase == null)
                return false;
            value = GetField(_mainBody, controllerBase);
            return value != null;
        }

        public static bool TryGetRespawnable(object sled, out object? value)
        {
            value = null;
            if (!TryGetControllerBase(sled, out object? controllerBase) || controllerBase == null)
                return false;
            value = GetField(_respawnable, controllerBase);
            return value != null;
        }

        public static bool TryGetHeadlightController(object sled, out object? value)
        {
            value = null;
            if (!TryGetControllerBase(sled, out object? controllerBase) || controllerBase == null)
                return false;
            value = GetField(_headlightController, controllerBase);
            return value != null;
        }

        public static bool TryGetStructure(object sled, out object? value)
        {
            value = null;
            if (!TryGetControllerBase(sled, out object? controllerBase) || controllerBase == null)
                return false;
            value = GetField(_structure, controllerBase);
            return value != null;
        }

        public static bool TryGetSuspension(object sled, out object? suspension)
        {
            EnsureInitialized();
            suspension = GetField(_suspension, sled);
            return suspension != null;
        }

        public static bool TryGetVehicle(object sled, out object? vehicle)
        {
            EnsureInitialized();
            vehicle = Invoke(_getVehicle, sled);
            return vehicle != null;
        }

        public static bool TryGetFuelNormalized(object sled, out double value) => TryNumber(_getFuel, sled, out value);
        public static bool TryGetFuelCapacity(object sled, out double value) => TryNumber(_getFuelCapacity, sled, out value);
        public static bool TryGetRpm(object sled, out double value) => TryNumber(_getRpm, sled, out value);

        public static bool TrySetFuelNormalized(object sled, double value)
            => InvokeVoid(_setFuel, sled, Convert.ToSingle(value, CultureInfo.InvariantCulture));

        public static bool TryAddFuelNormalized(object sled, double value)
            => InvokeVoid(_addFuel, sled, Convert.ToSingle(value, CultureInfo.InvariantCulture));

        public static bool TrySetEngine(object sled, bool enabled) => InvokeVoid(_setEngine, sled, enabled);

        public static bool TryGetEngine(object sled, out bool enabled)
        {
            enabled = false;
            object? raw = GetField(_engineState, sled);
            if (raw is not bool state) return false;
            enabled = state;
            return true;
        }

        public static bool TryGetHeadlightState(object sled, out bool enabled)
        {
            enabled = false;
            object? raw = GetField(_headlightState, sled);
            if (raw is not bool state) return false;
            enabled = state;
            return true;
        }

        public static bool TrySetHeadlightState(object sled, bool enabled)
        {
            EnsureInitialized();
            try
            {
                if (_headlightState == null || !_headlightState.DeclaringType!.IsInstanceOfType(sled)) return false;
                _headlightState.SetValue(sled, enabled);
                return true;
            }
            catch { return false; }
        }

        public static bool TryGetThrottle(object sled, out double value)
        {
            value = 0.0;
            EnsureInitialized();
            try
            {
                if (_inputState == null || _throttleState == null || !_inputState.DeclaringType!.IsInstanceOfType(sled))
                    return false;
                object? state = _inputState.GetValue(sled);
                if (state == null) return false;
                object? raw = _throttleState.GetValue(state);
                if (raw == null) return false;
                value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch { return false; }
        }

        public static void ReportFallbackOnce()
        {
            if (_reportedFallback) return;
            _reportedFallback = true;
            RuntimeLog.Warn("Exact current-build local sled binding unavailable; using compatibility discovery.");
        }

        private static void EnsureInitialized()
        {
            if (!_initialized) Resolve();
        }

        private static void Resolve()
        {
            _controllerType = ReflectionBridge.FindTypeExact("Controller");
            _snowmobileType = ReflectionBridge.FindTypeExact("SnowmobileController");
            _controllerGetInstance = FindMethod(_controllerType, "get_Instance", StaticFlags, Type.EmptyTypes);
            _controllerGetSnowmobile = FindMethod(_controllerType, "get_SnowmobileController", InstanceFlags, Type.EmptyTypes);
            _getFuel = FindMethod(_snowmobileType, "get_Fuel", InstanceFlags, Type.EmptyTypes);
            _getFuelCapacity = FindMethod(_snowmobileType, "get_FuelCapacity", InstanceFlags, Type.EmptyTypes);
            _setFuel = FindMethod(_snowmobileType, "SetFuel", InstanceFlags, new[] { typeof(float) });
            _addFuel = FindMethod(_snowmobileType, "AddFuel", InstanceFlags, new[] { typeof(float) });
            _getRpm = FindMethod(_snowmobileType, "get_Rpm", InstanceFlags, Type.EmptyTypes);
            _setEngine = FindMethod(_snowmobileType, "SetEngineOnOff", InstanceFlags, new[] { typeof(bool) });
            _getVehicle = FindMethod(_snowmobileType, "get_Vehicle", InstanceFlags, Type.EmptyTypes);

            _controllerBase = _snowmobileType?.GetField("controllerBase", InstanceFlags);
            Type? baseType = _controllerBase?.FieldType;
            _mainBody = baseType?.GetField("mainBody", InstanceFlags);
            _respawnable = baseType?.GetField("respawnable", InstanceFlags);
            _headlightController = baseType?.GetField("headLightController", InstanceFlags);
            _structure = baseType?.GetField("MONBCLKFJPG", InstanceFlags);
            _character = baseType?.GetField("character", InstanceFlags);
            _headlightState = _snowmobileType?.GetField("isHeadlightOn", InstanceFlags);
            _engineState = _snowmobileType?.GetField("isEngineOn", InstanceFlags);
            _inputState = _snowmobileType?.GetField("GJKCDNOBELI", InstanceFlags);
            _suspension = _snowmobileType?.GetField("ADGKAPLIGNP", InstanceFlags);
            _throttleState = _inputState?.FieldType.GetField("AINANLMJJDH", InstanceFlags);

            Type? playerType = _character?.FieldType ?? ReflectionBridge.FindTypeExact("PlayerManager");
            _driverTransform = playerType?.GetField("driverTransform", InstanceFlags);

            _initialized = true;
            RuntimeLog.Info(
                "Sledders exact bindings: localSled=" + (HasExactLocalSledBinding ? "yes" : "no") +
                ", localPlayer=" + (HasExactLocalPlayerBinding ? "yes" : "no") +
                ", riderTransform=" + (HasExactPlayerTransformBinding ? "yes" : "no") +
                ", vehicle=" + (_getVehicle != null ? "yes" : "no") +
                ", body=" + (_mainBody != null ? "yes" : "no") +
                ", fuel=" + (_getFuel != null && _setFuel != null ? "yes" : "no") +
                ", suspension=" + (_suspension != null ? "yes" : "no"));
        }

        private static MethodInfo? FindMethod(Type? type, string name, BindingFlags flags, Type[] args)
            => type?.GetMethod(name, flags, null, args, null);

        private static object? InvokeStatic(MethodInfo? method)
        {
            try { return method?.Invoke(null, null); }
            catch { return null; }
        }

        private static object? Invoke(MethodInfo? method, object target)
        {
            try
            {
                if (method == null || !method.DeclaringType!.IsInstanceOfType(target)) return null;
                return method.Invoke(target, null);
            }
            catch { return null; }
        }

        private static bool InvokeVoid(MethodInfo? method, object target, params object?[] args)
        {
            try
            {
                if (method == null || !method.DeclaringType!.IsInstanceOfType(target)) return false;
                method.Invoke(target, args);
                return true;
            }
            catch { return false; }
        }

        private static bool TryNumber(MethodInfo? method, object target, out double value)
        {
            value = 0.0;
            object? raw = Invoke(method, target);
            if (raw == null) return false;
            try
            {
                value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch { return false; }
        }

        private static object? GetField(FieldInfo? field, object target)
        {
            try
            {
                if (field == null || !field.DeclaringType!.IsInstanceOfType(target)) return null;
                return field.GetValue(target);
            }
            catch { return null; }
        }
    }
}
