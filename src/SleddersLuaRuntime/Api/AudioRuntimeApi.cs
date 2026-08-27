using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MoonSharp.Interpreter;
using SleddersLuaRuntime.Core;

namespace SleddersLuaRuntime.Api
{
    internal static class AudioRuntimeApi
    {
        private const int MaxWavBytes = 64 * 1024 * 1024;
        private const int MaxSampleRead = 65536;

        private static readonly Dictionary<string, string> NativeSfx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "buttonClick", "PlayButtonClickSfx" },
            { "buttonHighlight", "PlayButtonHighlightSfx" },
            { "itemClick", "PlayItemClickSfx" },
            { "tuningClick", "PlayTuningButtonClickSfx" },
            { "error", "PlayErrorSfx" },
            { "sledSelect", "PlaySnowmobileSelectSfx" },
            { "skiSelect", "PlaySkiSelectSfx" },
            { "wrapSelect", "PlayWrapSelectSfx" },
            { "driverSelect", "PlayDriverSelectSfx" },
            { "enterPhotoMode", "PlayEnterPhotoModeSfx" },
            { "exitPhotoMode", "PlayExitPhotoModeSfx" },
            { "shutter", "PlayTakePhotoSfx" }
        };

        public static void Enhance(LuaModInstance mod, Table audio)
        {
            audio.Set("getSources", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, audio);
                int max = args.Count > offset ? FrameworkApiUtil.RequireInt(args, offset, "audio.getSources(max)", 1, 512) : 128;
                return SourceArray(mod, FindSources(null, max, false));
            }));
            audio.Set("getPlayingSources", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, audio);
                int max = args.Count > offset ? FrameworkApiUtil.RequireInt(args, offset, "audio.getPlayingSources(max)", 1, 512) : 128;
                return SourceArray(mod, FindSources(null, max, true));
            }));
            audio.Set("getSledSources", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, audio);
                int max = args.Count > offset ? FrameworkApiUtil.RequireInt(args, offset, "audio.getSledSources(max)", 1, 256) : 64;
                object? sled = SleddersGameBindings.FindLocalSled();
                return SourceArray(mod, FindSources(sled, max, false));
            }));
            audio.Set("loadWav", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, audio);
                string path = FrameworkApiUtil.RequireString(args, offset, "audio.loadWav(relativePath)");
                object? clip = LoadWav(mod, path);
                return clip == null ? DynValue.Nil : WrapClip(mod, clip);
            }));
            audio.Set("createSource", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, audio);
                Table? options = args.Count > offset && args[offset].Type == DataType.Table ? args[offset].Table : null;
                object? source = CreateSource(mod, null, options);
                return source == null ? DynValue.Nil : WrapSource(mod, source);
            }));
            audio.Set("play", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, audio);
                if (args.Count <= offset || args[offset].Type != DataType.Table)
                    throw new ScriptRuntimeException("audio.play(clip [, options]) expects an audio clip wrapper.");
                object? clip = ObjectProxyBuilder.DynToRaw(mod, args[offset]);
                Table? options = args.Count > offset + 1 && args[offset + 1].Type == DataType.Table ? args[offset + 1].Table : null;
                object? source = clip == null ? null : CreateSource(mod, clip, options);
                if (source != null) SleddersGameBindings.TryCallAny(source, new[] { "Play" }, Array.Empty<object?>(), out _);
                return source == null ? DynValue.Nil : WrapSource(mod, source);
            }));
            audio.Set("nativeSfx", DynValue.NewTable(BuildNativeSfx(mod)));
            audio.Set("engine", DynValue.NewTable(BuildEngineTelemetry(mod)));
            audio.Set("presets", DynValue.NewTable(BuildPresets(mod)));
        }

        public static DynValue WrapSource(LuaModInstance mod, object source)
        {
            int handle = mod.Handles.Add(source);
            if (mod.TryGetCachedObject("audioSource", handle, out DynValue cached)) return cached;
            var table = new Table(mod.Script);
            table.Set("__handle", DynValue.NewNumber(handle));
            table.Set("__type", DynValue.NewString("audioSource"));
            table.Set("isValid", DynValue.NewCallback((ctx, args) => DynValue.NewBoolean(FrameworkApiUtil.Resolve(mod, handle) != null)));
            table.Set("getName", DynValue.NewCallback((ctx, args) => DynValue.NewString(SleddersGameBindings.GetFriendlyName(FrameworkApiUtil.RequireObject(mod, handle, "audio source")))));
            AddScalar(table, mod, handle, "Volume", "volume", 0.0, 1.0);
            AddScalar(table, mod, handle, "Pitch", "pitch", -3.0, 3.0);
            AddScalar(table, mod, handle, "Time", "time", 0.0, double.MaxValue);
            AddScalar(table, mod, handle, "SpatialBlend", "spatialBlend", 0.0, 1.0);
            AddBool(table, mod, handle, "Loop", "loop");
            AddBool(table, mod, handle, "Mute", "mute");
            table.Set("isPlaying", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "audio source");
                return SleddersGameBindings.TryGetAny(live, out object? value, "isPlaying") && value is bool b ? DynValue.NewBoolean(b) : DynValue.Nil;
            }));
            table.Set("getClip", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "audio source");
                return SleddersGameBindings.TryGetAny(live, out object? clip, "clip") && clip != null ? WrapClip(mod, clip) : DynValue.Nil;
            }));
            table.Set("setClip", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "audio source");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? clip = args.Count > offset && args[offset].Type == DataType.Table ? ObjectProxyBuilder.DynToRaw(mod, args[offset]) : null;
                return DynValue.NewBoolean(clip != null && SleddersGameBindings.TrySetAny(live, clip, "clip"));
            }));
            table.Set("getPos", DynValue.NewCallback((ctx, args) => ValueConverter.ToDynValue(mod, SleddersGameBindings.GetPosition(FrameworkApiUtil.RequireObject(mod, handle, "audio source")))));
            table.Set("setPos", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "audio source");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                object? transform = SleddersGameBindings.GetTransform(live);
                object? pos = FrameworkApiUtil.ReadVector3(mod, args, offset, "audioSource.setPos(vector3)");
                return DynValue.NewBoolean(transform != null && pos != null && SleddersGameBindings.TrySetAny(transform, pos, "position"));
            }));
            foreach (string method in new[] { "Play", "Pause", "UnPause", "Stop" })
            {
                string localMethod = method;
                string luaName = char.ToLowerInvariant(method[0]) + method.Substring(1);
                table.Set(luaName, DynValue.NewCallback((ctx, args) =>
                {
                    object live = FrameworkApiUtil.RequireObject(mod, handle, "audio source");
                    return DynValue.NewBoolean(SleddersGameBindings.TryCallAny(live, new[] { localMethod }, Array.Empty<object?>(), out _));
                }));
            }
            DynValue wrapped = DynValue.NewTable(table);
            mod.CacheObject("audioSource", handle, wrapped);
            return wrapped;
        }

        public static DynValue WrapClip(LuaModInstance mod, object clip)
        {
            int handle = mod.Handles.Add(clip);
            if (mod.TryGetCachedObject("audioClip", handle, out DynValue cached)) return cached;
            var table = new Table(mod.Script);
            table.Set("__handle", DynValue.NewNumber(handle));
            table.Set("__type", DynValue.NewString("audioClip"));
            table.Set("getName", DynValue.NewCallback((ctx, args) => DynValue.NewString(SleddersGameBindings.GetFriendlyName(FrameworkApiUtil.RequireObject(mod, handle, "audio clip")))));
            AddReadNumber(table, mod, handle, "Length", "length");
            AddReadNumber(table, mod, handle, "Channels", "channels");
            AddReadNumber(table, mod, handle, "Frequency", "frequency");
            AddReadNumber(table, mod, handle, "Samples", "samples");
            table.Set("getData", DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "audio clip");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                int sampleOffset = args.Count > offset ? FrameworkApiUtil.RequireInt(args, offset, "audioClip.getData(offset,count)", 0, int.MaxValue) : 0;
                int count = args.Count > offset + 1 ? FrameworkApiUtil.RequireInt(args, offset + 1, "audioClip.getData(offset,count)", 1, MaxSampleRead) : 4096;
                var data = new float[count];
                if (!ReflectionBridge.TryCall(live, "GetData", new object?[] { data, sampleOffset }, out object? ok) || ok is bool b && !b)
                    return DynValue.Nil;
                var result = new Table(mod.Script);
                for (int i = 0; i < data.Length; i++) result.Set(i + 1, DynValue.NewNumber(data[i]));
                return DynValue.NewTable(result);
            }));
            DynValue wrapped = DynValue.NewTable(table);
            mod.CacheObject("audioClip", handle, wrapped);
            return wrapped;
        }

        private static Table BuildNativeSfx(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("names", DynValue.NewCallback((ctx, args) =>
            {
                var result = new Table(mod.Script);
                int i = 1;
                foreach (string name in NativeSfx.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) result.Set(i++, DynValue.NewString(name));
                return DynValue.NewTable(result);
            }));
            table.Set("play", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string name = FrameworkApiUtil.RequireString(args, offset, "audio.nativeSfx.play(name)");
                if (!NativeSfx.TryGetValue(name, out string? method)) return DynValue.False;
                object? manager = GetSingleton("AudioManagerWwise");
                return DynValue.NewBoolean(manager != null && SleddersGameBindings.TryCallAny(manager, new[] { method }, Array.Empty<object?>(), out _));
            }));
            return table;
        }

        private static Table BuildEngineTelemetry(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("types", DynValue.NewCallback((ctx, args) =>
            {
                string[] names = { "Patriot850", "Etec850", "Kitty858", "Ace900", "Triple700", "Etec850GgbQuiet", "Liberty700" };
                var result = new Table(mod.Script);
                for (int i = 0; i < names.Length; i++) result.Set(i + 1, DynValue.NewString(names[i]));
                return DynValue.NewTable(result);
            }));
            table.Set("getRtpc", DynValue.NewCallback((ctx, args) => EngineValue("CurrentEngineRTPC")));
            DynValue getTypeValue = DynValue.NewCallback((ctx, args) => EngineValue("CurrentEngineTypeValue"));
            table.Set("getTypeValue", getTypeValue);
            table.Set("getType", getTypeValue);
            table.Set("setType", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string type = FrameworkApiUtil.RequireString(args, offset, "audio.engine.setType(type)");
                object? controller = GetEngineController();
                return DynValue.NewBoolean(controller != null && SleddersGameBindings.TryCallAny(
                    controller, new[] { "SetEngineType" }, new object?[] { type }, out _));
            }));
            table.Set("start", DynValue.NewCallback((ctx, args) =>
            {
                object? controller = GetEngineController();
                return DynValue.NewBoolean(controller != null && SleddersGameBindings.TryCallAny(
                    controller, new[] { "StartEngineSound" }, Array.Empty<object?>(), out _));
            }));
            table.Set("stop", DynValue.NewCallback((ctx, args) =>
            {
                object? controller = GetEngineController();
                return DynValue.NewBoolean(controller != null && SleddersGameBindings.TryCallAny(
                    controller, new[] { "StopEngineSound" }, Array.Empty<object?>(), out _));
            }));
            return table;
        }

        private static DynValue EngineValue(string name)
        {
            object? controller = GetEngineController();
            if (controller == null || !SleddersGameBindings.TryGetAnyOrGetter(controller, out object? value, name) || value == null)
                return DynValue.Nil;
            double? number = SleddersGameBindings.ToDouble(value);
            return number.HasValue ? DynValue.NewNumber(number.Value) : DynValue.NewString(value.ToString() ?? string.Empty);
        }

        private static object? GetEngineController()
        {
            object? sled = SleddersGameBindings.FindLocalSled();
            Type? type = ReflectionBridge.FindTypeExact("EngineSoundControllerWwise");
            return sled == null || type == null
                ? null
                : ReflectionBridge.GetComponentsInChildren(sled, type, true, 8).FirstOrDefault();
        }

        private static Table BuildPresets(LuaModInstance mod)
        {
            var table = new Table(mod.Script);
            table.Set("names", DynValue.NewCallback((ctx, args) =>
            {
                var result = new Table(mod.Script);
                int i = 1;
                foreach (object preset in GetPresets())
                {
                    if (SleddersGameBindings.TryGetAny(preset, out object? raw, "presetName") && raw is string name)
                        result.Set(i++, DynValue.NewString(name));
                }
                return DynValue.NewTable(result);
            }));
            table.Set("all", DynValue.NewCallback((ctx, args) =>
            {
                var result = new Table(mod.Script);
                int i = 1;
                foreach (object preset in GetPresets()) result.Set(i++, WrapPreset(mod, preset));
                return DynValue.NewTable(result);
            }));
            table.Set("current", DynValue.NewCallback((ctx, args) =>
            {
                object? manager = GetSingleton("AudioPresetManager");
                return manager != null && SleddersGameBindings.TryCallAny(
                    manager, new[] { "GetCurrentPreset" }, Array.Empty<object?>(), out object? preset) && preset != null
                    ? WrapPreset(mod, preset)
                    : DynValue.Nil;
            }));
            table.Set("get", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string name = FrameworkApiUtil.RequireString(args, offset, "audio.presets.get(name)");
                object? preset = GetPresets().FirstOrDefault(value =>
                    SleddersGameBindings.TryGetAny(value, out object? raw, "presetName") &&
                    raw is string presetName &&
                    string.Equals(presetName, name, StringComparison.OrdinalIgnoreCase));
                return preset == null ? DynValue.Nil : WrapPreset(mod, preset);
            }));
            table.Set("apply", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string name = FrameworkApiUtil.RequireString(args, offset, "audio.presets.apply(name)");
                object? manager = GetSingleton("AudioPresetManager");
                return DynValue.NewBoolean(manager != null && SleddersGameBindings.TryCallAny(
                    manager, new[] { "ApplyPresetByName" }, new object?[] { name }, out _));
            }));
            table.Set("reset", DynValue.NewCallback((ctx, args) =>
            {
                object? manager = GetSingleton("AudioPresetManager");
                return DynValue.NewBoolean(manager != null && SleddersGameBindings.TryCallAny(
                    manager, new[] { "ResetToDefaults" }, Array.Empty<object?>(), out _));
            }));
            table.Set("saveCurrent", DynValue.NewCallback((ctx, args) =>
            {
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                string name = FrameworkApiUtil.RequireString(args, offset, "audio.presets.saveCurrent(name [, description])");
                string description = args.Count > offset + 1 && args[offset + 1].Type == DataType.String
                    ? args[offset + 1].String
                    : string.Empty;
                object? manager = GetSingleton("AudioPresetManager");
                return DynValue.NewBoolean(manager != null && SleddersGameBindings.TryCallAny(
                    manager, new[] { "SaveCurrentSettingsAsPreset" }, new object?[] { name, description }, out _));
            }));
            return table;
        }

        private static DynValue WrapPreset(LuaModInstance mod, object preset)
        {
            IReadOnlyList<SemanticProperty> properties = new[]
            {
                new SemanticProperty("name", true, "presetName"),
                new SemanticProperty("type", true, "presetType"),
                new SemanticProperty("description", true, "description"),
                new SemanticProperty("masterVolume", true, "masterVolume"),
                new SemanticProperty("engineVolume", true, "engineSoundVolume"),
                new SemanticProperty("ambientVolume", true, "ambientVolume"),
                new SemanticProperty("sfxVolume", true, "sfxVolume"),
                new SemanticProperty("musicVolume", true, "musicVolume"),
                new SemanticProperty("enhanceBass", true, "enhanceBass"),
                new SemanticProperty("enhanceTreble", true, "enhanceTreble"),
                new SemanticProperty("spatialAudio", true, "spatialAudio"),
                new SemanticProperty("dynamicRange", true, "dynamicRange")
            };
            DynValue bag = SemanticPropertyBag.Wrap(mod, preset, "audioPreset", properties);
            Table wrapper = bag.Table;
            foreach (var item in new[]
            {
                new[] { "name", "Name" }, new[] { "type", "Type" }, new[] { "masterVolume", "MasterVolume" },
                new[] { "engineVolume", "EngineVolume" }, new[] { "ambientVolume", "AmbientVolume" },
                new[] { "sfxVolume", "SfxVolume" }, new[] { "musicVolume", "MusicVolume" },
                new[] { "enhanceBass", "EnhanceBass" }, new[] { "enhanceTreble", "EnhanceTreble" },
                new[] { "spatialAudio", "SpatialAudio" }, new[] { "dynamicRange", "DynamicRange" }
            }) SemanticPropertyBag.AddNamedAccessors(wrapper, mod, bag, item[0], item[1]);
            wrapper.Set("apply", DynValue.NewCallback((ctx, args) =>
            {
                object? manager = GetSingleton("AudioPresetManager");
                return DynValue.NewBoolean(manager != null && SleddersGameBindings.TryCallAny(
                    manager, new[] { "ApplyPreset" }, new object?[] { preset }, out _));
            }));
            return bag;
        }

        private static IReadOnlyList<object> GetPresets()
        {
            object? manager = GetSingleton("AudioPresetManager");
            if (manager == null || !SleddersGameBindings.TryCallAny(
                    manager, new[] { "GetAllPresets" }, Array.Empty<object?>(), out object? raw) || raw is not IEnumerable values)
                return Array.Empty<object>();
            var result = new List<object>();
            foreach (object? value in values) if (value != null) result.Add(value);
            return result;
        }

        private static IReadOnlyList<object> FindSources(object? target, int max, bool onlyPlaying)
        {
            Type? type = ReflectionBridge.FindTypeExact("UnityEngine.AudioSource");
            if (type == null) return Array.Empty<object>();
            IReadOnlyList<object> sources = target == null
                ? ReflectionBridge.FindObjectsOfType(type, max * 2)
                : ReflectionBridge.GetComponentsInChildren(target, type, true, max * 2);
            var result = new List<object>();
            foreach (object source in sources)
            {
                if (onlyPlaying && (!SleddersGameBindings.TryGetAny(source, out object? playing, "isPlaying") || playing is not bool b || !b)) continue;
                result.Add(source);
                if (result.Count >= max) break;
            }
            return result;
        }

        private static DynValue SourceArray(LuaModInstance mod, IEnumerable<object> values)
        {
            var table = new Table(mod.Script);
            int i = 1;
            foreach (object value in values) table.Set(i++, WrapSource(mod, value));
            return DynValue.NewTable(table);
        }

        private static void AddScalar(Table table, LuaModInstance mod, int handle, string stem, string member, double min, double max)
        {
            table.Set("get" + stem, DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "audio source");
                if (!SleddersGameBindings.TryGetAny(live, out object? raw, member)) return DynValue.Nil;
                double? n = SleddersGameBindings.ToDouble(raw);
                return n.HasValue ? DynValue.NewNumber(n.Value) : DynValue.Nil;
            }));
            table.Set("set" + stem, DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "audio source");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                double value = FrameworkApiUtil.RequireFiniteNumber(args, offset, "audioSource.set" + stem + "(value)");
                value = Math.Max(min, Math.Min(max, value));
                return DynValue.NewBoolean(SleddersGameBindings.TrySetAny(live, value, member));
            }));
        }

        private static void AddBool(Table table, LuaModInstance mod, int handle, string stem, string member)
        {
            table.Set("get" + stem, DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "audio source");
                return SleddersGameBindings.TryGetAny(live, out object? raw, member) && raw is bool b ? DynValue.NewBoolean(b) : DynValue.Nil;
            }));
            table.Set("set" + stem, DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "audio source");
                int offset = FrameworkApiUtil.MethodOffset(args, table);
                return DynValue.NewBoolean(SleddersGameBindings.TrySetAny(live, FrameworkApiUtil.RequireBool(args, offset, "audioSource.set" + stem + "(enabled)"), member));
            }));
        }

        private static void AddReadNumber(Table table, LuaModInstance mod, int handle, string stem, string member)
        {
            table.Set("get" + stem, DynValue.NewCallback((ctx, args) =>
            {
                object live = FrameworkApiUtil.RequireObject(mod, handle, "audio clip");
                if (!SleddersGameBindings.TryGetAny(live, out object? raw, member)) return DynValue.Nil;
                double? n = SleddersGameBindings.ToDouble(raw);
                return n.HasValue ? DynValue.NewNumber(n.Value) : DynValue.Nil;
            }));
        }

        private static object? CreateSource(LuaModInstance mod, object? clip, Table? options)
        {
            Type? gameObjectType = ReflectionBridge.FindTypeExact("UnityEngine.GameObject");
            Type? audioSourceType = ReflectionBridge.FindTypeExact("UnityEngine.AudioSource");
            if (gameObjectType == null || audioSourceType == null) return null;
            object? go;
            try { go = Activator.CreateInstance(gameObjectType, new object?[] { "SleddersLuaAudio_" + mod.Manifest.Id }); }
            catch { return null; }
            if (go == null) return null;
            object? source;
            try { source = ReflectionBridge.Call(go, "AddComponent", new object?[] { audioSourceType }); }
            catch { source = null; }
            if (source == null) { DestroyUnity(go); return null; }

            if (clip != null) SleddersGameBindings.TrySetAny(source, clip, "clip");
            if (options != null)
            {
                SetOptionNumber(source, options, "volume", "volume", 0.0, 1.0);
                SetOptionNumber(source, options, "pitch", "pitch", -3.0, 3.0);
                SetOptionNumber(source, options, "spatialBlend", "spatialBlend", 0.0, 1.0);
                SetOptionBool(source, options, "loop", "loop");
                DynValue pos = options.Get("position");
                if (pos.Type == DataType.Table)
                {
                    object? transform = SleddersGameBindings.GetTransform(source);
                    Type? vectorType = ReflectionBridge.FindTypeExact("UnityEngine.Vector3");
                    object? converted = vectorType == null ? null : ValueConverter.FromDynValue(mod, pos, vectorType);
                    if (transform != null && converted != null) SleddersGameBindings.TrySetAny(transform, converted, "position");
                }
            }
            RuntimeResourceRegistry.Register(mod.StateOwnerToken, () => DestroyUnity(go));
            return source;
        }

        private static void SetOptionNumber(object target, Table options, string key, string member, double min, double max)
        {
            DynValue value = options.Get(key);
            if (value.Type != DataType.Number || double.IsNaN(value.Number) || double.IsInfinity(value.Number)) return;
            SleddersGameBindings.TrySetAny(target, Math.Max(min, Math.Min(max, value.Number)), member);
        }

        private static void SetOptionBool(object target, Table options, string key, string member)
        {
            DynValue value = options.Get(key);
            if (value.Type == DataType.Boolean) SleddersGameBindings.TrySetAny(target, value.Boolean, member);
        }

        private static object? LoadWav(LuaModInstance mod, string relativePath)
        {
            string root = Path.GetFullPath(mod.ModRoot);
            string path = Path.GetFullPath(Path.Combine(root, relativePath));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new ScriptRuntimeException("audio.loadWav(path) only reads WAV files inside the mod directory.");
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaxWavBytes) throw new ScriptRuntimeException("WAV file is empty or exceeds the 64 MiB limit.");
            byte[] bytes = File.ReadAllBytes(path);
            WavData wav = ParseWav(bytes);
            Type? clipType = ReflectionBridge.FindTypeExact("UnityEngine.AudioClip");
            if (clipType == null) return null;
            object? clip;
            try { clip = ReflectionBridge.CallStatic(clipType, "Create", new object?[] { Path.GetFileNameWithoutExtension(path), wav.FrameCount, wav.Channels, wav.Frequency, false }); }
            catch { clip = null; }
            if (clip == null || !ReflectionBridge.TryCall(clip, "SetData", new object?[] { wav.Samples, 0 }, out object? ok) || ok is bool b && !b)
            {
                if (clip != null) DestroyUnity(clip);
                return null;
            }
            RuntimeResourceRegistry.Register(mod.StateOwnerToken, () => DestroyUnity(clip));
            return clip;
        }

        private static WavData ParseWav(byte[] bytes)
        {
            if (bytes.Length < 44 || ReadAscii(bytes, 0, 4) != "RIFF" || ReadAscii(bytes, 8, 4) != "WAVE")
                throw new ScriptRuntimeException("Unsupported WAV: expected RIFF/WAVE.");
            ushort format = 0, channels = 0, bits = 0;
            int frequency = 0;
            int dataOffset = -1, dataLength = 0;
            int p = 12;
            while (p + 8 <= bytes.Length)
            {
                string id = ReadAscii(bytes, p, 4);
                int size = BitConverter.ToInt32(bytes, p + 4);
                int start = p + 8;
                if (size < 0 || start + size > bytes.Length) break;
                if (id == "fmt " && size >= 16)
                {
                    format = BitConverter.ToUInt16(bytes, start);
                    channels = BitConverter.ToUInt16(bytes, start + 2);
                    frequency = BitConverter.ToInt32(bytes, start + 4);
                    bits = BitConverter.ToUInt16(bytes, start + 14);
                }
                else if (id == "data") { dataOffset = start; dataLength = size; }
                p = start + size + (size & 1);
            }
            if (dataOffset < 0 || channels == 0 || frequency <= 0) throw new ScriptRuntimeException("Unsupported WAV: missing fmt/data chunks.");
            if (format != 1 && format != 3) throw new ScriptRuntimeException("Unsupported WAV encoding; PCM and IEEE float are supported.");
            int bytesPerSample = bits / 8;
            if (bytesPerSample <= 0 || (format == 3 && bits != 32) || (format == 1 && bits != 8 && bits != 16 && bits != 24 && bits != 32))
                throw new ScriptRuntimeException("Unsupported WAV bit depth; PCM 8/16/24/32 or float32 are supported.");
            int count = dataLength / bytesPerSample;
            var samples = new float[count];
            int cursor = dataOffset;
            for (int i = 0; i < count; i++, cursor += bytesPerSample)
            {
                if (format == 3) samples[i] = BitConverter.ToSingle(bytes, cursor);
                else if (bits == 8) samples[i] = (bytes[cursor] - 128) / 128f;
                else if (bits == 16) samples[i] = BitConverter.ToInt16(bytes, cursor) / 32768f;
                else if (bits == 24)
                {
                    int v = bytes[cursor] | (bytes[cursor + 1] << 8) | (bytes[cursor + 2] << 16);
                    if ((v & 0x800000) != 0) v |= unchecked((int)0xff000000);
                    samples[i] = v / 8388608f;
                }
                else samples[i] = BitConverter.ToInt32(bytes, cursor) / 2147483648f;
            }
            return new WavData(samples, channels, frequency);
        }

        private static string ReadAscii(byte[] bytes, int offset, int count) => System.Text.Encoding.ASCII.GetString(bytes, offset, count);

        private static object? GetSingleton(string typeName)
        {
            Type? type = ReflectionBridge.FindTypeExact(typeName);
            if (type == null) return null;
            try { return ReflectionBridge.GetStaticMember(type, "Instance"); }
            catch { return ReflectionBridge.FindObjectsOfType(type, 16).FirstOrDefault(); }
        }

        private static void DestroyUnity(object value)
        {
            Type? objectType = ReflectionBridge.FindTypeExact("UnityEngine.Object");
            if (objectType == null) return;
            try { ReflectionBridge.CallStatic(objectType, "Destroy", new object?[] { value }); } catch { }
        }

        private sealed class WavData
        {
            public WavData(float[] samples, int channels, int frequency)
            {
                Samples = samples;
                Channels = channels;
                Frequency = frequency;
            }
            public float[] Samples { get; }
            public int Channels { get; }
            public int Frequency { get; }
            public int FrameCount => Channels <= 0 ? 0 : Samples.Length / Channels;
        }
    }
}
