-- CameraProjection.lua
-- Project the sled's world position into GUI coordinates.

function onDraw()
    local sled = sledders.player.getSled()
    if not sled then return end

    local pos = sled.getPos()
    if not pos then return end

    local gui = sledders.camera.worldToGui(pos)
    if not gui or gui.z <= 0 then return end

    sledders.screen.setColor(255, 255, 255)
    sledders.screen.print("SLED", gui.x - 20, gui.y - 12, 60, 24)
end
