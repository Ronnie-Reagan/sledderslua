local sled = nil
local overlay = true

local function currentSled()
    if not sled or not sled.isValid() then
        sled = sledders.player.getSled()
    end
    return sled
end

local function isVector3(value)
    return type(value) == "table"
        and type(value.x) == "number"
        and type(value.y) == "number"
        and type(value.z) == "number"
end

local function runSafeApiSweep()
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

    check("api metadata", function()
        return type(sledders.api.version) == "string"
            and type(sledders.api.runtimeVersion) == "string"
            and type(sledders.mod.id) == "string"
    end)

    check("game metadata", function()
        return type(sledders.game.getVersion()) == "string"
            and type(sledders.game.getScene()) == "string"
    end)

    check("time", function()
        return type(sledders.time.getDelta()) == "number"
            and type(sledders.time.getFixedDelta()) == "number"
            and type(sledders.time.getDeltaMs()) == "number"
            and type(sledders.time.getFixedDeltaMs()) == "number"
            and type(sledders.time.getFPS()) == "number"
            and type(sledders.time.getUptime()) == "number"
    end)

    check("window", function()
        local resolution = sledders.window.getResolution()
        return sledders.window.getWidth() > 0
            and sledders.window.getHeight() > 0
            and type(sledders.window.isFocused()) == "boolean"
            and type(resolution) == "table"
            and resolution.width > 0
            and resolution.height > 0
    end)

    check("screen availability", function()
        return sledders.screen.isAvailable() == true
            and sledders.screen.getWidth() > 0
            and sledders.screen.getHeight() > 0
    end)

    check("value constructors", function()
        local v = sledders.vector3(1, 2, 3)
        local c = sledders.color(255, 128, 0, 255)
        return isVector3(v)
            and type(c) == "table"
            and type(c.r) == "number"
            and type(c.g) == "number"
            and type(c.b) == "number"
            and type(c.a) == "number"
    end)

    local s = currentSled()
    check("player sled discovery", function()
        return sledders.player.hasSled() == true and s ~= nil and s.isValid()
    end)

    if not s then
        print(string.format("Safe API sweep: %d/%d passed (no local sled for sled-specific checks)", passed, total))
        return
    end

    check("player state", function()
        return isVector3(sledders.player.getPos())
            and type(sledders.player.getRot()) == "table"
            and type(sledders.player.getSpeed()) == "number"
    end)

    check("sled service", function()
        local all = sledders.sled.getAll(16)
        return type(all) == "table" and #all >= 1
    end)

    check("sled state reads", function()
        return type(s.getName()) == "string"
            and isVector3(s.getPos())
            and type(s.getRot()) == "table"
            and isVector3(s.getVel())
            and isVector3(s.getWorldVel())
            and type(s.getForwardSpeed()) == "number"
            and type(s.getSpeed()) == "number"
            and type(s.getMass()) == "number"
            and type(s.getRPM()) == "number"
            and type(s.getThrottle()) == "number"
    end)

    check("velocity identity writes", function()
        local localVel = s.getVel()
        local worldVel = s.getWorldVel()
        return isVector3(localVel)
            and isVector3(worldVel)
            and s.setVel(localVel) == true
            and s.setWorldVel(worldVel) == true
            and s.addVel(0, 0, 0) == true
            and s.addWorldVel(0, 0, 0) == true
    end)

    check("zero-force writes", function()
        return s.addForce(0, 0, 0) == true
            and s.addWorldForce(0, 0, 0) == true
    end)

    check("mass identity write", function()
        local mass = s.getMass()
        return type(mass) == "number" and mass > 0 and s.setMass(mass) == true
    end)

    check("headlights identity/override lifecycle", function()
        local lights = s.getHeadlights()
        if type(lights) ~= "boolean" then return false, "getHeadlights returned nil" end
        if s.setHeadlights(lights) ~= true then return false, "setHeadlights failed" end
        if s.forceHeadlights(lights) ~= true then return false, "forceHeadlights failed" end
        if s.areHeadlightsForced() ~= true then return false, "override not registered" end
        return s.releaseHeadlights() == true
    end)

    check("engine identity write", function()
        local running = s.isEngineOn()
        return type(running) == "boolean" and s.setEngineOn(running) == true
    end)

    check("fuel reads/identity writes", function()
        local fuel = s.getFuel()
        local capacity = s.getFuelCapacity()
        local percent = s.getFuelPercent()
        return type(fuel) == "number"
            and type(capacity) == "number"
            and capacity > 0
            and type(percent) == "number"
            and s.setFuel(fuel) == true
            and s.addFuel(0) == true
            and type(s.isFuelEmpty()) == "boolean"
    end)

    check("camera reads/identity write", function()
        local fov = sledders.camera.getFov()
        return type(fov) == "number"
            and isVector3(sledders.camera.getPos())
            and type(sledders.camera.getRot()) == "table"
            and sledders.camera.setFov(fov) == true
    end)

    check("audio identity write", function()
        local volume = sledders.audio.getVolume()
        return type(volume) == "number" and sledders.audio.setVolume(volume) == true
    end)

    check("storage round trip", function()
        local key = "__runtime_smoke_test"
        if sledders.storage.set(key, "ok") ~= true then return false, "set failed" end
        if sledders.storage.get(key) ~= "ok" then return false, "get mismatch" end
        if sledders.storage.delete(key) ~= true then return false, "delete failed" end
        return sledders.storage.save() == true
    end)

    print(string.format("Safe API sweep: %d/%d passed", passed, total))
end

function onLoad()
    print("Sledders Lua smoke test loaded")
    print("L / Ctrl+L: input matching")
    print("Ctrl+Shift+1: print sled state")
    print("Ctrl+Shift+2: local +10 m/s boost")
    print("Ctrl+Shift+3: teleport +5 m world Y")
    print("Ctrl+Shift+4: toggle HUD")
    print("Ctrl+Shift+5: safe API sweep")
end

function onSledChanged()
    sled = sledders.player.getSled()
end

function onDraw()
    local s = currentSled()
    if not overlay or not s then return end
    sledders.screen.setColor(0, 0, 0, 180)
    sledders.screen.rectangle(18, 18, 280, 64)
    sledders.screen.setColor(255, 255, 255)
    sledders.screen.print(s.getName(), 28, 27)
    sledders.screen.print(string.format("Speed %.1f m/s", s.getSpeed() or 0), 28, 49)
    sledders.screen.resetColor()
end

sledders.input.onPressed("l", function()
    print("plain L")
end)

sledders.input.onPressed("ctrl+l", function()
    print("Ctrl+L")
end)

sledders.input.onPressed("ctrl+shift+1", function()
    local s = currentSled()
    if not s then
        print("No local sled")
        return
    end
    print("Sled: " .. s.getName())
    print("Position: " .. tostring(s.getPos()))
    print("Local velocity: " .. tostring(s.getVel()))
    print("World velocity: " .. tostring(s.getWorldVel()))
    print("Fuel: " .. tostring(s.getFuel()) .. " / " .. tostring(s.getFuelCapacity()))
    print("RPM: " .. tostring(s.getRPM()))
    print("Headlights: " .. tostring(s.getHeadlights()))
end)

sledders.input.onPressed("ctrl+shift+2", function()
    local s = currentSled()
    if s then s.addVel(0, 0, 10) end
end)

sledders.input.onPressed("ctrl+shift+3", function()
    local s = currentSled()
    if not s then return end
    local p = s.getPos()
    if p then s.teleport(p.x, p.y + 5, p.z) end
end)

sledders.input.onPressed("ctrl+shift+4", function()
    overlay = not overlay
end)

sledders.input.onPressed("ctrl+shift+5", runSafeApiSweep)
