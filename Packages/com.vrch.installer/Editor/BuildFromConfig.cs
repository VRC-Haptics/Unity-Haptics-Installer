using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using com.vrcfury.api;
using com.vrcfury.api.Components;
using HapticsInstaller.Runtime;
using UnityEditor;
using VRC;
using VRC.Dynamics;
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
            _menuParent.transform.SetParent(_prefabRoot.transform);
            _nodesParent.transform.SetParent(_prefabRoot.transform);
            
            // build nodes
            for (int i = 0; i < conf.nodes.Length; i++)
            {
                BuildHapticNode(conf.nodes[i], i, conf.meta.map_name);
            }
            
            // build the menu for this prefab
            string prefabPath = $"/haptic/prefabs/{conf.meta.map_author}/{conf.meta.map_name}";
            VRCExpressionParameters parameters = BuildMenuParameters(conf, prefabPath, GlobalIntensityParam);
            VRCExpressionsMenu expressionsMenu = BuildMenu(conf, parameters, GlobalIntensityParam);
            
            // Create Controller Merger
            FuryFullController menu = FuryComponents.CreateFullController(_menuParent);
            menu.AddParams(parameters);
            menu.AddGlobalParam(GlobalIntensityParam);
            menu.AddGlobalParam(GlobalVisualizerParam);
            menu.AddMenu(expressionsMenu);
            
            // create prefab for the finished product
            PrefabUtility.SaveAsPrefabAssetAndConnect(
                _prefabRoot, 
                $"Assets/Haptics/{conf.meta.map_author}_{conf.meta.map_name}_{conf.meta.map_version}.prefab", 
                InteractionMode.UserAction);
            
            return _prefabRoot;
        }

        /// <summary>
        /// Builds the in-game menu for this prefab and places the generated assets in the Assets folder.
        /// </summary>
        /// <param name="conf">The Config we are generating from</param>
        /// <param name="parameters">The VRCParameters Asset to build the menu around</param>
        /// <param name="intensityPath">The parameter path to set the global intensity to</param>
        /// <returns></returns>
        private static VRCExpressionsMenu BuildMenu(Config conf, VRCExpressionParameters parameters, string intensityPath)
        {
            // build main menu (Will be under Haptics)
            
            // build intensity parameter list
            var intensityMenuParam = new VRCExpressionsMenu.Control.Parameter { name = intensityPath };
            var intensityList = new List<VRCExpressionsMenu.Control.Parameter> { intensityMenuParam };

            // create intensity control.
            VRCExpressionsMenu.Control intensity = new VRCExpressionsMenu.Control
            {
                name = "Feedback Intensity",
                icon = null,
                labels = null,
                parameter = null,
                style = VRCExpressionsMenu.Control.Style.Style1,
                subMenu = null,
                subParameters = intensityList.ToArray(),
                type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                value = 1f,

            };
            
            // build visibility parameter
            var visibilityMenuParam = new VRCExpressionsMenu.Control.Parameter { name = GlobalVisualizerParam };

            // create node visibility control
            VRCExpressionsMenu.Control nodeShowToggle = new VRCExpressionsMenu.Control
            {
                name = "Show Nodes",
                icon = null,
                labels = null,
                parameter = visibilityMenuParam,
                style = VRCExpressionsMenu.Control.Style.Style1,
                subMenu = null,
                subParameters = null,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
                value = 1f,

            };
                
            // create and fill the menu asset.
            VRCExpressionsMenu menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.controls.Add(nodeShowToggle);
            menu.controls.Add(intensity);
            menu.Parameters = parameters;
            menu.MarkDirty();
            
            // save menu asset.
            string savePath = GeneratedAssetPath +
                              $"Menu_{conf.meta.map_author}_{conf.meta.map_name}_{conf.meta.map_version}.asset";
            Utils.CreateDirectoryFromAssetPath(savePath);
            AssetDatabase.CreateAsset(menu, savePath);
            AssetDatabase.SaveAssets();
            
            // add sub-menu's to the parent.
            var parentMenu = AddToParentMenu(menu);
            
            return parentMenu;
        }

        /// <summary>
        /// Adds the given `subMenu` to the parent `Haptics` menu.
        /// Essentially give the Prefix Haptics/{menu name} to the submenu's address.
        /// Either loads from disk and appends or creates a new asset file.
        /// </summary>
        /// <param name="conf">The config that a prefab is being generated for</param>
        /// <param name="subMenu">The submenu that should be added.</param>
        /// <param name="menuName">The user-facing name for this submenu</param>
        /// <returns>The Modified Parent menu. NOTE: The parent is automatically saved to disk. </returns>
        private static VRCExpressionsMenu AddToParentMenu(VRCExpressionsMenu subMenu) 
        {
            const string parentSavePath = GeneratedAssetPath + "Menu_Haptics_Parent.asset";
            
            // Create entry for submenu
            VRCExpressionsMenu.Control newControl = new VRCExpressionsMenu.Control
            {
                name = "Haptics",
                icon = null,
                labels = null,
                parameter = null,
                style = VRCExpressionsMenu.Control.Style.Style1,
                subMenu = subMenu,
                subParameters = null,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                value = 1f,

            };
            
            VRCExpressionsMenu parentMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(parentSavePath);

            if (parentMenu == null)
            {
                // create empty parent menu (to gather all the submenus under one name).
                parentMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                parentMenu.controls.Add(newControl);
                parentMenu.MarkDirty();
                
                Utils.CreateDirectoryFromAssetPath(parentSavePath);
                AssetDatabase.CreateAsset(parentMenu, parentSavePath);
            }
            else
            {
                // remove old instance of the same menu if needed
                var duplicate = parentMenu.controls.FirstOrDefault(control => control.name == newControl.name);
                if (duplicate != null)
                {
                    parentMenu.controls.Remove(duplicate);
                }
                parentMenu.controls.Add(newControl);
                
            }
            
            EditorUtility.SetDirty(parentMenu);
            AssetDatabase.SaveAssets();

            return parentMenu;
        }

        /// <summary>
        /// Builds the VRC Parameters that this prefab needs.
        /// If a parameter is not private to this prefab, it is added as a VRCFury global parameter.
        /// </summary>
        /// <param name="conf"></param>
        /// <param name="prefabPath"></param>
        /// <param name="menuPath"></param>
        /// <returns></returns>
        private static VRCExpressionParameters BuildMenuParameters(Config conf, string prefabPath, string menuPath)
        {
            // Create a new instance of VRCExpressionParameters
            VRCExpressionParameters parametersAsset = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            List<VRCExpressionParameters.Parameter> parameterList = new List<VRCExpressionParameters.Parameter>();
            
            // create the parameter that makes this prefab identifiable.
            VRCExpressionParameters.Parameter prefabParam = new VRCExpressionParameters.Parameter
            {
                name = prefabPath,
                valueType = VRCExpressionParameters.ValueType.Int,
                saved = true,
                defaultValue = conf.meta.map_version,
                networkSynced = false
            };
            
            // create the parameter for showing the nodes
            VRCExpressionParameters.Parameter globalShowNodes = new VRCExpressionParameters.Parameter
            {
                name = GlobalVisualizerParam,
                valueType = VRCExpressionParameters.ValueType.Bool,
                saved = true,
                defaultValue = 0,
                networkSynced = true
            };
            
            // create the parameter that sets the global intensity
            VRCExpressionParameters.Parameter globalParam = new VRCExpressionParameters.Parameter
            {
                name = menuPath,
                valueType = VRCExpressionParameters.ValueType.Float,
                saved = true,
                defaultValue = 1.0f,
                networkSynced = false
            };

            // add parameters to our list
            parameterList.Add(globalParam);
            parameterList.Add(prefabParam);
            parameterList.Add(globalShowNodes);
            parametersAsset.parameters = parameterList.ToArray();
            
            // save the generated Parameters asset.
            string savePath = GeneratedAssetPath +
                              $"Parameters_{conf.meta.map_author}_{conf.meta.map_name}_{conf.meta.map_version}.asset";
            Utils.CreateDirectoryFromAssetPath(savePath);
            AssetDatabase.CreateAsset(parametersAsset, savePath);
            AssetDatabase.SaveAssets();
            
            return parametersAsset; 
        }
        
        /// <summary>
        /// Builds a singular haptic node and returns the instance 
        /// </summary>
        /// <param name="node">The config describing the desired node</param>
        /// <param name="index">The index of this node in the config list.</param>
        /// <param name="prefabName">The name of the parent prefab</param>
        /// <returns>The fully built, but unoptimized, haptic node.</returns>
        public static GameObject BuildHapticNode(Node node, int index, string prefabName)
        {
            // strip OSC address prefix
            const string prefix = "/avatar/parameters/";
            string localAddr = node.address;
            if (node.address.StartsWith(prefix))
            {
                localAddr = node.address.Substring(prefix.Length);
            } else { Debug.LogError("OSC address is not valid for node: " + index); }
            
            // create new node object
            GameObject nodeObj = new GameObject(index + "_" + prefabName);
            nodeObj.transform.SetParent(_nodesParent.transform);
            nodeObj.transform.SetPositionAndRotation(GetNodePosition(node), Quaternion.identity);

            // move node to skeleton on avatar build
            FuryComponents.CreateArmatureLink(nodeObj)
                .LinkTo(node.target_bone);
            
            //FU VRCFury (can't get the bone off of the objects)
            var boneObj = nodeObj.AddComponent<TargetBone>();
            boneObj.targetBone = node.target_bone;
            
            // add vrc contact
            var collisionTags = new List<string> {"Head", "Hand", "Foot", "Torso"};
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
            string prefabPath;
            if (_useLowPoly)
            {
                prefabPath = "Packages/com.vrch.haptics-installer/Assets/Visualizers/default_icosphere_20tri.prefab";
            }
            else
            {
                prefabPath = "Packages/com.vrch.haptics-installer/Assets/Visualizers/default_icosphere_80tri.prefab";
            }
            
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
