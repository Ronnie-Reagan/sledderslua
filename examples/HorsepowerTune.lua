-- HorsepowerTune.lua
-- Vehicle-definition tuning is separate from live Rigidbody mass.

local originalHorsepower = nil

function onLoad()
    local sled = sledders.player.getSled()
    if not sled then return end

    local vehicle = sled.getVehicle()
    if not vehicle then return end

    originalHorsepower = vehicle.getHorsepower()
    print("Stock horsepower: " .. tostring(originalHorsepower))
end

sledders.input.onPressed("ctrl+h", function()
    local sled = sledders.player.getSled()
    if not sled then return end
    local vehicle = sled.getVehicle()
    if vehicle then
        vehicle.setHorsepower(200)
        print("Vehicle definition horsepower set to 200")
    end
end)

sledders.input.onPressed("ctrl+shift+h", function()
    local sled = sledders.player.getSled()
    if not sled or not originalHorsepower then return end
    local vehicle = sled.getVehicle()
    if vehicle then vehicle.setHorsepower(originalHorsepower) end
end)
