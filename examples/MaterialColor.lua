-- MaterialColor.lua
-- Recolor the local sled hood through stable renderer/material wrappers.

local red = false

sledders.input.onPressed("ctrl+c", function()
    local sled = sledders.player.getSled()
    if not sled then return end

    local renderers = sled.getRenderers("hood")
    red = not red
    local color = red and sledders.color(1, 0.05, 0.05, 1) or sledders.color(1, 1, 1, 1)

    for _, renderer in ipairs(renderers) do
        renderer.setColor(color)
    end
end)
