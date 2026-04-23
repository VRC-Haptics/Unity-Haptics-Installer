using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Common;
using Newtonsoft.Json;
using NUnit.Framework;
using Scripts;
using VRC.SDK3.Avatars.Components;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditorInternal;

namespace Editor
{
    [CustomEditor(typeof(HapticEditor))]
    public class HapticEditorInspector : UnityEditor.Editor
    {
        private readonly HashSet<int> _selectedIndices = new();
        private static readonly Dictionary<Animator, Dictionary<HumanBodyBones, HashSet<int>>> _boneIndexCache = new();
        private readonly HashSet<HumanBodyBones> _expandedBones = new();
        private Vector2 _scrollPos;

        private static Config _lastConfig;
        private static bool _lastConfigValid;
        
        private List<OffsetsAsset> _offsetGroups = new();
        private ReorderableList _offsetGroupsReorder;
        private List<String> _bakeError = new();
        
        private List<ConfigRepository.ConfigEntry> _availableConfigs = new();
        private int _selectedConfigIndex = -1;

        private void OnEnable()
        {
            _offsetGroupsReorder = new ReorderableList(_offsetGroups, typeof(OffsetsAsset), true, true, true, true)
            {
                drawHeaderCallback = rect =>
                    EditorGUI.LabelField(rect, "Offsets to Bake"),

                drawElementCallback = (rect, index, _, _) =>
                {
                    rect.y += 2;
                    _offsetGroups[index] = (OffsetsAsset)EditorGUI.ObjectField(
                        new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight),
                        _offsetGroups[index],
                        typeof(OffsetsAsset),
                        true);
                },

                onAddCallback = _ => _offsetGroups.Add(null),
                onRemoveCallback = l => _offsetGroups.RemoveAt(l.index)
            };
        }

        // =====================================================================
        // Inspector
        // =====================================================================
        public override void OnInspectorGUI()
        {
            var editor = (HapticEditor)target;

            if (editor.AvatarAnimator == null || !editor.AvatarAnimator.isHuman)
            {
                EditorGUILayout.HelpBox("Parent must have a humanoid Animator.", MessageType.Error);
                return;
            }

            EditorGUI.BeginChangeCheck();
            editor.offsets = (OffsetsAsset)EditorGUILayout.ObjectField(
                "Offsets Asset", editor.offsets, typeof(OffsetsAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(editor, "Assign Offsets");
                EditorUtility.SetDirty(editor);
                _selectedIndices.Clear();
            }

            editor.snapTarget = (GameObject)EditorGUILayout.ObjectField(
                "Snap Target", editor.snapTarget, typeof(GameObject), true);

            EditorGUILayout.Space();
            ConfigToOffsetsGui(editor);

            if (editor.offsets == null) return;

            EditorGUILayout.Space(10);
            var offsets = editor.offsets;
            EditorGUILayout.HelpBox($"{offsets.mapName} by {offsets.mapAuthor} — {offsets.nodeOffsets.Length} nodes",
                MessageType.None);

            // Group by bone
            var boneGroups = BuildBoneGroups(offsets);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            foreach (var kvp in boneGroups.OrderBy(b => b.Key.ToString()))
            {
                DrawBoneGroup(editor, kvp.Key, kvp.Value);
            }

            EditorGUILayout.EndScrollView();

            // Selection tools
            var selected = GetSelectedOffsets(offsets);
            if (selected.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Selection ({selected.Count} nodes)", EditorStyles.boldLabel);

                if (selected.Count > 1) DrawBulkEditor(offsets, selected);

                if (TryGetMesh(editor, out _, out _))
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Snap to Mesh")) SnapSelectedToMesh(editor, offsets, selected);
                    if (GUILayout.Button("Align to Normals")) AlignSelectedToNormals(editor, offsets, selected);
                    EditorGUILayout.EndHorizontal();
                }

                if (GUILayout.Button("Reset Selected Offsets"))
                {
                    Undo.RecordObject(offsets, "Reset Offsets");
                    var selectedSet = new HashSet<int>(selected.Select(s => s.index));

                    foreach (var (node, _) in selected)
                    {
                        node.positionOffset = Vector3.zero;
                        node.rotationOffset = Vector3.zero;
                        node.scaleMultiplier = 1.0f;
                        node.targetBone = node.baseBone;

                        if (node.mirrorIndex >= 0 && !selectedSet.Contains(node.mirrorIndex))
                        {
                            var mirror = offsets.nodeOffsets[node.mirrorIndex];
                            mirror.positionOffset = Vector3.zero;
                            mirror.rotationOffset = Vector3.zero;
                            mirror.scaleMultiplier = 1.0f;
                            mirror.targetBone = mirror.baseBone;
                        }
                    }

                    EditorUtility.SetDirty(offsets);
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button("Deselect All"))
                {
                    _selectedIndices.Clear();
                    SceneView.RepaintAll();
                }
            }
            
            // Start exporting section
            EditorGUILayout.Space(30);
            EditorGUILayout.HelpBox("Bake multiple offset files into a haptic prefab.", MessageType.Info);
            _offsetGroupsReorder.DoLayoutList();

            if (GUILayout.Button("Bake Into Prefab"))
            {
                _bakeError.Clear();
                var confs = new List<Config>();
                var someError = false;
                foreach (var offset in _offsetGroups)
                {
                    var (res, conf) = ConfigRepository.TryFind(offset);
                    if (conf != null)
                    {
                        confs.Add(conf.Value.config);
                    }
                    else
                    {
                        someError = true;
                        switch (res)
                        {
                            case ConfigRepository.ConfigLookupResult.NotFound:
                                _bakeError.Add($"[{offset.mapName}]: Unable to find config with name");
                                break;
                            case ConfigRepository.ConfigLookupResult.DifferentVersionExists:
                                _bakeError.Add($"[{offset.mapName}] Unable to find specified version: {offset.mapVersion}");
                                break;
                            case ConfigRepository.ConfigLookupResult.DifferentAuthorExists:
                                _bakeError.Add($"[{offset.mapName}] Unable to map with specified author: {offset.mapAuthor}");
                                break;
                            case ConfigRepository.ConfigLookupResult.Found:
                                // unreachable.
                                break;
                        }
                    }
                }
                
                if (!someError)
                {
                    var hips = editor.GetBoneTransform(HumanBodyBones.Hips);
                    if (hips != null)
                    {
                        var saveDir = Path.Join("Assets/Haptics", "Combined");
                        var buildErr = BuildPrefab.Build(saveDir, editor.AvatarRoot.gameObject, hips.parent.gameObject, editor.GetAvatar(), _offsetGroups);
                        if (buildErr.IsError())
                        {
                            _bakeError.AddRange(buildErr.Errors);
                        }
                    }
                    else
                    {
                        _bakeError.Add("Couldn't find animator on avatar, make sure it is alongside the VRC descriptor.");
                    }
                }
            }

            if (_bakeError.Count > 0)
            {
                var err = "Errors Baking Prefabs: \n";
                foreach (var smol in _bakeError)
                {
                    err += smol + "\n";
                }
                EditorGUILayout.HelpBox(err, MessageType.Error);
            }
        }

        private void ConfigToOffsetsGui(HapticEditor editor)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Create Offsets File", EditorStyles.boldLabel);

            // Refresh / load available configs
            if (_availableConfigs.Count == 0 || GUILayout.Button("Refresh Configs"))
            {
                _availableConfigs = ConfigRepository.LoadAll();
                _selectedConfigIndex = Mathf.Clamp(_selectedConfigIndex, -1, _availableConfigs.Count - 1);
            }

            // Import button
            if (GUILayout.Button("Import Config File"))
            {
                string path = EditorUtility.OpenFilePanel("Select JSON Config", "", "json");
                if (!string.IsNullOrEmpty(path))
                {
                    var imported = ConfigRepository.Import(path);
                    if (imported != null)
                    {
                        _availableConfigs = ConfigRepository.LoadAll();
                        // Auto-select the newly imported config
                        _selectedConfigIndex = _availableConfigs.FindIndex(e =>
                            e.config.meta.map_name == imported.meta.map_name &&
                            e.config.meta.map_author == imported.meta.map_author &&
                            e.config.meta.map_version == imported.meta.map_version);
                    }
                    else
                    {
                        Debug.LogError($"Unable to load config from file: {path}");
                    }
                }

                GUIUtility.ExitGUI();
                return;
            }

            // Dropdown
            if (_availableConfigs.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No configs found. Import a config or add .json files to:\n" + ConfigRepository.ConfigDirectory,
                    MessageType.Warning);
            }
            else
            {
                var displayNames = _availableConfigs.Select(e => e.displayName).ToArray();
                _selectedConfigIndex = Mathf.Clamp(_selectedConfigIndex, 0, displayNames.Length - 1);
                _selectedConfigIndex = EditorGUILayout.Popup("Config", _selectedConfigIndex, displayNames);

                var selected = _availableConfigs[_selectedConfigIndex];
                EditorGUILayout.HelpBox(
                    $"{selected.config.meta.map_name} by {selected.config.meta.map_author}" +
                    $" (v{selected.config.meta.map_version}, {selected.config.nodes.Length} nodes)", MessageType.Info);

                if (GUILayout.Button("Create Offset From Config"))
                {
                    string savePath = EditorUtility.SaveFilePanelInProject("Save Offsets Asset",
                        selected.config.meta.map_name + "_offsets", "asset", "Choose where to save the offsets file.");

                    if (!string.IsNullOrEmpty(savePath))
                    {
                        // Re-read from disk so scaling doesn't mutate the cached config
                        var freshConfig = ConfigRepository.TryLoad(selected.filePath);
                        var asset = CreateOffsetsFromConfig(freshConfig, editor.AvatarRoot.gameObject);
                        AssetDatabase.CreateAsset(asset, savePath);
                        AssetDatabase.SaveAssets();

                        Undo.RecordObject(editor, "Assign New Offsets");
                        editor.offsets = asset;
                        EditorUtility.SetDirty(editor);
                        _selectedIndices.Clear();

                        EditorGUIUtility.PingObject(asset);
                    }

                    GUIUtility.ExitGUI();
                    return;
                }
            }

            EditorGUILayout.EndVertical();
        }

        private static void ScaleConfig(Config config, GameObject avatarRoot)
        {
            VRCAvatarDescriptor aviDesc = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (aviDesc == null)
            {
                Debug.LogError($"Could not find a {nameof(VRCAvatarDescriptor)} component on Avatar Root");
                return;
            }

            Animator anim = aviDesc.GetComponent<Animator>();
            if (anim == null)
            {
                Debug.LogError($"Could not find a {nameof(Animator)} component on Avatar.");
                return;
            }

            Avatar avatar = anim.avatar;
            if (avatar == null)
            {
                Debug.LogError($"Could not find a {nameof(Avatar)} component on Avatar Root Animator component");
                return;
            }

            Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            GameObject armature = hips.parent.gameObject;

            var bones = Utils.GetBonesMap(avatar, armature);

            float eyeHeight = aviDesc.ViewPosition.y - avatarRoot.transform.position.y;
            const float standardEyeHeight = 1.585f;
            float ratio = eyeHeight / standardEyeHeight;

            foreach (Node node in config.nodes)
            {
                if (!bones.TryGetValue(node.target_bone, out var bonePos))
                {
                    Debug.LogWarning($"Could not find bone for node {node.target_bone}");
                    bonePos = armature.transform;
                }

                if (bonePos != null)
                {
                    var sizeScale = bonePos.position + (node.GetNodePosition() - bonePos.position) * ratio;
                    float yOffset = eyeHeight * (1 - ratio);
                    Vector3 offset = avatarRoot.transform.position * (1 - ratio);
                    sizeScale.x -= offset.x;
                    sizeScale.y += offset.y - yOffset;
                    sizeScale.z -= offset.z;
                    node.SetPosition(sizeScale);
                }

                node.radius *= ratio;
            }
        }

        private static OffsetsAsset CreateOffsetsFromConfig(Config config, GameObject avatarRoot)
        {
            ScaleConfig(config, avatarRoot);

            var asset = ScriptableObject.CreateInstance<OffsetsAsset>();
            asset.mapAuthor = config.meta.map_author;
            asset.mapName = config.meta.map_name;
            asset.mapVersion = config.meta.map_version;
            asset.useLowPoly = true;

            asset.nodeOffsets = new OffsetsAsset.NodeOffset[config.nodes.Length];
            for (int i = 0; i < config.nodes.Length; i++)
            {
                var src = config.nodes[i];
                var hasRay = src.ray != null;
                asset.nodeOffsets[i] = new OffsetsAsset.NodeOffset
                {
                    nodeId = src.address,
                    baseBone = src.target_bone,
                    basePosition = src.GetNodePosition(),
                    baseRotation = hasRay ? src.ray.rotation_offset : Vector3.zero,
                    baseRadius = src.radius,
                    hasRay = hasRay,
                    baseRayPositionOffset = hasRay ? src.ray.position_offset : Vector3.zero,
                    baseRayRotationOffset = hasRay ? src.ray.rotation_offset : Vector3.zero,
                    baseRayLength = hasRay ? src.ray.size : 0f,
                    targetBone = src.target_bone,
                    positionOffset = Vector3.zero,
                    rotationOffset = Vector3.zero,
                    scaleMultiplier = 1.0f,
                    rayLenMultiplier = 1.0f,
                    rayOffset = hasRay ? src.ray.position_offset.z : 0.0f,
                    
                    mirrorIndex = -1
                };
            }

            MirrorUtils.DetectMirrorPairs(asset);
            return asset;
        }

        // =====================================================================
        // Bone Group Drawing
        // =====================================================================
        private Dictionary<HumanBodyBones, List<int>> BuildBoneGroups(OffsetsAsset offsets)
        {
            var groups = new Dictionary<HumanBodyBones, List<int>>();
            for (int i = 0; i < offsets.nodeOffsets.Length; i++)
            {
                var bone = offsets.nodeOffsets[i].targetBone;
                if (!groups.ContainsKey(bone)) groups[bone] = new List<int>();
                groups[bone].Add(i);
            }

            return groups;
        }

        private void DrawBoneGroup(HapticEditor editor, HumanBodyBones bone, List<int> indices)
        {
            var offsets = editor.offsets;
            bool expanded = _expandedBones.Contains(bone);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            EditorGUI.indentLevel++;
            bool newExpanded = EditorGUILayout.Foldout(expanded, $"  {bone} ({indices.Count} nodes)", true);
            EditorGUI.indentLevel--;
            if (newExpanded != expanded)
            {
                if (newExpanded)
                    _expandedBones.Add(bone);
                else
                    _expandedBones.Remove(bone);
            }

            GUILayout.FlexibleSpace();

            if (TryGetMesh(editor, out _, out _))
            {
                if (GUILayout.Button("Snap", GUILayout.Width(45)))
                {
                    var groupNodes = indices.Select(i => (offsets.nodeOffsets[i], i)).ToList();
                    SnapSelectedToMesh(editor, offsets, groupNodes);
                }
            }

            if (GUILayout.Button("Select All", GUILayout.Width(70)))
            {
                foreach (int i in indices) _selectedIndices.Add(i);
                SceneView.RepaintAll();
            }

            EditorGUILayout.EndHorizontal();

            if (newExpanded)
            {
                EditorGUI.indentLevel++;
                foreach (int i in indices) DrawNodeRow(editor, i);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        private void DrawNodeRow(HapticEditor editor, int index)
        {
            var offsets = editor.offsets;
            var node = offsets.nodeOffsets[index];
            bool isSelected = _selectedIndices.Contains(index);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            string prefix = isSelected ? "\u25bc " : "\u25b6 ";
            string label = string.IsNullOrEmpty(node.nodeId) ? $"Node {index}" : node.nodeId;
            string mirrorTag = node.mirrorIndex >= 0 ? "  [M]" : "";

            if (GUILayout.Button(prefix + label + mirrorTag, EditorStyles.label))
            {
                if (Event.current.shift)
                {
                    if (isSelected)
                        _selectedIndices.Remove(index);
                    else
                        _selectedIndices.Add(index);
                }
                else
                {
                    if (isSelected)
                        _selectedIndices.Remove(index);
                    else
                    {
                        _selectedIndices.Clear();
                        _selectedIndices.Add(index);
                    }
                }

                SceneView.RepaintAll();
            }

            GUILayout.FlexibleSpace();

            bool boneChanged = node.targetBone != node.baseBone;
            if (boneChanged)
            {
                var prev = GUI.color;
                GUI.color = Color.yellow;
                EditorGUILayout.LabelField($"({node.baseBone})", GUILayout.Width(100));
                GUI.color = prev;
            }

            EditorGUILayout.EndHorizontal();

            if (isSelected)
            {
                EditorGUI.indentLevel++;
                Undo.RecordObject(offsets, "Edit Node Offset");

                EditorGUI.BeginChangeCheck();
                var newBone = (HumanBodyBones)EditorGUILayout.EnumPopup("Target Bone", node.targetBone);
                if (EditorGUI.EndChangeCheck())
                {
                    node.targetBone = newBone;
                    if (node.mirrorIndex >= 0)
                        offsets.nodeOffsets[node.mirrorIndex].targetBone = MirrorUtils.MirrorBone(newBone);
                }

                EditorGUI.BeginChangeCheck();
                var newPos = EditorGUILayout.Vector3Field("Position Offset", node.positionOffset);
                if (EditorGUI.EndChangeCheck())
                {
                    node.positionOffset = newPos;
                    if (node.mirrorIndex >= 0)
                    {
                        var mirror = offsets.nodeOffsets[node.mirrorIndex];
                        var fullLocal = node.basePosition + newPos;
                        mirror.positionOffset = MirrorUtils.MirrorPosition(fullLocal) - mirror.basePosition;
                    }
                }

                EditorGUI.BeginChangeCheck();
                var newRot = EditorGUILayout.Vector3Field("Rotation Offset", node.rotationOffset);
                if (EditorGUI.EndChangeCheck())
                {
                    node.rotationOffset = newRot;
                    if (node.mirrorIndex >= 0)
                    {
                        var mirror = offsets.nodeOffsets[node.mirrorIndex];
                        var fullRot = Quaternion.Euler(node.baseRotation + newRot);
                        mirror.rotationOffset = MirrorUtils.MirrorEuler(fullRot.eulerAngles) - mirror.baseRotation;
                    }
                }

                EditorGUI.BeginChangeCheck();
                var newScale = EditorGUILayout.Slider("Node Scale (both ray and sphere)", node.scaleMultiplier, 0.01f, 3.0f);
                if (EditorGUI.EndChangeCheck())
                {
                    node.scaleMultiplier = newScale;
                    if (node.mirrorIndex >= 0)
                        offsets.nodeOffsets[node.mirrorIndex].scaleMultiplier = MirrorUtils.MirrorScale(newScale);
                }
                
                EditorGUI.BeginChangeCheck();
                var newRaySize = EditorGUILayout.Slider("Ray Length", node.rayLenMultiplier, 0.001f, 0.2f);
                if (EditorGUI.EndChangeCheck())
                {
                    node.rayLenMultiplier = newRaySize;
                    if (node.mirrorIndex >= 0)
                        offsets.nodeOffsets[node.mirrorIndex].rayLenMultiplier = MirrorUtils.MirrorScale(newRaySize);
                }
                
                EditorGUI.BeginChangeCheck();
                var newRayOffset = EditorGUILayout.Slider("Z ray Offset", node.rayOffset, 0.01f, 3.0f);
                if (EditorGUI.EndChangeCheck())
                {
                    node.rayOffset = newRayOffset;
                    if (node.mirrorIndex >= 0)
                        offsets.nodeOffsets[node.mirrorIndex].rayOffset = MirrorUtils.MirrorScale(newRayOffset);
                }

                if (TryGetMesh(editor, out _, out _))
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Snap to Mesh"))
                    {
                        var single = new List<(OffsetsAsset.NodeOffset node, int index)> { (node, index) };
                        SnapSelectedToMesh(editor, offsets, single);
                    }

                    if (GUILayout.Button("Align to Normals"))
                    {
                        var single = new List<(OffsetsAsset.NodeOffset node, int index)> { (node, index) };
                        AlignSelectedToNormals(editor, offsets, single);
                    }

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Base Values", EditorStyles.miniLabel);
                GUI.enabled = false;
                EditorGUILayout.Vector3Field("Base Position", node.basePosition);
                EditorGUILayout.Vector3Field("Base Rotation", node.baseRotation);
                EditorGUILayout.FloatField("Base Radius", node.baseRadius);
                EditorGUILayout.EnumPopup("Original Bone", node.baseBone);
                if (node.mirrorIndex >= 0) EditorGUILayout.IntField("Mirror Index", node.mirrorIndex);
                GUI.enabled = true;

                EditorUtility.SetDirty(offsets);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        // =====================================================================
        // Bulk Editor
        // =====================================================================
        private void DrawBulkEditor(OffsetsAsset offsets, List<(OffsetsAsset.NodeOffset node, int index)> selected)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Bulk Edit", EditorStyles.boldLabel);

            Undo.RecordObject(offsets, "Bulk Edit Offsets");
            var first = selected[0].node;
            var selectedSet = new HashSet<int>(selected.Select(s => s.index));

            EditorGUI.showMixedValue = !selected.TrueForAll(s => s.node.targetBone == first.targetBone);
            EditorGUI.BeginChangeCheck();
            var bone = (HumanBodyBones)EditorGUILayout.EnumPopup("Target Bone", first.targetBone);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var (node, _) in selected)
                {
                    node.targetBone = bone;
                    if (node.mirrorIndex >= 0 && !selectedSet.Contains(node.mirrorIndex))
                        offsets.nodeOffsets[node.mirrorIndex].targetBone = MirrorUtils.MirrorBone(bone);
                }
            }

            EditorGUI.showMixedValue = !selected.TrueForAll(s => s.node.positionOffset == first.positionOffset);
            EditorGUI.BeginChangeCheck();
            var pos = EditorGUILayout.Vector3Field("Position Offset", first.positionOffset);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var (node, _) in selected)
                {
                    node.positionOffset = pos;
                    if (node.mirrorIndex >= 0 && !selectedSet.Contains(node.mirrorIndex))
                    {
                        var mirror = offsets.nodeOffsets[node.mirrorIndex];
                        var fullLocal = node.basePosition + pos;
                        mirror.positionOffset = MirrorUtils.MirrorPosition(fullLocal) - mirror.basePosition;
                    }
                }
            }

            EditorGUI.showMixedValue = !selected.TrueForAll(s => s.node.rotationOffset == first.rotationOffset);
            EditorGUI.BeginChangeCheck();
            var rot = EditorGUILayout.Vector3Field("Rotation Offset", first.rotationOffset);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var (node, _) in selected)
                {
                    node.rotationOffset = rot;
                    if (node.mirrorIndex >= 0 && !selectedSet.Contains(node.mirrorIndex))
                    {
                        var mirror = offsets.nodeOffsets[node.mirrorIndex];
                        var fullRot = Quaternion.Euler(node.baseRotation + rot);
                        mirror.rotationOffset = MirrorUtils.MirrorEuler(fullRot.eulerAngles) - mirror.baseRotation;
                    }
                }
            }

            EditorGUI.showMixedValue = !selected.TrueForAll(s => s.node.scaleMultiplier == first.scaleMultiplier);
            EditorGUI.BeginChangeCheck();
            var scale = EditorGUILayout.Slider("Scale Multiplier", first.scaleMultiplier, 0.01f, 3.0f);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var (node, _) in selected)
                {
                    node.scaleMultiplier = scale;
                    if (node.mirrorIndex >= 0 && !selectedSet.Contains(node.mirrorIndex))
                        offsets.nodeOffsets[node.mirrorIndex].scaleMultiplier = MirrorUtils.MirrorScale(scale);
                }
            }

            EditorGUI.showMixedValue = false;
            EditorUtility.SetDirty(offsets);
            EditorGUILayout.EndVertical();
        }

        // =====================================================================
        // Scene Handles
        // =====================================================================
        private void OnSceneGUI()
        {
            var editor = (HapticEditor)target;
            if (editor.offsets == null || editor.AvatarRoot == null) return;

            var offsets = editor.offsets;
            var rootTransform = editor.AvatarRoot;

            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.cyan }
            };

            // Two passes: occluded (faint), then visible (full)
            DrawNodePass(offsets, rootTransform, labelStyle, CompareFunction.Greater, 0.07f, false);
            DrawNodePass(offsets, rootTransform, labelStyle, CompareFunction.LessEqual, 1f, true);

            Handles.zTest = CompareFunction.Always;
        }

        private void DrawNodePass(OffsetsAsset offsets, Transform rootTransform, GUIStyle labelStyle,
            CompareFunction zTest, float alphaMultiplier, bool drawHandles)
        {
            Handles.zTest = zTest;

            for (int i = 0; i < offsets.nodeOffsets.Length; i++)
            {
                var node = offsets.nodeOffsets[i];

                var worldPos = rootTransform.TransformPoint(node.EffectivePosition);
                var worldRot = rootTransform.rotation * Quaternion.Euler(node.EffectiveRotation);
                bool isSelected = _selectedIndices.Contains(i);
                bool isMirrorOfSelected = node.mirrorIndex >= 0 && _selectedIndices.Contains(node.mirrorIndex);

                float radius = node.baseRadius * node.scaleMultiplier;

                // --- Main node disc ---
                if (isSelected)
                {
                    Handles.color = new Color(0f, 1f, 1f, 0.25f * alphaMultiplier);
                    Handles.DrawSolidDisc(worldPos, Camera.current.transform.forward, radius * 1.2f);
                    Handles.color = new Color(0f, 1f, 1f, 1f * alphaMultiplier);
                    Handles.DrawWireDisc(worldPos, Camera.current.transform.forward, radius * 1.2f);
                }
                else if (isMirrorOfSelected)
                {
                    Handles.color = new Color(1f, 0.6f, 0f, 0.2f * alphaMultiplier);
                    Handles.DrawSolidDisc(worldPos, Camera.current.transform.forward, radius * 1.1f);
                    Handles.color = new Color(1f, 0.6f, 0f, 0.8f * alphaMultiplier);
                    Handles.DrawWireDisc(worldPos, Camera.current.transform.forward, radius * 1.1f);
                }
                else
                {
                    Handles.color = new Color(1f, 1f, 1f, 0.3f * alphaMultiplier);
                    Handles.DrawWireDisc(worldPos, Camera.current.transform.forward, radius);
                }

                // --- Ray visualization ---
                if (node.hasRay && node.baseRayLength > 0f)
                {
                    float scale = node.scaleMultiplier;
                    var rayOrigin = worldPos + worldRot * node.baseRayPositionOffset;
                    var rayDir = (worldPos - rayOrigin).normalized;
                    float rayLen = node.EffectiveRayLen;
                    float rayRadius = radius * 0.4f;

                    if (isSelected)
                    {
                        Handles.color = new Color(1f, 0.3f, 0.3f, 0.2f * alphaMultiplier);
                        Handles.DrawSolidDisc(rayOrigin, Camera.current.transform.forward, rayRadius);
                        Handles.color = new Color(1f, 0.3f, 0.3f, 1f * alphaMultiplier);
                        Handles.DrawWireDisc(rayOrigin, Camera.current.transform.forward, rayRadius);
                    }
                    else
                    {
                        Handles.color = new Color(1f, 0.3f, 0.3f, 0.4f * alphaMultiplier);
                        Handles.DrawWireDisc(rayOrigin, Camera.current.transform.forward, rayRadius);
                    }

                    Handles.color = new Color(1f, 0.3f, 0.3f, 0.8f * alphaMultiplier);
                    Handles.DrawLine(rayOrigin, rayOrigin + rayDir * rayLen);

                    var rayTip = rayOrigin + rayDir * rayLen;
                    Handles.DrawSolidDisc(rayTip, Camera.current.transform.forward, 0.002f);
                }

                // --- Label (only on visible pass) ---
                if (drawHandles)
                {
                    var trimmed = node.nodeId.Replace("/avatar/parameters/Haptics/Nodes/", "");
                    string shortName = string.IsNullOrEmpty(node.nodeId) ? $"{i}" : trimmed;
                    Handles.Label(worldPos + new Vector3(0.0f, 0.02f, 0.0f), shortName, labelStyle);
                }

                // --- Click to select (only on visible pass) ---
                if (drawHandles)
                {
                    float handleSize = HandleUtility.GetHandleSize(worldPos) * 0.06f;
                    if (Handles.Button(worldPos, Quaternion.identity, handleSize, handleSize * 1.5f,
                            Handles.DotHandleCap))
                    {
                        if (Event.current.shift)
                        {
                            if (isSelected)
                                _selectedIndices.Remove(i);
                            else
                                _selectedIndices.Add(i);
                        }
                        else
                        {
                            _selectedIndices.Clear();
                            _selectedIndices.Add(i);
                        }

                        Repaint();
                    }
                }

                if (!isSelected || !drawHandles) continue;

                // --- Position handle (always visible for usability) ---
                Handles.zTest = CompareFunction.Always;

                EditorGUI.BeginChangeCheck();
                var newWorldPos = Handles.PositionHandle(worldPos, worldRot);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(offsets, "Move Node Offset");
                    var newLocal = rootTransform.InverseTransformPoint(newWorldPos);
                    node.positionOffset = newLocal - node.basePosition;

                    if (node.mirrorIndex >= 0)
                    {
                        var mirror = offsets.nodeOffsets[node.mirrorIndex];
                        var mirroredLocal = MirrorUtils.MirrorPosition(newLocal);
                        mirror.positionOffset = mirroredLocal - mirror.basePosition;
                    }

                    EditorUtility.SetDirty(offsets);
                }

                // --- Rotation handle ---
                EditorGUI.BeginChangeCheck();
                var newWorldRot = Handles.RotationHandle(worldRot, worldPos);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(offsets, "Rotate Node Offset");
                    var localRot = Quaternion.Inverse(rootTransform.rotation) * newWorldRot;
                    node.rotationOffset = localRot.eulerAngles - node.baseRotation;

                    if (node.mirrorIndex >= 0)
                    {
                        var mirror = offsets.nodeOffsets[node.mirrorIndex];
                        var mirroredRot = MirrorUtils.MirrorRotation(localRot);
                        mirror.rotationOffset = mirroredRot.eulerAngles - mirror.baseRotation;
                    }

                    EditorUtility.SetDirty(offsets);
                }

                Handles.zTest = zTest;
            }
        }

        // =====================================================================
        // Helpers
        // =====================================================================
        private static bool TryGetMesh(HapticEditor editor, out Mesh mesh, out Transform meshTransform)
        {
            bool ok = TryGetMesh(editor, out var data);
            mesh = data.mesh;
            meshTransform = data.meshTransform;
            return ok;
        }

        private List<(OffsetsAsset.NodeOffset node, int index)> GetSelectedOffsets(OffsetsAsset offsets)
        {
            var result = new List<(OffsetsAsset.NodeOffset, int)>();
            foreach (int i in _selectedIndices)
            {
                if (i >= 0 && i < offsets.nodeOffsets.Length) result.Add((offsets.nodeOffsets[i], i));
            }

            return result;
        }

        private struct MeshData
        {
            public Mesh mesh;
            public Transform meshTransform;
            public BoneWeight[] boneWeights; // null for static meshes
            public Transform[] smrBones; // null for static meshes
        }

        private static bool TryGetMesh(HapticEditor editor, out MeshData data)
        {
            data = default;
            if (editor.snapTarget == null) return false;

            var smr = editor.snapTarget.GetComponent<SkinnedMeshRenderer>() ??
                      editor.snapTarget.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null)
            {
                var baked = new Mesh();
                smr.BakeMesh(baked);
                data.mesh = baked;
                data.meshTransform = smr.transform;
                data.boneWeights = smr.sharedMesh.boneWeights;
                data.smrBones = smr.bones;
                return true;
            }

            var mf = editor.snapTarget.GetComponent<MeshFilter>() ??
                     editor.snapTarget.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                data.mesh = mf.sharedMesh;
                data.meshTransform = mf.transform;
                return true;
            }

            return false;
        }

        private static HashSet<int> GetBoneIndices(Animator animator, Transform[] smrBones, HumanBodyBones targetBone)
        {
            if (!_boneIndexCache.TryGetValue(animator, out var cache))
            {
                cache = BuildBoneIndexCache(animator, smrBones);
                _boneIndexCache[animator] = cache;
            }

            return cache.TryGetValue(targetBone, out var set) ? set : new HashSet<int>();
        }
        
        private static Dictionary<HumanBodyBones, HashSet<int>> BuildBoneIndexCache(Animator animator, Transform[] smrBones)
        {
            // Build lookup: transform -> HumanBodyBones
            var humanBoneByTransform = new Dictionary<Transform, HumanBodyBones>();
            foreach (HumanBodyBones hb in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (hb == HumanBodyBones.LastBone) continue;
                var t = animator.GetBoneTransform(hb);
                if (t != null) humanBoneByTransform[t] = hb;
            }

            // For each SMR bone, walk up to find its nearest humanoid ancestor
            var result = new Dictionary<HumanBodyBones, HashSet<int>>();
            for (int i = 0; i < smrBones.Length; i++)
            {
                var t = smrBones[i];
                if (t == null) continue;

                var nearest = FindNearestHumanBone(t, humanBoneByTransform);
                if (nearest == HumanBodyBones.LastBone) continue;

                if (!result.ContainsKey(nearest))
                    result[nearest] = new HashSet<int>();
                result[nearest].Add(i);
            }

            return result;
        }

        private static HumanBodyBones FindNearestHumanBone(Transform t, Dictionary<Transform, HumanBodyBones> map)
        {
            var current = t;
            while (current != null)
            {
                if (map.TryGetValue(current, out var bone))
                    return bone;
                current = current.parent;
            }
            return HumanBodyBones.LastBone;
        }

        private void SnapSelectedToMesh(HapticEditor editor, OffsetsAsset offsets,
            List<(OffsetsAsset.NodeOffset node, int index)> selected)
        {
            if (!TryGetMesh(editor, out var md)) return;

            var root = editor.AvatarRoot;
            Undo.RecordObject(offsets, "Snap Nodes to Mesh");

            var selectedSet = new HashSet<int>(selected.Select(s => s.index));

            foreach (var (node, _) in selected)
            {
                var worldPos = root.TransformPoint(node.EffectivePosition);

                HashSet<int> boneIdx = null;
                if (md.boneWeights != null && md.smrBones != null)
                    boneIdx = GetBoneIndices(editor.AvatarAnimator, md.smrBones, node.targetBone);

                var hit = MeshUtils.FindClosestPoint(md.mesh, md.meshTransform, worldPos, md.boneWeights, boneIdx);

                var snappedLocal = root.InverseTransformPoint(hit.worldPoint);
                node.positionOffset = snappedLocal - node.basePosition;

                // Mirror: only if mirror is not also being snapped independently
                if (node.mirrorIndex >= 0 && !selectedSet.Contains(node.mirrorIndex))
                {
                    var mirror = offsets.nodeOffsets[node.mirrorIndex];
                    var mirroredLocal = MirrorUtils.MirrorPosition(snappedLocal);
                    mirror.positionOffset = mirroredLocal - mirror.basePosition;
                }
            }

            EditorUtility.SetDirty(offsets);
            SceneView.RepaintAll();
        }

        private void AlignSelectedToNormals(HapticEditor editor, OffsetsAsset offsets,
            List<(OffsetsAsset.NodeOffset node, int index)> selected)
        {
            if (!TryGetMesh(editor, out var md)) return;

            var root = editor.AvatarRoot;
            Undo.RecordObject(offsets, "Align Nodes to Normals");

            var selectedSet = new HashSet<int>(selected.Select(s => s.index));

            foreach (var (node, _) in selected)
            {
                var worldPos = root.TransformPoint(node.EffectivePosition);

                HashSet<int> boneIdx = null;
                if (md.boneWeights != null && md.smrBones != null)
                    boneIdx = GetBoneIndices(editor.AvatarAnimator, md.smrBones, node.targetBone);

                var hit = MeshUtils.FindClosestPoint(md.mesh, md.meshTransform, worldPos, md.boneWeights, boneIdx);

                var worldRot = Quaternion.LookRotation(hit.worldNormal);
                var localRot = Quaternion.Inverse(root.rotation) * worldRot;
                node.rotationOffset = localRot.eulerAngles - node.baseRotation;

                if (node.mirrorIndex >= 0 && !selectedSet.Contains(node.mirrorIndex))
                {
                    var mirror = offsets.nodeOffsets[node.mirrorIndex];
                    var mirroredRot = MirrorUtils.MirrorRotation(localRot);
                    mirror.rotationOffset = mirroredRot.eulerAngles - mirror.baseRotation;
                }
            }

            EditorUtility.SetDirty(offsets);
            SceneView.RepaintAll();
        }
    }
}