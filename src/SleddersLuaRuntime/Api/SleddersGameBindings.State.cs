using System;
using System.Collections.Generic;
using System.Linq;

namespace SleddersLuaRuntime.Api
{
    internal static partial class SleddersGameBindings
    {
        public static IReadOnlyList<object> FindHeadlights(object sled)
        {
            Type? lightType = ReflectionBridge.FindTypeExact("UnityEngine.Light");
            if (lightType == null)
                return Array.Empty<object>();

            IReadOnlyList<object> lights = ReflectionBridge.GetComponentsInChildren(sled, lightType, true, 48);
            if (lights.Count == 0)
                return Array.Empty<object>();

            var scored = lights
                .Select(light => new { Light = light, Score = ScoreHeadlight(light) })
                .Where(x => x.Score > -1000)
                .ToArray();

            object[] explicitMatches = scored.Where(x => x.Score >= 100).OrderByDescending(x => x.Score).Select(x => x.Light).ToArray();
            if (explicitMatches.Length > 0)
                return explicitMatches;

            // Fallback for sleds whose headlight objects have generic names.
            object[] plausible = scored.Where(x => x.Score >= 0).Select(x => x.Light).ToArray();
            return plausible.Length <= 6 ? plausible : Array.Empty<object>();
        }

        public static bool SetHeadlights(object sled, bool enabled)
        {
            // Normal setters only change the game headlight state.
            return ApplyHeadlightState(sled, enabled, refreshVisuals: false);
        }

        public static bool ForceHeadlights(object sled, bool enabled, string owner)
        {
            if (string.IsNullOrWhiteSpace(owner))
                return SetHeadlights(sled, enabled);

            lock (SemanticStateGate)
            {
                if (!HeadlightOverrides.Any(x => ReferenceEquals(x.Sled, sled)))
                {
                    bool? native = ReadHeadlightStateDirect(sled);
                    if (native.HasValue)
                        HeadlightBaselines[sled] = native.Value;
                }

                HeadlightOverrideEntry? entry = HeadlightOverrides
                    .FirstOrDefault(x => ReferenceEquals(x.Sled, sled) &&
                                         string.Equals(x.Owner, owner, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    entry = new HeadlightOverrideEntry { Sled = sled, Owner = owner };
                    HeadlightOverrides.Add(entry);
                }

                entry.Enabled = enabled;
                entry.Sequence = ++_semanticSequence;
            }

            return ApplyHeadlightState(sled, enabled, refreshVisuals: true);
        }


        public static bool? AreHeadlightsOn(object sled)
        {
            bool? direct = ReadHeadlightStateDirect(sled);
            return direct ?? GetManagedHeadlightState(sled);
        }

        public static bool ToggleHeadlights(object sled)
        {
            bool? state = AreHeadlightsOn(sled);
            if (state.HasValue)
                return SetHeadlights(sled, !state.Value);

            if (TryCallAny(sled, new[] { "ToggleHeadlights", "ToggleHeadLights", "ToggleLights" }, Array.Empty<object?>(), out _))
                return true;

            return SetHeadlights(sled, true);
        }

        public static bool ReleaseHeadlights(object sled, string owner)
        {
            bool removed;
            bool? restore = null;
            lock (SemanticStateGate)
            {
                removed = HeadlightOverrides.RemoveAll(x =>
                    ReferenceEquals(x.Sled, sled) &&
                    string.Equals(x.Owner, owner, StringComparison.OrdinalIgnoreCase)) > 0;

                if (!HeadlightOverrides.Any(x => ReferenceEquals(x.Sled, sled)) &&
                    HeadlightBaselines.TryGetValue(sled, out bool baseline))
                {
                    restore = baseline;
                    HeadlightBaselines.Remove(sled);
                }
            }

            bool? remaining = GetManagedHeadlightState(sled);
            if (remaining.HasValue)
                ApplyHeadlightState(sled, remaining.Value, refreshVisuals: true);
            else if (restore.HasValue && IsValidSled(sled))
                ApplyHeadlightState(sled, restore.Value, refreshVisuals: true);

            return removed;
        }

        public static bool IsHeadlightControlled(object sled, string? owner = null)
        {
            lock (SemanticStateGate)
            {
                return HeadlightOverrides.Any(x =>
                    ReferenceEquals(x.Sled, sled) &&
                    (string.IsNullOrWhiteSpace(owner) ||
                     string.Equals(x.Owner, owner, StringComparison.OrdinalIgnoreCase)));
            }
        }

        public static void ReleaseOverrides(string owner)
        {
            if (string.IsNullOrWhiteSpace(owner))
                return;

            List<object> touched;
            var restore = new Dictionary<object, bool>(ReferenceComparer.Instance);
            lock (SemanticStateGate)
            {
                touched = HeadlightOverrides
                    .Where(x => string.Equals(x.Owner, owner, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Sled)
                    .Distinct(ReferenceComparer.Instance)
                    .ToList();

                HeadlightOverrides.RemoveAll(x =>
                    string.Equals(x.Owner, owner, StringComparison.OrdinalIgnoreCase));

                foreach (object sled in touched)
                {
                    if (!HeadlightOverrides.Any(x => ReferenceEquals(x.Sled, sled)) &&
                        HeadlightBaselines.TryGetValue(sled, out bool baseline))
                    {
                        restore[sled] = baseline;
                        HeadlightBaselines.Remove(sled);
                    }
                }
            }

            foreach (object sled in touched)
            {
                if (!IsValidSled(sled))
                    continue;

                bool? remaining = GetManagedHeadlightState(sled);
                if (remaining.HasValue)
                    ApplyHeadlightState(sled, remaining.Value, refreshVisuals: true);
                else if (restore.TryGetValue(sled, out bool baseline))
                    ApplyHeadlightState(sled, baseline, refreshVisuals: true);
            }
        }

        public static void ApplyManagedStates()
        {
            HeadlightOverrideEntry[] active;
            lock (SemanticStateGate)
            {
                HeadlightOverrides.RemoveAll(x => !IsValidSled(x.Sled));
                active = HeadlightOverrides
                    .GroupBy(x => x.Sled, ReferenceComparer.Instance)
                    .Select(group => group.OrderByDescending(x => x.Sequence).First())
                    .ToArray();
            }

            // Only reapply a forced state when another writer changes it.
            foreach (HeadlightOverrideEntry entry in active)
            {
                bool? current = ReadHeadlightStateDirect(entry.Sled);
                if (!current.HasValue || current.Value != entry.Enabled)
                    ApplyHeadlightState(entry.Sled, entry.Enabled, refreshVisuals: true);
            }
        }

        private static bool ApplyHeadlightState(object sled, bool enabled, bool refreshVisuals)
        {
            bool changed = SleddersBindingResolver.TrySetHeadlightState(sled, enabled);

            if (!changed)
            {
                foreach (string member in HeadlightBoolMembers)
                {
                    Type? memberType = ResolveMemberType(sled, member);
                    if (memberType == typeof(bool) && ReflectionBridge.TrySetMember(sled, member, enabled))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (TryCallAny(sled,
                    new[] { "SetHeadlights", "SetHeadLights", "SetHeadlight", "SetLights" },
                    new object?[] { enabled }, out _))
            {
                changed = true;
            }

            if (!refreshVisuals && changed)
                return true;

            // Forced mode also refreshes the native headlight controller immediately.
            bool visualEnabled = enabled && (IsEngineOn(sled) ?? true);
            object? nativeLights = null;
            if (!SleddersBindingResolver.TryGetHeadlightController(sled, out nativeLights))
            {
                if (TryGetAny(sled, out object? controllerBase, "controllerBase") &&
                    controllerBase != null &&
                    TryGetAny(controllerBase, out object? fallbackLights, "headLightController", "headlightController"))
                    nativeLights = fallbackLights;
            }

            if (nativeLights != null)
            {
                if (TryCallAny(nativeLights,
                        new[] { "Refresh", "SetState", "SetEnabled" },
                        new object?[] { visualEnabled }, out _))
                    changed = true;
            }

            // Fallback for custom/older sleds.
            foreach (object light in FindHeadlights(sled))
            {
                Type? enabledType = ResolveMemberType(light, "enabled");
                if (enabledType == typeof(bool) &&
                    ReflectionBridge.TrySetMember(light, "enabled", visualEnabled))
                    changed = true;
            }

            return changed;
        }

        private static bool? ReadHeadlightStateDirect(object sled)
        {
            if (SleddersBindingResolver.TryGetHeadlightState(sled, out bool exactState))
                return exactState;

            if (TryGetAny(sled, out object? raw, HeadlightBoolMembers) && raw is bool state)
                return state;

            IReadOnlyList<object> lights = FindHeadlights(sled);
            if (lights.Count == 0)
                return null;

            bool anyKnown = false;
            bool any = false;
            foreach (object light in lights)
            {
                if (TryGetAny(light, out raw, "enabled") && raw is bool enabled)
                {
                    anyKnown = true;
                    any |= enabled;
                }
            }

            return anyKnown ? any : (bool?)null;
        }

        private static bool? GetManagedHeadlightState(object sled, string? owner = null)
        {
            lock (SemanticStateGate)
            {
                IEnumerable<HeadlightOverrideEntry> query = HeadlightOverrides
                    .Where(x => ReferenceEquals(x.Sled, sled));
                if (!string.IsNullOrWhiteSpace(owner))
                    query = query.Where(x => string.Equals(x.Owner, owner, StringComparison.OrdinalIgnoreCase));

                HeadlightOverrideEntry? latest = query
                    .OrderByDescending(x => x.Sequence)
                    .FirstOrDefault();
                return latest == null ? (bool?)null : latest.Enabled;
            }
        }

        public static double? GetFuel(object sled)
        {
            double? raw = GetNativeFuel(sled);
            if (!raw.HasValue)
                return null;

            double? capacity = GetFuelCapacity(sled);
            if (capacity.HasValue && UsesNormalizedFuel(sled, raw.Value, capacity.Value))
                return raw.Value * capacity.Value;
            return raw.Value;
        }

        public static double? GetFuelCapacity(object sled)
        {
            if (SleddersBindingResolver.TryGetFuelCapacity(sled, out double exactCapacity))
                return exactCapacity;

            if (TryGetAnyOrGetter(sled, out object? value, FuelCapacityMembers))
                return ToDouble(value);

            object? definition = GetVehicleDefinition(sled);
            if (definition != null &&
                TryGetAnyOrGetter(definition, out value, FuelCapacityMembers))
                return ToDouble(value);

            return null;
        }

        public static double? GetFuelPercent(object sled)
        {
            double? fuel = GetFuel(sled);
            double? capacity = GetFuelCapacity(sled);
            if (!fuel.HasValue || !capacity.HasValue || capacity.Value <= 0.0)
                return null;
            return Math.Max(0.0, Math.Min(1.0, fuel.Value / capacity.Value));
        }

        public static bool SetFuel(object sled, double litres)
        {
            double? capacity = GetFuelCapacity(sled);
            if (capacity.HasValue)
                litres = Math.Max(0.0, Math.Min(capacity.Value, litres));
            else
                litres = Math.Max(0.0, litres);

            double nativeAmount = litres;
            double? currentRaw = GetNativeFuel(sled);
            if (capacity.HasValue && UsesNormalizedFuel(sled, currentRaw ?? 1.0, capacity.Value))
                nativeAmount = capacity.Value <= 0.0 ? 0.0 : litres / capacity.Value;

            if (SleddersBindingResolver.TrySetFuelNormalized(sled, nativeAmount))
                return true;

            if (TryCallAny(sled, new[] { "SetFuel" }, new object?[] { nativeAmount }, out _))
                return true;

            foreach (string member in FuelMembers)
            {
                Type? type = ResolveMemberType(sled, member);
                if (type == null)
                    continue;
                if (ReflectionBridge.TrySetMember(sled, member, ValueConverter.ChangeType(nativeAmount, type)))
                    return true;
            }
            return false;
        }

        public static bool AddFuel(object sled, double litres)
        {
            double? capacity = GetFuelCapacity(sled);
            if (capacity.HasValue && capacity.Value > 0.0)
            {
                double normalizedDelta = litres / capacity.Value;
                if (SleddersBindingResolver.TryAddFuelNormalized(sled, normalizedDelta))
                    return true;
            }

            double? current = GetFuel(sled);
            return current.HasValue && SetFuel(sled, current.Value + litres);
        }

        private static double? GetNativeFuel(object sled)
        {
            if (SleddersBindingResolver.TryGetFuelNormalized(sled, out double exactFuel))
                return exactFuel;
            return TryGetAnyOrGetter(sled, out object? value, FuelMembers) ? ToDouble(value) : null;
        }

        private static bool UsesNormalizedFuel(object sled, double raw, double capacity)
        {
            if (capacity <= 1.5)
                return false;

            // Fuel is normalized in the controller; Lua exposes litres.
            if (string.Equals(sled.GetType().Name, "SnowmobileController", StringComparison.OrdinalIgnoreCase))
                return raw >= -0.001 && raw <= 1.001;

            return raw >= -0.001 && raw <= 1.001 && capacity >= 5.0;
        }

        public static double? GetRpm(object sled)
        {
            if (SleddersBindingResolver.TryGetRpm(sled, out double exactRpm))
                return exactRpm;
            return TryGetAnyOrGetter(sled, out object? value, RpmMembers) ? ToDouble(value) : null;
        }

        public static double? GetThrottle(object sled)
        {
            if (SleddersBindingResolver.TryGetThrottle(sled, out double exactThrottle))
                return exactThrottle;

            if (TryGetAnyOrGetter(sled, out object? value, ThrottleMembers))
                return ToDouble(value);

            if (TryGetAny(sled, out object? state, "GJKCDNOBELI") &&
                state != null &&
                TryGetAny(state, out value, "AINANLMJJDH"))
                return ToDouble(value);

            return null;
        }

        public static bool? IsEngineOn(object sled)
        {
            if (SleddersBindingResolver.TryGetEngineRunning(sled, out bool exactState))
                return exactState;

            if (TryGetAnyOrGetter(sled, out object? value, EngineOnMembers) && value is bool state)
                return state;
            return null;
        }

        public static bool SetEngineRunning(object sled, bool running)
        {
            // Use the exact game setter so its fuel guard and controller state stay in sync.
            if (SleddersBindingResolver.TrySetEngineRunning(sled, running))
                return true;

            if (TryCallAny(sled, new[] { "SetEngineOnOff" }, new object?[] { running }, out _))
                return true;

            string[] methods = running
                ? new[] { "StartEngine", "TurnEngineOn", "EngineOn" }
                : new[] { "StopEngine", "TurnEngineOff", "EngineOff" };

            if (TryCallAny(sled, methods, Array.Empty<object?>(), out _))
                return true;

            if (TryCallAny(sled, new[] { "SetEngineOn", "SetEngineRunning" }, new object?[] { running }, out _))
                return true;

            foreach (string member in EngineOnMembers)
            {
                Type? type = ResolveMemberType(sled, member);
                if (type == typeof(bool) && ReflectionBridge.TrySetMember(sled, member, running))
                    return true;
            }
            return false;
        }
    }
}
