sledders.input.onPressed("ctrl+r", function()
    local sled = sledders.player.getSled()
    if sled and sled.fillFuel() then
        print("Fuel tank filled: " .. tostring(sled.getFuel()) .. " L")
    end
end)
