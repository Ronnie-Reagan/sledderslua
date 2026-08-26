using System.IO;

namespace SleddersLuaRuntime.Core
{
    internal sealed class LuaModSource
    {
        public string Key { get; set; } = string.Empty;
        public string ModuleRoot { get; set; } = string.Empty;
        public string MainPath { get; set; } = string.Empty;
        public string? ManifestPath { get; set; }
        public string FallbackId { get; set; } = string.Empty;
        public string FallbackName { get; set; } = string.Empty;

        public static LuaModSource FromDirectory(string directory)
        {
            string name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return new LuaModSource
            {
                Key = directory,
                ModuleRoot = directory,
                MainPath = Path.Combine(directory, "main.lua"),
                ManifestPath = Path.Combine(directory, "manifest.lua"),
                FallbackId = name,
                FallbackName = name
            };
        }

        public static LuaModSource FromSingleFile(string file)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            return new LuaModSource
            {
                Key = file,
                ModuleRoot = Path.GetDirectoryName(file)!,
                MainPath = file,
                ManifestPath = null,
                FallbackId = name,
                FallbackName = name
            };
        }
    }
}
