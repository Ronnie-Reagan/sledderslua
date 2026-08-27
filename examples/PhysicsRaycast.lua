-- PhysicsRaycast.lua
-- Raycast down from the local sled and report ground distance.

sledders.input.onPressed("ctrl+g", function()
    local sled = sledders.player.getSled()
    if not sled then return end

    local hit = sledders.physics.raycast(
        sled.getPos(),
        sledders.vector3(0, -1, 0),
        100
    )

    if hit then
        print(string.format("Ground hit %.2f m below sled", hit.distance or -1))
    else
        print("No hit")
    end
end)
