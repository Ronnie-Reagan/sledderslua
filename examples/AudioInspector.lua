-- AudioInspector.lua
-- Inspect currently playing Unity AudioSources. Some Sledders audio is Wwise
-- and therefore will not appear as an AudioSource/AudioClip.

sledders.input.onPressed("ctrl+a", function()
    local sources = sledders.audio.getPlayingSources(64)
    print("Playing Unity AudioSources: " .. tostring(#sources))

    for i, source in ipairs(sources) do
        local clip = source.getClip()
        print(i, source.getName(), "volume=" .. tostring(source.getVolume()), clip and clip.getName() or "<no clip>")
    end
end)
