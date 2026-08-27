-- NativeInput.lua
-- Enumerate the game's real Unity Input System actions.

sledders.input.onPressed("ctrl+i", function()
    local actions = sledders.input.native.actions(128)
    print("Native actions: " .. tostring(#actions))
    for _, action in ipairs(actions) do
        print(action.getName(), action.getPhase(), action.getExpectedControlType())
    end
end)
