using System;
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;

namespace  Editor
{
    public class HapticsInstaller: EditorWindow
    {
        private static string _selectedConfigPath = "";
        private static string _configJsonContent = "";
        private static Config _config = null;
        
        [MenuItem("Haptics Installer/Start Installer")]
        static void ShowInstaller()
        {
            // Opens an instance of this window, or focuses it if already open.
            GetWindow<HapticsInstaller>("Haptics Installer");
        }

        // This method renders the GUI elements of the window.
        private void OnGUI()
        {
            
            GUILayout.Label("Config Selector", EditorStyles.boldLabel);

            // Button to open the file panel.
            if (GUILayout.Button("Select Configuration File"))
            {
                // Launch the file selection panel filtered to JSON files.
                string path = EditorUtility.OpenFilePanel("Select JSON File", "", "json");

                // Check if a file was selected.
                if (!string.IsNullOrEmpty(path))
                {
                    _selectedConfigPath = path;
                    // Read the contents of the JSON file.
                    _configJsonContent = File.ReadAllText(path);
                    // Here you can trigger additional processing of your parsed JSON.
                    Debug.Log("Config File loaded from: " + _selectedConfigPath);
                }
            }

            // If a file has been selected, display its path.
            if (!string.IsNullOrEmpty(_selectedConfigPath))
            {
                EditorGUILayout.Space();
                GUILayout.Label("Selected File:", EditorStyles.label);
                // Check validation and fill the config if needed.
                if (ValidateConfig())
                {
                    // config is guaranteed to be non-null at this point
                    // and at least one node.
                    string validationMessage = "Valid Configuration:\n";
                    validationMessage += "Author: " + _config.meta.map_author + "\n";
                    validationMessage += "Name: " + _config.meta.map_name + "\n";
                    validationMessage += "Version: " + _config.meta.map_version + "\n";
                    validationMessage += "Number of nodes: " + _config.nodes.Length;
                    
                    EditorGUILayout.TextField(_selectedConfigPath.Split("/").Last());
                    EditorGUILayout.HelpBox(validationMessage, MessageType.Info);
                    Debug.Log(validationMessage);
                }
                else
                {
                    EditorGUILayout.HelpBox("Invalid configuration", MessageType.Error);
                }
            }
        }

        static bool ValidateConfig()
        {
            // Try to convert the JSON string into a Config object.
            _config = JsonUtility.FromJson<Config>(_configJsonContent);
        
            // Check if deserialization failed.
            if (_config == null)
            {
                Debug.LogError("Failed to parse JSON.");
                return false;
            }
        
            // Ensure the nodes array exists and contains at least one node.
            if (_config.nodes == null || _config.nodes.Length == 0)
            {
                Debug.LogError("Validation failed: The configuration must contain at least one node.");
                return false;
            }
            
            // Make sure metadata is filled out
            if (
                _config.meta == null ||
                _config.meta.map_author == null ||
                _config.meta.map_name == null
            )
            {
                Debug.LogError("Validation failed: The metadata is missing or empty.");
                Debug.LogError(_config.meta);
                return false;
            }

            return true;
        }
    }
}
