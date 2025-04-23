using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;
using Newtonsoft.Json;
using UnityEditorInternal;

namespace Editor
{
    public class HapticsInstaller : EditorWindow
    {
        private static string _selectedConfigPath = "";
        private static string _configJsonContent = "";
        private static Config _config;
        private static bool _configValid;
        private static GameObject _avatarRoot;
        private static bool UseLowPoly = true;
        private static List<GameObject> PrefabsToOptimize = new ();
        // The ReorderableList to edit the list in the GUI.
        private static ReorderableList _prefabReorderableList;

        [MenuItem("Haptics/Start Installer")]
        static void ShowInstaller()
        {
            GetWindow<HapticsInstaller>("Haptics Installer");
        }

        private static void InitList()
        {
            // Initialize the ReorderableList with the prefabsToOptimize list.
            _prefabReorderableList = new ReorderableList(PrefabsToOptimize, typeof(GameObject), true, true, true, true)
            {
                // Callback for drawing the header.
                drawHeaderCallback = (Rect rect) =>
                {
                    EditorGUI.LabelField(rect, "Prefabs to Optimize");
                },
                    
                // Draw each element (each GameObject field).
                drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
                {
                    rect.y += 2;
                    // Allow scene objects by setting allowSceneObjects to true.
                    PrefabsToOptimize[index] = (GameObject)EditorGUI.ObjectField(
                        new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                        PrefabsToOptimize[index],
                        typeof(GameObject),
                        true);
                },
                    
                // Override the add callback so that a null entry is added instead of creating a new GameObject.
                onAddCallback = (ReorderableList list) =>
                {
                    PrefabsToOptimize.Add(null);
                },
                    
                // Remove callback to remove the selected element.
                onRemoveCallback = (ReorderableList list) => { PrefabsToOptimize.RemoveAt(list.index); }
            };
        }
        
        private void OnEnable()
        {
            InitList();
        }

        private void OnGUI()
        {
            if (GUILayout.Button("Reset Window"))
            {
                ResetEditorWindow();
            }
            
            // draw the generator part of the gui
            GeneratorGui();

            EditorGUILayout.Space(50);
            // draw the optimizer part of the gui
            OptimizeGui();

        }

        static void ResetEditorWindow()
        {
            _selectedConfigPath = string.Empty;
            _configJsonContent = string.Empty;
            _config = null;
            _configValid = false;
            _avatarRoot = null;

            PrefabsToOptimize.Clear();
        }
        
        /// The prefab generator section for the installer gui
        static void GeneratorGui()
        {
            GUILayout.Label("Prefab Builder", EditorStyles.boldLabel);
            
            EditorGUILayout.HelpBox(
                "Select your avatar root and a valid configuration file to start installing haptics.", 
                MessageType.Info);

            // avatar root selection
            _avatarRoot =
                (GameObject)EditorGUILayout.ObjectField("Avatar Root:", _avatarRoot, typeof(GameObject), true);
            
            // Use low poly check mark
            EditorGUILayout.Toggle("Use Low Poly:", UseLowPoly);

            // load configuration button
            if (GUILayout.Button("Select Configuration File"))
            {
                string path = EditorUtility.OpenFilePanel("Select JSON File", "", "json");
                if (!string.IsNullOrEmpty(path))
                {
                    // read file and validate results
                    _selectedConfigPath = path;
                    _configJsonContent = File.ReadAllText(path);
                    _config = JsonConvert.DeserializeObject<Config>(_configJsonContent);
                    _configValid = ValidateConfig();

                    Debug.Log("Config File loaded from: " + _selectedConfigPath);
                }
            }
            
            // Prefab build button
            GUI.enabled = _configValid && _avatarRoot != null;
            if (GUILayout.Button("Create Prefab"))
            {
                GameObject generatedPrefab = BuildFromConfig.Build(_avatarRoot, _config, UseLowPoly);
                // should never be null, but idk
                if (generatedPrefab != null)
                {
                    PrefabsToOptimize.Add(generatedPrefab);
                }
            }
            GUI.enabled = true;

            // display config loaded status
            if (!string.IsNullOrEmpty(_selectedConfigPath))
            {
                if (_configValid)
                {
                    string validationMessage = "Valid Configuration:\n";
                    validationMessage += "  Author: " + _config.meta.map_author + "\n";
                    validationMessage += "  Name: " + _config.meta.map_name + "\n";
                    validationMessage += "  Version: " + _config.meta.map_version + "\n";
                    validationMessage += "  Number of Nodes: " + _config.nodes.Length;

                    EditorGUILayout.HelpBox(validationMessage, MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("Invalid configuration", MessageType.Error);
                }
            }
        }

        bool PrefabListNotNull()
        {
            foreach (var prefab in PrefabsToOptimize)
            {
                if (prefab == null) return false;
            }
            return true;
        }

        /// <summary>
        /// Optimizer for the generated prefabs
        /// </summary>
        void OptimizeGui()
        {
            GUILayout.Label("Prefab Optimizer", EditorStyles.boldLabel);
            
            // Render the reorderable list.
            EditorGUILayout.HelpBox("Drag and drop your prefabs into the list below to add them for optimization.", MessageType.Info);
            _prefabReorderableList.DoLayoutList();

            GUI.enabled = PrefabsToOptimize.Count > 0 && PrefabListNotNull() && _avatarRoot != null;;
            if (GUILayout.Button("Bake Prefabs"))
            {
                OptimizePrefab.OptimizePrefabs(PrefabsToOptimize.ToArray(), _avatarRoot);
            }
            GUI.enabled = true;
        }

        /// Returns true if the config has at least one node and the required metadata
        static bool ValidateConfig()
        {
            if (_config == null)
            {
                Debug.LogError("Failed to parse JSON.");
                return false;
            }

            if (_config.nodes == null || _config.nodes.Length == 0)
            {
                Debug.LogError("Validation failed: The configuration must contain at least one node.");
                return false;
            }

            if (_config.meta == null || _config.meta.map_author == null || _config.meta.map_name == null)
            {
                Debug.LogError("Validation failed: The metadata is missing or empty.");
                return false;
            }

            return true;
        }
    }
}