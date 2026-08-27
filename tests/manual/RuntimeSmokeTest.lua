local cachedSled = nil
local overlay = true

local function currentSled()
    if not cachedSled or not cachedSled.isValid() then
        cachedSled = sledders.player.getSled()
    end
    return cachedSled
end

local function isVector3(value)
    return type(value) == "table"
        and type(value.x) == "number"
        and type(value.y) == "number"
        and type(value.z) == "number"
end

local function safeIdentity(getter, setter)
    local value = getter()
    if value == nil then return false, "getter returned nil" end
    return setter(value) == true
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

    check("API 3.2 metadata/capabilities", function()
        return sledders.api.version == "3.2"
            and type(sledders.api.runtimeVersion) == "string"
            and type(sledders.api.has) == "function"
            and sledders.api.has("sled")
            and sledders.api.has("sled.vehicle")
            and sledders.api.has("sled.tuning")
            and sledders.api.has("hud")
            and sledders.api.has("camera.projection")
            and sledders.api.has("audio.sources")
            and sledders.api.has("world")
            and sledders.api.has("scene.objects")
            and sledders.api.has("physics.queries")
            and sledders.api.has("assets.bundles")
            and sledders.api.has("input.native")
    end)

    check("game/time/window", function()
        local resolution = sledders.window.getResolution()
        return type(sledders.game.getVersion()) == "string"
            and type(sledders.game.getScene()) == "string"
            and type(sledders.time.getDelta()) == "number"
            and type(sledders.time.getFixedDelta()) == "number"
            and (sledders.time.getFps() == nil or type(sledders.time.getFps()) == "number")
            and type(sledders.time.getUptime()) == "number"
            and type(resolution) == "table"
            and resolution.width > 0
            and resolution.height > 0
    end)

    check("screen/value constructors", function()
        local v = sledders.vector3(1, 2, 3)
        local c = sledders.color(255, 128, 0, 255)
        return isVector3(v)
            and type(c) == "table"
            and type(c.r) == "number"
            and type(c.g) == "number"
            and type(c.b) == "number"
            and type(c.a) == "number"
            and type(sledders.screen.getWidth()) == "number"
            and type(sledders.screen.getHeight()) == "number"
    end)

    local s = currentSled()
    check("local sled discovery", function()
        return sledders.player.hasSled() == true and s ~= nil and s.isValid() == true
    end)

    if not s then
        print(string.format("Safe API sweep: %d/%d passed (no local sled for remaining checks)", passed, total))
        return
    end

    check("player transform", function()
        local pos = sledders.player.getPos()
        local rot = sledders.player.getRot()
        local transform = sledders.player.getTransform()
        return isVector3(pos)
            and isVector3(rot)
            and transform ~= nil
            and transform.isValid()
            and sledders.player.setPos(pos)
            and sledders.player.setRot(rot)
    end)

    check("sled telemetry", function()
        return type(s.getName()) == "string"
            and isVector3(s.getPos())
            and isVector3(s.getRot())
            and isVector3(s.getVel())
            and isVector3(s.getWorldVel())
            and isVector3(s.getAngularVel())
            and type(s.getForwardSpeed()) == "number"
            and type(s.getSpeed()) == "number"
            and type(s.getMass()) == "number"
            and type(s.getRpm()) == "number"
            and type(s.getThrottle()) == "number"
            and type(s.getFuel()) == "number"
            and type(s.getFuelCapacity()) == "number"
    end)

    check("sled identity writes", function()
        local pos, rot = s.getPos(), s.getRot()
        local localVel, worldVel, angular = s.getVel(), s.getWorldVel(), s.getAngularVel()
        local mass = s.getMass()
        return s.setPos(pos)
            and s.setRot(rot)
            and s.setVel(localVel)
            and s.setWorldVel(worldVel)
            and s.setAngularVel(angular)
            and s.setMass(mass)
            and s.addForce(0, 0, 0)
            and s.addWorldForce(0, 0, 0)
            and s.addTorque(0, 0, 0)
            and s.addWorldTorque(0, 0, 0)
    end)

    check("vehicle definition", function()
        local vehicle = s.getVehicle()
        if not vehicle or not vehicle.isValid() then return false, "vehicle unavailable" end
        local hp = vehicle.getHorsepower()
        local weight = vehicle.getWeight()
        local rpm = vehicle.getMaxRpm()
        return type(hp) == "number"
            and type(weight) == "number"
            and type(rpm) == "number"
            and vehicle.setHorsepower(hp)
            and vehicle.setWeight(weight)
            and vehicle.setMaxRpm(rpm)
            and #vehicle.keys() > 10
    end)

    check("body wrapper", function()
        local body = s.getBody()
        if not body or not body.isValid() then return false, "body unavailable" end
        local mass = body.getMass()
        local velocity = body.getVelocity()
        return type(mass) == "number"
            and isVector3(velocity)
            and body.setMass(mass)
            and body.setVelocity(velocity)
            and body.addForce(sledders.vector3(0, 0, 0))
            and body.addTorque(sledders.vector3(0, 0, 0))
    end)

    check("tuning wrappers", function()
        local tuning = s.getTuning()
        local controller = tuning and tuning.getController()
        if not controller then return false, "controller tuning unavailable" end
        local keys = controller.keys()
        return type(keys) == "table"
            and #keys >= 10
            and controller.isWritable("clutchRpmMin") == true
            and type(controller.get("clutchRpmMin")) == "number"
    end)

    check("structure/visual wrappers", function()
        local structure = s.getStructure()
        if not structure then return false, "structure unavailable" end
        local groups = structure.groups()
        local renderers = s.getRenderers("hood")
        if type(groups) ~= "table" or type(renderers) ~= "table" then return false end
        if #renderers > 0 then
            local r = renderers[1]
            return r.isValid() == true and type(r.getEnabled()) == "boolean" and type(r.getMaterials()) == "table"
        end
        return true
    end)

    check("headlight lifecycle", function()
        local state = s.getHeadlights()
        if type(state) ~= "boolean" then return false, "headlight state unavailable" end
        if not s.setHeadlights(state) then return false, "identity setter failed" end
        if not s.forceHeadlights(state) then return false, "force failed" end
        if s.areHeadlightsForced() ~= true then return false, "override not registered" end
        return s.releaseHeadlights() == true
    end)

    check("engine/fuel identity", function()
        local running = s.isEngineOn()
        local fuel = s.getFuel()
        return type(running) == "boolean"
            and type(fuel) == "number"
            and s.setEngineOn(running)
            and s.setFuel(fuel)
            and s.addFuel(0)
    end)

    check("camera/projection", function()
        local fov = sledders.camera.getFov()
        local screen = sledders.camera.worldToScreen(s.getPos())
        local ray = sledders.camera.screenPointToRay(sledders.vector3(sledders.window.getWidth() / 2, sledders.window.getHeight() / 2, 0))
        return type(fov) == "number"
            and sledders.camera.setFov(fov)
            and isVector3(sledders.camera.getPos())
            and isVector3(sledders.camera.getRot())
            and isVector3(screen)
            and type(ray) == "table"
            and isVector3(ray.origin)
            and isVector3(ray.direction)
    end)

    check("HUD discovery", function()
        local elements = sledders.hud.elements()
        local speed = sledders.hud.get("speedMeter")
        return type(elements) == "table"
            and #elements > 5
            and (speed == nil or speed.isValid() == true)
    end)

    check("native input discovery", function()
        local actions = sledders.input.native.actions(128)
        if type(actions) ~= "table" then return false end
        if #actions == 0 then return true end
        local a = actions[1]
        return a.isValid() == true and type(a.getName()) == "string"
    end)

    check("world reads / identity setters", function()
        local condition = sledders.world.snow.getCondition()
        local hardness = sledders.world.snow.getHardness()
        local time = sledders.world.time.getTimeOfDay()
        return type(condition) == "string"
            and type(hardness) == "number"
            and type(time) == "number"
            and sledders.world.snow.setCondition(condition)
            and sledders.world.snow.setHardness(hardness)
            and sledders.world.time.setTimeOfDay(time)
            and type(sledders.world.weather.names()) == "table"
    end)

    check("audio", function()
        local volume = sledders.audio.getVolume()
        local sources = sledders.audio.getSources(64)
        return type(volume) == "number"
            and sledders.audio.setVolume(volume)
            and type(sources) == "table"
            and type(sledders.audio.nativeSfx.names()) == "table"
            and type(sledders.audio.presets.names()) == "table"
    end)

    check("scene/physics services", function()
        local sceneSled = sledders.scene.getLocalSled()
        local gravity = sledders.physics.getGravity()
        local hit = sledders.physics.raycast(s.getPos(), sledders.vector3(0, -1, 0), 100)
        return sceneSled ~= nil
            and sceneSled.isValid()
            and isVector3(gravity)
            and sledders.physics.setGravity(gravity)
            and (hit == nil or type(hit) == "table")
    end)

    check("storage round trip", function()
        local key = "__runtime_smoke_test"
        local value = { ok = true, pos = sledders.vector3(1, 2, 3), values = { 1, 2, 3 } }
        if not sledders.storage.set(key, value) then return false, "set failed" end
        local round = sledders.storage.get(key)
        if type(round) ~= "table" or round.ok ~= true or not isVector3(round.pos) then return false, "roundtrip mismatch" end
        if not sledders.storage.delete(key) then return false, "delete failed" end
        return sledders.storage.save() == true
    end)

    print(string.format("Safe API sweep: %d/%d passed", passed, total))
end

function onLoad()
    print("Sledders Lua API 3.2 smoke test loaded")
    print("Ctrl+Shift+1: print local sled/framework state")
    print("Ctrl+Shift+2: local +10 m/s boost")
    print("Ctrl+Shift+3: teleport +5 m world Y")
    print("Ctrl+Shift+4: toggle smoke overlay")
    print("Ctrl+Shift+5: run safe API 3.2 sweep")
end

function onSledChanged()
    cachedSled = sledders.player.getSled()
end

function onDraw()
    local s = currentSled()
    if not overlay or not s then return end
    sledders.screen.setColor(0, 0, 0, 180)
    sledders.screen.rectangle(18, 18, 310, 82)
    sledders.screen.setColor(255, 255, 255)
    sledders.screen.print(s.getName(), 28, 27)
    sledders.screen.print(string.format("Speed %.1f m/s", s.getSpeed() or 0), 28, 49)
    sledders.screen.print(string.format("RPM %.0f | Fuel %.1f L", s.getRpm() or 0, s.getFuel() or 0), 28, 71)
    sledders.screen.resetColor()
end

sledders.input.onPressed("ctrl+shift+1", function()
    local s = currentSled()
    if not s then print("No local sled") return end
    local vehicle = s.getVehicle()
    print("Sled: " .. tostring(s.getName()))
    print("Position: " .. tostring(s.getPos()))
    print("Local velocity: " .. tostring(s.getVel()))
    print("Fuel: " .. tostring(s.getFuel()) .. " / " .. tostring(s.getFuelCapacity()))
    print("RPM: " .. tostring(s.getRpm()))
    print("Horsepower: " .. tostring(vehicle and vehicle.getHorsepower()))
    print("Weather: " .. tostring(sledders.world.weather.getName()))
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

sledders.input.onPressed("l", function()
    print("plain L")
end)

sledders.input.onPressed("ctrl+l", function()
    print("Ctrl+L")
end)
