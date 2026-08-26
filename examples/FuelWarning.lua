function onDraw()
    local sled = sledders.player.getSled()
    if not sled then return end

    local fuel = sled.getFuelPercent()
    if fuel and fuel < 0.15 then
        sledders.screen.setColor(255, 255, 255)
        sledders.screen.print("LOW FUEL", 20, 20)
    end
end
