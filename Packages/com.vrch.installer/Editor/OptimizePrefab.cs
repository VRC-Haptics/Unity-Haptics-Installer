using System.Collections.Generic;
using com.vrcfury.api;
using com.vrcfury.api.Components;
using HapticsInstaller.Runtime;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Contact.Components;
using Object = UnityEngine.Object;

namespace Editor
{
    public class OptimizePrefab
    {
        private const string dumpPath = "Haptics/Ignore Me/Individual Nodes";
        private const string GlobalVisualizerParam = "/haptic/global/show";
        private static List<GameObject> Prefabs = new ();
        private static GameObject _optimPrefabParent;
        private static Dictionary<HumanBodyBones, List<GameObject>> _nodeGroups = new();
        
        public static GameObject OptimizePrefabs(GameObject[] inPrefabs, GameObject avatarRoot)
        {
            Prefabs.Clear();
            _nodeGroups.Clear();
            
            Debug.Log("in prefabs: " + inPrefabs.Length);
            // create our final parent.
            _optimPrefabParent = new GameObject("Haptics-Integration")
            {
                transform = { parent = avatarRoot.transform }
            };

            // create our copies to wreak havoc on
            foreach(var prefab in inPrefabs)
            {
                Debug.Log("The name: " + prefab.name);
                GameObject ourCopy = Object.Instantiate(prefab, _optimPrefabParent.transform, true);
                Prefabs.Add(ourCopy);
            }
            
            // create nodes parent on our main object.
            GameObject optimNodes = new GameObject("nodes");
            optimNodes.transform.SetParent(_optimPrefabParent.transform);
            
            Debug.Log("Optimized prefabs: " + Prefabs.Count);
            
            // group our nodes by what bone they want to be parented to.
            foreach (GameObject prefab in Prefabs)
            {
                if (prefab == null) continue; // Skip destroyed references

                Debug.Log("Grouping nodes on: " + prefab.name);
                Transform nodesTransform = prefab.transform.Find("nodes");
                if (nodesTransform == null) {
                    Debug.LogError(prefab.name + " has no nodes under the `nodes` object");
                    continue;
                }

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
                foreach (Transform child in prefab.transform) {
                    Object.DestroyImmediate(child.gameObject); 
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
                // add bone our bones parent named with the HumanSkeletonBones name.
                GameObject boneGroup = new GameObject(bone.ToString());
                boneGroup.transform.SetParent(optimNodes.transform);
                
                // add vrcfury armatureLinker component to nodes parent
                FuryComponents.CreateArmatureLink(optimNodes).LinkTo(bone);

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
            
            // Create an instance of the visualizers at each node?
            for (int j = 0; j < boneGroup.transform.childCount; j++)
            {
                // get target transform
                GameObject node = boneGroup.transform.GetChild(j).gameObject;
                var t = node.transform;
                GameObject tempVisual = Object.Instantiate(visualsPrefab, t.position, t.rotation);
                
                
                MeshFilter mf = tempVisual.GetComponent<MeshFilter>();
                if (mf != null)
                {
                    CombineInstance ci = new CombineInstance
                    {
                        mesh = mf.sharedMesh,
                        transform = tempVisual.transform.localToWorldMatrix
                    };
                    combine.Add(ci);
                }

                Object.DestroyImmediate(tempVisual); // Clean up temporary instance
            }
            
            GameObject combined = new GameObject("visuals");
            combined.transform.SetParent(boneGroup.transform);

            Mesh combinedMesh = new Mesh();
            combinedMesh.CombineMeshes(combine.ToArray());
            
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
        /// Merges and creates the menu for us
        /// </summary>
        /// <param name="prefabs">The prefabs to get the menu from.</param>
        static void CreateMenu(List<GameObject> prefabs)
        {
            var first = prefabs[0];
            if (first == null)
            {
                Debug.LogError("Empty prefab list in menu creation");
                return;
            }
    
            // Try to find the menu child by name instead of getting the first child
            Transform menuObj = first.transform.Find("menu");
            if (menuObj == null)
            {
                Debug.LogError("No menu in prefab");
                return;
            }
    
            menuObj.SetParent(_optimPrefabParent.transform);
        }

    }
}