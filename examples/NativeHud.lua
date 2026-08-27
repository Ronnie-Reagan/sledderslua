-- NativeHud.lua
-- Hide one native HUD element without replacing the entire HUD.

local hidden = false

sledders.input.onPressed("ctrl+u", function()
    hidden = not hidden
    sledders.hud.setElementVisible("rpmMeter", not hidden)
    print("RPM meter visible: " .. tostring(not hidden))
end)

sledders.input.onPressed("ctrl+shift+u", function()
    local meter = sledders.hud.get("speedMeter")
    if meter then
        meter.setUnit("km/h")
    end
end)
