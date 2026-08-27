using System.Collections.Generic;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class TuningApi
    {
        private static readonly IReadOnlyList<SemanticProperty> ControllerProperties = new[]
        {
            new SemanticProperty("throttleExponent", true, "throttleExponent"),
            new SemanticProperty("driverTorqueRoll", true, "driverTorgueFactorRoll"),
            new SemanticProperty("driverTorquePitch", true, "driverTorgueFactorPitch"),
            new SemanticProperty("sledTorque", true, "snowmobileTorgueFactor"),
            new SemanticProperty("fuelConsumptionEnabled", true, "enableFuelConsumption"),
            new SemanticProperty("fuelUsageMultiplier", true, "fuelUsageMultiplier"),
            new SemanticProperty("rpmSensitivity", true, "rpmSensitivity"),
            new SemanticProperty("rpmSensitivityDown", true, "rpmSensitivityDown"),
            new SemanticProperty("rpmSoundVariation", true, "rpmSoundVariation"),
            new SemanticProperty("minThrottleOnClutchEngagement", true, "minThrottleOnClutchEngagement"),
            new SemanticProperty("clutchRpmMin", true, "clutchRpmMin"),
            new SemanticProperty("clutchRpmMax", true, "clutchRpmMax"),
            new SemanticProperty("wheelieThreshold", true, "wheelieThreshold"),
            new SemanticProperty("drowningDepth", true, "drowningDepth"),
            new SemanticProperty("drowningTime", true, "drowningTime")
        };

        private static readonly IReadOnlyList<SemanticProperty> BaseProperties = new[]
        {
            new SemanticProperty("skisMaxAngle", true, "skisMaxAngle"),
            new SemanticProperty("verticalWeightTransfer", true, "enableVerticalWeightTransfer"),
            new SemanticProperty("horizontalWeightTransferMode", true, "horizontalWeightTransferMode"),
            new SemanticProperty("toeAngle", true, "toeAngle"),
            new SemanticProperty("driverLowStanceTargetY", true, "driverLowStanceTargetPositionY"),
            new SemanticProperty("switchbackJump", true, "switchbackJump"),
            new SemanticProperty("hopOverPreJump", true, "hopOverPreJump"),
            new SemanticProperty("switchBackLeanDistance", true, "switchBackLeanDistance"),
            new SemanticProperty("trailLeanDistance", true, "trailLeanDistance"),
            new SemanticProperty("switchbackTransitionTime", true, "switchbackTransitionTime")
        };

        private static readonly IReadOnlyList<SemanticProperty> SuspensionProperties = new[]
        {
            new SemanticProperty("subSteps", true, "suspensionSubSteps"),
            new SemanticProperty("interpolatedTerrainNormals", true, "interpolatedTerrainNormals"),
            new SemanticProperty("debugGraph", true, "debugGraph"),
            new SemanticProperty("antiRollBarFactor", true, "antiRollBarFactor"),
            new SemanticProperty("skiAutoTurn", true, "skiAutoTurn"),
            new SemanticProperty("uniformTrackFrictionVelocity", true, "uniformTrackFrictionVelocity"),
            new SemanticProperty("clampTrackSideFriction", true, "clampTrackSideFriction"),
            new SemanticProperty("trackRigidityFront", true, "trackRigidityFront"),
            new SemanticProperty("trackRigidityRear", true, "trackRigidityRear"),
            new SemanticProperty("tiltFrictionMode", true, "tiltFrictionMode"),
            new SemanticProperty("reduceSuspensionForceByTilt", true, "reduceSuspensionForceByTilt"),
            new SemanticProperty("reduceSkiGripForceByTilt", true, "reduceSkiGripForceByTilt"),
            new SemanticProperty("reduceTrackGripForceByTilt", true, "reduceTrackGripForceByTilt")
        };

        private static readonly IReadOnlyList<SemanticProperty> ShockProperties = new[]
        {
            new SemanticProperty("mass", true, "mass"),
            new SemanticProperty("maxCompression", true, "maxCompression"),
            new SemanticProperty("compression", true, "compression"),
            new SemanticProperty("velocity", true, "velocity"),
            new SemanticProperty("averageVelocity", true, "avarageVelocity")
        };

        private static readonly IReadOnlyList<SemanticProperty> ShockSettingsProperties = new[]
        {
            new SemanticProperty("springFactor", true, "springFactor"),
            new SemanticProperty("damperFactor", true, "damperFactor"),
            new SemanticProperty("fastCompressionVelocityThreshold", true, "fastCompressionVelocityThreshold"),
            new SemanticProperty("fastReboundVelocityThreshold", true, "fastReboundVelocityThreshold"),
            new SemanticProperty("compressionRatio", true, "compressionRatio"),
            new SemanticProperty("compressionFastRatio", true, "compressionFastRatio"),
            new SemanticProperty("reboundRatio", true, "reboundRatio"),
            new SemanticProperty("reboundFastRatio", true, "reboundFastRatio")
        };

        public static DynValue Wrap(LuaModInstance mod, object sled)
        {
            int handle = mod.Handles.Add(sled);
            if (mod.TryGetCachedObject("tuning", handle, out DynValue cached)) return cached;

            var table = new Table(mod.Script);
            table.Set("__handle", DynValue.NewNumber(handle));
            table.Set("__type", DynValue.NewString("sledTuning"));
            table.Set("isValid", DynValue.NewCallback((ctx, args) => DynValue.NewBoolean(FrameworkApiUtil.Resolve(mod, handle) != null)));

            table.Set("getController", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "sled tuning");
                return SemanticPropertyBag.Wrap(mod, live, "sledControllerTuning", ControllerProperties);
            }));

            table.Set("getBase", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "sled tuning");
                object? controllerBase = SleddersGameBindings.GetControllerBase(live);
                return controllerBase == null ? DynValue.Nil : SemanticPropertyBag.Wrap(mod, controllerBase, "sledBaseTuning", BaseProperties);
            }));

            table.Set("getSuspension", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "sled tuning");
                object? suspension = ResolveSuspension(live);
                return suspension == null ? DynValue.Nil : SemanticPropertyBag.Wrap(mod, suspension, "suspension", SuspensionProperties);
            }));

            table.Set("getShock", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "sled tuning");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string which = args.Count > offset && args[offset].Type == DataType.String ? args[offset].String.Trim().ToLowerInvariant() : "front";
                object? suspension = ResolveSuspension(live);
                if (suspension == null) return DynValue.Nil;
                string[] names = which == "rear" ? new[] { "rearSuspension" } : new[] { "frontSuspension" };
                return SleddersGameBindings.TryGetAny(suspension, out object? shock, names) && shock != null
                    ? WrapShock(mod, shock)
                    : DynValue.Nil;
            }));

            DynValue wrapped = DynValue.NewTable(table);
            mod.CacheObject("tuning", handle, wrapped);
            return wrapped;
        }

        private static object? ResolveSuspension(object sled)
        {
            if (SleddersBindingResolver.TryGetSuspension(sled, out object? exact) && exact != null) return exact;
            return SleddersGameBindings.TryGetAny(sled, out object? value, "suspensionController", "SuspensionController", "ADGKAPLIGNP") ? value : null;
        }

        private static DynValue WrapShock(LuaModInstance mod, object shock)
        {
            DynValue bag = SemanticPropertyBag.Wrap(mod, shock, "shock", ShockProperties);
            Table table = bag.Table;
            table.Set("getSoft", DynValue.NewCallback((ctx, args) =>
            {
                object live = RequireBagTarget(mod, table, "shock");
                return SleddersGameBindings.TryGetAny(live, out object? settings, "soft") && settings != null
                    ? SemanticPropertyBag.Wrap(mod, settings, "shockSettings", ShockSettingsProperties)
                    : DynValue.Nil;
            }));
            table.Set("getHard", DynValue.NewCallback((ctx, args) =>
            {
                object live = RequireBagTarget(mod, table, "shock");
                return SleddersGameBindings.TryGetAny(live, out object? settings, "hard") && settings != null
                    ? SemanticPropertyBag.Wrap(mod, settings, "shockSettings", ShockSettingsProperties)
                    : DynValue.Nil;
            }));
            table.Set("reset", DynValue.NewCallback((ctx, args) =>
            {
                object live = RequireBagTarget(mod, table, "shock");
                return DynValue.NewBoolean(SleddersGameBindings.TryCallAny(live, new[] { "Reset" }, System.Array.Empty<object?>(), out _));
            }));
            return bag;
        }

        private static object RequireBagTarget(LuaModInstance mod, Table table, string label)
        {
            DynValue handle = table.Get("__handle");
            if (handle.Type != DataType.Number) throw new ScriptRuntimeException(label + " handle is missing.");
            return FrameworkApiUtil.RequireObject(mod, (int)handle.Number, label);
        }
    }
}
