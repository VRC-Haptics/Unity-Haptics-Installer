using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using com.vrcfury.api;
using com.vrcfury.api.Components;
using HapticsInstaller.Runtime;
using HarmonyLib;
using UnityEditor;
using VRC;
using VRC.Dynamics;
using VRC.PackageManagement.Core.Types;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;

namespace Editor
{
    /// <summary>
    /// Provides the Build() function, which builds an entire prefab.
    /// </summary>
    public abstract class BuildFromConfig
    {
        // File directories
        private const string GeneratedAssetPath = "Assets/Haptics/Generated/";
        private const string MeshesPath = "Assets/Haptics/Assets/Mesh";
        private const string ScriptsPath = "Assets/Haptics/Assets/Scripts";
        private const string PackageScriptsPath = "Packages/com.vrch.haptics-installer/Assets/Scripts/";
        
        // OSC Parameter Paths
        private const string GlobalVisualizerParam = "/haptic/global/show";
        private const string GlobalIntensityParam = "/haptic/global/intensity";
        
        private static string _baseName = "";
        private static GameObject _prefabRoot;
        private static GameObject _nodesParent;
        private static GameObject _menuParent;
        private static bool _useLowPoly = true;
        
        /// <summary>
        /// Builds a standalone unoptimized haptic prefab under the parent object.
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="conf"></param>
        /// <param name="lowPoly"></param>
        /// <returns>Instance of the object on the avatar</returns>
        public static GameObject Build(GameObject parent, Config conf, bool lowPoly)
        {
            _useLowPoly = lowPoly;
            
            // setup base structure
            _baseName = "Haptic-Prefab_" + conf.meta.map_name + "_" + conf.meta.map_author;
            _prefabRoot = new GameObject(_baseName);
            _nodesParent = new GameObject("nodes");
            _menuParent = new GameObject("menu");
            _prefabRoot.transform.SetParent(parent.transform);
            _prefabRoot.transform.localPosition = Vector3.zero;
            _menuParent.transform.SetParent(_prefabRoot.transform);
            _menuParent.transform.localPosition = Vector3.zero;
            _nodesParent.transform.SetParent(_prefabRoot.transform);
            _nodesParent.transform.localPosition = Vector3.zero;
            
            // build nodes
            for (int i = 0; i < conf.nodes.Length; i++)
            {
                if (conf.nodes[i].is_external_address)
                {
                    Debug.Log("Skipping creating a node for external address");
                    continue;
                }
                BuildHapticNode(conf.nodes[i], i, conf.meta.map_name);
            }
            
            // build the menu for this prefab;
            var (expressionsMenu, expressionsParameters) = BuildMenu(conf);
            
            // Create Controller Merger
            FuryFullController menu = FuryComponents.CreateFullController(_menuParent);
            menu.AddParams(expressionsParameters);
            menu.AddGlobalParam(GlobalIntensityParam);
            menu.AddGlobalParam(GlobalVisualizerParam);
            menu.AddMenu(expressionsMenu);
            
            // Add data script
            var menuDataScript = _menuParent.AddComponent<MenuData>();
            menuDataScript.vrcMenu = expressionsMenu;
            
            // create prefab for the finished product
            PrefabUtility.SaveAsPrefabAssetAndConnect(
                _prefabRoot, 
                $"Assets/Haptics/{conf.meta.map_author}_{conf.meta.map_name}_{conf.meta.map_version}.prefab", 
                InteractionMode.UserAction);
            
            return _prefabRoot;
        }

        /// <summary>
        /// Builds the in-game menu for this prefab and places the generated assets in the generated assets path.
        /// </summary>
        /// <param name="conf">The Config we are generating a prefab for.</param>
        /// <returns></returns>
        private static (VRCExpressionsMenu, VRCExpressionParameters) BuildMenu(Config conf)
        {
            // create save directories.
            string generatedPrefabFolder = $"{GeneratedAssetPath}/{conf.meta.map_author} {conf.meta.map_name}/";
            Utils.CreateDirectoryFromAssetPath(generatedPrefabFolder);
            string rootMenuPath = generatedPrefabFolder + "Menu_Root.asset";
            string mainMenuPath = generatedPrefabFolder + "Menu_Main.asset";
            string prefabMenuPath = generatedPrefabFolder + $"Menu_{conf.meta.map_name}.asset"; // eventually support per/prefab devices
            string parametersPath = generatedPrefabFolder + $"Parameters_{conf.meta.map_name}.asset";
            
            // Create Copy of pre-generated assets.
            string menuAssetsPath = "Packages/com.vrch.haptics-installer/Assets/Menu/";
            AssetDatabase.CopyAsset(menuAssetsPath + "Menu_Root.asset", rootMenuPath);
            AssetDatabase.CopyAsset(menuAssetsPath + "Menu_Main.asset", mainMenuPath);
            AssetDatabase.CopyAsset(menuAssetsPath + $"Parameters_Basic.asset", parametersPath);
            
            // load copies into memory
            var rootMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(rootMenuPath);
            var mainMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(mainMenuPath);
            var menuParameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(parametersPath);
            
            // Point internal references to new copies
            rootMenu.Parameters = menuParameters;
            rootMenu.controls.First().subMenu = mainMenu;
            mainMenu.Parameters = menuParameters;
            
            // Add advertising parameter.
            var paramlist = menuParameters.parameters.ToList();
            var advertisingParameter = new VRCExpressionParameters.Parameter
            {
                name = $"haptic/prefabs/{conf.meta.map_author}/{conf.meta.map_name}/v{conf.meta.map_version}",
                defaultValue = conf.meta.map_version,
                networkSynced = false,
                saved = true,
                valueType = VRCExpressionParameters.ValueType.Int
            };
            paramlist.Add(advertisingParameter);
            menuParameters.parameters = paramlist.ToArray();
            
            // Add user-facing menu listing
            var prefabControl = new VRCExpressionsMenu.Control
            {
                name = $"{conf.meta.map_name} V{conf.meta.map_version}",
                icon = null,
                labels = null,
                parameter = null,
                style = VRCExpressionsMenu.Control.Style.Style1,
                subMenu = null,
                subParameters = null,
                type = VRCExpressionsMenu.Control.ControlType.Button,
                value = 1f,
            };
            mainMenu.controls.Add(prefabControl);
            
            // Make sure changes are saved.
            rootMenu.MarkDirty();
            mainMenu.MarkDirty();
            menuParameters.MarkDirty();
            AssetDatabase.SaveAssets();
            
            return (rootMenu, menuParameters);
        }
        
        /// <summary>
        /// Builds a singular haptic node and returns the instance 
        /// </summary>
        /// <param name="node">The config describing the desired node</param>
        /// <param name="index">The index of this node in the config list.</param>
        /// <param name="prefabName">The name of the parent prefab</param>
        /// <returns>The fully built, but unoptimized, haptic node.</returns>
        private static GameObject BuildHapticNode(Node node, int index, string prefabName)
        {
            // strip OSC address prefix
            const string prefix = "/avatar/parameters/";
            var localAddr = node.address;
            if (node.address.StartsWith(prefix))
            {
                localAddr = node.address[prefix.Length..];
            } else { Debug.LogError("OSC address is not valid for node: " + index); }
            
            // create new node object
            GameObject nodeObj = new GameObject(index + "_" + prefabName);
            nodeObj.transform.SetParent(_nodesParent.transform);
            nodeObj.transform.localPosition = GetNodePosition(node);

            // move node to skeleton on avatar build
            FuryComponents.CreateArmatureLink(nodeObj)
                .LinkTo(node.target_bone);
            
            //FU VRCFury (can't get the bone off of the objects)
            var boneObj = nodeObj.AddComponent<TargetBone>();
            boneObj.targetBone = node.target_bone;
            
            // add vrc contact
            var collisionTags = new List<string> {"Head", "Hand", "Foot", "Torso", "HapticCollider", "Finger"};
            ContactReceiver recv = nodeObj.AddComponent<VRCContactReceiver>();
            recv.parameter = localAddr;
            recv.allowOthers = true;
            recv.allowSelf = false;
            recv.localOnly = true;
            recv.radius = node.radius;
            recv.collisionTags = collisionTags;
            recv.receiverType = ContactReceiver.ReceiverType.Proximity;
            
            // create the node visualizer 
            const string dumpPath = "Haptics/Ignore Me/Individual Nodes";
            GameObject visualizer = CreateVisualizer(node, nodeObj);
            FuryToggle vizToggle = FuryComponents.CreateToggle(visualizer);
            vizToggle.SetSaved();
            vizToggle.SetGlobalParameter(GlobalVisualizerParam);
            vizToggle.SetMenuPath(dumpPath);
            var actions = vizToggle.GetActions();
            actions.AddTurnOn(visualizer);
            
            //create the prefab
            
            return nodeObj;
        }

        /// <summary>
        /// Simply converts the seperate x, y, z entries in the node  into a vec.
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        private static Vector3 GetNodePosition(Node node)
        {
            return new Vector3(
                node.node_data.x,
                node.node_data.y,
                node.node_data.z
            );
        }

        /// <summary>
        ///    Creates a Visualizer under the nodeObject.
        ///    Manages loading and scaling.
        /// </summary>
        /// <param name="node">The configurations description of our haptic node</param>
        /// <param name="nodeObj">The object that this visualizer will be representing</param>
        /// <returns>The instance of the Visualizer created.</returns>
        private static GameObject CreateVisualizer(Node node, GameObject nodeObj)
        {
            var prefabPath = _useLowPoly ? "Packages/com.vrch.haptics-installer/Assets/Visualizers/default_icosphere_20tri.prefab" 
                : "Packages/com.vrch.haptics-installer/Assets/Visualizers/default_icosphere_80tri.prefab";

            // load desired prefab.
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError("Failed to load prefab at path: " + prefabPath);
                return null;
            }
            
            // Instantiate the prefab.
            GameObject prefabInstance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (prefabInstance != null)
            {
                // attach to our parent node.
                prefabInstance.transform.SetParent(nodeObj.transform);
                prefabInstance.transform.localPosition = Vector3.zero;
                prefabInstance.transform.localRotation = Quaternion.identity;

                // scale with radius of the node. 
                // prefabs are pre-scaled to work with a radius of defaultRadius
                float defaultRadius = 0.0375f;
                float scaleFactor = node.radius / defaultRadius;
                prefabInstance.transform.localScale *= scaleFactor;
                
                return prefabInstance;
            }
            Debug.LogError("Failed to instantiate prefab from: " + prefabPath);

            return null;
        }
    }
}
