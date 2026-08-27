using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class GameplayApi
    {
        public static Table BuildPlayer(LuaModInstance mod)
        {
            var player = new Table(mod.Script);
            player.Set("getSled", DynValue.NewCallback((ctx, args) =>
            {
                object? sled = SleddersGameBindings.FindLocalSled();
                return sled == null ? DynValue.Nil : WrapSled(mod, sled);
            }));
            player.Set("hasSled", DynValue.NewCallback((ctx, args) => DynValue.NewBoolean(SleddersGameBindings.FindLocalSled() != null)));
            player.Set("getPos", DynValue.NewCallback((ctx, args) =>
            {
                object? p = SleddersGameBindings.FindPlayerObject();
                object? t = ResolvePlayerTransform(p);
                return t != null && SleddersGameBindings.TryGetAny(t, out object? v, "position") ? ValueConverter.ToDynValue(mod, v) : DynValue.Nil;
            }));
            player.Set("setPos", DynValue.NewCallback((ctx, args) =>
            {
                object? p = SleddersGameBindings.FindPlayerObject(); object? t = ResolvePlayerTransform(p);
                if (t == null) return DynValue.False; int offset = FrameworkApiUtil.MethodOffset(args, player); object? v = FrameworkApiUtil.ReadVector3(mod, args, offset, "player.setPos(vector3)");
                return DynValue.NewBoolean(v != null && SleddersGameBindings.TrySetAny(t, v, "position"));
            }));
            player.Set("getRot", DynValue.NewCallback((ctx, args) =>
            {
                object? p = SleddersGameBindings.FindPlayerObject(); object? t = ResolvePlayerTransform(p);
                return t != null && SleddersGameBindings.TryGetAny(t, out object? v, "eulerAngles") ? ValueConverter.ToDynValue(mod, v) : DynValue.Nil;
            }));
            player.Set("setRot", DynValue.NewCallback((ctx, args) =>
            {
                object? p = SleddersGameBindings.FindPlayerObject(); object? t = ResolvePlayerTransform(p);
                if (t == null) return DynValue.False; int offset = FrameworkApiUtil.MethodOffset(args, player); object? q = FrameworkApiUtil.ReadEulerQuaternion(mod, args, offset, "player.setRot(eulerVector3)");
                return DynValue.NewBoolean(q != null && SleddersGameBindings.TrySetAny(t, q, "rotation"));
            }));
            player.Set("getTransform", DynValue.NewCallback((ctx, args) =>
            {
                object? p = SleddersGameBindings.FindPlayerObject();
                object? t = ResolvePlayerTransform(p);
                return t == null ? DynValue.Nil : TransformApi.Wrap(mod, t);
            }));
            player.Set("getRenderers", DynValue.NewCallback((ctx, args) =>
            {
                object? p = SleddersGameBindings.FindPlayerObject();
                if (p == null) return DynValue.NewTable(new Table(mod.Script));
                int offset = FrameworkApiUtil.MethodOffset(args, player);
                int max = args.Count > offset ? FrameworkApiUtil.RequireInt(args, offset, "player.getRenderers(max)", 1, 256) : 64;
                return VisualApi.GetRenderers(mod, p, max);
            }));
            player.Set("getState", DynValue.NewCallback((ctx, args) =>
            {
                object? p = SleddersGameBindings.FindPlayerObject(); if (p == null) return DynValue.Nil;
                return SemanticPropertyBag.Wrap(mod, p, "playerState", new[] {
                    new SemanticProperty("bodyOffset", true, "bodyOffset"),
                    new SemanticProperty("drivingStance", true, "currentDrivingStance"),
                    new SemanticProperty("switchbackStance", true, "currentSwitchbackStance"),
                    new SemanticProperty("isPerformingAction", true, "isPerformingAction"),
                    new SemanticProperty("handRotationWeight", true, "handRotationWeight"),
                    new SemanticProperty("shoulderDefaultWeight", true, "shoulderDefaultWeight"),
                    new SemanticProperty("shoulderTargetWeight", true, "shoulderTargetWeight"),
                    new SemanticProperty("shoulderMaxWeight", true, "shoulderMaxWeight"),
                    new SemanticProperty("shoulderWeightFactor", true, "shoulderWeightFactor"),
                    new SemanticProperty("shoulderSmoothSpeed", true, "shoulderSmoothSpeed")
                });
            }));

            object? ResolvePlayerTransform(object? playerObject)
            {
                if (playerObject == null) return null;
                if (SleddersBindingResolver.HasExactPlayerTransformBinding)
                    return SleddersBindingResolver.GetPlayerTransform(playerObject);
                return SleddersGameBindings.GetTransform(playerObject);
            }

            return player;
        }

        public static Table BuildSledService(LuaModInstance mod)
        {
            var service = new Table(mod.Script);

            service.Set("getAll", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, service);
                int max = args.Count > offset && !args[offset].IsNil()
                    ? FrameworkApiUtil.RequireInt(args, offset, "sled.getAll(max)", 1, 64)
                    : 16;

                var result = new Table(mod.Script);
                int i = 1;
                foreach (object sled in SleddersGameBindings.FindLiveSleds(max * 2))
                {
                    if (!SleddersGameBindings.IsValidSled(sled)) continue;
                    result.Set(i++, WrapSled(mod, sled));
                    if (i > max) break;
                }
                return DynValue.NewTable(result);
            }));
            return service;
        }

        public static DynValue WrapSled(LuaModInstance mod, object sled)
        {
            int handle = mod.Handles.Add(sled);
            if (mod.TryGetCachedObject("sled", handle, out DynValue cached)) return cached;

            var table = new Table(mod.Script);
            table.Set("__handle", DynValue.NewNumber(handle));
            table.Set("__type", DynValue.NewString("sled"));
            object? Live() => mod.Handles.Get(handle);

            table.Set("isValid", DynValue.NewCallback((ctx, args) => DynValue.NewBoolean(SleddersGameBindings.IsValidSled(Live()))));
            table.Set("getName", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live();
                return live == null ? DynValue.Nil : DynValue.NewString(SleddersGameBindings.GetFriendlyName(live));
            }));

            table.Set("getPos", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live();
                return ValueConverter.ToDynValue(mod, live == null ? null : SleddersGameBindings.GetPosition(live));
            }));
            table.Set("setPos", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live();
                if (live == null) return DynValue.False;
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? pos = FrameworkApiUtil.ReadVector3(mod, args, offset, "sled.setPos(vector3)");
                object? body = SleddersGameBindings.GetRigidbody(live);
                object? transform = SleddersGameBindings.GetTransform(live);
                bool ok = pos != null && ((body != null && SleddersGameBindings.TrySetAny(body, pos, "position")) ||
                    (transform != null && SleddersGameBindings.TrySetAny(transform, pos, "position")));
                return DynValue.NewBoolean(ok);
            }));
            table.Set("teleport", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live();
                if (live == null) return DynValue.False;
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? pos = FrameworkApiUtil.ReadVector3(mod, args, offset, "sled.teleport(vector3 [, preserveVelocity])");
                bool preserve = args.Count > offset + 1 && args[offset + 1].Type == DataType.Boolean && args[offset + 1].Boolean;
                return DynValue.NewBoolean(pos != null && SleddersGameBindings.Teleport(live, pos, preserve));
            }));
            table.Set("getRot", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live();
                return ValueConverter.ToDynValue(mod, live == null ? null : SleddersGameBindings.GetRotation(live));
            }));
            table.Set("setRot", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live();
                if (live == null) return DynValue.False;
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? q = FrameworkApiUtil.ReadEulerQuaternion(mod, args, offset, "sled.setRot(eulerVector3)");
                object? transform = SleddersGameBindings.GetTransform(live);
                return DynValue.NewBoolean(q != null && transform != null && SleddersGameBindings.TrySetAny(transform, q, "rotation"));
            }));

            table.Set("getVel", DynValue.NewCallback((ctx, args) => { object? live = Live(); return ValueConverter.ToDynValue(mod, live == null ? null : SleddersGameBindings.GetLocalVelocity(live)); }));
            table.Set("getWorldVel", DynValue.NewCallback((ctx, args) => { object? live = Live(); return ValueConverter.ToDynValue(mod, live == null ? null : SleddersGameBindings.GetVelocity(live)); }));
            table.Set("setVel", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); if (live == null) return DynValue.False;
                int offset = FrameworkApiUtil.MethodOffset(args, table); object? v = FrameworkApiUtil.ReadVector3(mod, args, offset, "sled.setVel(vector3)");
                return DynValue.NewBoolean(v != null && SleddersGameBindings.SetLocalVelocity(live, v));
            }));
            table.Set("setWorldVel", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); if (live == null) return DynValue.False;
                int offset = FrameworkApiUtil.MethodOffset(args, table); object? v = FrameworkApiUtil.ReadVector3(mod, args, offset, "sled.setWorldVel(vector3)");
                return DynValue.NewBoolean(v != null && SleddersGameBindings.SetVelocity(live, v));
            }));
            table.Set("getAngularVel", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); object? body = live == null ? null : SleddersGameBindings.GetRigidbody(live);
                return body != null && SleddersGameBindings.TryGetAny(body, out object? value, "angularVelocity") ? ValueConverter.ToDynValue(mod, value) : DynValue.Nil;
            }));
            table.Set("setAngularVel", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); object? body = live == null ? null : SleddersGameBindings.GetRigidbody(live); if (body == null) return DynValue.False;
                int offset = FrameworkApiUtil.MethodOffset(args, table); object? v = FrameworkApiUtil.ReadVector3(mod, args, offset, "sled.setAngularVel(vector3)");
                return DynValue.NewBoolean(v != null && SleddersGameBindings.TrySetAny(body, v, "angularVelocity"));
            }));
            table.Set("getForwardSpeed", DynValue.NewCallback((ctx, args) => { object? live = Live(); return NullableNumber(live == null ? null : SleddersGameBindings.GetForwardSpeed(live)); }));
            table.Set("getSpeed", DynValue.NewCallback((ctx, args) => { object? live = Live(); return NullableNumber(live == null ? null : SleddersGameBindings.GetSpeed(live)); }));

            table.Set("getMass", DynValue.NewCallback((ctx, args) => { object? live = Live(); return NullableNumber(live == null ? null : SleddersGameBindings.GetMass(live)); }));
            table.Set("setMass", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); if (live == null) return DynValue.False;
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                return DynValue.NewBoolean(SleddersGameBindings.SetMass(live, FrameworkApiUtil.RequireFiniteNumber(args, offset, "sled.setMass(kg)")));
            }));
            table.Set("getBody", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); object? body = live == null ? null : SleddersGameBindings.GetRigidbody(live);
                return body == null ? DynValue.Nil : PhysicsBodyApi.Wrap(mod, body);
            }));
            table.Set("getVehicle", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); object? vehicle = live == null ? null : SleddersGameBindings.GetVehicleDefinition(live);
                return vehicle == null ? DynValue.Nil : VehicleApi.Wrap(mod, vehicle);
            }));
            table.Set("getTuning", DynValue.NewCallback((ctx, args) => { object? live = Live(); return live == null ? DynValue.Nil : TuningApi.Wrap(mod, live); }));
            table.Set("getStructure", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live();
                object? structure = live == null ? null : SleddersGameBindings.GetStructure(live);
                return live == null || structure == null ? DynValue.Nil : SledStructureApi.Wrap(mod, structure, live);
            }));
            table.Set("getRenderers", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); if (live == null) return DynValue.NewTable(new Table(mod.Script));
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string group = args.Count > offset && args[offset].Type == DataType.String ? args[offset].String : "all";
                return VisualApi.GetSledRenderers(mod, live, group);
            }));

            table.Set("addForce", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); if (live == null) return DynValue.False; int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? v = FrameworkApiUtil.ReadVector3(mod, args, offset, "sled.addForce(vector3)"); return DynValue.NewBoolean(v != null && SleddersGameBindings.AddLocalForce(live, v));
            }));
            table.Set("addWorldForce", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); if (live == null) return DynValue.False; int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? v = FrameworkApiUtil.ReadVector3(mod, args, offset, "sled.addWorldForce(vector3)"); return DynValue.NewBoolean(v != null && SleddersGameBindings.AddForce(live, v));
            }));
            table.Set("addTorque", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); object? body = live == null ? null : SleddersGameBindings.GetRigidbody(live); if (body == null) return DynValue.False;
                int offset = FrameworkApiUtil.MethodOffset(args, table); object? v = FrameworkApiUtil.ReadVector3(mod, args, offset, "sled.addTorque(vector3)");
                return DynValue.NewBoolean(v != null && SleddersGameBindings.TryCallAny(body, new[] { "AddRelativeTorque" }, new object?[] { v }, out _));
            }));
            table.Set("addWorldTorque", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); object? body = live == null ? null : SleddersGameBindings.GetRigidbody(live); if (body == null) return DynValue.False;
                int offset = FrameworkApiUtil.MethodOffset(args, table); object? v = FrameworkApiUtil.ReadVector3(mod, args, offset, "sled.addWorldTorque(vector3)");
                return DynValue.NewBoolean(v != null && SleddersGameBindings.TryCallAny(body, new[] { "AddTorque" }, new object?[] { v }, out _));
            }));

            table.Set("getHeadlights", DynValue.NewCallback((ctx, args) => { object? live = Live(); return NullableBool(live == null ? null : SleddersGameBindings.AreHeadlightsOn(live)); }));
            table.Set("setHeadlights", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); if (live == null) return DynValue.False; int offset = FrameworkApiUtil.MethodOffset(args, table);
                return DynValue.NewBoolean(SleddersGameBindings.SetHeadlights(live, FrameworkApiUtil.RequireBool(args, offset, "sled.setHeadlights(enabled)")));
            }));
            table.Set("toggleHeadlights", DynValue.NewCallback((ctx, args) => { object? live = Live(); return DynValue.NewBoolean(live != null && SleddersGameBindings.ToggleHeadlights(live)); }));
            table.Set("forceHeadlights", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); if (live == null) return DynValue.False; int offset = FrameworkApiUtil.MethodOffset(args, table);
                return DynValue.NewBoolean(SleddersGameBindings.ForceHeadlights(live, FrameworkApiUtil.RequireBool(args, offset, "sled.forceHeadlights(enabled)"), mod.StateOwnerToken));
            }));
            table.Set("areHeadlightsForced", DynValue.NewCallback((ctx, args) => { object? live = Live(); return DynValue.NewBoolean(live != null && SleddersGameBindings.IsHeadlightControlled(live, mod.StateOwnerToken)); }));
            table.Set("releaseHeadlights", DynValue.NewCallback((ctx, args) => { object? live = Live(); return DynValue.NewBoolean(live != null && SleddersGameBindings.ReleaseHeadlights(live, mod.StateOwnerToken)); }));

            DynValue getRpm = DynValue.NewCallback((ctx, args) => { object? live = Live(); return NullableNumber(live == null ? null : SleddersGameBindings.GetRpm(live)); });
            table.Set("getRpm", getRpm); table.Set("getRPM", getRpm);
            table.Set("getDisplayRpm", DynValue.NewCallback((ctx, args) => NamedNumber(Live(), "RpmForSpeedoMeter")));
            table.Set("getTrackSpeed", DynValue.NewCallback((ctx, args) => NamedNumber(Live(), "TrackSpeedForSpeedoMeter")));
            table.Set("getThrottle", DynValue.NewCallback((ctx, args) => { object? live = Live(); return NullableNumber(live == null ? null : SleddersGameBindings.GetThrottle(live)); }));
            table.Set("isEngineOn", DynValue.NewCallback((ctx, args) => { object? live = Live(); return NullableBool(live == null ? null : SleddersGameBindings.IsEngineOn(live)); }));
            table.Set("setEngineOn", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); if (live == null) return DynValue.False; int offset = FrameworkApiUtil.MethodOffset(args, table);
                return DynValue.NewBoolean(SleddersGameBindings.SetEngineRunning(live, FrameworkApiUtil.RequireBool(args, offset, "sled.setEngineOn(enabled)")));
            }));
            table.Set("startEngine", DynValue.NewCallback((ctx, args) => { object? live = Live(); return DynValue.NewBoolean(live != null && SleddersGameBindings.SetEngineRunning(live, true)); }));
            table.Set("stopEngine", DynValue.NewCallback((ctx, args) => { object? live = Live(); return DynValue.NewBoolean(live != null && SleddersGameBindings.SetEngineRunning(live, false)); }));

            table.Set("getFuel", DynValue.NewCallback((ctx, args) => { object? live = Live(); return NullableNumber(live == null ? null : SleddersGameBindings.GetFuel(live)); }));
            table.Set("getFuelCapacity", DynValue.NewCallback((ctx, args) => { object? live = Live(); return NullableNumber(live == null ? null : SleddersGameBindings.GetFuelCapacity(live)); }));
            table.Set("getFuelPercent", DynValue.NewCallback((ctx, args) => { object? live = Live(); return NullableNumber(live == null ? null : SleddersGameBindings.GetFuelPercent(live)); }));
            table.Set("setFuel", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); if (live == null) return DynValue.False; int offset = FrameworkApiUtil.MethodOffset(args, table);
                return DynValue.NewBoolean(SleddersGameBindings.SetFuel(live, FrameworkApiUtil.RequireFiniteNumber(args, offset, "sled.setFuel(litres)")));
            }));
            table.Set("addFuel", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); if (live == null) return DynValue.False; int offset = FrameworkApiUtil.MethodOffset(args, table);
                return DynValue.NewBoolean(SleddersGameBindings.AddFuel(live, FrameworkApiUtil.RequireFiniteNumber(args, offset, "sled.addFuel(litres)")));
            }));
            table.Set("fillFuel", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); if (live == null) return DynValue.False; double? cap = SleddersGameBindings.GetFuelCapacity(live);
                return DynValue.NewBoolean(cap.HasValue && SleddersGameBindings.SetFuel(live, cap.Value));
            }));
            table.Set("isFuelEmpty", DynValue.NewCallback((ctx, args) => { object? live = Live(); double? fuel = live == null ? null : SleddersGameBindings.GetFuel(live); return fuel.HasValue ? DynValue.NewBoolean(fuel.Value <= 0.0001) : DynValue.Nil; }));

            AddStateAccessors(table, Live, "Parking", new[] { "isParking", "IsParking" }, new[] { "SetParking" });
            AddStateAccessors(table, Live, "Frozen", new[] { "isFrozen", "IsFrozen" }, Array.Empty<string>());
            AddStateAccessors(table, Live, "Visible", new[] { "isShowing", "IsShowing", "isVisible", "IsVisible" }, new[] { "ShowVehicle" });
            table.Set("isDead", DynValue.NewCallback((ctx, args) => NamedBool(Live(), "isDead", "IsDead")));
            table.Set("isInAir", DynValue.NewCallback((ctx, args) => NamedBoolOrCall(Live(), "IsInAir")));
            table.Set("isWheelie", DynValue.NewCallback((ctx, args) => NamedBoolOrCall(Live(), "IsWheelie")));
            table.Set("getClientId", DynValue.NewCallback((ctx, args) =>
            {
                object? live = Live(); if (live == null) return DynValue.Nil;
                return SleddersGameBindings.TryGetAnyOrGetter(live, out object? raw, "clientId", "ClientId") && raw != null ? DynValue.NewString(raw.ToString() ?? string.Empty) : DynValue.Nil;
            }));

            DynValue wrapped = DynValue.NewTable(table);
            mod.CacheObject("sled", handle, wrapped);
            return wrapped;

            DynValue NamedNumber(object? target, params string[] names)
            {
                if (target == null || !SleddersGameBindings.TryGetAnyOrGetter(target, out object? raw, names)) return DynValue.Nil;
                double? n = SleddersGameBindings.ToDouble(raw); return n.HasValue ? DynValue.NewNumber(n.Value) : DynValue.Nil;
            }
            DynValue NamedBool(object? target, params string[] names)
            {
                return target != null && SleddersGameBindings.TryGetAnyOrGetter(target, out object? raw, names) && raw is bool b ? DynValue.NewBoolean(b) : DynValue.Nil;
            }
            DynValue NamedBoolOrCall(object? target, string name)
            {
                if (target == null) return DynValue.Nil;
                if (SleddersGameBindings.TryGetAnyOrGetter(target, out object? raw, name) && raw is bool b) return DynValue.NewBoolean(b);
                return SleddersGameBindings.TryCallAny(target, new[] { name }, Array.Empty<object?>(), out raw) && raw is bool c ? DynValue.NewBoolean(c) : DynValue.Nil;
            }
            void AddStateAccessors(Table t, Func<object?> target, string stem, string[] members, string[] methods)
            {
                t.Set("get" + stem, DynValue.NewCallback((ctx, args) => NamedBool(target(), members)));
                t.Set("set" + stem, DynValue.NewCallback((ctx, args) =>
                {
                    object? live = target(); if (live == null) return DynValue.False; int offset = FrameworkApiUtil.MethodOffset(args, t);
                    bool value = FrameworkApiUtil.RequireBool(args, offset, "sled.set" + stem + "(enabled)");
                    if (methods.Length > 0 && SleddersGameBindings.TryCallAny(live, methods, new object?[] { value }, out _)) return DynValue.True;
                    return DynValue.NewBoolean(SleddersGameBindings.TrySetAny(live, value, members));
                }));
            }
        }

        public static Table BuildCamera(LuaModInstance mod)
        {
            var camera = new Table(mod.Script);
            camera.Set("getFov", DynValue.NewCallback((ctx, args) =>
            {
                object? raw = UnityBridge.MainCamera;
                if (raw == null || !SleddersGameBindings.TryGetAny(raw, out object? value, "fieldOfView")) return DynValue.Nil;
                return ValueConverter.ToDynValue(mod, value);
            }));
            camera.Set("setFov", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, camera);
                double fov = FrameworkApiUtil.RequireFiniteNumber(args, offset, "camera.setFov(degrees)");
                if (fov < 1.0 || fov > 179.0)
                    throw new ScriptRuntimeException("camera.setFov(degrees) expects a value from 1 to 179.");
                object? raw = UnityBridge.MainCamera;
                Type? type = raw == null ? null : SleddersGameBindings.ResolveMemberType(raw, "fieldOfView");
                if (raw == null || type == null) return DynValue.False;
                return DynValue.NewBoolean(ReflectionBridge.TrySetMember(raw, "fieldOfView", ValueConverter.ChangeType(fov, type)));
            }));
            camera.Set("getPos", DynValue.NewCallback((ctx, args) =>
            {
                object? raw = UnityBridge.MainCamera;
                return ValueConverter.ToDynValue(mod, raw == null ? null : SleddersGameBindings.GetPosition(raw));
            }));
            camera.Set("getRot", DynValue.NewCallback((ctx, args) =>
            {
                object? raw = UnityBridge.MainCamera;
                return ValueConverter.ToDynValue(mod, raw == null ? null : SleddersGameBindings.GetRotation(raw));
            }));
            AdvancedCameraApi.Enhance(mod, camera);
            return camera;
        }

        public static Table MakeVector3(Script script, double x, double y, double z)
        {
            var value = new Table(script);
            value.Set("__type", DynValue.NewString("vector3"));
            value.Set("x", DynValue.NewNumber(x));
            value.Set("y", DynValue.NewNumber(y));
            value.Set("z", DynValue.NewNumber(z));
            ValueConverter.DecorateValueTable(script, value, new[] { "x", "y", "z" });
            return value;
        }

        private static DynValue NullableNumber(double? value) => value.HasValue ? DynValue.NewNumber(value.Value) : DynValue.Nil;
        private static DynValue NullableBool(bool? value) => value.HasValue ? DynValue.NewBoolean(value.Value) : DynValue.Nil;

        private static int MethodOffset(CallbackArguments args, Table table)
        {
            return args.Count > 0 && args[0].Type == DataType.Table && ReferenceEquals(args[0].Table, table) ? 1 : 0;
        }

        private static object? ReadVectorArgument(LuaModInstance mod, CallbackArguments args, int index, Type targetType, string usage)
        {
            if (args.Count <= index)
                throw new ScriptRuntimeException(usage + " requires a vector table or x, y, z numbers.");

            if (args[index].Type == DataType.Table)
                return ValueConverter.FromDynValue(mod, args[index], targetType);

            if (args.Count >= index + 3 &&
                args[index].Type == DataType.Number &&
                args[index + 1].Type == DataType.Number &&
                args[index + 2].Type == DataType.Number)
            {
                Table value = MakeVector3(mod.Script, args[index].Number, args[index + 1].Number, args[index + 2].Number);
                return ValueConverter.FromDynValue(mod, DynValue.NewTable(value), targetType);
            }

            throw new ScriptRuntimeException(usage + " expects a vector table or three numbers.");
        }

        private static double RequireNumber(CallbackArguments args, int index, string usage)
        {
            if (args.Count <= index || args[index].Type != DataType.Number) throw new ScriptRuntimeException(usage + " expects a number.");
            return args[index].Number;
        }

        private static bool RequireBool(CallbackArguments args, int index, string usage)
        {
            if (args.Count <= index || args[index].Type != DataType.Boolean) throw new ScriptRuntimeException(usage + " expects true or false.");
            return args[index].Boolean;
        }
    }
}
