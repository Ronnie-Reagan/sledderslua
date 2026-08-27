using System;

namespace SleddersLuaRuntime.Api
{
    internal static partial class SleddersGameBindings
    {
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
    }
}
