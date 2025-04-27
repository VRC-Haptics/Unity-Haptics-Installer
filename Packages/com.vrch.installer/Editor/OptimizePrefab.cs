using System.Collections.Generic;
using System.Linq;
using com.vrcfury.api;
using com.vrcfury.api.Components;
using HapticsInstaller.Runtime;
using UnityEditor;
using UnityEngine;
using VRC;
using VRC.Dynamics;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;
using Object = UnityEngine.Object;

namespace Editor
{
    public class OptimizePrefab
    {
        // can't disable menu entries on vrcfury so we create a dump folder in the menu.
        private const string dumpPath = "Haptics/Ignore Me/Individual Nodes";
        private const string GeneratedAssetPath = "Assets/Haptics/Generated/";
        private const string GeneratedOptimFolder = "Assets/Haptics/Generated/Optimized Prefab/";
        private const string GeneratedOptimMeshPath = "Assets/Haptics/Generated/Optimized Prefab/";
        
        private static List<GameObject> Prefabs = new ();
        private static GameObject _optimPrefabParent;
        private static Dictionary<HumanBodyBones, List<GameObject>> _nodeGroups = new();
        private static List<bool> _emptyNodes = new List<bool>();
        
        // OSC Parameter Paths
        private const string GlobalVisualizerParam = "/haptic/global/show";
        private const string GlobalIntensityParam = "/haptic/global/intensity";
        
        public static GameObject OptimizePrefabs(GameObject[] inPrefabs, GameObject avatarRoot)
        {
            Prefabs.Clear();
            _nodeGroups.Clear();
            
            // create our final parent.
            _optimPrefabParent = new GameObject("Haptics-Integration")
            {
                transform = { parent = avatarRoot.transform }
            };

            // create our copies to wreak havoc on
            foreach(var prefab in inPrefabs)
            {
                GameObject ourCopy = Object.Instantiate(prefab, _optimPrefabParent.transform, true);
                Prefabs.Add(ourCopy);
            }
            
            // create nodes parent on our main object.
            GameObject optimNodes = new GameObject("nodes");
            optimNodes.transform.SetParent(_optimPrefabParent.transform);
            
            // group our nodes by what bone they want to be parented to.
            foreach (var prefab in Prefabs)
            {
                if (prefab == null) continue; // Skip destroyed references
                
                Transform nodesTransform = prefab.transform.Find("nodes");
                if (nodesTransform == null) {
                    Debug.LogWarning(prefab.name + " has no nodes under the `nodes` object. Be sure you know what you are doing");
                    _emptyNodes.Add(true);
                    continue;
                }
                _emptyNodes.Add(false);

                GatherHapticNodes(nodesTransform, optimNodes);
            }
            
            // create our menu
            CreateMenu(Prefabs);
            
            // destroy contents of our version of the prefabs
            // we don't need the contents of this copy.
            // Keep the original to denote which prefabs this contains.
            foreach (GameObject prefab in Prefabs)
            {
                if (prefab == null) continue;
                prefab.name = prefab.name.Replace("(Clone)", "");
                for (int i = prefab.transform.childCount - 1; i >= 0; --i)
                {
                    Object.DestroyImmediate(prefab.transform.GetChild(i).gameObject);
                }
            }
            
            PrefabUtility.SaveAsPrefabAssetAndConnect(
                _optimPrefabParent, 
                $"Assets/Haptics/{_optimPrefabParent.name}.prefab", 
                InteractionMode.UserAction);
            
            return _optimPrefabParent;
        }

        /// <summary>
        /// Gather haptic nodes under the prefab into our _nodeGroups value
        /// </summary>
        /// <param name="nodeParent">Nodes under a prefab</param>
        /// <param name="optimNodes"></param>
        static void GatherHapticNodes(Transform nodeParent, GameObject optimNodes)
        {
            _nodeGroups.Clear();
            // Gather nodes into _nodeGroups
            for (int j = 0; j < nodeParent.childCount; j++)
            {
                GameObject node = nodeParent.GetChild(j).gameObject;
                var parentBone = GetTargetBone(node);
                // if we already have a list add to it, otherwise create a new entry.
                if (_nodeGroups.ContainsKey(parentBone))
                {
                    _nodeGroups[parentBone].Add(node);
                }
                else
                {
                    var newList = new List<GameObject> { node };
                    _nodeGroups.Add(parentBone, newList);
                }
            }
            
            // open our prefabs
            string prefabPath = "Packages/com.vrch.haptics-installer/Assets/Visualizers/default_icosphere_20tri.prefab";
            GameObject visualsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (visualsPrefab == null)
            {
                Debug.LogError("Failed to load prefab at path: " + prefabPath);
                return;
            }
            
            // Push new nodes into our main nodes section
            foreach (var (bone, nodeList) in _nodeGroups)
            { 
                GameObject boneGroup = new GameObject(bone.ToString());
                boneGroup.transform.SetParent(optimNodes.transform);
                
                FuryComponents.CreateArmatureLink(boneGroup).LinkTo(bone);

                // create contact nodes under the parent.
                foreach (var node in nodeList)
                {
                    if (node == null) continue;
                    // create new node with the originals position and set parent
                    GameObject newNode = new GameObject(node.name);
                    newNode.transform.position = node.transform.position;
                    newNode.transform.SetParent(boneGroup.transform);
                    
                    // copy contact from original node
                    VRCContactReceiver ogContact = node.GetComponent<VRCContactReceiver>();
                    if (ogContact != null)
                    {
                        Utils.CopyComponent(ogContact, newNode);
                    }
                    else
                    {
                        Debug.LogError("Unable to find contact node for: " + node.name);
                    }
                    
                    // copy targetBone from original node
                    TargetBone ogTarget = node.GetComponent<TargetBone>();
                    if (ogTarget != null)
                    {
                        Utils.CopyComponent(ogTarget, newNode);
                    }
                    else
                    {
                        Debug.LogError("Unable to find contact node for: " + node.name);
                    }
                }
                
                // build the visualizers
                BuildVisualsForBone(boneGroup, visualsPrefab);
            }
        }

        /// <summary>
        /// Build the visualizer for this boneGroup.
        /// Since all nodes parented to the same bone won't move we can consolidate them.
        /// </summary>
        /// <param name="boneGroup">The node group parent (the actual object that will be moved on the avatar)</param>
        static void BuildVisualsForBone(GameObject boneGroup, GameObject visualsPrefab)
        {
            List<CombineInstance> combine = new List<CombineInstance>();
            
            // Create an instance of the visualizers at each node
            var worldToLocal = boneGroup.transform.worldToLocalMatrix;
            for (int i = 0; i < boneGroup.transform.childCount; i++)
            {
                Transform t = boneGroup.transform.GetChild(i);
                var contact = t.GetComponent<VRCContactReceiver>();

                GameObject temp = Object.Instantiate(visualsPrefab, t.position, t.rotation);

                float scale = contact.radius / 0.0375f;
                temp.transform.localScale *= scale;

                if (temp.TryGetComponent(out MeshFilter mf))
                {
                    combine.Add(new CombineInstance
                    {
                        mesh      = mf.sharedMesh,                      // keep it serialisable
                        transform = worldToLocal * mf.transform.localToWorldMatrix
                    });
                }
                Object.DestroyImmediate(temp);    // editor-only
            }
            
            GameObject combined = new GameObject("visuals");
            combined.transform.SetParent(boneGroup.transform);

            Mesh combinedMesh = new Mesh();
            combinedMesh.CombineMeshes(combine.ToArray());
            combinedMesh.RecalculateNormals();
            combinedMesh.RecalculateBounds();
            AssetDatabase.CreateAsset(combinedMesh, GeneratedOptimMeshPath + $"VisMesh_{boneGroup.name}.asset");
            AssetDatabase.SaveAssets();
            
            MeshFilter combinedMF = combined.AddComponent<MeshFilter>();
            combinedMF.mesh = combinedMesh;
            
            MeshRenderer combinedMR = combined.AddComponent<MeshRenderer>();
            combinedMR.sharedMaterial = visualsPrefab.GetComponent<MeshRenderer>().sharedMaterial;
            
            FuryToggle vizToggle = FuryComponents.CreateToggle(combined);
            vizToggle.SetSaved();
            vizToggle.SetGlobalParameter(GlobalVisualizerParam);
            vizToggle.SetMenuPath(dumpPath);
            var actions = vizToggle.GetActions();
            actions.AddTurnOn(combined);
        }

        /// <summary>
        /// Resolves the TargetBone on the given node
        /// </summary>
        /// <param name="node">The gameObject representing the Haptic Node</param>
        /// <returns></returns>
        static HumanBodyBones GetTargetBone(GameObject node)
        {
            //TODO: THROW A PROMPT FOR THE USER TO SELECT THE NODE MANUALLY.
            var script = node.GetComponent<TargetBone>();
            return script != null ? script.targetBone : HumanBodyBones.Head;
        }

        /// <summary>
        /// Merges the menu for the prefabs.
        /// </summary>
        /// <param name="prefabs">The prefabs to get the menu from.</param>
        static void CreateMenu(List<GameObject> prefabs)
        {
            // define our copies paths
            Utils.CreateDirectoryFromAssetPath(GeneratedOptimFolder);
            string rootMenuPath = GeneratedOptimFolder + "Menu_Root.asset";
            string mainMenuPath = GeneratedOptimFolder + "Menu_Main.asset";
            string parametersPath = GeneratedOptimFolder + $"Parameters_Main.asset";
            
            // Create Copy of pre-generated assets.
            string menuAssetsPath = "Packages/com.vrch.haptics-installer/Assets/Menu/";
            AssetDatabase.CopyAsset(menuAssetsPath + "Menu_Root.asset", rootMenuPath);
            AssetDatabase.CopyAsset(menuAssetsPath + "Menu_Main.asset", mainMenuPath);
            AssetDatabase.CopyAsset(menuAssetsPath + $"Parameters_Basic.asset", parametersPath);
            
            // load copies into memory
            var rootMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(rootMenuPath);
            var mainMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(mainMenuPath);
            var menuParameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(parametersPath);
            
            // compile lists from contents
            var parameters = new List<VRCExpressionParameters>();
            var menus = new List<VRCExpressionsMenu>();
            foreach (var prefab in prefabs)
            {
                Transform child = prefab.transform.Find("menu");
                if (child == null)
                {
                   Debug.LogError($"Unable to find menu for: {prefab.name}");
                   continue; 
                }
                
                MenuData md = child.GetComponent<MenuData>();
                if (md != null)
                {
                    parameters.Add(md.vrcMenu.Parameters);
                    // add main menu (strip root)
                    menus.Add(md.vrcMenu.controls.First().subMenu);
                }
                else
                {
                    Debug.LogError($"Unable to find MenuData on menu: {prefab.name}");
                }
            }
            
            // merge parameters and menu's
            Utils.MergeParameters(parameters, menuParameters);
            Utils.MergeMainMenus(menus, mainMenu);
            
            // update the menu references
            rootMenu.controls.First().subMenu = mainMenu;
            rootMenu.Parameters = menuParameters;
            mainMenu.Parameters = menuParameters;

            // ensure saved.
            rootMenu.MarkDirty();
            mainMenu.MarkDirty();
            menuParameters.MarkDirty();
            AssetDatabase.SaveAssets();

            // create new vrcfury component on base prefab.
            GameObject menuObject = new GameObject("menu");
            menuObject.transform.SetParent(_optimPrefabParent.transform, false);
            var furyCon = FuryComponents.CreateFullController(menuObject);
            furyCon.AddParams(menuParameters);
            furyCon.AddGlobalParam(GlobalIntensityParam);
            furyCon.AddGlobalParam(GlobalVisualizerParam);
            furyCon.AddMenu(rootMenu);
        }

    }
}