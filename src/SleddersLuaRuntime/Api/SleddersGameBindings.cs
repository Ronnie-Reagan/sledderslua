using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SleddersLuaRuntime.Api
{
    internal static class SleddersGameBindings
    {
        private static readonly Stopwatch BindingClock = Stopwatch.StartNew();
        private static object? _cachedLocalSled;
        private static double _cachedLocalSledUntil;
        private static object? _cachedBodyOwner;
        private static object? _cachedBody;

        private sealed class HeadlightOverrideEntry
        {
            public object Sled { get; set; } = null!;
            public string Owner { get; set; } = string.Empty;
            public bool Enabled { get; set; }
            public long Sequence { get; set; }
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private static readonly object SemanticStateGate = new object();
        private static readonly List<HeadlightOverrideEntry> HeadlightOverrides = new List<HeadlightOverrideEntry>();
        private static readonly Dictionary<object, bool> HeadlightBaselines =
            new Dictionary<object, bool>(ReferenceComparer.Instance);
        private static long _semanticSequence;

        public static void InvalidateCache()
        {
            _cachedLocalSled = null;
            _cachedLocalSledUntil = 0.0;
            _cachedBodyOwner = null;
            _cachedBody = null;

            // Scene changes invalidate cached Unity objects.
            lock (SemanticStateGate)
            {
                HeadlightOverrides.Clear();
                HeadlightBaselines.Clear();
            }
        }

        private static readonly string[] HeadlightBoolMembers =
        {
            // Current Sledders field first, then compatibility aliases.
            "isHeadlightOn", "HeadlightsOn", "HeadLightsOn", "headlightsOn", "headLightsOn",
            "IsHeadlightOn", "IsHeadlightsOn", "LightsOn", "lightsOn"
        };

        private static readonly string[] FuelMembers =
        {
            // Current Sledders exposes Fuel; older builds used these obfuscated names.
            "Fuel", "fuel", "CurrentFuel", "currentFuel", "AGOOKLCEKOL", "PLDFOBGFCII"
        };
        private static readonly string[] FuelCapacityMembers =
        {
            "FuelCapacity", "fuelCapacity", "MaxFuel", "maxFuel"
        };
        private static readonly string[] RpmMembers =
        {
            // DNANHFLDJHD is the current getter for the controller ABOLHJGEOPL RPM state.
            "RPM", "Rpm", "rpm", "EngineRPM", "EngineRpm", "engineRpm", "CurrentRPM", "CurrentRpm",
            "DNANHFLDJHD", "ABOLHJGEOPL"
        };
        private static readonly string[] EngineOnMembers =
        {
            "isEngineOn", "IsEngineOn", "EngineOn", "engineOn", "IsRunning", "isRunning"
        };
        private static readonly string[] ThrottleMembers =
        {
            "Throttle", "throttle", "ThrottleInput", "throttleInput", "Gas", "gas"
        };

        public static object? FindLocalSled()
        {
            double now = BindingClock.Elapsed.TotalSeconds;
            string scene = UnityBridge.ActiveSceneName;
            if (IsNonGameplayScene(scene))
            {
                _cachedLocalSled = null;
                _cachedLocalSledUntil = now + 0.25;
                return null;
            }
            if (now < _cachedLocalSledUntil && (_cachedLocalSled == null || IsValidSled(_cachedLocalSled)))
                return _cachedLocalSled;

            object? best = SleddersBindingResolver.FindLocalSled();
            if (best != null && !IsValidSled(best))
                best = null;

            if (best == null)
            {
                SleddersBindingResolver.ReportCompatibilityFallbackOnce();

                Type? exact = ReflectionBridge.FindTypeExact("SnowmobileController");
                best = FindBestSledOfType(exact);

                if (best == null)
                {
                    Type[] fallbacks = ReflectionBridge.FindTypes("SnowmobileController", 24)
                        .Where(t => !Contains(t.Name, "Remote") && !Contains(t.Name, "Preview") && !t.IsAbstract)
                        .OrderByDescending(t => string.Equals(t.Name, "SnowmobileController", StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    foreach (Type type in fallbacks)
                    {
                        best = FindBestSledOfType(type);
                        if (best != null)
                            break;
                    }
                }
            }

            if (!ReferenceEquals(best, _cachedLocalSled))
            {
                _cachedBodyOwner = null;
                _cachedBody = null;
            }
            _cachedLocalSled = best;
            _cachedLocalSledUntil = now + 0.25;
            return best;
        }

        public static IReadOnlyList<object> FindLiveSleds(int max)
        {
            var result = new List<object>();
            Type? exact = ReflectionBridge.FindTypeExact("SnowmobileController");
            if (exact != null)
            {
                foreach (object candidate in ReflectionBridge.FindObjectsOfType(exact, Math.Max(max * 4, 16)))
                {
                    if (!IsLiveSceneObject(candidate))
                        continue;
                    result.Add(candidate);
                    if (result.Count >= max)
                        break;
                }
            }
            return result;
        }

        public static object? FindPlayerObject()
        {
            object? localPlayer = SleddersBindingResolver.FindLocalPlayer();
            if (localPlayer != null)
                return localPlayer;

            foreach (string typeName in new[] { "PlayerManager", "PlayerInstancier" })
            {
                Type? type = ReflectionBridge.FindTypeExact(typeName);
                if (type == null)
                    continue;

                object? candidate = ReflectionBridge.FindObjectsOfType(type, 16)
                    .OrderByDescending(ScorePlayerObject)
                    .FirstOrDefault();
                if (candidate != null)
                    return candidate;
            }
            return null;
        }

        public static bool IsValidSled(object? sled)
        {
            return sled != null && IsLiveSceneObject(sled) &&
                   !Contains(sled.GetType().Name, "Remote") &&
                   Contains(sled.GetType().Name, "Snowmobile");
        }

        public static string GetFriendlyName(object target)
        {
            // Runtime controllers are usually named "Body"; prefer the vehicle definition name.
            if (Contains(target.GetType().Name, "SnowmobileController"))
            {
                object? definition = GetVehicleDefinition(target);
                if (definition != null)
                {
                    if (TryGetAny(definition, out object? display,
                            "displayName", "DisplayName", "vehicleName", "VehicleName",
                            "sledName", "SledName", "name") &&
                        display != null)
                    {
                        string semantic = display.ToString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(semantic) &&
                            !string.Equals(semantic, "Body", StringComparison.OrdinalIgnoreCase))
                            return semantic;
                    }

                    string? definitionName = ReflectionBridge.TryGetObjectName(definition);
                    if (!string.IsNullOrWhiteSpace(definitionName))
                        return definitionName!;
                }
            }

            object? gameObject = GetGameObject(target);
            string? name = gameObject != null ? ReflectionBridge.TryGetObjectName(gameObject) : null;
            if (string.IsNullOrWhiteSpace(name))
                name = ReflectionBridge.TryGetObjectName(target);
            return string.IsNullOrWhiteSpace(name) ? target.GetType().Name : name!;
        }

        public static object? GetVehicleDefinition(object sled)
        {
            if (SleddersBindingResolver.TryGetVehicle(sled, out object? exactVehicle))
                return exactVehicle;

            if (TryCallAny(sled,
                    new[] { "GetVehicle", "GetVehicleDefinition", "GetVehicleScriptableObject", "KCBOABAJENP" },
                    Array.Empty<object?>(), out object? result) &&
                result != null)
                return result;

            if (TryGetAny(sled, out object? value,
                    "Vehicle", "vehicle", "VehicleDefinition", "vehicleDefinition",
                    "KJFNKMCOKLL") &&
                value != null)
                return value;

            return null;
        }

        public static object? GetGameObject(object target)
        {
            if (string.Equals(target.GetType().FullName, "UnityEngine.GameObject", StringComparison.Ordinal))
                return target;
            return TryGetAny(target, out object? value, "gameObject") ? value : null;
        }

        public static object? GetTransform(object target)
        {
            if (TryGetAny(target, out object? transform, "transform"))
                return transform;
            object? gameObject = GetGameObject(target);
            return gameObject != null && TryGetAny(gameObject, out transform, "transform") ? transform : null;
        }

        public static object? GetControllerBase(object sled)
        {
            if (SleddersBindingResolver.TryGetControllerBase(sled, out object? exact))
                return exact;
            return TryGetAny(sled, out object? value, "controllerBase", "ControllerBase") ? value : null;
        }

        public static object? GetRespawnable(object sled)
        {
            if (SleddersBindingResolver.TryGetRespawnable(sled, out object? exactRespawnable))
                return exactRespawnable;

            object? controllerBase = GetControllerBase(sled);
            if (controllerBase != null && TryGetAny(controllerBase, out object? respawnable, "respawnable", "Respawnable") && respawnable != null)
                return respawnable;

            Type? respawnableType = ReflectionBridge.FindTypeExact("Respawnable");
            if (respawnableType == null)
                return null;

            return ReflectionBridge.GetComponentsInChildren(sled, respawnableType, true, 8).FirstOrDefault();
        }

        public static object? GetRigidbody(object sled)
        {
            if (ReferenceEquals(sled, _cachedBodyOwner) && _cachedBody != null)
                return _cachedBody;

            // Prefer the exact current-build controllerBase.mainBody binding.
            if (SleddersBindingResolver.TryGetMainBody(sled, out object? exactBody) && exactBody != null)
            {
                _cachedBodyOwner = sled;
                _cachedBody = exactBody;
                return exactBody;
            }

            object? controllerBase = GetControllerBase(sled);
            if (controllerBase != null && TryGetAny(controllerBase, out object? mainBody, "mainBody", "MainBody") && mainBody != null)
            {
                _cachedBodyOwner = sled;
                _cachedBody = mainBody;
                return mainBody;
            }

            Type? rigidbodyType = ReflectionBridge.FindTypeExact("UnityEngine.Rigidbody");
            if (rigidbodyType == null)
                return null;

            IReadOnlyList<object> bodies = ReflectionBridge.GetComponentsInChildren(sled, rigidbodyType, true, 32);
            if (bodies.Count == 0)
                return null;

            object? best = bodies.OrderByDescending(ScoreRigidbody).FirstOrDefault();
            if (best != null)
            {
                _cachedBodyOwner = sled;
                _cachedBody = best;
            }
            return best;
        }

        public static object? GetPosition(object target)
        {
            object? transform = GetTransform(target);
            return transform != null && TryGetAny(transform, out object? value, "position") ? value : null;
        }

        public static bool Teleport(object sled, object position, bool preserveVelocity)
        {
            if (!IsValidSled(sled))
                return false;

            object? oldVelocity = preserveVelocity ? GetVelocity(sled) : null;
            object? body = GetRigidbody(sled);
            object? transform = GetTransform(sled);
            object? rotation = transform != null && TryGetAny(transform, out object? currentRotation, "rotation")
                ? currentRotation
                : null;

            bool moved = false;
            object? respawnable = GetRespawnable(sled);
            if (respawnable != null && rotation != null)
            {
                // Current build: Respawn(Vector3, Quaternion, bool).
                moved = TryCallAny(respawnable, new[] { "Respawn" }, new object?[] { position, rotation, false }, out _);
            }

            if (!moved && body != null)
                moved = TrySetAny(body, position, "position");
            if (!moved && transform != null)
                moved = TrySetAny(transform, position, "position");
            if (!moved)
                return false;

            if (preserveVelocity && oldVelocity != null)
                SetVelocity(sled, oldVelocity);
            else
                ZeroBodyMotion(body);

            return true;
        }

        private static void ZeroBodyMotion(object? body)
        {
            if (body == null)
                return;
            Type? vectorType = ResolveMemberType(body, "linearVelocity") ?? ResolveMemberType(body, "velocity");
            if (vectorType == null)
                return;
            object? zero = Activator.CreateInstance(vectorType);
            if (zero == null)
                return;
            TrySetAny(body, zero, "linearVelocity", "velocity");
            TrySetAny(body, zero, "angularVelocity");
        }

        public static object? GetRotation(object target)
        {
            object? transform = GetTransform(target);
            return transform != null && TryGetAny(transform, out object? value, "eulerAngles") ? value : null;
        }

        public static object? GetVelocity(object sled)
        {
            object? body = GetRigidbody(sled);
            if (body == null)
                return null;
            return TryGetAny(body, out object? value, "linearVelocity", "velocity") ? value : null;
        }

        public static bool SetVelocity(object sled, object velocity)
        {
            // Raw world-space velocity path.
            object? body = GetRigidbody(sled);
            return body != null && TrySetAny(body, velocity, "linearVelocity", "velocity");
        }

        public static object? GetLocalVelocity(object sled)
        {
            object? worldVelocity = GetVelocity(sled);
            return worldVelocity == null ? null : WorldDirectionToLocal(sled, worldVelocity);
        }

        public static bool SetLocalVelocity(object sled, object localVelocity)
        {
            object? worldVelocity = LocalDirectionToWorld(sled, localVelocity);
            return worldVelocity != null && SetVelocity(sled, worldVelocity);
        }

        public static bool AddVelocity(object sled, object worldDelta)
        {
            object? current = GetVelocity(sled);
            if (current == null)
                return false;

            object? sum = AddVectorValues(current, worldDelta);
            return sum != null && SetVelocity(sled, sum);
        }

        public static bool AddLocalVelocity(object sled, object localDelta)
        {
            object? worldDelta = LocalDirectionToWorld(sled, localDelta);
            return worldDelta != null && AddVelocity(sled, worldDelta);
        }

        public static double? GetForwardSpeed(object sled)
        {
            object? local = GetLocalVelocity(sled);
            return local != null && TryReadVector3(local, out _, out _, out double z) ? z : (double?)null;
        }

        public static object? LocalDirectionToWorld(object sled, object localDirection)
        {
            object? transform = GetMotionTransform(sled);
            if (transform == null)
                return null;

            return TryCallAny(transform, new[] { "TransformDirection" }, new object?[] { localDirection }, out object? result)
                ? result
                : null;
        }

        public static object? WorldDirectionToLocal(object sled, object worldDirection)
        {
            object? transform = GetMotionTransform(sled);
            if (transform == null)
                return null;

            return TryCallAny(transform, new[] { "InverseTransformDirection" }, new object?[] { worldDirection }, out object? result)
                ? result
                : null;
        }

        private static object? GetMotionTransform(object sled)
        {
            // mainBody rotation is the local frame exposed to Lua.
            object? body = GetRigidbody(sled);
            object? bodyTransform = body != null ? GetTransform(body) : null;
            return bodyTransform ?? GetTransform(sled);
        }

        private static object? AddVectorValues(object a, object b)
        {
            if (!TryReadVector3(a, out double ax, out double ay, out double az) ||
                !TryReadVector3(b, out double bx, out double by, out double bz))
                return null;

            Type targetType = a.GetType();
            object? result = Activator.CreateInstance(targetType);
            if (result == null)
                return null;

            if (!TrySetAny(result, ax + bx, "x") ||
                !TrySetAny(result, ay + by, "y") ||
                !TrySetAny(result, az + bz, "z"))
                return null;

            return result;
        }

        public static double? GetSpeed(object sled)
        {
            object? velocity = GetVelocity(sled);
            if (velocity == null || !TryReadVector3(velocity, out double x, out double y, out double z))
                return null;
            return Math.Sqrt(x * x + y * y + z * z);
        }

        public static double? GetMass(object sled)
        {
            object? body = GetRigidbody(sled);
            if (body == null || !TryGetAny(body, out object? raw, "mass"))
                return null;
            return ToDouble(raw);
        }

        public static bool SetMass(object sled, double mass)
        {
            object? body = GetRigidbody(sled);
            if (body == null || mass <= 0.0)
                return false;

            Type? type = ResolveMemberType(body, "mass");
            return type != null && TrySetAny(body, ValueConverter.ChangeType(mass, type), "mass");
        }

        public static bool AddForce(object sled, object worldForce)
        {
            object? body = GetRigidbody(sled);
            if (body == null)
                return false;
            return TryCallAny(body, new[] { "AddForce" }, new object?[] { worldForce }, out _);
        }

        public static bool AddLocalForce(object sled, object localForce)
        {
            object? worldForce = LocalDirectionToWorld(sled, localForce);
            return worldForce != null && AddForce(sled, worldForce);
        }

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

            foreach (string member in HeadlightBoolMembers)
            {
                Type? memberType = ResolveMemberType(sled, member);
                if (memberType == typeof(bool) && ReflectionBridge.TrySetMember(sled, member, enabled))
                {
                    changed = true;
                    break;
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

        public static bool TryGetAny(object target, out object? value, params string[] names)
        {
            foreach (string name in names)
            {
                if (ReflectionBridge.TryGetMember(target, name, out value))
                    return true;
            }
            value = null;
            return false;
        }

        public static bool TryGetAnyOrGetter(object target, out object? value, params string[] names)
        {
            if (TryGetAny(target, out value, names))
                return true;

            foreach (string name in names)
            {
                if (ReflectionBridge.TryCall(target, "get_" + name, Array.Empty<object?>(), out value))
                    return true;
            }

            value = null;
            return false;
        }

        public static bool TrySetAny(object target, object? value, params string[] names)
        {
            foreach (string name in names)
            {
                Type? type = ResolveMemberType(target, name);
                if (type == null)
                    continue;

                object? converted;
                try { converted = ValueConverter.ChangeType(value, type); }
                catch { continue; }

                if (ReflectionBridge.TrySetMember(target, name, converted))
                    return true;
            }
            return false;
        }

        public static bool TryCallAny(object target, IEnumerable<string> names, IReadOnlyList<object?> args, out object? result)
        {
            foreach (string name in names)
            {
                if (ReflectionBridge.TryCall(target, name, args, out result))
                    return true;
            }
            result = null;
            return false;
        }

        public static Type? ResolveMemberType(object target, string member)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                                                         System.Reflection.BindingFlags.Public |
                                                         System.Reflection.BindingFlags.NonPublic;
            Type type = target.GetType();
            var property = type.GetProperties(flags).FirstOrDefault(p => string.Equals(p.Name, member, StringComparison.OrdinalIgnoreCase));
            if (property != null)
                return property.PropertyType;
            var field = type.GetFields(flags).FirstOrDefault(f => string.Equals(f.Name, member, StringComparison.OrdinalIgnoreCase));
            return field?.FieldType;
        }

        public static bool TryReadVector3(object vector, out double x, out double y, out double z)
        {
            x = y = z = 0.0;
            if (!TryGetAny(vector, out object? rx, "x") ||
                !TryGetAny(vector, out object? ry, "y") ||
                !TryGetAny(vector, out object? rz, "z"))
                return false;

            double? dx = ToDouble(rx);
            double? dy = ToDouble(ry);
            double? dz = ToDouble(rz);
            if (!dx.HasValue || !dy.HasValue || !dz.HasValue)
                return false;
            x = dx.Value;
            y = dy.Value;
            z = dz.Value;
            return true;
        }

        public static double? ToDouble(object? value)
        {
            if (value == null)
                return null;
            try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        private static object? FindBestSledOfType(Type? type)
        {
            if (type == null || type.IsAbstract)
                return null;

            return ReflectionBridge.FindObjectsOfType(type, 64)
                .Where(IsLiveSceneObject)
                .Select(obj => new { Obj = obj, Score = ScoreSledObject(obj) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Obj)
                .FirstOrDefault();
        }

        private static int ScoreSledObject(object obj)
        {
            int score = 0;
            Type type = obj.GetType();
            string assembly = type.Assembly.GetName().Name ?? string.Empty;
            string typeName = type.Name;
            string name = GetFriendlyName(obj);

            if (assembly.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase)) score += 1000;
            if (typeName.Equals("SnowmobileController", StringComparison.OrdinalIgnoreCase)) score += 4000;
            else if (Contains(typeName, "SnowmobileController")) score += 1500;
            if (Contains(typeName, "Remote")) score -= 10000;
            if (IsLiveSceneObject(obj)) score += 5000; else score -= 5000;
            if (IsActiveObject(obj)) score += 700;
            if (GetRigidbody(obj) != null) score += 500;
            if (TryGetAnyOrGetter(obj, out _, FuelMembers)) score += 250;

            string lower = name.ToLowerInvariant();
            if (lower.Contains("preview") || lower.Contains("repository") || lower.Contains("item") || lower.Contains("career")) score -= 2000;
            if (lower.Contains("remote") || lower.Contains("ghost")) score -= 5000;
            return score;
        }

        private static int ScorePlayerObject(object obj)
        {
            int score = 0;
            if (obj.GetType().Assembly.GetName().Name == "Assembly-CSharp") score += 500;
            if (IsLiveSceneObject(obj)) score += 1000;
            if (IsActiveObject(obj)) score += 300;
            return score;
        }

        private static int ScoreRigidbody(object body)
        {
            int score = 0;
            string name = GetFriendlyName(body).ToLowerInvariant();
            if (name.Contains("body") || name.Contains("chassis") || name.Contains("snowmobile") || name.Contains("sled")) score += 500;
            if (name.Contains("ski") || name.Contains("track") || name.Contains("driver") || name.Contains("rider")) score -= 200;
            if (TryGetAny(body, out object? kinematic, "isKinematic") && kinematic is bool isKinematic && !isKinematic) score += 100;
            double? mass = TryGetAny(body, out object? massRaw, "mass") ? ToDouble(massRaw) : null;
            if (mass.HasValue) score += (int)Math.Min(250.0, Math.Max(0.0, mass.Value));
            return score;
        }

        private static int ScoreHeadlight(object light)
        {
            string name = GetFriendlyName(light).ToLowerInvariant();
            if (name.Contains("brake") || name.Contains("tail") || name.Contains("rear") || name.Contains("reverse") ||
                name.Contains("indicator") || name.Contains("turn") || name.Contains("dash") || name.Contains("gauge") ||
                name.Contains("warning"))
                return -10000;

            int score = 0;
            if (name.Contains("head")) score += 1000;
            if (name.Contains("front")) score += 600;
            if (name.Contains("beam")) score += 500;
            if (name.Contains("lamp")) score += 250;
            if (name.Contains("spot")) score += 150;
            if (name.Contains("light")) score += 50;
            return score;
        }

        private static bool IsLiveSceneObject(object target)
        {
            object? gameObject = GetGameObject(target);
            if (gameObject == null)
                return false;

            if (!TryGetAny(gameObject, out object? scene, "scene") || scene == null)
                return true; // Old Unity versions can hide Scene through reflection; don't reject solely for that.

            if (ReflectionBridge.TryCall(scene, "IsValid", Array.Empty<object?>(), out object? validRaw) && validRaw is bool valid && !valid)
                return false;
            if (TryGetAny(scene, out object? loadedRaw, "isLoaded") && loadedRaw is bool loaded && !loaded)
                return false;
            return true;
        }

        private static bool IsActiveObject(object target)
        {
            object? gameObject = GetGameObject(target);
            return gameObject != null && TryGetAny(gameObject, out object? active, "activeInHierarchy") && active is bool b && b;
        }

        private static bool IsNonGameplayScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return false;
            string scene = sceneName.Trim();
            return scene.Equals("TitleScreen", StringComparison.OrdinalIgnoreCase) ||
                   scene.Equals("Garage", StringComparison.OrdinalIgnoreCase) ||
                   scene.Equals("LoadingScene", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Contains(string value, string needle)
        {
            return value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
