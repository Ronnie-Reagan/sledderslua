local timer = 0

function onTick(dt)
    timer = timer + dt
    if timer < 1 then return end
    timer = timer - 1

    local sled = sledders.player.getSled()
    if sled then
        print(string.format("%.1f m/s", sled.getSpeed() or 0))
    end
end
