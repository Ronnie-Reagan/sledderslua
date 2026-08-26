sledders.input.onPressed("h", function()
    local sled = sledders.player.getSled()
    if not sled then return end

    local wasOn = sled.getHeadlights()
    if wasOn == nil then return end

    sled.setHeadlights(not wasOn)
    print("Headlights toggled " .. (wasOn and "off" or "on"))
end)
