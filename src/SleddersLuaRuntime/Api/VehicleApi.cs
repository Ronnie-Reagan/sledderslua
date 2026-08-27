using System.Collections.Generic;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class VehicleApi
    {
        private static readonly IReadOnlyList<SemanticProperty> Properties = new[]
        {
            new SemanticProperty("name", true, "displayName"),
            new SemanticProperty("description", true, "snowmobileDescription", "description"),
            new SemanticProperty("prefabName", true, "prefabName"),
            new SemanticProperty("group", true, "group"),
            new SemanticProperty("category", true, "category"),
            new SemanticProperty("locked", true, "isLocked"),
            new SemanticProperty("turbo", true, "isTurboOn"),
            new SemanticProperty("engineText", true, "engineText"),
            new SemanticProperty("skiStance", true, "skiStance"),
            new SemanticProperty("lengthName", true, "lengthName"),
            new SemanticProperty("lengthIndex", true, "lenghtIndex"),
            new SemanticProperty("lugHeight", true, "lugHeight"),
            new SemanticProperty("friction", true, "coefficientOfFriction"),
            new SemanticProperty("weight", true, "weight"),
            new SemanticProperty("powerFactor", true, "powerFactor"),
            new SemanticProperty("horsepower", true, "horsePower"),
            new SemanticProperty("maxRpm", true, "maxRpm"),
            new SemanticProperty("fuelCapacity", true, "fuelCapacity"),
            new SemanticProperty("fuelConsumption", true, "fuelConsumption"),
            new SemanticProperty("skiDistanceOffset", true, "skisXDistanceOffset"),
            new SemanticProperty("centerOfMassOffset", true, "centerOfMassOffset"),
            new SemanticProperty("driverCenterOfMassOffset", true, "driverCenterOfMassOffset"),
            new SemanticProperty("engineAudioType", true, "engineAudioType"),
            new SemanticProperty("hasBarpads", true, "hasBarpads"),
            new SemanticProperty("hasHandleGuards", true, "hasHandleGuards"),
            new SemanticProperty("hasWindshield", true, "hasWindshield"),
            new SemanticProperty("hasSnowFlaps", true, "hasSnowFlaps"),
            new SemanticProperty("hasHighHandlebar", true, "hasHighHandleBar"),
            new SemanticProperty("hasDefaultHandleGuards", true, "hasDefaultHandleGuards"),
            new SemanticProperty("hasRemovableRearParts", true, "hasRemovableRearParts"),
            new SemanticProperty("hasTunnelAccessories", true, "hasTunnelAccessories"),
            new SemanticProperty("canChangeSkis", true, "canChangeSkis"),
            new SemanticProperty("canChangeBumpers", true, "canChangeBumpers"),
            new SemanticProperty("canChangeSpindle", true, "canChangeSpindle"),
            new SemanticProperty("canChangeHandlebar", true, "canChangeHandleBar", "canChangeHandlebar")
        };

        public static DynValue Wrap(LuaModInstance mod, object vehicle)
        {
            DynValue bag = SemanticPropertyBag.Wrap(mod, vehicle, "vehicle", Properties);
            Table table = bag.Table;

            Add(table, mod, bag, "name", "Name");
            Add(table, mod, bag, "description", "Description");
            Add(table, mod, bag, "weight", "Weight");
            Add(table, mod, bag, "horsepower", "Horsepower");
            Add(table, mod, bag, "maxRpm", "MaxRpm");
            Add(table, mod, bag, "fuelCapacity", "FuelCapacity");
            Add(table, mod, bag, "fuelConsumption", "FuelConsumption");
            Add(table, mod, bag, "friction", "Friction");
            Add(table, mod, bag, "lugHeight", "LugHeight");
            Add(table, mod, bag, "skiStance", "SkiStance");
            Add(table, mod, bag, "powerFactor", "PowerFactor");
            Add(table, mod, bag, "turbo", "Turbo");
            Add(table, mod, bag, "centerOfMassOffset", "CenterOfMassOffset");
            Add(table, mod, bag, "driverCenterOfMassOffset", "DriverCenterOfMassOffset");
            Add(table, mod, bag, "engineAudioType", "EngineAudioType");
            return bag;
        }

        private static void Add(Table table, LuaModInstance mod, DynValue bag, string key, string stem)
            => SemanticPropertyBag.AddNamedAccessors(table, mod, bag, key, stem);
    }
}
