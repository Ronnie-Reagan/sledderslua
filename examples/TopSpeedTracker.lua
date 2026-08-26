local best = sledders.storage.get("bestKph", 0)

function onTick(dt)
    local sled = sledders.player.getSled()
    if not sled then return end

    local speed = (sled.getSpeed() or 0) * 3.6
    if speed > best then
        best = speed
        sledders.storage.set("bestKph", best)
    end
end

function onDraw()
    sledders.screen.setColor(255, 255, 255)
    sledders.screen.print(string.format("Top speed: %.1f kph", best), 20, 20)
end
