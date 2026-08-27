using System;
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
        private static Type? _controllerType;
        private static Type? _netClientType;
        private static bool _initialized;
        private static bool _reportedControllerFallback;

        public static void Initialize()
        {
            ResolveTypes();
        }

        public static object? FindLocalSled()
        {
            EnsureInitialized();

            object? controller = GetSingleton(_controllerType);
            if (controller == null)
                return null;

            object? sled = GetInstanceMember(controller, "SnowmobileController");
            return sled;
        }

        public static object? FindLocalPlayer()
        {
            EnsureInitialized();

            object? netClient = GetSingleton(_netClientType);
            if (netClient == null)
                return null;

            return GetInstanceMember(netClient, "LocalPlayer");
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
            _initialized = true;

            RuntimeLog.Info(
                "Sledders bindings: Controller=" + (_controllerType != null ? "exact" : "missing") +
                ", NetClient=" + (_netClientType != null ? "exact" : "missing"));
        }

        private static object? GetSingleton(Type? type)
        {
            if (type == null)
                return null;

            try
            {
                return ReflectionBridge.GetStaticMember(type, "Instance");
            }
            catch
            {
                try
                {
                    return ReflectionBridge.CallStatic(type, "get_Instance", Array.Empty<object?>());
                }
                catch
                {
                    return null;
                }
            }
        }

        private static object? GetInstanceMember(object target, string member)
        {
            try
            {
                return ReflectionBridge.GetMember(target, member);
            }
            catch
            {
                try
                {
                    return ReflectionBridge.Call(target, "get_" + member, Array.Empty<object?>());
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
