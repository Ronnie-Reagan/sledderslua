using System;
using MelonLoader;

namespace SleddersLuaRuntime.Core
{
    internal static class RuntimeLog
    {
        public static void Info(string message) => MelonLogger.Msg($"[SleddersLua] {message}");
        public static void Warn(string message) => MelonLogger.Warning($"[SleddersLua] {message}");
        public static void Error(string message) => MelonLogger.Error($"[SleddersLua] {message}");

        public static void Exception(string context, Exception ex)
        {
            Error($"{context}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }
}
