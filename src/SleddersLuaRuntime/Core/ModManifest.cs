using System;
using System.Collections.Generic;
using System.IO;
using MoonSharp.Interpreter;

namespace SleddersLuaRuntime.Core
{
    internal sealed class ModManifest
    {
        public string Id { get; private set; } = string.Empty;
        public string Name { get; private set; } = string.Empty;
        public string Author { get; private set; } = "Unknown";
        public string Version { get; private set; } = "0.0.0";
        public string Api { get; private set; } = "3.1";
        public HashSet<string> Permissions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool HasPermission(string permission) => Permissions.Contains(permission);

        public static ModManifest Load(string? manifestPath, string fallbackId, string fallbackName)
        {
            var manifest = new ModManifest
            {
                Id = SanitizeId(fallbackId),
                Name = fallbackName
            };

            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
                return manifest;

            var script = new Script(CoreModules.Preset_HardSandbox);
            DynValue result = script.DoString(File.ReadAllText(manifestPath), codeFriendlyName: manifestPath);
            if (result.Type != DataType.Table)
                throw new InvalidDataException("manifest.lua must return a table.");

            var table = result.Table;
            manifest.Id = SanitizeId(GetString(table, "id", manifest.Id));
            manifest.Name = GetString(table, "name", manifest.Name);
            manifest.Author = GetString(table, "author", manifest.Author);
            manifest.Version = GetString(table, "version", manifest.Version);
            manifest.Api = GetString(table, "api", manifest.Api);

            DynValue permissions = table.Get("permissions");
            if (permissions.Type == DataType.Table)
            {
                foreach (var pair in permissions.Table.Pairs)
                {
                    if (pair.Value.Type == DataType.String && !string.IsNullOrWhiteSpace(pair.Value.String))
                        manifest.Permissions.Add(pair.Value.String.Trim());
                }
            }

            return manifest;
        }

        private static string GetString(Table table, string key, string fallback)
        {
            DynValue value = table.Get(key);
            return value.Type == DataType.String && !string.IsNullOrWhiteSpace(value.String)
                ? value.String.Trim()
                : fallback;
        }

        private static string SanitizeId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return "unnamed-mod";

            var chars = id.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.'))
                    chars[i] = '-';
            }

            string sanitized = new string(chars).Trim('-', '.');
            while (sanitized.IndexOf("..", StringComparison.Ordinal) >= 0)
                sanitized = sanitized.Replace("..", ".");
            return string.IsNullOrWhiteSpace(sanitized) ? "unnamed-mod" : sanitized;
        }
    }
}
