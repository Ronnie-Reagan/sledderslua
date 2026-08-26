local sled = nil
local overlay = true

local function currentSled()
    if not sled or not sled.isValid() then
        sled = sledders.player.getSled()
    end
    return sled
end

function onLoad()
    print("Sledders Lua smoke test loaded")
    print("L / Ctrl+L: input matching")
    print("Ctrl+Shift+1: print sled state")
    print("Ctrl+Shift+2: local +10 m/s boost")
    print("Ctrl+Shift+3: teleport +5 m world Y")
    print("Ctrl+Shift+4: toggle HUD")
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
