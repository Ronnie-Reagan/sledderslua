using System;
using System.Globalization;
using System.Reflection;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    /// <summary>
    /// Resolves deterministic entry points exposed by the current Sledders assembly.
    /// Stable APIs should prefer these paths and use broad reflection discovery only
    /// as a compatibility fallback.
    /// </summary>
    internal static class SleddersBindingResolver
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static Type? _controllerType;
        private static Type? _netClientType;
        private static Type? _snowmobileType;

        private static MethodInfo? _controllerGetInstance;
        private static MethodInfo? _controllerGetSnowmobile;
        private static MethodInfo? _netClientGetInstance;
        private static MethodInfo? _netClientGetLocalPlayer;

        private static MethodInfo? _getFuel;
        private static MethodInfo? _getFuelCapacity;
        private static MethodInfo? _setFuel;
        private static MethodInfo? _addFuel;
        private static MethodInfo? _getRpm;
        private static MethodInfo? _setEngineOnOff;
        private static MethodInfo? _getVehicle;

        private static FieldInfo? _controllerBase;
        private static FieldInfo? _mainBody;
        private static FieldInfo? _respawnable;
        private static FieldInfo? _headLightController;
        private static FieldInfo? _headlightState;
        private static FieldInfo? _engineState;
        private static FieldInfo? _inputState;
        private static FieldInfo? _throttleState;

        private static bool _initialized;
        private static bool _reportedControllerFallback;

        public static void Initialize()
        {
            ResolveTypes();
        }

        public static object? FindLocalSled()
        {
            EnsureInitialized();

            object? controller = InvokeStatic(_controllerGetInstance);
            return controller == null ? null : InvokeInstance(_controllerGetSnowmobile, controller);
        }

        public static object? FindLocalPlayer()
        {
            EnsureInitialized();

            object? netClient = InvokeStatic(_netClientGetInstance);
            return netClient == null ? null : InvokeInstance(_netClientGetLocalPlayer, netClient);
        }

        public static bool TryGetFuelNormalized(object sled, out double value)
        {
            return TryInvokeNumber(_getFuel, sled, out value);
        }

        public static bool TryGetFuelCapacity(object sled, out double value)
        {
            return TryInvokeNumber(_getFuelCapacity, sled, out value);
        }

        public static bool TrySetFuelNormalized(object sled, double value)
        {
            return TryInvokeVoid(_setFuel, sled, Convert.ToSingle(value, CultureInfo.InvariantCulture));
        }

        public static bool TryAddFuelNormalized(object sled, double amount)
        {
            return TryInvokeVoid(_addFuel, sled, Convert.ToSingle(amount, CultureInfo.InvariantCulture));
        }

        public static bool TryGetRpm(object sled, out double value)
        {
            return TryInvokeNumber(_getRpm, sled, out value);
        }

        public static bool TrySetEngineRunning(object sled, bool running)
        {
            return TryInvokeVoid(_setEngineOnOff, sled, running);
        }

        public static bool TryGetEngineRunning(object sled, out bool running)
        {
            running = false;
            object? raw = TryGetField(_engineState, sled);
            if (raw is not bool state)
                return false;

            running = state;
            return true;
        }

        public static bool TryGetVehicle(object sled, out object? vehicle)
        {
            vehicle = InvokeInstance(_getVehicle, sled);
            return vehicle != null;
        }

        public static bool TryGetControllerBase(object sled, out object? controllerBase)
        {
            controllerBase = TryGetField(_controllerBase, sled);
            return controllerBase != null;
        }

        public static bool TryGetMainBody(object sled, out object? body)
        {
            body = null;
            if (!TryGetControllerBase(sled, out object? controllerBase) || controllerBase == null)
                return false;

            body = TryGetField(_mainBody, controllerBase);
            return body != null;
        }

        public static bool TryGetRespawnable(object sled, out object? respawnable)
        {
            respawnable = null;
            if (!TryGetControllerBase(sled, out object? controllerBase) || controllerBase == null)
                return false;

            respawnable = TryGetField(_respawnable, controllerBase);
            return respawnable != null;
        }

        public static bool TryGetHeadlightController(object sled, out object? headlightController)
        {
            headlightController = null;
            if (!TryGetControllerBase(sled, out object? controllerBase) || controllerBase == null)
                return false;

            headlightController = TryGetField(_headLightController, controllerBase);
            return headlightController != null;
        }

        public static bool TryGetHeadlightState(object sled, out bool enabled)
        {
            enabled = false;
            object? raw = TryGetField(_headlightState, sled);
            if (raw is not bool state)
                return false;

            enabled = state;
            return true;
        }

        public static bool TrySetHeadlightState(object sled, bool enabled)
        {
            try
            {
                if (_headlightState == null || !_headlightState.DeclaringType!.IsInstanceOfType(sled))
                    return false;

                _headlightState.SetValue(sled, enabled);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetThrottle(object sled, out double value)
        {
            value = 0.0;
            try
            {
                if (_inputState == null || _throttleState == null || !_inputState.DeclaringType!.IsInstanceOfType(sled))
                    return false;

                object? state = _inputState.GetValue(sled);
                if (state == null)
                    return false;

                object? raw = _throttleState.GetValue(state);
                if (raw == null)
                    return false;

                value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void ReportCompatibilityFallbackOnce()
        {
            if (_reportedControllerFallback)
                return;

            _reportedControllerFallback = true;
            RuntimeLog.Warn("Exact local-sled binding was unavailable; using compatibility object discovery.");
        }

        private static void EnsureInitialized()
        {
            if (!_initialized)
                ResolveTypes();
        }

        private static void ResolveTypes()
        {
            _controllerType = ReflectionBridge.FindTypeExact("Controller");
            _netClientType = ReflectionBridge.FindTypeExact("NetClient");
            _snowmobileType = ReflectionBridge.FindTypeExact("SnowmobileController");

            _controllerGetInstance = FindMethod(_controllerType, "get_Instance", StaticFlags, Type.EmptyTypes);
            _controllerGetSnowmobile = FindMethod(_controllerType, "get_SnowmobileController", InstanceFlags, Type.EmptyTypes);
            _netClientGetInstance = FindMethod(_netClientType, "get_Instance", StaticFlags, Type.EmptyTypes);
            _netClientGetLocalPlayer = FindMethod(_netClientType, "get_LocalPlayer", InstanceFlags, Type.EmptyTypes);

            _getFuel = FindMethod(_snowmobileType, "get_Fuel", InstanceFlags, Type.EmptyTypes);
            _getFuelCapacity = FindMethod(_snowmobileType, "get_FuelCapacity", InstanceFlags, Type.EmptyTypes);
            _setFuel = FindMethod(_snowmobileType, "SetFuel", InstanceFlags, new[] { typeof(float) });
            _addFuel = FindMethod(_snowmobileType, "AddFuel", InstanceFlags, new[] { typeof(float) });
            _getRpm = FindMethod(_snowmobileType, "get_Rpm", InstanceFlags, Type.EmptyTypes);
            _setEngineOnOff = FindMethod(_snowmobileType, "SetEngineOnOff", InstanceFlags, new[] { typeof(bool) });
            _getVehicle = FindMethod(_snowmobileType, "get_Vehicle", InstanceFlags, Type.EmptyTypes);

            _controllerBase = _snowmobileType?.GetField("controllerBase", InstanceFlags);
            Type? controllerBaseType = _controllerBase?.FieldType;
            _mainBody = controllerBaseType?.GetField("mainBody", InstanceFlags);
            _respawnable = controllerBaseType?.GetField("respawnable", InstanceFlags);
            _headLightController = controllerBaseType?.GetField("headLightController", InstanceFlags);
            _headlightState = _snowmobileType?.GetField("isHeadlightOn", InstanceFlags);
            _engineState = _snowmobileType?.GetField("isEngineOn", InstanceFlags);

            _inputState = _snowmobileType?.GetField("GJKCDNOBELI", InstanceFlags);
            _throttleState = _inputState?.FieldType.GetField("AINANLMJJDH", InstanceFlags);

            _initialized = true;

            RuntimeLog.Info(
                "Sledders bindings: " +
                "Controller=" + BindingState(_controllerGetInstance, _controllerGetSnowmobile) +
                ", NetClient=" + BindingState(_netClientGetInstance, _netClientGetLocalPlayer) +
                ", SledCore=" + BindingState(_getFuel, _getFuelCapacity, _setFuel, _addFuel, _getRpm, _setEngineOnOff, _getVehicle, _controllerBase, _mainBody) +
                ", Throttle=" + BindingState(_inputState, _throttleState));
        }

        private static MethodInfo? FindMethod(Type? type, string name, BindingFlags flags, Type[] parameters)
        {
            return type?.GetMethod(name, flags, null, parameters, null);
        }

        private static string BindingState(params MemberInfo?[] members)
        {
            foreach (MemberInfo? member in members)
            {
                if (member == null)
                    return "fallback";
            }
            return "exact";
        }

        private static object? InvokeStatic(MethodInfo? method)
        {
            try { return method?.Invoke(null, null); }
            catch { return null; }
        }

        private static object? InvokeInstance(MethodInfo? method, object target)
        {
            try
            {
                if (method == null || !method.DeclaringType!.IsInstanceOfType(target))
                    return null;
                return method.Invoke(target, null);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryInvokeNumber(MethodInfo? method, object target, out double value)
        {
            value = 0.0;
            object? raw = InvokeInstance(method, target);
            if (raw == null)
                return false;

            try
            {
                value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryInvokeVoid(MethodInfo? method, object target, params object?[] args)
        {
            try
            {
                if (method == null || !method.DeclaringType!.IsInstanceOfType(target))
                    return false;

                method.Invoke(target, args);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object? TryGetField(FieldInfo? field, object target)
        {
            try
            {
                if (field == null || !field.DeclaringType!.IsInstanceOfType(target))
                    return null;
                return field.GetValue(target);
            }
            catch
            {
                return null;
            }
        }
    }
}
