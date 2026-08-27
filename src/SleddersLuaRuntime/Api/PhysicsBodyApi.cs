using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class PhysicsBodyApi
    {
        private static readonly IReadOnlyList<SemanticProperty> Properties = new[]
        {
            new SemanticProperty("mass", true, "mass"),
            new SemanticProperty("position", true, "position"),
            new SemanticProperty("rotation", true, "rotation"),
            new SemanticProperty("velocity", true, "linearVelocity", "velocity"),
            new SemanticProperty("angularVelocity", true, "angularVelocity"),
            new SemanticProperty("centerOfMass", true, "centerOfMass"),
            new SemanticProperty("linearDamping", true, "linearDamping", "drag"),
            new SemanticProperty("angularDamping", true, "angularDamping", "angularDrag"),
            new SemanticProperty("useGravity", true, "useGravity"),
            new SemanticProperty("isKinematic", true, "isKinematic"),
            new SemanticProperty("maxAngularVelocity", true, "maxAngularVelocity"),
            new SemanticProperty("sleepThreshold", true, "sleepThreshold"),
            new SemanticProperty("solverIterations", true, "solverIterations"),
            new SemanticProperty("solverVelocityIterations", true, "solverVelocityIterations"),
            new SemanticProperty("collisionDetectionMode", true, "collisionDetectionMode"),
            new SemanticProperty("interpolation", true, "interpolation")
        };

        public static DynValue Wrap(LuaModInstance mod, object body)
        {
            int handle = mod.Handles.Add(body);
            if (mod.TryGetCachedObject("body", handle, out DynValue cached)) return cached;

            DynValue bag = SemanticPropertyBag.Wrap(mod, body, "rigidbody", Properties);
            Table table = bag.Table;
            table.Set("__type", DynValue.NewString("rigidbody"));

            SemanticPropertyBag.AddNamedAccessors(table, mod, bag, "mass", "Mass");
            SemanticPropertyBag.AddNamedAccessors(table, mod, bag, "velocity", "Velocity");
            SemanticPropertyBag.AddNamedAccessors(table, mod, bag, "angularVelocity", "AngularVelocity");
            SemanticPropertyBag.AddNamedAccessors(table, mod, bag, "centerOfMass", "CenterOfMass");
            SemanticPropertyBag.AddNamedAccessors(table, mod, bag, "linearDamping", "LinearDamping");
            SemanticPropertyBag.AddNamedAccessors(table, mod, bag, "angularDamping", "AngularDamping");
            SemanticPropertyBag.AddNamedAccessors(table, mod, bag, "useGravity", "UseGravity");
            SemanticPropertyBag.AddNamedAccessors(table, mod, bag, "isKinematic", "Kinematic");

            table.Set("getPos", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "rigidbody");
                return SleddersGameBindings.TryGetAny(live, out object? value, "position") ? ValueConverter.ToDynValue(mod, value) : DynValue.Nil;
            }));
            table.Set("setPos", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "rigidbody");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? vector = FrameworkApiUtil.ReadVector3(mod, args, offset, "body.setPos(vector3)");
                return DynValue.NewBoolean(vector != null && SleddersGameBindings.TrySetAny(live, vector, "position"));
            }));
            table.Set("getRot", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "rigidbody");
                if (!SleddersGameBindings.TryGetAny(live, out object? q, "rotation") || q == null) return DynValue.Nil;
                return SleddersGameBindings.TryGetAny(q, out object? euler, "eulerAngles") ? ValueConverter.ToDynValue(mod, euler) : ValueConverter.ToDynValue(mod, q);
            }));
            table.Set("setRot", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "rigidbody");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? quaternion = FrameworkApiUtil.ReadEulerQuaternion(mod, args, offset, "body.setRot(eulerVector3)");
                return DynValue.NewBoolean(quaternion != null && SleddersGameBindings.TrySetAny(live, quaternion, "rotation"));
            }));
            table.Set("addForce", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "rigidbody");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? vector = FrameworkApiUtil.ReadVector3(mod, args, offset, "body.addForce(vector3)");
                return DynValue.NewBoolean(vector != null && SleddersGameBindings.TryCallAny(live, new[] { "AddForce" }, new object?[] { vector }, out _));
            }));
            table.Set("addTorque", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "rigidbody");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? vector = FrameworkApiUtil.ReadVector3(mod, args, offset, "body.addTorque(vector3)");
                return DynValue.NewBoolean(vector != null && SleddersGameBindings.TryCallAny(live, new[] { "AddTorque" }, new object?[] { vector }, out _));
            }));
            table.Set("wake", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "rigidbody");
                return DynValue.NewBoolean(SleddersGameBindings.TryCallAny(live, new[] { "WakeUp" }, Array.Empty<object?>(), out _));
            }));
            table.Set("sleep", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "rigidbody");
                return DynValue.NewBoolean(SleddersGameBindings.TryCallAny(live, new[] { "Sleep" }, Array.Empty<object?>(), out _));
            }));
            table.Set("resetMotion", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "rigidbody");
                Type? v3 = ReflectionBridge.FindTypeExact("UnityEngine.Vector3");
                object? zero = v3 == null ? null : Activator.CreateInstance(v3);
                if (zero == null) return DynValue.False;
                bool a = SleddersGameBindings.TrySetAny(live, zero, "linearVelocity", "velocity");
                bool b = SleddersGameBindings.TrySetAny(live, zero, "angularVelocity");
                return DynValue.NewBoolean(a || b);
            }));

            mod.CacheObject("body", handle, bag);
            return bag;
        }
    }
}
