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

    print(string.format("Dev reflection sweep: %d/%d passed", passed, total))
end

function onLoad()
    print("Runtime dev smoke test loaded")
    print("Ctrl+Shift+9: run read-only dev reflection sweep")
end

sledders.input.onPressed("ctrl+shift+9", runDevSweep)
