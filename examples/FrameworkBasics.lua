-- FrameworkBasics.lua
-- Common reads/writes without using reflection.

local function sled()
    return sledders.player.getSled()
end

function onLoad()
    print("Framework basics loaded; press H for headlights, R to refill, B for boost")
end

sledders.input.onPressed("h", function()
    local s = sled()
    if s then s.toggleHeadlights() end
end)

sledders.input.onPressed("r", function()
    local s = sled()
    if s then s.fillFuel() end
end)

sledders.input.onPressed("b", function()
    local s = sled()
    if s then s.addVel(0, 0, 8) end
end)

function onTick(dt)
    local s = sled()
    if s and sledders.input.wasPressed("f8") then
        print(string.format("speed=%.1f m/s rpm=%.0f fuel=%.1f L", s.getSpeed() or 0, s.getRpm() or 0, s.getFuel() or 0))
    end
end
