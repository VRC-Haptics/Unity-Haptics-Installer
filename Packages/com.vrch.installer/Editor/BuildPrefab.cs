using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using com.vrcfury.api;
using Common;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.PlayerLoop;
using VRC;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;

namespace Editor
{
    public class BuildPrefab
    {
        public readonly struct BuildResult
        {
            public enum BuildResultStatus
            {
                Completed,
                WithErrors,
                UnableToResolve,
                Failed,
            }

            public readonly BuildResultStatus Result;
            public readonly Transform Root;
            public readonly string[] Errors;

            private BuildResult(BuildResultStatus status, Transform integrationRoot, string[] errors)
            {
                Result = status;
                Root = integrationRoot;
                Errors = errors;
            }

            public bool IsError()
            {
                return Result != BuildResultStatus.Completed;
            }

            public static BuildResult Completed(Transform root) => new(BuildResultStatus.Completed, root, null);
            public static BuildResult WithText(string[] errors) => new(BuildResultStatus.WithErrors, null, errors);
            public static BuildResult FailVariant(BuildResultStatus var) => new(var, null, null);
        }

        private const string GeneratedAssetPath = "Assets/Haptics/Generated/";
        private static MenuBuilder _menuBuilder = new MenuBuilder();
        private static VisualsBuilder _visualsBuilder = new VisualsBuilder();
        private static Resolved _resolvedNodes;
        private static Dictionary<HumanBodyBones, Transform> _boneRoots = new();

        public static BuildResult Build(string saveDir, GameObject aviRoot, GameObject armatureRoot, Avatar animAvatar,
            List<OffsetsAsset> offsets)
        {
            _menuBuilder = new();
            _visualsBuilder = new();
            _resolvedNodes = new Resolved();
            _boneRoots = new();
            
            Utils.CreateDirectoryFromAssetPath(saveDir);
            
            var root = new GameObject("Haptic Integration");
            root.transform.SetParent(aviRoot.transform, false);
            _menuBuilder.SetAnimationRoot(root);

            var nodes = Resolved.TryResolve(offsets);
            if (nodes == null)
            {
                return BuildResult.FailVariant(BuildResult.BuildResultStatus.UnableToResolve);
            }

            _resolvedNodes = nodes.Value;

            // pull modified targets from offsets.
            var bonesUsed = _resolvedNodes.Nodes.SelectMany((pair, idx) => pair.Value.Select(n => n.TargetBone))
                .Distinct()
                .ToList();

            foreach (var offset in offsets)
            {
                _menuBuilder.AddParam(offset.IDAddress);
            }

            // create roots for each of our human body bones we will need
            var bonePositions = Utils.GetBonesMap(animAvatar, armatureRoot);
            var pairs = new List<(HumanBodyBones, GameObject)>();
            foreach (var bone in bonesUsed)
            {
                var go = new GameObject(bone.ToString());
                go.transform.SetParent(root.transform, false);
                bonePositions.TryGetValue(bone, out var bonePos);
                if (bonePos == null)
                {
                    Debug.LogError($"A bone is required: {bone}, But isn't present on the animators avatar.");
                    return BuildResult.WithText(new[]
                    {
                        $"A bone is required: {bone}, But isn't present on the animators avatar."
                    });
                }

                go.transform.SetPositionAndRotation(bonePos.position, go.transform.rotation);
                FuryComponents.CreateArmatureLink(go).LinkTo(bone);
                pairs.Add((bone, go));
                _boneRoots.Add(bone, go.transform);
            }

            _visualsBuilder.SetBones(pairs);

            // build each node
            foreach (var pair in _resolvedNodes.Nodes)
            {
                var bone = pair.Key;
                bonePositions.TryGetValue(bone, out var bonePos);
                foreach (var (node, i) in pair.Value.Select((n, i) => (n, i)))
                {
                    var go = new GameObject($"{bone.ToString()}_{i}");
                    // zero to prefab root
                    go.transform.SetParent(root.transform, false);
                    // offset by whats needed
                    go.transform.localPosition = node.Pos;
                    go.transform.localRotation = Quaternion.Euler(node.Rot);
                    // transform back into the parents space when parenting to node.
                    go.transform.SetParent(_boneRoots[bone], true);

                    var vis = new VisualsBuilder.VisualInfo
                    {
                        GlobalPosition = go.transform.position, Radius = node.SphereRadius
                    };
                    _visualsBuilder.AddNode(bone, vis);

                    // build individual node properties
                    var res = BuildNode(go, node);
                    if (res.IsError())
                    {
                        return BuildResult.WithText(new[] { $"Unable to build node: {node.Address}" });
                    }
                }
            }

            _visualsBuilder.Build(saveDir);
            _menuBuilder.GenerateClips(saveDir);
            _menuBuilder.MakeMenu(root, saveDir);
            return BuildResult.Completed(root.transform);
        }

        /// <summary>
        /// Builds out node on game object, assuming position/rotation has been applied.
        /// </summary>
        /// <returns>Returns the failure result of the build, on success returns nothing</returns>
        private static BuildResult BuildNode(GameObject root, MinNode node)
        {
            // build contact
            if (node.SphereRadius > 0.001)
            {
               var recv = root.AddComponent<VRCContactReceiver>();
                           recv.parameter = node.Address;
                           recv.allowOthers = true;
                           recv.allowSelf = false;
                           recv.localOnly = true;
                           recv.radius = node.SphereRadius;
                           recv.collisionTags = node.Tags.ToList();
                           recv.receiverType = ContactReceiver.ReceiverType.Proximity;
                           _menuBuilder.AddContact(recv); 
            }
            

            // only build ray if it has been configured by the node.
            if (node.RayLen > 0.0001)
            {
                var rayGo = new GameObject("ray");
                rayGo.transform.SetParent(root.transform, false);
                rayGo.transform.localPosition = new Vector3(rayGo.transform.localPosition.x,
                    rayGo.transform.localPosition.y, rayGo.transform.localPosition.z + node.RayPos);

                var rayTarget = new GameObject("rayTarget");
                rayTarget.transform.localPosition = Vector3.zero;
                rayTarget.transform.SetParent(rayGo.transform, false);

                var ray = rayGo.AddComponent<VRCRaycast>();
                ray.Parameter = node.Address;
                ray.ResultTransform = rayTarget.transform;
                ray.Distance = node.RayLen;
                ray.BehaviorOnMiss = VRCRaycast.MissBehavior.SnapToStart;
                ray.RaycastCollisionMode = VRCRaycast.CollisionMode.HitWorldsAndPlayers;
                ray.ApplyTransformScale = true;

                _menuBuilder.AddRaycast(ray);
            }

            return BuildResult.Completed(root.transform);
        }

        private class VisualsBuilder
        {
            public struct VisualInfo
            {
                public float Radius;
                public Vector3 GlobalPosition;
            }

            private const string VisualPrefabPath = "Packages/com.vrch.haptics-installer/Assets/Visualizers/default_icosphere_20tri.prefab";
            private GameObject _prefab;
            private List<(HumanBodyBones, GameObject)> _boneRef = new();
            private Dictionary<HumanBodyBones, List<VisualInfo>> _vis = new();

            /// <summary>
            /// All human body bones and their in-game objects we will be representing. 
            /// </summary>
            /// <param name="boneRef"></param>
            public void SetBones(List<(HumanBodyBones, GameObject)> boneRef)
            {
                _boneRef = boneRef;
            }

            public void AddNode(HumanBodyBones bone, VisualInfo info)
            {
                if (!_vis.TryGetValue(bone, out var list))
                {
                    list = new List<VisualInfo>();
                    _vis[bone] = list;
                }

                list.Add(info);
            }

            public void Build(string saveDir)
            {
                _prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPrefabPath);
                if (_prefab == null)
                {
                    Debug.LogError($"Could not load visual prefab at: {VisualPrefabPath}");
                    return;
                }

                foreach (var (bone, parent) in _boneRef)
                {
                    if (!_vis.TryGetValue(bone, out var infos)) continue;

                    var combines = new List<CombineInstance>();

                    foreach (var info in infos)
                    {
                        var temp = Object.Instantiate(_prefab, parent.transform);
                        temp.transform.position = info.GlobalPosition;

                        float scale = info.Radius * 40f;
                        temp.transform.localScale = Vector3.one * scale;

                        var mf = temp.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null)
                        {
                            combines.Add(new CombineInstance
                            {
                                mesh = mf.sharedMesh,
                                // transform relative to the parent bone
                                transform = parent.transform.worldToLocalMatrix * temp.transform.localToWorldMatrix
                            });
                        }

                        Object.DestroyImmediate(temp);
                    }

                    if (combines.Count == 0) continue;

                    var combined = new Mesh();
                    combined.CombineMeshes(combines.ToArray(), true, true);
                    combined.RecalculateNormals();
                    combined.RecalculateBounds();

                    var visual = new GameObject($"Visual_{bone}");
                    visual.transform.SetParent(parent.transform, false);
                    visual.AddComponent<MeshFilter>().sharedMesh = combined;
                    visual.AddComponent<MeshRenderer>().sharedMaterial =
                        _prefab.GetComponent<MeshRenderer>().sharedMaterial;
                    _menuBuilder.AddVisual(visual);

                    Utils.CreateDirectoryFromAssetPath(Path.Combine(saveDir, $"VisMesh_{bone}.asset"));
                    AssetDatabase.CreateAsset(combined, Path.Combine(saveDir, $"VisMesh_{bone}.asset"));
                    AssetDatabase.SaveAssets();
                }
            }
        }

        private class MenuBuilder
        {
            private const string PropertyEnable = "m_IsActive";
            private const string MenuAssetsPath = "Packages/com.vrch.haptics-installer/Assets/Menu/";
            private static readonly AnimationCurve CurveOn = AnimationCurve.Constant(0f, 0f, 1f);
            private static readonly AnimationCurve CurveOff = AnimationCurve.Constant(0f, 0f, 0f);

            public const string ParamGlobalVisualizer = "haptic/global/show";
            public const string ParamColliders = "haptic/global/enable_colliders";
            public const string ParamRays = "haptic/global/enable_rays";

            private GameObject _animationRoot;
            private AnimatorController _animatorController;
            private List<string> additionalParams = new();
            private List<VRCRaycast> rays = new List<VRCRaycast>();
            private List<VRCContactReceiver> contacts = new List<VRCContactReceiver>();
            private List<GameObject> visuals = new List<GameObject>();

            public void AddParam(string param)
            {
                additionalParams.Add(param);
            }

            public void SetAnimationRoot(GameObject animationRoot)
            {
                _animationRoot = animationRoot;
            }

            public void AddRaycast(VRCRaycast raycast)
            {
                rays.Add(raycast);
            }

            public void AddContact(VRCContactReceiver receiver)
            {
                contacts.Add(receiver);
            }

            public void AddVisual(GameObject visual)
            {
                visuals.Add(visual);
            }

            /// <summary>
            ///  Generates the animators to toggle everything.
            /// </summary>
            /// <param name="saveDir"></param>
            public void GenerateClips(string saveDir)
            {
                // create clips for turning on and off each category.
                AnimationClip visualOn = new AnimationClip { name = "visuals_On" };
                AnimationClip visualOff = new AnimationClip { name = "visuals_Off" };
                foreach (var obj in visuals)
                {
                    string path = AnimationUtility.CalculateTransformPath(obj.transform, _animationRoot.transform);
                    visualOn.SetCurve(path, typeof(GameObject), PropertyEnable, CurveOn);
                    visualOff.SetCurve(path, typeof(GameObject), PropertyEnable, CurveOff);
                }

                AssetDatabase.CreateAsset(visualOn, Path.Combine(saveDir, "visuals_On.anim"));
                AssetDatabase.CreateAsset(visualOff, Path.Combine(saveDir, "visuals_Off.anim"));

                AnimationClip raysOn = new AnimationClip { name = "rays_On" };
                AnimationClip raysOff = new AnimationClip { name = "rays_Off" };
                foreach (var obj in rays)
                {
                    string path = AnimationUtility.CalculateTransformPath(obj.transform, _animationRoot.transform);
                    raysOn.SetCurve(path, typeof(GameObject), PropertyEnable, CurveOn);
                    raysOff.SetCurve(path, typeof(GameObject), PropertyEnable, CurveOff);
                }

                AssetDatabase.CreateAsset(raysOn, Path.Combine(saveDir, "rays_On.anim"));
                AssetDatabase.CreateAsset(raysOff, Path.Combine(saveDir, "rays_Off.anim"));

                AnimationClip contactsOn = new AnimationClip { name = "contacts_On" };
                AnimationClip contactsOff = new AnimationClip { name = "contacts_Off" };
                foreach (var obj in contacts)
                {
                    string path = AnimationUtility.CalculateTransformPath(obj.transform, _animationRoot.transform);
                    contactsOn.SetCurve(path, typeof(VRCContactReceiver), "m_Enabled", CurveOn);
                    contactsOff.SetCurve(path, typeof(VRCContactReceiver), "m_Enabled", CurveOff);
                }

                AssetDatabase.CreateAsset(contactsOn, Path.Combine(saveDir, "contacts_On.anim"));
                AssetDatabase.CreateAsset(contactsOff, Path.Combine(saveDir, "contacts_Off.anim"));

                // initiate controller
                AnimatorController controller =
                    AnimatorController.CreateAnimatorControllerAtPath(Path.Combine(saveDir, "toggles.controller"));
                controller.AddParameter(ParamColliders, AnimatorControllerParameterType.Bool);
                controller.AddParameter(ParamRays, AnimatorControllerParameterType.Bool);
                controller.AddParameter(ParamGlobalVisualizer, AnimatorControllerParameterType.Bool);

                AddToggleLayer(controller, "Visuals", ParamGlobalVisualizer, visualOn, visualOff);
                AddToggleLayer(controller, "Rays", ParamRays, raysOn, raysOff);
                AddToggleLayer(controller, "Contacts", ParamColliders, contactsOn, contactsOff);

                controller.MarkDirty();
                _animatorController = controller;
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            /// <summary>
            ///  Inserts VRCFury menu merger on avatar root. 
            /// </summary>
            /// <param name="prefabRoot"></param>
            public void MakeMenu(GameObject prefabRoot, string saveDir)
            {
                // define our copies paths
                string rootMenuPath = saveDir + "Menu_Root.asset";
                string mainMenuPath = saveDir + "Menu_Main.asset";
                string parametersPath = saveDir + $"Parameters_Main.asset";
            
                // Create Copy of pre-generated assets.
                AssetDatabase.CopyAsset(MenuAssetsPath + "Menu_Root.asset", rootMenuPath);
                AssetDatabase.CopyAsset(MenuAssetsPath + "Menu_Main.asset", mainMenuPath);
                AssetDatabase.CopyAsset(MenuAssetsPath + $"Parameters_Basic.asset", parametersPath);
                
                var rootMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(rootMenuPath);
                var mainMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(mainMenuPath);
                var menuParameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(parametersPath);

                // append identifying parameters
                var current = menuParameters.parameters.ToList();
                foreach (var param in additionalParams)
                {
                    current.Add(new VRCExpressionParameters.Parameter
                    {
                        name = param,
                        valueType = VRCExpressionParameters.ValueType.Float,
                        defaultValue = 0f,
                        saved = false,
                        networkSynced = true
                    });
                }
                menuParameters.parameters = current.ToArray();
                
                // update the menu references
                rootMenu.controls.First().subMenu = mainMenu;
                rootMenu.Parameters = menuParameters;
                mainMenu.Parameters = menuParameters;
                
                // ensure saved.
                rootMenu.MarkDirty();
                mainMenu.MarkDirty();
                menuParameters.MarkDirty();
                AssetDatabase.SaveAssets();
                
                var furyCon = FuryComponents.CreateFullController(prefabRoot);
                    furyCon.AddParams(menuParameters);
                    furyCon.AddController(_animatorController);
                    furyCon.AddGlobalParam("*");
                    furyCon.AddMenu(rootMenu);
            }

            private void AddToggleLayer(AnimatorController controller, string layerName, string paramName,
                AnimationClip onClip, AnimationClip offClip)
            {
                var layer = new AnimatorControllerLayer
                {
                    name = layerName, defaultWeight = 1f, stateMachine = new AnimatorStateMachine()
                };

                // Unity requires the state machine to be saved as a sub-asset
                AssetDatabase.AddObjectToAsset(layer.stateMachine, AssetDatabase.GetAssetPath(controller));

                var stateOff = layer.stateMachine.AddState("Off");
                stateOff.writeDefaultValues = true;
                stateOff.motion = offClip;
                var stateOn = layer.stateMachine.AddState("On");
                stateOn.motion = onClip;
                stateOn.writeDefaultValues = true;

                layer.stateMachine.defaultState = stateOff;

                // off -> on when param is true
                var toOn = stateOff.AddTransition(stateOn);
                toOn.hasExitTime = false;
                toOn.duration = 0f;
                toOn.AddCondition(AnimatorConditionMode.If, 0, paramName);

                // on -> off when param is false
                var toOff = stateOn.AddTransition(stateOff);
                toOff.hasExitTime = false;
                toOff.duration = 0f;
                toOff.AddCondition(AnimatorConditionMode.IfNot, 0, paramName);

                controller.AddLayer(layer);
            }
        }

        private readonly struct MinNode
        {
            public const string OscPrefix = "/avatar/parameters/";

            // god this language is not ergonomic
            public static readonly IReadOnlyList<string> CollisionTags =
                new[] { "Head", "Hand", "Foot", "Torso", "HapticCollider", "Finger" };

            public readonly string Address;
            public readonly HumanBodyBones TargetBone;
            public readonly string[] Tags;
            public readonly Vector3 Pos;
            public readonly Vector3 Rot;

            /// How far to move the ray relative to main node along Z axis.
            public readonly float RayPos;

            public readonly float RayLen;

            /// Radius that the sphere should be
            public readonly float SphereRadius;

            MinNode(string address, HumanBodyBones targetBone, string[] tags, Vector3 pos, Vector3 rot, float rayPos,
                float sphereRadius, float rayLen)
            {
                Address = address;
                TargetBone = targetBone;
                Tags = tags;
                Pos = pos;
                Rot = rot;
                RayPos = rayPos;
                RayLen = rayLen;
                SphereRadius = sphereRadius;
            }

            public static MinNode? TryMinNode(OffsetsAsset.NodeOffset offset)
            {
                if (!offset.nodeId.Contains(OscPrefix))
                {
                    Debug.LogError(
                        $"Node id should start with `/avatar/parameters/`, base configuration is likely broken: {offset.nodeId}");
                    return null;
                }

                var trimmed = offset.nodeId.Replace(OscPrefix, "");

                return new MinNode(trimmed, offset.targetBone, CollisionTags.ToArray(), offset.EffectivePosition,
                    offset.EffectiveRotation, offset.EffectiveRayPos, offset.EffectiveRadius, offset.EffectiveRayLen);
            }
        }

        private struct Resolved
        {
            public Dictionary<HumanBodyBones, List<MinNode>> Nodes;

            public static Resolved? TryResolve(List<OffsetsAsset> offsets)
            {
                var dict = new Dictionary<HumanBodyBones, List<MinNode>>();
                foreach (var offset in offsets)
                {
                    foreach (var node in offset.nodeOffsets)
                    {
                        var min = MinNode.TryMinNode(node);
                        if (min == null)
                        {
                            Debug.LogError($"[{offset.mapName}] Unable to process node: {node.nodeId}");
                            return null;
                        }

                        if (!dict.TryGetValue(node.targetBone, out var list))
                        {
                            list = new List<MinNode>();
                            dict[node.targetBone] = list;
                        }

                        list.Add(min.Value);
                    }
                }

                var temp = new Resolved { Nodes = dict };
                return temp;
            }
        }
    }
}