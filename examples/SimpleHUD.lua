local sled = nil

function onTick(dt)
    if not sled or not sled.isValid() then
        sled = sledders.player.getSled()
    end
end

function onDraw()
    if not sled then return end

    local x, y, w, h = 20, 20, 240, 72
    sledders.screen.setColor(0, 0, 0, 170)
    sledders.screen.rectangle(x, y, w, h)

    sledders.screen.setColor(255, 255, 255)
    sledders.screen.print(sled.getName(), x + 10, y + 8)
    sledders.screen.print(string.format("Speed: %.1f m/s", sled.getSpeed() or 0), x + 10, y + 30)
    sledders.screen.print(string.format("RPM: %.0f", sled.getRpm() or 0), x + 10, y + 50)
end
