using System;

namespace SleddersLuaRuntime.Core
{
    internal static class Compat
    {
        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static bool Contains(string value, string query, StringComparison comparison)
        {
            if (value == null || query == null)
                return false;
            return value.IndexOf(query, comparison) >= 0;
        }
    }
}
