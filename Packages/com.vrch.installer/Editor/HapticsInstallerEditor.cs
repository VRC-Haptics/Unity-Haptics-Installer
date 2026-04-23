using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;
using Common;
using Editor.build_prefab;
using Newtonsoft.Json;
using UnityEditorInternal;
using VRC.SDK3.Avatars.Components;

namespace Editor
{
    public partial class HapticsInstaller : EditorWindow
    {
        // --- Config Loading (Section 1) ---
        private static string _selectedConfigPath = "";
        private static string _configJsonContent = "";
        private static Config _config;
        private static bool _configValid;
        private static readonly bool UseLowPoly = true;

        // --- Offsets Editing (Section 2) ---
        private static OffsetsAsset _currentOffsets;
        private Vector2 _nodeScrollPos;

        // --- Baking (Section 3) ---
        private static GameObject _avatarRoot;
        private static string _collectionName = "Optimized";
        private static readonly List<OffsetsAsset> OffsetsToBake = new();
        private static ReorderableList _offsetsReorderableList;

        [MenuItem("Haptics/Start Installer")]
        static void ShowInstaller()
        {
            GetWindow<HapticsInstaller>("Haptics Installer");
        }

        private void OnEnable()
        {
            InitOffsetsList();

            var desc = Object.FindObjectsByType<VRCAvatarDescriptor>(FindObjectsSortMode.None);
            if (desc.Length > 0)
                _avatarRoot = desc[0].gameObject;
        }

        private static void InitOffsetsList()
        {
            _offsetsReorderableList = new ReorderableList(OffsetsToBake, typeof(OffsetsAsset), true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Offset Files to Bake"),

                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    rect.y += 2;
                    OffsetsToBake[index] = (OffsetsAsset)EditorGUI.ObjectField(
                        new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                        OffsetsToBake[index],
                        typeof(OffsetsAsset),
                        false);
                },

                onAddCallback = list => OffsetsToBake.Add(null),
                onRemoveCallback = list => OffsetsToBake.RemoveAt(list.index)
            };
        }

        private void OnGUI()
        {
            if (GUILayout.Button("Reset Window"))
                ResetEditorWindow();

            EditorGUILayout.Space(10);
            ConfigToOffsetsGui();

            EditorGUILayout.Space(20);
            OffsetsEditorGui();

            EditorGUILayout.Space(20);
            BakeGui();
        }

        static void ResetEditorWindow()
        {
            _selectedConfigPath = string.Empty;
            _configJsonContent = string.Empty;
            _config = null;
            _configValid = false;
            _avatarRoot = null;
            _currentOffsets = null;
            OffsetsToBake.Clear();
        }

        // =====================================================================
        // SECTION 1: Load Config → Save as Offsets .asset (no scene objects)
        // =====================================================================
        void ConfigToOffsetsGui()
        {
            GUILayout.Label("1. Create Offsets from Config", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Load a config file, then save it as an editable offsets asset. Nothing is added to the scene.",
                MessageType.Info);

            if (GUILayout.Button("Select Configuration File"))
            {
                string path = EditorUtility.OpenFilePanel("Select JSON File", "", "json");
                if (!string.IsNullOrEmpty(path))
                    LoadConfig(path);
            }

            if (!string.IsNullOrEmpty(_selectedConfigPath) && _configValid)
            {
                string info = $"Config: {_config.meta.map_name} by {_config.meta.map_author}" +
                              $" (v{_config.meta.map_version}, {_config.nodes.Length} nodes)";
                EditorGUILayout.HelpBox(info, MessageType.Info);

                if (GUILayout.Button("Save as Offsets Asset…"))
                {
                    string savePath = EditorUtility.SaveFilePanelInProject(
                        "Save Offsets Asset", _config.meta.map_name + "_offsets", "asset",
                        "Choose where to save the offsets file.");

                    if (!string.IsNullOrEmpty(savePath))
                    {
                        var asset = CreateOffsetsFromConfig(_config, _selectedConfigPath);
                        AssetDatabase.CreateAsset(asset, savePath);
                        AssetDatabase.SaveAssets();
                        _currentOffsets = asset;
                        OffsetsToBake.Add(asset);
                        EditorGUIUtility.PingObject(asset);
                    }
                }
            }
            else if (!string.IsNullOrEmpty(_selectedConfigPath) && !_configValid)
            {
                EditorGUILayout.HelpBox("Invalid configuration", MessageType.Error);
            }
        }

        static OffsetsAsset CreateOffsetsFromConfig(Config config, string configPath)
        {
            var asset = ScriptableObject.CreateInstance<OffsetsAsset>();
            asset.mapAuthor = config.meta.map_author;
            asset.mapName = config.meta.map_name;
            asset.mapVersion = config.meta.map_version;
            asset.useLowPoly = UseLowPoly;

            asset.nodeOffsets = new OffsetsAsset.NodeOffset[config.nodes.Length];
            for (int i = 0; i < config.nodes.Length; i++)
            {
                var src = config.nodes[i];
                asset.nodeOffsets[i] = new OffsetsAsset.NodeOffset
                {
                    nodeId          = src.address,
                    baseBone        = src.target_bone,
                    basePosition    = src.GetNodePosition(),
                    baseRotation    = Vector3.zero,
                    baseRadius      = src.radius,
                    targetBone      = src.target_bone,
                    positionOffset  = Vector3.zero,
                    rotationOffset  = Vector3.zero,
                    scaleMultiplier = 1f
                };
            }

            return asset;
        }
        
        void OffsetsEditorGui()
        {
            GUILayout.Label("2. Edit Offsets", EditorStyles.boldLabel);

            _currentOffsets = (OffsetsAsset)EditorGUILayout.ObjectField(
                "Offsets Asset:", _currentOffsets, typeof(OffsetsAsset), false);

            if (_currentOffsets == null) return;

            EditorGUILayout.HelpBox(
                $"{_currentOffsets.mapName} — {_currentOffsets.nodeOffsets.Length} nodes",
                MessageType.None);

            _nodeScrollPos = EditorGUILayout.BeginScrollView(_nodeScrollPos, GUILayout.MaxHeight(300));

            Undo.RecordObject(_currentOffsets, "Edit Node Offsets");
            EditorGUI.BeginChangeCheck();

            for (int i = 0; i < _currentOffsets.nodeOffsets.Length; i++)
            {
                var node = _currentOffsets.nodeOffsets[i];

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(node.nodeId, EditorStyles.boldLabel);
                node.positionOffset = EditorGUILayout.Vector3Field("Position", node.positionOffset);
                node.rotationOffset = EditorGUILayout.Vector3Field("Rotation", node.rotationOffset);
                node.scaleMultiplier = EditorGUILayout.Slider("Scale", node.scaleMultiplier, 0.1f, 3f);
                EditorGUILayout.EndVertical();
            }

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(_currentOffsets);

            EditorGUILayout.EndScrollView();
        }

        // =====================================================================
        // SECTION 3: Bake (scene objects created here and only here)
        // =====================================================================
        void BakeGui()
        {
            GUILayout.Label("3. Bake to Prefab", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            _avatarRoot = (GameObject)EditorGUILayout.ObjectField(
                "Avatar Root:", _avatarRoot, typeof(GameObject), true);

            _collectionName = EditorGUILayout.TextField("Collection Name:", _collectionName);

            EditorGUILayout.HelpBox(
                "Add your offset assets below, then bake them into one optimized prefab.",
                MessageType.Info);

            _offsetsReorderableList.DoLayoutList();

            GUI.enabled = OffsetsToBake.Count > 0 && OffsetsListValid() && _avatarRoot != null;
            if (GUILayout.Button("Bake Prefabs"))
            {
                var builtPrefabs = new List<GameObject>();
                foreach (var offsets in OffsetsToBake)
                {
                    //string json = File.ReadAllText(offsets.configPath);
                    //Config cfg = JsonConvert.DeserializeObject<Config>(json);
                    //GameObject built = BuildFromConfig.Build(_avatarRoot, cfg, offsets.useLowPoly);
                    //ApplyOffsets(built, offsets);
                    //builtPrefabs.Add(built);
                }

                OptimizePrefab.OptimizePrefabs(
                    builtPrefabs.ToArray(), _avatarRoot, _collectionName, new VisualizerAsset.Default());
            }
            GUI.enabled = true;
        }

        static void ApplyOffsets(GameObject prefab, OffsetsAsset offsets)
        {
            Transform nodesParent = prefab.transform.Find("nodes");
            if (nodesParent == null) return;

            foreach (var offset in offsets.nodeOffsets)
            {
                Transform node = nodesParent.Find(offset.nodeId);
                if (node == null) continue;

                node.localPosition += offset.positionOffset;
                node.localEulerAngles += offset.rotationOffset;
                //node.localScale = (node.localScale * offset.scaleMultiplier).;
            }
        }

        bool OffsetsListValid()
        {
            foreach (var o in OffsetsToBake)
                if (o == null) return false;
            return true;
        }

        // =====================================================================
        // Shared Helpers
        // =====================================================================
        static Config LoadConfig(string path)
        {
            _selectedConfigPath = path;
            _configJsonContent = File.ReadAllText(path);
            _config = JsonConvert.DeserializeObject<Config>(_configJsonContent);
            _configValid = ValidateConfig();
            Debug.Log("Config loaded: " + _selectedConfigPath);
            return _config;
        }

        static bool ValidateConfig()
        {
            if (_config == null)
            {
                Debug.LogError("Failed to parse JSON.");
                return false;
            }
            if (_config.nodes == null || _config.nodes.Length == 0)
            {
                Debug.LogError("Config must contain at least one node.");
                return false;
            }
            if (_config.meta == null || _config.meta.map_author == null || _config.meta.map_name == null)
            {
                Debug.LogError("Metadata is missing or incomplete.");
                return false;
            }
            return true;
        }
    }
}