using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SleddersLuaRuntime.Api
{
    internal static partial class SleddersGameBindings
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

            if (best == null && !SleddersBindingResolver.HasExactLocalSledBinding)
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
            if (localPlayer != null || SleddersBindingResolver.HasExactLocalPlayerBinding)
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
