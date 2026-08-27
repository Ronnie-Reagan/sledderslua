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

            DynValue getSled = DynValue.NewCallback((ctx, args) =>
            {
                object? sled = SleddersGameBindings.FindLocalSled();
                return sled == null ? DynValue.Nil : WrapSled(mod, sled);
            });
            player.Set("getSled", getSled);

            player.Set("hasSled", DynValue.NewCallback((ctx, args) =>
            {
                return DynValue.NewBoolean(SleddersGameBindings.FindLocalSled() != null);
            }));

            DynValue getPosition = DynValue.NewCallback((ctx, args) =>
            {
                object? playerObject = SleddersGameBindings.FindPlayerObject();
                object? value = playerObject == null ? null : SleddersGameBindings.GetPosition(playerObject);
                if (value == null)
                {
                    object? sled = SleddersGameBindings.FindLocalSled();
                    value = sled == null ? null : SleddersGameBindings.GetPosition(sled);
                }
                return ValueConverter.ToDynValue(mod, value);
            });
            player.Set("getPos", getPosition);

            DynValue getRotation = DynValue.NewCallback((ctx, args) =>
            {
                object? playerObject = SleddersGameBindings.FindPlayerObject();
                object? value = playerObject == null ? null : SleddersGameBindings.GetRotation(playerObject);
                if (value == null)
                {
                    object? sled = SleddersGameBindings.FindLocalSled();
                    value = sled == null ? null : SleddersGameBindings.GetRotation(sled);
                }
                return ValueConverter.ToDynValue(mod, value);
            });
            player.Set("getRot", getRotation);

            player.Set("getSpeed", DynValue.NewCallback((ctx, args) =>
            {
                object? sled = SleddersGameBindings.FindLocalSled();
                return NullableNumber(sled == null ? null : SleddersGameBindings.GetSpeed(sled));
            }));


            return player;
        }

        public static Table BuildSledService(LuaModInstance mod)
        {
            var service = new Table(mod.Script);

            service.Set("getAll", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, service);
                int max = args.Count > offset && args[offset].Type == DataType.Number
                    ? Math.Max(1, Math.Min(64, (int)args[offset].Number))
                    : 16;

                var result = new Table(mod.Script);
                int i = 1;
                foreach (object sled in SleddersGameBindings.FindLiveSleds(max))
                    result.Set(i++, WrapSled(mod, sled));
                return DynValue.NewTable(result);
            }));
            return service;
        }

        public static DynValue WrapSled(LuaModInstance mod, object sled)
        {
            int handle = mod.Handles.Add(sled);
            if (mod.TryGetCachedObject("sled", handle, out DynValue cached))
                return cached;

            var table = new Table(mod.Script);
            table.Set("__handle", DynValue.NewNumber(handle));

            table.Set("isValid", DynValue.NewCallback((ctx, args) =>
                DynValue.NewBoolean(SleddersGameBindings.IsValidSled(sled))));
            table.Set("getName", DynValue.NewCallback((ctx, args) =>
                DynValue.NewString(SleddersGameBindings.GetFriendlyName(sled))));

            DynValue getPosition = DynValue.NewCallback((ctx, args) =>
            {
                return ValueConverter.ToDynValue(mod, SleddersGameBindings.GetPosition(sled));
            });
            DynValue setPosition = DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                object? transform = SleddersGameBindings.GetTransform(sled);
                Type? targetType = transform == null ? null : SleddersGameBindings.ResolveMemberType(transform, "position");
                if (targetType == null) return DynValue.False;
                object? vector = ReadVectorArgument(mod, args, offset, targetType, "sled.setPos(vector3) or sled.setPos(x, y, z)");
                return DynValue.NewBoolean(vector != null && SleddersGameBindings.Teleport(sled, vector, preserveVelocity: false));
            });
            table.Set("getPos", getPosition);
            table.Set("setPos", setPosition);
            table.Set("teleport", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                object? transform = SleddersGameBindings.GetTransform(sled);
                Type? targetType = transform == null ? null : SleddersGameBindings.ResolveMemberType(transform, "position");
                if (targetType == null) return DynValue.False;
                object? vector = ReadVectorArgument(mod, args, offset, targetType, "sled.teleport(vector3) or sled.teleport(x, y, z)");
                return DynValue.NewBoolean(vector != null && SleddersGameBindings.Teleport(sled, vector, preserveVelocity: false));
            }));

            DynValue getRotation = DynValue.NewCallback((ctx, args) =>
            {
                return ValueConverter.ToDynValue(mod, SleddersGameBindings.GetRotation(sled));
            });
            table.Set("getRot", getRotation);

            // Sled velocity is local: +X right, +Y up, +Z forward.
            DynValue getVelocity = DynValue.NewCallback((ctx, args) =>
            {
                return ValueConverter.ToDynValue(mod, SleddersGameBindings.GetLocalVelocity(sled));
            });
            DynValue getWorldVelocity = DynValue.NewCallback((ctx, args) =>
            {
                return ValueConverter.ToDynValue(mod, SleddersGameBindings.GetVelocity(sled));
            });
            DynValue setVelocity = DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                object? body = SleddersGameBindings.GetRigidbody(sled);
                Type? targetType = body == null
                    ? null
                    : SleddersGameBindings.ResolveMemberType(body, "linearVelocity") ?? SleddersGameBindings.ResolveMemberType(body, "velocity");
                if (targetType == null) return DynValue.False;
                object? vector = ReadVectorArgument(mod, args, offset, targetType, "sled.setVel(vector3) or sled.setVel(x, y, z)");
                return DynValue.NewBoolean(vector != null && SleddersGameBindings.SetLocalVelocity(sled, vector));
            });
            DynValue setWorldVelocity = DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                object? body = SleddersGameBindings.GetRigidbody(sled);
                Type? targetType = body == null
                    ? null
                    : SleddersGameBindings.ResolveMemberType(body, "linearVelocity") ?? SleddersGameBindings.ResolveMemberType(body, "velocity");
                if (targetType == null) return DynValue.False;
                object? vector = ReadVectorArgument(mod, args, offset, targetType, "sled.setWorldVel(vector3) or sled.setWorldVel(x, y, z)");
                return DynValue.NewBoolean(vector != null && SleddersGameBindings.SetVelocity(sled, vector));
            });
            DynValue addVelocity = DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                object? body = SleddersGameBindings.GetRigidbody(sled);
                Type? targetType = body == null
                    ? null
                    : SleddersGameBindings.ResolveMemberType(body, "linearVelocity") ?? SleddersGameBindings.ResolveMemberType(body, "velocity");
                if (targetType == null) return DynValue.False;
                object? vector = ReadVectorArgument(mod, args, offset, targetType, "sled.addVel(vector3) or sled.addVel(x, y, z)");
                return DynValue.NewBoolean(vector != null && SleddersGameBindings.AddLocalVelocity(sled, vector));
            });
            DynValue addWorldVelocity = DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                object? body = SleddersGameBindings.GetRigidbody(sled);
                Type? targetType = body == null
                    ? null
                    : SleddersGameBindings.ResolveMemberType(body, "linearVelocity") ?? SleddersGameBindings.ResolveMemberType(body, "velocity");
                if (targetType == null) return DynValue.False;
                object? vector = ReadVectorArgument(mod, args, offset, targetType, "sled.addWorldVel(vector3) or sled.addWorldVel(x, y, z)");
                return DynValue.NewBoolean(vector != null && SleddersGameBindings.AddVelocity(sled, vector));
            });
            table.Set("getVel", getVelocity);
            table.Set("getWorldVel", getWorldVelocity);
            table.Set("setVel", setVelocity);
            table.Set("setWorldVel", setWorldVelocity);
            table.Set("addVel", addVelocity);
            table.Set("addWorldVel", addWorldVelocity);
            table.Set("getForwardSpeed", DynValue.NewCallback((ctx, args) => NullableNumber(SleddersGameBindings.GetForwardSpeed(sled))));

            table.Set("getSpeed", DynValue.NewCallback((ctx, args) => NullableNumber(SleddersGameBindings.GetSpeed(sled))));
            table.Set("getMass", DynValue.NewCallback((ctx, args) => NullableNumber(SleddersGameBindings.GetMass(sled))));
            table.Set("setMass", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                return DynValue.NewBoolean(SleddersGameBindings.SetMass(sled, RequireNumber(args, offset, "sled.setMass(mass)")));
            }));
            table.Set("addForce", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                Type? vectorType = ReflectionBridge.FindTypeExact("UnityEngine.Vector3");
                object? vector = vectorType == null ? null : ReadVectorArgument(mod, args, offset, vectorType, "sled.addForce(vector3) or sled.addForce(x, y, z)");
                return DynValue.NewBoolean(vector != null && SleddersGameBindings.AddLocalForce(sled, vector));
            }));
            table.Set("addWorldForce", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                Type? vectorType = ReflectionBridge.FindTypeExact("UnityEngine.Vector3");
                object? vector = vectorType == null ? null : ReadVectorArgument(mod, args, offset, vectorType, "sled.addWorldForce(vector3) or sled.addWorldForce(x, y, z)");
                return DynValue.NewBoolean(vector != null && SleddersGameBindings.AddForce(sled, vector));
            }));

            DynValue getHeadlights = DynValue.NewCallback((ctx, args) => NullableBool(SleddersGameBindings.AreHeadlightsOn(sled)));
            DynValue setHeadlights = DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                bool enabled = RequireBool(args, offset, "sled.setHeadlights(enabled)");
                return DynValue.NewBoolean(SleddersGameBindings.SetHeadlights(sled, enabled));
            });
            table.Set("getHeadlights", getHeadlights);
            table.Set("setHeadlights", setHeadlights);
            table.Set("toggleHeadlights", DynValue.NewCallback((ctx, args) =>
            {
                return DynValue.NewBoolean(SleddersGameBindings.ToggleHeadlights(sled));
            }));
            table.Set("forceHeadlights", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                bool enabled = RequireBool(args, offset, "sled.forceHeadlights(enabled)");
                return DynValue.NewBoolean(SleddersGameBindings.ForceHeadlights(sled, enabled, mod.Manifest.Id));
            }));
            table.Set("areHeadlightsForced", DynValue.NewCallback((ctx, args) =>
                DynValue.NewBoolean(SleddersGameBindings.IsHeadlightControlled(sled, mod.Manifest.Id))));
            table.Set("releaseHeadlights", DynValue.NewCallback((ctx, args) =>
            {
                return DynValue.NewBoolean(SleddersGameBindings.ReleaseHeadlights(sled, mod.Manifest.Id));
            }));

            table.Set("getRPM", DynValue.NewCallback((ctx, args) => NullableNumber(SleddersGameBindings.GetRpm(sled))));
            table.Set("getThrottle", DynValue.NewCallback((ctx, args) => NullableNumber(SleddersGameBindings.GetThrottle(sled))));
            table.Set("isEngineOn", DynValue.NewCallback((ctx, args) => NullableBool(SleddersGameBindings.IsEngineOn(sled))));
            table.Set("setEngineOn", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                return DynValue.NewBoolean(SleddersGameBindings.SetEngineRunning(sled, RequireBool(args, offset, "sled.setEngineOn(enabled)")));
            }));
            table.Set("startEngine", DynValue.NewCallback((ctx, args) =>
            {
                return DynValue.NewBoolean(SleddersGameBindings.SetEngineRunning(sled, true));
            }));
            table.Set("stopEngine", DynValue.NewCallback((ctx, args) =>
            {
                return DynValue.NewBoolean(SleddersGameBindings.SetEngineRunning(sled, false));
            }));

            table.Set("getFuel", DynValue.NewCallback((ctx, args) => NullableNumber(SleddersGameBindings.GetFuel(sled))));
            table.Set("getFuelCapacity", DynValue.NewCallback((ctx, args) => NullableNumber(SleddersGameBindings.GetFuelCapacity(sled))));
            table.Set("getFuelPercent", DynValue.NewCallback((ctx, args) => NullableNumber(SleddersGameBindings.GetFuelPercent(sled))));
            table.Set("setFuel", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                return DynValue.NewBoolean(SleddersGameBindings.SetFuel(sled, RequireNumber(args, offset, "sled.setFuel(litres)")));
            }));
            table.Set("addFuel", DynValue.NewCallback((ctx, args) =>
            {
                int offset = MethodOffset(args, table);
                return DynValue.NewBoolean(SleddersGameBindings.AddFuel(
                    sled,
                    RequireNumber(args, offset, "sled.addFuel(litres)")));
            }));
            table.Set("fillFuel", DynValue.NewCallback((ctx, args) =>
            {
                double? capacity = SleddersGameBindings.GetFuelCapacity(sled);
                return DynValue.NewBoolean(capacity.HasValue && SleddersGameBindings.SetFuel(sled, capacity.Value));
            }));
            table.Set("isFuelEmpty", DynValue.NewCallback((ctx, args) =>
            {
                double? amount = SleddersGameBindings.GetFuel(sled);
                return amount.HasValue ? DynValue.NewBoolean(amount.Value <= 0.0001) : DynValue.Nil;
            }));

            DynValue wrapped = DynValue.NewTable(table);
            mod.CacheObject("sled", handle, wrapped);
            return wrapped;
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
                double fov = RequireNumber(args, offset, "camera.setFov(number)");
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
