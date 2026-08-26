local sled

local function currentSled()
    if not sled or not sled.isValid() then
        sled = sledders.player.getSled()
    end
    return sled
end

sledders.input.onPressed("f", function()
    local s = currentSled()
    if not s then return end

    local enabled = s.getHeadlights()
    if enabled == nil then return end
    s.forceHeadlights(not enabled)
end)

sledders.input.onPressed("r", function()
    local s = currentSled()
    if s then s.releaseHeadlights() end
end)
