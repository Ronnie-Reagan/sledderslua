using System;
using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class AdvancedCameraApi
    {
        private static readonly Dictionary<string, string> PhotoNumberSetters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "fov", "FieldOfView" },
            { "roll", "ViewRoll" },
            { "focusDistance", "FocusDistance" },
            { "focalLength", "FocalLength" },
            { "aperture", "Aperture" },
            { "exposure", "Exposure" },
            { "contrast", "Contrast" },
            { "saturation", "Saturation" },
            { "vignette", "Vignette" },
            { "bloomIntensity", "Bloom" },
            { "bloomThreshold", "BloomThreshold" },
            { "temperature", "Temperature" },
            { "timeOfDay", "TimeOfDay" }
        };

        private static readonly Dictionary<string, string> PhotoBoolSetters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "snow", "Snow" },
            { "fog", "Fog" },
            { "storm", "Storm" },
            { "depthOfField", "SetDOFEnabled" },
            { "driverHeadRotation", "SetDriverHeadRotation" }
        };

        private static readonly Dictionary<string, string> PhotoRanges = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "fov", "FieldOfViewRange" },
            { "roll", "ViewRollRange" },
            { "focusDistance", "FocusDistanceRange" },
            { "focalLength", "FocalLengthRange" },
            { "aperture", "ApertureRange" },
            { "exposure", "ExposureRange" },
            { "contrast", "ContrastRange" },
            { "saturation", "SaturationRange" },
            { "vignette", "VignetteRange" },
            { "bloomIntensity", "BloomIntensityRange" },
            { "bloomThreshold", "BloomThresholdRange" },
            { "temperature", "TemperatureRange" },
            { "timeOfDay", "TimeOfDayRange" }
        };

        public static void Enhance(LuaModInstance mod, Table camera)
        {
            camera.Set("setPos", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, camera);
                object? raw = UnityBridge.MainCamera;
                object? transform = raw == null ? null : SleddersGameBindings.GetTransform(raw);
                object? value = FrameworkApiUtil.ReadVector3(mod, args, offset, "camera.setPos(vector3)");
                return DynValue.NewBoolean(transform != null && value != null && SleddersGameBindings.TrySetAny(transform, value, "position"));
            }));
            camera.Set("setRot", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, camera);
                object? raw = UnityBridge.MainCamera;
                object? transform = raw == null ? null : SleddersGameBindings.GetTransform(raw);
                object? value = FrameworkApiUtil.ReadEulerQuaternion(mod, args, offset, "camera.setRot(eulerVector3)");
                return DynValue.NewBoolean(transform != null && value != null && SleddersGameBindings.TrySetAny(transform, value, "rotation"));
            }));
            camera.Set("getTransform", DynValue.NewCallback((ctx, args) =>
            {
                object? raw = UnityBridge.MainCamera;
                object? transform = raw == null ? null : SleddersGameBindings.GetTransform(raw);
                return transform == null ? DynValue.Nil : TransformApi.Wrap(mod, transform);
            }));
            camera.Set("worldToScreen", DynValue.NewCallback((ctx, args) => Project(mod, camera, args, false, false)));
            camera.Set("worldToGui", DynValue.NewCallback((ctx, args) => Project(mod, camera, args, false, true)));
            camera.Set("screenToWorld", DynValue.NewCallback((ctx, args) => Project(mod, camera, args, true, false)));
            camera.Set("guiToWorld", DynValue.NewCallback((ctx, args) => Project(mod, camera, args, true, true)));
            camera.Set("screenPointToRay", DynValue.NewCallback((ctx, args) => ScreenPointToRay(mod, camera, args)));

            camera.Set("getModes", DynValue.NewCallback((ctx, args) => StringArray(mod, new[] { "FirstPerson", "ThirdPersonNear", "ThirdPerson", "DroneMode" })));
            camera.Set("getMode", DynValue.NewCallback((ctx, args) =>
            {
                object? follower = FindOne("CameraFollower");
                return follower != null && ReflectionBridge.TryCall(follower, "get_currentCameraMode", Array.Empty<object?>(), out object? value) && value != null
                    ? DynValue.NewString(value.ToString() ?? string.Empty)
                    : DynValue.Nil;
            }));
            camera.Set("setMode", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, camera);
                string mode = FrameworkApiUtil.RequireString(args, offset, "camera.setMode(mode)");
                object? follower = FindOne("CameraFollower");
                return DynValue.NewBoolean(follower != null && SleddersGameBindings.TryCallAny(follower, new[] { "SetSelectedCameraMode" }, new object?[] { mode }, out _));
            }));
            camera.Set("nextMode", DynValue.NewCallback((ctx, args) =>
            {
                object? follower = FindOne("CameraFollower");
                if (follower == null || !ReflectionBridge.TryCall(follower, "get_currentCameraMode", Array.Empty<object?>(), out object? current) || current == null)
                    return DynValue.Nil;
                if (!ReflectionBridge.TryCall(follower, "NextCameraMode", new object?[] { current }, out object? next) || next == null)
                    return DynValue.Nil;
                SleddersGameBindings.TryCallAny(follower, new[] { "SetSelectedCameraMode" }, new object?[] { next }, out _);
                return DynValue.NewString(next.ToString() ?? string.Empty);
            }));
            camera.Set("saveMode", DynValue.NewCallback((ctx, args) =>
            {
                object? follower = FindOne("CameraFollower");
                return DynValue.NewBoolean(follower != null && SleddersGameBindings.TryCallAny(follower, new[] { "SaveSelectedCameraMode" }, Array.Empty<object?>(), out _));
            }));
            camera.Set("getDroneDistance", DynValue.NewCallback((ctx, args) => FollowerNumber("GetDroneDistance")));
            camera.Set("setDroneDistance", DynValue.NewCallback((ctx, args) => SetFollowerNumber(args, camera, "SetDroneDistance", "camera.setDroneDistance(value)")));
            camera.Set("setDroneHeightOffset", DynValue.NewCallback((ctx, args) => SetFollowerNumber(args, camera, "SetDroneHeightOffset", "camera.setDroneHeightOffset(value)")));
            camera.Set("resetDroneHeightOffset", DynValue.NewCallback((ctx, args) =>
            {
                object? follower = FindOne("CameraFollower");
                return DynValue.NewBoolean(follower != null && SleddersGameBindings.TryCallAny(follower, new[] { "ResetDroneHeightOffset" }, Array.Empty<object?>(), out _));
            }));

            camera.Set("free", DynValue.NewTable(BuildFreeCamera(mod)));
            camera.Set("photo", DynValue.NewTable(BuildPhotoMode(mod)));
        }

        private static Table BuildFreeCamera(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("modes", DynValue.NewCallback((ctx, args) => StringArray(mod, new[] { "PhotoMode", "DroneMode" })));
            table.Set("getMode", DynValue.NewCallback((ctx, args) =>
            {
                object? free = GetSingleton("FreeCameraController");
                return free != null && SleddersGameBindings.TryGetAnyOrGetter(free, out object? value, "CurrentMode") && value != null
                    ? DynValue.NewString(value.ToString() ?? string.Empty) : DynValue.Nil;
            }));
            table.Set("isActive", DynValue.NewCallback((ctx, args) =>
            {
                object? free = GetSingleton("FreeCameraController");
                return free != null && ReflectionBridge.TryCall(free, "IsAnyModeActive", Array.Empty<object?>(), out object? value) && value is bool b
                    ? DynValue.NewBoolean(b) : DynValue.Nil;
            }));
            table.Set("isModeActive", DynValue.NewCallback((ctx, args) =>
            {
                object? free = GetSingleton("FreeCameraController");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string mode = FrameworkApiUtil.RequireString(args, offset, "camera.free.isModeActive(mode)");
                return free != null && ReflectionBridge.TryCall(free, "IsModeActive", new object?[] { mode }, out object? value) && value is bool b
                    ? DynValue.NewBoolean(b) : DynValue.Nil;
            }));
            table.Set("activate", DynValue.NewCallback((ctx, args) =>
            {
                object? free = GetSingleton("FreeCameraController");
                if (free == null) return DynValue.False;
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string mode = args.Count > offset && args[offset].Type == DataType.String ? args[offset].String : "DroneMode";
                int posIndex = args.Count > offset && args[offset].Type == DataType.String ? offset + 1 : offset;
                object? position = args.Count > posIndex
                    ? FrameworkApiUtil.ReadVector3(mod, args, posIndex, "camera.free.activate([mode,] position)")
                    : GetDefaultCameraPosition();
                if (position == null) return DynValue.False;
                return ReflectionBridge.TryCall(free, "ActivateCamera", new object?[] { mode, position }, out object? result) && result is bool b
                    ? DynValue.NewBoolean(b) : DynValue.False;
            }));
            table.Set("deactivate", DynValue.NewCallback((ctx, args) =>
            {
                object? free = GetSingleton("FreeCameraController");
                if (free == null) return DynValue.False;
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string? mode = args.Count > offset && args[offset].Type == DataType.String ? args[offset].String : null;
                if (mode == null && SleddersGameBindings.TryGetAnyOrGetter(free, out object? current, "CurrentMode") && current != null)
                    mode = current.ToString();
                return DynValue.NewBoolean(mode != null && SleddersGameBindings.TryCallAny(free, new[] { "DeactivateCamera" }, new object?[] { mode }, out _));
            }));
            table.Set("forceExit", DynValue.NewCallback((ctx, args) =>
            {
                object? free = GetSingleton("FreeCameraController");
                return DynValue.NewBoolean(free != null && SleddersGameBindings.TryCallAny(free, new[] { "ForceExitAllModes" }, Array.Empty<object?>(), out _));
            }));
            table.Set("getTrackedPosition", DynValue.NewCallback((ctx, args) =>
            {
                object? free = GetSingleton("FreeCameraController");
                return free != null && SleddersGameBindings.TryGetAnyOrGetter(free, out object? value, "TrackedPosition")
                    ? ValueConverter.ToDynValue(mod, value) : DynValue.Nil;
            }));
            table.Set("getMovementEnabled", DynValue.NewCallback((ctx, args) =>
            {
                object? free = GetSingleton("FreeCameraController");
                return free != null && SleddersGameBindings.TryGetAnyOrGetter(free, out object? value, "IsCameraMovementEnabled") && value is bool b
                    ? DynValue.NewBoolean(b) : DynValue.Nil;
            }));
            table.Set("setMovementEnabled", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? free = GetSingleton("FreeCameraController");
                return DynValue.NewBoolean(free != null && SleddersGameBindings.TryCallAny(free, new[] { "SetCameraMovementEnabled" }, new object?[] { FrameworkApiUtil.RequireBool(args, offset, "camera.free.setMovementEnabled(enabled)") }, out _));
            }));
            table.Set("getMovementSpeed", DynValue.NewCallback((ctx, args) => FreeDroneNumber("MovementSpeed")));
            table.Set("setMovementSpeed", DynValue.NewCallback((ctx, args) => SetFreeDroneNumber(args, table, "MovementSpeed", "camera.free.setMovementSpeed(value)")));
            table.Set("getMaxDistance", DynValue.NewCallback((ctx, args) => FreeDroneNumber("MaxDistance")));
            table.Set("setMaxDistance", DynValue.NewCallback((ctx, args) => SetFreeDroneNumber(args, table, "MaxDistance", "camera.free.setMaxDistance(value)")));
            table.Set("getMovementMode", DynValue.NewCallback((ctx, args) =>
            {
                object? drone = GetFreeDroneController();
                return drone != null && SleddersGameBindings.TryGetAnyOrGetter(drone, out object? value, "CurrentMovementMode") && value != null
                    ? DynValue.NewString(value.ToString() ?? string.Empty) : DynValue.Nil;
            }));
            table.Set("setMovementMode", DynValue.NewCallback((ctx, args) =>
            {
                object? drone = GetFreeDroneController();
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string mode = FrameworkApiUtil.RequireString(args, offset, "camera.free.setMovementMode(mode)");
                return DynValue.NewBoolean(drone != null && SleddersGameBindings.TrySetAny(drone, mode, "CurrentMovementMode"));
            }));
            table.Set("setTreeCollisionEnabled", DynValue.NewCallback((ctx, args) =>
            {
                object? drone = GetFreeDroneController();
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                return DynValue.NewBoolean(drone != null && SleddersGameBindings.TryCallAny(drone, new[] { "SetTreeCollisionEnabled" }, new object?[] { FrameworkApiUtil.RequireBool(args, offset, "camera.free.setTreeCollisionEnabled(enabled)") }, out _));
            }));
            return table;
        }

        private static Table BuildPhotoMode(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("getEnabled", DynValue.NewCallback((ctx, args) =>
            {
                object? photo = GetSingleton("PhotoModeController");
                return photo != null && SleddersGameBindings.TryGetAnyOrGetter(photo, out object? raw, "PhotoModeOn") && raw is bool b
                    ? DynValue.NewBoolean(b) : DynValue.Nil;
            }));
            table.Set("setEnabled", DynValue.NewCallback((ctx, args) =>
            {
                object? photo = GetSingleton("PhotoModeController");
                object? sled = SleddersGameBindings.FindLocalSled();
                object? controllerBase = sled == null ? null : SleddersGameBindings.GetControllerBase(sled);
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                bool enabled = FrameworkApiUtil.RequireBool(args, offset, "camera.photo.setEnabled(enabled)");
                return DynValue.NewBoolean(photo != null && controllerBase != null && SleddersGameBindings.TryCallAny(photo, new[] { "SetPhotoMode" }, new object?[] { enabled, controllerBase }, out _));
            }));
            table.Set("getFov", DynValue.NewCallback((ctx, args) => PhotoCallNumber("GetFieldOfView")));
            table.Set("setFov", DynValue.NewCallback((ctx, args) => SetPhotoNumber(args, table, "FieldOfView", "camera.photo.setFov(value)")));
            table.Set("getRoll", DynValue.NewCallback((ctx, args) => PhotoCallNumber("get_CurrentRollValue")));
            table.Set("setRoll", DynValue.NewCallback((ctx, args) => SetPhotoNumber(args, table, "ViewRoll", "camera.photo.setRoll(value)")));

            foreach (KeyValuePair<string, string> pair in PhotoNumberSetters)
            {
                if (pair.Key == "fov" || pair.Key == "roll") continue;
                string key = pair.Key;
                string method = pair.Value;
                table.Set("set" + UpperFirst(key), DynValue.NewCallback((ctx, args) => SetPhotoNumber(args, table, method, "camera.photo.set" + UpperFirst(key) + "(value)")));
            }
            foreach (KeyValuePair<string, string> pair in PhotoBoolSetters)
            {
                string key = pair.Key;
                string method = pair.Value;
                table.Set("set" + UpperFirst(key), DynValue.NewCallback((ctx, args) => SetPhotoBool(args, table, method, "camera.photo.set" + UpperFirst(key) + "(enabled)")));
            }
            table.Set("getRange", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string key = FrameworkApiUtil.RequireString(args, offset, "camera.photo.getRange(name)");
                if (!PhotoRanges.TryGetValue(key, out string? property)) return DynValue.Nil;
                object? photo = GetSingleton("PhotoModeController");
                if (photo == null || !SleddersGameBindings.TryGetAnyOrGetter(photo, out object? range, property) || range == null)
                    return DynValue.Nil;
                var result = new Table(mod.Script);
                if (SleddersGameBindings.TryGetAny(range, out object? min, "min") && SleddersGameBindings.ToDouble(min) is double minN) result.Set("min", DynValue.NewNumber(minN));
                if (SleddersGameBindings.TryGetAny(range, out object? max, "max") && SleddersGameBindings.ToDouble(max) is double maxN) result.Set("max", DynValue.NewNumber(maxN));
                return DynValue.NewTable(result);
            }));
            table.Set("keys", DynValue.NewCallback((ctx, args) => StringArray(mod, PhotoNumberSetters.Keys.Concat(PhotoBoolSetters.Keys).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))));
            table.Set("hasSavedSettings", DynValue.NewCallback((ctx, args) =>
            {
                object? photo = GetSingleton("PhotoModeController");
                return photo != null && ReflectionBridge.TryCall(photo, "HasSavedSettings", Array.Empty<object?>(), out object? raw) && raw is bool b ? DynValue.NewBoolean(b) : DynValue.Nil;
            }));
            table.Set("disableAllFilters", DynValue.NewCallback((ctx, args) =>
            {
                object? photo = GetSingleton("PhotoModeController");
                return DynValue.NewBoolean(photo != null && SleddersGameBindings.TryCallAny(photo, new[] { "DisableAllFilters" }, Array.Empty<object?>(), out _));
            }));
            return table;
        }

        private static DynValue Project(LuaModInstance mod, Table table, CallbackArguments args, bool inverse, bool gui)
        {
            int offset = FrameworkApiUtil.MethodOffset(args, table);
            object? camera = UnityBridge.MainCamera;
            object? point = FrameworkApiUtil.ReadVector3(mod, args, offset, inverse ? "camera.screenToWorld(point)" : "camera.worldToScreen(point)");
            if (camera == null || point == null) return DynValue.Nil;
            if (gui && SleddersGameBindings.TryGetAny(point, out object? yRaw, "y"))
            {
                double? y = SleddersGameBindings.ToDouble(yRaw);
                if (y.HasValue) SleddersGameBindings.TrySetAny(point, WindowApi.GetHeight() - y.Value, "y");
            }
            string method = inverse ? "ScreenToWorldPoint" : "WorldToScreenPoint";
            if (!ReflectionBridge.TryCall(camera, method, new object?[] { point }, out object? result) || result == null) return DynValue.Nil;
            if (gui && !inverse && SleddersGameBindings.TryGetAny(result, out object? ry, "y"))
            {
                double? y = SleddersGameBindings.ToDouble(ry);
                if (y.HasValue) SleddersGameBindings.TrySetAny(result, WindowApi.GetHeight() - y.Value, "y");
            }
            return ValueConverter.ToDynValue(mod, result);
        }

        private static DynValue ScreenPointToRay(LuaModInstance mod, Table table, CallbackArguments args)
        {
            int offset = FrameworkApiUtil.MethodOffset(args, table);
            object? camera = UnityBridge.MainCamera;
            object? point = FrameworkApiUtil.ReadVector3(mod, args, offset, "camera.screenPointToRay(point)");
            if (camera == null || point == null || !ReflectionBridge.TryCall(camera, "ScreenPointToRay", new object?[] { point }, out object? ray) || ray == null)
                return DynValue.Nil;
            var result = new Table(mod.Script);
            if (SleddersGameBindings.TryGetAny(ray, out object? origin, "origin")) result.Set("origin", ValueConverter.ToDynValue(mod, origin));
            if (SleddersGameBindings.TryGetAny(ray, out object? direction, "direction")) result.Set("direction", ValueConverter.ToDynValue(mod, direction));
            return DynValue.NewTable(result);
        }

        private static object? FindOne(string typeName)
        {
            Type? type = ReflectionBridge.FindTypeExact(typeName);
            return type == null ? null : ReflectionBridge.FindObjectsOfType(type, 16).FirstOrDefault();
        }

        private static object? GetSingleton(string typeName)
        {
            Type? type = ReflectionBridge.FindTypeExact(typeName);
            if (type == null) return null;
            try { return ReflectionBridge.GetStaticMember(type, "Instance"); }
            catch { return FindOne(typeName); }
        }

        private static object? GetFreeDroneController()
        {
            object? free = GetSingleton("FreeCameraController");
            if (free == null) return null;
            return SleddersGameBindings.TryGetAnyOrGetter(free, out object? drone, "DroneCameraController") ? drone : null;
        }

        private static DynValue FreeDroneNumber(string name)
        {
            object? drone = GetFreeDroneController();
            if (drone == null || !SleddersGameBindings.TryGetAnyOrGetter(drone, out object? value, name)) return DynValue.Nil;
            double? n = SleddersGameBindings.ToDouble(value);
            return n.HasValue ? DynValue.NewNumber(n.Value) : DynValue.Nil;
        }

        private static DynValue SetFreeDroneNumber(CallbackArguments args, Table table, string name, string usage)
        {
            int offset = FrameworkApiUtil.MethodOffset(args, table);
            object? drone = GetFreeDroneController();
            return DynValue.NewBoolean(drone != null && SleddersGameBindings.TrySetAny(drone, FrameworkApiUtil.RequireFiniteNumber(args, offset, usage), name));
        }

        private static DynValue FollowerNumber(string method)
        {
            object? follower = FindOne("CameraFollower");
            if (follower == null || !ReflectionBridge.TryCall(follower, method, Array.Empty<object?>(), out object? raw)) return DynValue.Nil;
            return SleddersGameBindings.ToDouble(raw) is double n ? DynValue.NewNumber(n) : DynValue.Nil;
        }

        private static DynValue SetFollowerNumber(CallbackArguments args, Table table, string method, string usage)
        {
            object? follower = FindOne("CameraFollower");
            int offset = FrameworkApiUtil.MethodOffset(args, table);
            float value = (float)FrameworkApiUtil.RequireFiniteNumber(args, offset, usage);
            return DynValue.NewBoolean(follower != null && SleddersGameBindings.TryCallAny(follower, new[] { method }, new object?[] { value }, out _));
        }

        private static DynValue PhotoCallNumber(string method)
        {
            object? photo = GetSingleton("PhotoModeController");
            if (photo == null || !ReflectionBridge.TryCall(photo, method, Array.Empty<object?>(), out object? raw)) return DynValue.Nil;
            return SleddersGameBindings.ToDouble(raw) is double n ? DynValue.NewNumber(n) : DynValue.Nil;
        }

        private static DynValue SetPhotoNumber(CallbackArguments args, Table table, string method, string usage)
        {
            object? photo = GetSingleton("PhotoModeController");
            int offset = FrameworkApiUtil.MethodOffset(args, table);
            float value = (float)FrameworkApiUtil.RequireFiniteNumber(args, offset, usage);
            return DynValue.NewBoolean(photo != null && SleddersGameBindings.TryCallAny(photo, new[] { method }, new object?[] { value }, out _));
        }

        private static DynValue SetPhotoBool(CallbackArguments args, Table table, string method, string usage)
        {
            object? photo = GetSingleton("PhotoModeController");
            int offset = FrameworkApiUtil.MethodOffset(args, table);
            bool value = FrameworkApiUtil.RequireBool(args, offset, usage);
            return DynValue.NewBoolean(photo != null && SleddersGameBindings.TryCallAny(photo, new[] { method }, new object?[] { value }, out _));
        }

        private static object? GetDefaultCameraPosition()
        {
            object? camera = UnityBridge.MainCamera;
            object? position = camera == null ? null : SleddersGameBindings.GetPosition(camera);
            if (position != null) return position;
            object? sled = SleddersGameBindings.FindLocalSled();
            return sled == null ? null : SleddersGameBindings.GetPosition(sled);
        }

        private static string UpperFirst(string value) => string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value.Substring(1);

        private static DynValue StringArray(LuaModInstance mod, IEnumerable<string> values)
        {
            var table = new Table(mod.Script);
            int i = 1;
            foreach (string value in values) table.Set(i++, DynValue.NewString(value));
            return DynValue.NewTable(table);
        }
    }
}
