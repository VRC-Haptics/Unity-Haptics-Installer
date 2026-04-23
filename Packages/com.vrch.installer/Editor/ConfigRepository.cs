using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Common;
using Newtonsoft.Json;
using UnityEditor.UI;
using UnityEngine;

namespace Editor
{
    public static class ConfigRepository
    {
        public static readonly string ConfigDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "vrch-gui", "map_configs");

        public struct ConfigEntry
        {
            public string filePath;
            public Config config;
            public string displayName;
        }
        
        public enum ConfigLookupResult
        {
            Found,
            NotFound,
            DifferentVersionExists,
            DifferentAuthorExists
        }
        
        public static bool ValidateConfig(Config config)
        {
            if (config == null)
            {
                Debug.LogError("Failed to parse JSON.");
                return false;
            }

            if (config.nodes == null || config.nodes.Length == 0)
            {
                Debug.LogError("Config must contain at least one node.");
                return false;
            }

            if (config.meta == null || config.meta.map_author == null || config.meta.map_name == null)
            {
                Debug.LogError("Metadata is missing or incomplete.");
                return false;
            }

            return true;
        }
        
        /// <summary>
        /// Trys to load the config for the given name.
        /// </summary>
        /// <param name="mapName"></param>
        /// <param name="mapAuthor"></param>
        /// <param name="mapVersion"></param>
        /// <returns></returns>
        public static (ConfigLookupResult result, ConfigEntry? entry) TryFind(
            OffsetsAsset offset)
        {
            var all = LoadAll();

            var exact = all.FirstOrDefault(e =>
                e.config.meta.map_name == offset.mapName &&
                e.config.meta.map_author == offset.mapAuthor &&
                e.config.meta.map_version == offset.mapVersion);

            if (exact.config != null)
                return (ConfigLookupResult.Found, exact);

            var sameNameAndAuthor = all.FirstOrDefault(e =>
                e.config.meta.map_name == offset.mapName &&
                e.config.meta.map_author == offset.mapAuthor);

            if (sameNameAndAuthor.config != null)
                return (ConfigLookupResult.DifferentVersionExists, sameNameAndAuthor);

            var sameNameOnly = all.FirstOrDefault(e =>
                e.config.meta.map_name == offset.mapName);

            if (sameNameOnly.config != null)
                return (ConfigLookupResult.DifferentAuthorExists, sameNameOnly);

            return (ConfigLookupResult.NotFound, null);
        }

        /// <summary>
        /// Scans the config directory and returns all valid configs.
        /// </summary>
        public static List<ConfigEntry> LoadAll()
        {
            var results = new List<ConfigEntry>();

            if (!Directory.Exists(ConfigDirectory))
                Directory.CreateDirectory(ConfigDirectory);

            foreach (var file in Directory.GetFiles(ConfigDirectory, "*.json"))
            {
                var config = TryLoad(file);
                if (config == null) continue;

                results.Add(new ConfigEntry
                {
                    filePath = file,
                    config = config,
                    displayName = $"{config.meta.map_name} - {config.meta.map_author} - v{config.meta.map_version}"
                });
            }

            return results;
        }

        /// <summary>
        /// Validates and imports a config file into the repository directory.
        /// Returns the validated Config on success, null on failure.
        /// </summary>
        public static Config Import(string sourcePath)
        {
            var config = TryLoad(sourcePath);
            if (config == null)
            {
                Debug.LogError("Import failed: file is not a valid config.");
                return null;
            }

            if (!Directory.Exists(ConfigDirectory))
                Directory.CreateDirectory(ConfigDirectory);

            // Build a deterministic filename from meta content
            string safeName = SanitizeFileName(
                $"{config.meta.map_name}_{config.meta.map_author}_v{config.meta.map_version}");
            string destPath = Path.Combine(ConfigDirectory, safeName + ".json");

            File.Copy(sourcePath, destPath, overwrite: true);
            Debug.Log($"Config imported to: {destPath}");
            return config;
        }

        /// <summary>
        /// Tries to load and validate a config from a file path.
        /// Returns null if invalid.
        /// </summary>
        public static Config TryLoad(string path)
        {
            if (!File.Exists(path)) return null;

            try
            {
                var json = File.ReadAllText(path);
                var config = JsonConvert.DeserializeObject<Config>(json);
                return !ValidateConfig(config) ? null : config;
            }
            catch
            {
                return null;
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}