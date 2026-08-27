local function runDevSweep()
    local passed = 0
    local total = 0

    local function check(name, fn)
        total = total + 1
        local ok, result, detail = pcall(fn)
        if ok and result == true then
            passed = passed + 1
            print("[PASS] " .. name)
        else
            local reason = ok and detail or result
            print("[FAIL] " .. name .. (reason ~= nil and (" - " .. tostring(reason)) or ""))
        end
    end

    local timeType = nil
    check("resolve UnityEngine.Time", function()
        timeType = sledders.dev.type("UnityEngine.Time")
        return type(timeType) == "table"
            and timeType.fullName == "UnityEngine.Time"
    end)

    check("inspect static Time members", function()
        if not timeType then return false, "Time type was not resolved" end
        local members = timeType.members("time", 32)
        return type(members) == "table" and #members > 0
    end)

    check("read Time.timeScale", function()
        if not timeType then return false, "Time type was not resolved" end
        return type(timeType.get("timeScale")) == "number"
    end)

    check("discover camera types", function()
        local types = sledders.dev.types("Camera", 8)
        return type(types) == "table" and #types > 0
    end)

    check("discover camera objects", function()
        local objects = sledders.dev.objects("Camera", 8)
        if type(objects) ~= "table" or #objects == 0 then
            return false, "no live camera objects found"
        end

        local camera = objects[1]
        if type(camera.typeName()) ~= "string" then
            return false, "camera typeName failed"
        end

        local members = camera.members("fieldOfView")
        if type(members) ~= "table" then
            return false, "camera members failed"
        end

        local dump = camera.dump("fieldOfView")
        return type(dump) == "string" and #dump > 0
    end)

    check("inspect local sled mass sources", function()
        local objects = sledders.dev.objects("SnowmobileController", 16)
        if type(objects) ~= "table" then
            return false, "SnowmobileController discovery failed"
        end

        local localController = nil
        for _, object in ipairs(objects) do
            if object.typeName() == "SnowmobileController" then
                localController = object
                break
            end
        end
        if not localController then
            return false, "no exact SnowmobileController object found"
        end

        local controllerBase = localController.get("controllerBase")
        if type(controllerBase) ~= "table" then
            return false, "controllerBase unavailable"
        end

        local mainBody = controllerBase.get("mainBody")
        if type(mainBody) ~= "table" then
            return false, "mainBody unavailable"
        end

        local bodyMass = mainBody.get("mass")
        local vehicle = localController.call("get_Vehicle")
        if type(vehicle) ~= "table" then
            return false, "vehicle definition unavailable"
        end

        local vehicleWeight = vehicle.get("weight")
        if type(bodyMass) ~= "number" or type(vehicleWeight) ~= "number" then
            return false, "mass values were not numeric"
        end

        print(string.format(
            "[PROBE] mass: mainBody.mass=%.3f kg, vehicle.weight=%.3f kg",
            bodyMass,
            vehicleWeight))
        return true
    end)

    check("inspect teleport binding shapes", function()
        local controllerType = sledders.dev.type("Controller")
        local respawnableType = sledders.dev.type("Respawnable")
        if type(controllerType) ~= "table" or type(respawnableType) ~= "table" then
            return false, "Controller or Respawnable type unavailable"
        end

        local requestMembers = controllerType.members("RequestGamePositionChange", 16)
        local respawnMembers = respawnableType.members("Respawn", 32)
        if type(requestMembers) ~= "table" or #requestMembers == 0 then
            return false, "RequestGamePositionChange not found"
        end
        if type(respawnMembers) ~= "table" or #respawnMembers == 0 then
            return false, "Respawn overloads not found"
        end

        print("[PROBE] teleport: RequestGamePositionChange members=" .. tostring(#requestMembers)
            .. ", Respawn members=" .. tostring(#respawnMembers))
        return true
    end)

    print(string.format("Dev reflection sweep: %d/%d passed", passed, total))
end

function onLoad()
    print("Runtime dev smoke test loaded")
    print("Ctrl+Shift+9: run read-only dev reflection sweep")
end

sledders.input.onPressed("ctrl+shift+9", runDevSweep)
