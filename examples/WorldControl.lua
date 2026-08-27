-- WorldControl.lua
-- Simple native world changes.

sledders.input.onPressed("ctrl+1", function()
    sledders.world.snow.setCondition("Powder")
end)

sledders.input.onPressed("ctrl+2", function()
    sledders.world.time.setTimeOfDay(12.0)
end)

sledders.input.onPressed("ctrl+3", function()
    print("Weather: " .. tostring(sledders.world.weather.getName()))
    print("Snow: " .. tostring(sledders.world.snow.getCondition()))
    print("Clock: " .. tostring(sledders.world.time.getClock()))
end)
