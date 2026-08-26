sledders.input.onPressed("e", function()
    local sled = sledders.player.getSled()
    if not sled then return end

    local running = sled.isEngineOn()
    if running == nil then return end
    sled.setEngineOn(not running)
end)
