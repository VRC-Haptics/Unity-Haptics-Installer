using System.Collections.Generic;
using System.Linq;
using com.vrcfury.api;
using com.vrcfury.api.Components;
using Common;
using HapticsInstaller.Runtime;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;

namespace Editor.build_prefab
{
    public static class OffsetBuilder
    {
        private const string GlobalVisualizerParam = "haptic/global/show";
        private const string GlobalIntensityParam = "haptic/global/intensity";
        private const string OscPrefix = "/avatar/parameters/";

        /// <summary>
        /// Builds a baked prefab directly from config + offsets. No intermediate step.
        /// </summary>
        public static GameObject Build(GameObject avatarRoot, Config conf, OffsetCollection offsets)
        {
            string savePath = $"Assets/Haptics/Baked/{conf.meta.map_author}_{conf.meta.map_name}/";
            Utils.CreateDirectoryFromAssetPath(savePath);

            string baseName = $"Haptic_{conf.meta.map_name}_{conf.meta.map_author}";
            GameObject root = new GameObject(baseName);
            root.transform.SetParent(avatarRoot.transform);
            root.transform.localPosition = Vector3.zero;

            // apply avatar scaling
            float ratio = GetAvatarScaleRatio(avatarRoot);

            // group nodes by bone
            var boneGroups = new Dictionary<HumanBodyBones, List<(Node node, NodeOffset offset)>>();
            for (int i = 0; i < conf.nodes.Length; i++)
            {
                var node = conf.nodes[i];
                if (node.is_external_address) continue;

                var offset = i < offsets.offsets.Count ? offsets.offsets[i] : new NodeOffset { configIndex = i };

                if (!boneGroups.ContainsKey(node.target_bone))
                    boneGroups[node.target_bone] = new();

                boneGroups[node.target_bone].Add((node, offset));
            }

            // build per-bone groups
            GameObject nodesParent = new GameObject("nodes");
            nodesParent.transform.SetParent(root.transform);

            string visPrefabPath = "Packages/com.vrch.haptics-installer/Assets/Visualizers/default_icosphere_20tri.prefab";
            GameObject visPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(visPrefabPath);

            var toggle = new ToggleBuilder(root, GlobalVisualizerParam, false, savePath, "VisualsToggle");

            foreach (var (bone, entries) in boneGroups)
            {
                GameObject boneGroup = new GameObject(bone.ToString());
                boneGroup.transform.SetParent(nodesParent.transform);
                FuryComponents.CreateArmatureLink(boneGroup).LinkTo(bone);

                foreach (var (node, offset) in entries)
                {
                    string localAddr = node.address.StartsWith(OscPrefix)
                        ? node.address[OscPrefix.Length..]
                        : node.address;

                    // compute final position: config base + offset, scaled
                    Vector3 finalPos = offset.ApplyPosition(node.GetNodePosition());
                    float finalRadius = offset.ApplyRadius(node.radius) * ratio;

                    GameObject nodeObj = new GameObject($"{offset.configIndex}_{conf.meta.map_name}");
                    nodeObj.transform.SetParent(boneGroup.transform);
                    nodeObj.transform.localPosition = finalPos;

                    // contact receiver
                    var recv = nodeObj.AddComponent<VRCContactReceiver>();
                    recv.parameter = localAddr;
                    recv.allowOthers = true;
                    recv.allowSelf = false;
                    recv.localOnly = true;
                    recv.radius = finalRadius;
                    recv.collisionTags = new List<string>
                        { "Head", "Hand", "Foot", "Torso", "HapticCollider", "Finger" };
                    recv.receiverType = ContactReceiver.ReceiverType.Proximity;

                    var tb = nodeObj.AddComponent<TargetBone>();
                    tb.targetBone = node.target_bone;
                }

                // combined visualizer per bone
                if (visPrefab != null)
                {
                    var visuals = BuildCombinedVisuals(boneGroup, visPrefab, savePath);
                    if (visuals != null) toggle.AddObject(visuals);
                }
            }

            var controller = toggle.Finalize();

            // menu
            string generatedPrefabFolder = $"{BuildFromConfig.GeneratedAssetPath}/{conf.meta.map_author} {conf.meta.map_name}/";
            var (rootMenu, parameters) = BuildFromConfig.BuildMenu_Static(conf, generatedPrefabFolder);
            var fury = FuryComponents.CreateFullController(root);
            fury.AddParams(parameters);
            if (controller != null) fury.AddController(controller);
            fury.AddGlobalParam(GlobalIntensityParam);
            fury.AddGlobalParam(GlobalVisualizerParam);
            fury.AddMenu(rootMenu);

            PrefabUtility.SaveAsPrefabAssetAndConnect(
                root,
                savePath + $"{baseName}.prefab",
                InteractionMode.UserAction);

            Debug.Log($"Built haptic prefab: {baseName} with {conf.nodes.Length} nodes");
            return root;
        }

        private static float GetAvatarScaleRatio(GameObject avatarRoot)
        {
            var desc = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (desc == null) return 1f;
            float eyeHeight = desc.ViewPosition.y - avatarRoot.transform.position.y;
            return eyeHeight / 1.585f;
        }

        private static GameObject BuildCombinedVisuals(GameObject boneGroup, GameObject visPrefab, string savePath)
        {
            var combine = new List<CombineInstance>();
            var worldToLocal = boneGroup.transform.worldToLocalMatrix;

            for (int i = 0; i < boneGroup.transform.childCount; i++)
            {
                Transform node = boneGroup.transform.GetChild(i);
                var contact = node.GetComponent<VRCContactReceiver>();
                if (contact == null) continue;

                GameObject temp = Object.Instantiate(visPrefab, node.position, node.rotation);
                temp.transform.localScale *= contact.radius / 0.0375f;

                if (temp.TryGetComponent(out MeshFilter mf))
                {
                    combine.Add(new CombineInstance
                    {
                        mesh = mf.sharedMesh,
                        transform = worldToLocal * mf.transform.localToWorldMatrix
                    });
                }
                Object.DestroyImmediate(temp);
            }

            if (combine.Count == 0) return null;

            GameObject combined = new GameObject("visuals");
            combined.transform.SetParent(boneGroup.transform);

            Mesh mesh = new Mesh();
            mesh.CombineMeshes(combine.ToArray());
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            string meshPath = savePath + $"Vis_{boneGroup.name}.asset";
            Utils.CreateDirectoryFromAssetPath(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            combined.AddComponent<MeshFilter>().mesh = mesh;
            combined.AddComponent<MeshRenderer>().sharedMaterial =
                visPrefab.GetComponent<MeshRenderer>().sharedMaterial;

            return combined;
        }

        /// <summary>
        /// Simplified menu builder — same logic as before but streamlined.
        /// </summary>
        private static (VRCExpressionsMenu, VRCExpressionParameters) BuildMenu(Config conf)
        {
            string generatedPrefabFolder = $"{BuildFromConfig.GeneratedAssetPath}/{conf.meta.map_author} {conf.meta.map_name}/";
            return BuildFromConfig.BuildMenu_Static(conf, generatedPrefabFolder);
        }
        
        
    }
}