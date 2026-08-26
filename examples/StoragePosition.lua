sledders.input.onPressed("k", function()
    local sled = sledders.player.getSled()
    if sled then sledders.storage.set("position", sled.getPos()) end
end)

sledders.input.onPressed("j", function()
    local sled = sledders.player.getSled()
    local position = sledders.storage.get("position")
    if sled and position then sled.teleport(position) end
end)
