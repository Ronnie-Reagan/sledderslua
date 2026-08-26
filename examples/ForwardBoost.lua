sledders.input.onPressed("space", function()
    local sled = sledders.player.getSled()
    if not sled then return end

    sled.addVel(0, 0, 25)
end)
