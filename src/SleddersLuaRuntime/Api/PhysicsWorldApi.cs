using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class PhysicsWorldApi
    {
        public static Table Build(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("getGravity", DynValue.NewCallback((ctx, args) =>
            {
                Type? physics = ReflectionBridge.FindTypeExact("UnityEngine.Physics");
                if (physics == null) return DynValue.Nil;
                try { return ValueConverter.ToDynValue(mod, ReflectionBridge.GetStaticMember(physics, "gravity")); }
                catch { return DynValue.Nil; }
            }));
            table.Set("setGravity", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? gravity = FrameworkApiUtil.ReadVector3(mod, args, offset, "physics.setGravity(vector3)");
                Type? physics = ReflectionBridge.FindTypeExact("UnityEngine.Physics");
                if (physics == null || gravity == null) return DynValue.False;
                try { ReflectionBridge.SetStaticMember(physics, "gravity", gravity); return DynValue.True; }
                catch { return DynValue.False; }
            }));
            table.Set("raycast", DynValue.NewCallback((ctx, args) => Raycast(mod, table, args)));
            table.Set("overlapSphere", DynValue.NewCallback((ctx, args) => OverlapSphere(mod, table, args)));
            table.Set("ignoreCollision", DynValue.NewCallback((ctx, args) => IgnoreCollision(mod, table, args)));
            return table;
        }

        private static DynValue Raycast(LuaModInstance mod, Table table, CallbackArguments args)
        {
            int offset = FrameworkApiUtil.MethodOffset(args, table);
            object? origin = FrameworkApiUtil.ReadVector3(mod, args, offset, "physics.raycast(origin, direction [, distance, layerMask])");
            int directionIndex = args[offset].Type == DataType.Table ? offset + 1 : offset + 3;
            object? direction = FrameworkApiUtil.ReadVector3(mod, args, directionIndex, "physics.raycast(origin, direction [, distance, layerMask])");
            int optional = directionIndex + (args[directionIndex].Type == DataType.Table ? 1 : 3);
            double distance = args.Count > optional && !args[optional].IsNil()
                ? FrameworkApiUtil.RequireFiniteNumber(args, optional, "physics.raycast(..., distance)")
                : double.MaxValue;
            int layerMask = args.Count > optional + 1 && !args[optional + 1].IsNil()
                ? FrameworkApiUtil.RequireInt(args, optional + 1, "physics.raycast(..., layerMask)", int.MinValue, int.MaxValue)
                : -5; // Unity DefaultRaycastLayers
            if (origin == null || direction == null) return DynValue.Nil;

            Type? physicsType = ReflectionBridge.FindTypeExact("UnityEngine.Physics");
            Type? vectorType = ReflectionBridge.FindTypeExact("UnityEngine.Vector3");
            Type? hitType = ReflectionBridge.FindTypeExact("UnityEngine.RaycastHit");
            if (physicsType == null || vectorType == null || hitType == null) return DynValue.Nil;

            MethodInfo? method = physicsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "Raycast") return false;
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 5 && p[0].ParameterType == vectorType && p[1].ParameterType == vectorType &&
                           p[2].ParameterType.IsByRef && p[2].ParameterType.GetElementType() == hitType &&
                           p[3].ParameterType == typeof(float) && p[4].ParameterType == typeof(int);
                });
            if (method == null) return DynValue.Nil;

            object? hit = Activator.CreateInstance(hitType);
            object?[] call = { origin, direction, hit, (float)Math.Min(float.MaxValue, Math.Max(0.0, distance)), layerMask };
            try
            {
                if (method.Invoke(null, call) is not bool didHit || !didHit || call[2] == null)
                    return DynValue.Nil;
                return WrapHit(mod, call[2]!);
            }
            catch { return DynValue.Nil; }
        }

        private static DynValue WrapHit(LuaModInstance mod, object hit)
        {
            var table = new Table(mod.Script);
            foreach (string member in new[] { "point", "normal" })
                if (SleddersGameBindings.TryGetAny(hit, out object? value, member))
                    table.Set(member, ValueConverter.ToDynValue(mod, value));
            if (SleddersGameBindings.TryGetAny(hit, out object? distance, "distance") && SleddersGameBindings.ToDouble(distance) is double d)
                table.Set("distance", DynValue.NewNumber(d));
            if (SleddersGameBindings.TryGetAny(hit, out object? collider, "collider") && collider != null)
                table.Set("collider", SceneApi.Wrap(mod, collider));
            if (SleddersGameBindings.TryGetAny(hit, out object? rigidbody, "rigidbody") && rigidbody != null)
                table.Set("body", PhysicsBodyApi.Wrap(mod, rigidbody));
            return DynValue.NewTable(table);
        }

        private static DynValue OverlapSphere(LuaModInstance mod, Table table, CallbackArguments args)
        {
            int offset = FrameworkApiUtil.MethodOffset(args, table);
            object? center = FrameworkApiUtil.ReadVector3(mod, args, offset, "physics.overlapSphere(center, radius [, layerMask, max])");
            int radiusIndex = args[offset].Type == DataType.Table ? offset + 1 : offset + 3;
            double radius = FrameworkApiUtil.RequireFiniteNumber(args, radiusIndex, "physics.overlapSphere(center, radius [, layerMask, max])");
            int layerMask = args.Count > radiusIndex + 1 && !args[radiusIndex + 1].IsNil()
                ? FrameworkApiUtil.RequireInt(args, radiusIndex + 1, "physics.overlapSphere(..., layerMask)", int.MinValue, int.MaxValue)
                : -5;
            int max = args.Count > radiusIndex + 2 && !args[radiusIndex + 2].IsNil()
                ? FrameworkApiUtil.RequireInt(args, radiusIndex + 2, "physics.overlapSphere(..., max)", 1, 1024)
                : 256;
            if (center == null || radius < 0.0) return DynValue.NewTable(new Table(mod.Script));

            Type? physicsType = ReflectionBridge.FindTypeExact("UnityEngine.Physics");
            if (physicsType == null) return DynValue.NewTable(new Table(mod.Script));
            object? raw;
            try { raw = ReflectionBridge.CallStatic(physicsType, "OverlapSphere", new object?[] { center, (float)radius, layerMask }); }
            catch
            {
                try { raw = ReflectionBridge.CallStatic(physicsType, "OverlapSphere", new object?[] { center, (float)radius }); }
                catch { raw = null; }
            }
            var result = new Table(mod.Script);
            if (raw is IEnumerable values)
            {
                int i = 1;
                foreach (object? value in values)
                {
                    if (value == null) continue;
                    result.Set(i++, SceneApi.Wrap(mod, value));
                    if (i > max) break;
                }
            }
            return DynValue.NewTable(result);
        }

        private static DynValue IgnoreCollision(LuaModInstance mod, Table table, CallbackArguments args)
        {
            int offset = FrameworkApiUtil.MethodOffset(args, table);
            if (args.Count <= offset + 1 || args[offset].Type != DataType.Table || args[offset + 1].Type != DataType.Table)
                throw new ScriptRuntimeException("physics.ignoreCollision(colliderA, colliderB [, ignore]) expects two object wrappers.");
            object? a = ObjectProxyBuilder.DynToRaw(mod, args[offset]);
            object? b = ObjectProxyBuilder.DynToRaw(mod, args[offset + 1]);
            bool ignore = args.Count > offset + 2 && !args[offset + 2].IsNil()
                ? FrameworkApiUtil.RequireBool(args, offset + 2, "physics.ignoreCollision(colliderA, colliderB [, ignore])")
                : true;
            Type? physics = ReflectionBridge.FindTypeExact("UnityEngine.Physics");
            if (physics == null || a == null || b == null) return DynValue.False;
            try { ReflectionBridge.CallStatic(physics, "IgnoreCollision", new object?[] { a, b, ignore }); return DynValue.True; }
            catch { return DynValue.False; }
        }
    }
}
