local originalFov = nil
local wide = false

sledders.input.onPressed("o", function()
    if not originalFov then
        originalFov = sledders.camera.getFov()
    end
    if not originalFov then return end

    wide = not wide
    sledders.camera.setFov(wide and 100 or originalFov)
end)

function onUnload()
    if originalFov then
        sledders.camera.setFov(originalFov)
    end
end
