using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Editor
{
    public interface Utils
    {
        /// <summary>
        /// Takes in an Asset path or empty path and creates it if not present.
        /// </summary>
        /// <param name="assetPath"></param>
        public static void CreateDirectoryFromAssetPath(string assetPath)
        {
            string directoryPath = Path.GetDirectoryName(assetPath);
            if (Directory.Exists(directoryPath))
                return;
            Directory.CreateDirectory(directoryPath);
            AssetDatabase.Refresh();
        }

        public static bool MoveAsset(string oldPath, string newPath)
        {
            CreateDirectoryFromAssetPath(newPath);
            string error = AssetDatabase.MoveAsset(oldPath, newPath);
            if (error != null)
            {
                Debug.LogError(error);
                return false;
            }
            return true;
        }
        
        public static T CopyComponent<T>(T original, GameObject destination) where T : Component
        {
            T copy = destination.AddComponent<T>();
            var type = typeof(T);
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                field.SetValue(copy, field.GetValue(original));
            }
            return copy;
        }

        /// <summary>
        /// flatten a list of parameter's into a singular list with one instance of each parameter.
        /// </summary>
        /// <param name="parameterAssets">The input mergedAsset Re-exported</param>
        /// <returns></returns>
        public static VRCExpressionParameters MergeParameters(
            List<VRCExpressionParameters> parameterAssets, 
            VRCExpressionParameters mergedAsset
            )
        {
            var parameterList = new Dictionary<string, VRCExpressionParameters.Parameter>();

            // Gather all parameters, one instance of each parameter.
            foreach (var subParam in parameterAssets)
            {
                foreach (var param in subParam.parameters)
                {
                    parameterList.TryAdd(param.name, param);
                }
            }

            mergedAsset.parameters = parameterList.Values.ToArray();
            Debug.Log("Total cost of merged parameters: "+ mergedAsset.CalcTotalCost());
            return mergedAsset;
        }

        /// <summary>
        /// Flatten list of a prefabs "main" menu's into the input menu.
        /// </summary>
        /// <param name="mainMenus"></param>
        /// <returns>Re-export the input expressions menu.</returns>
        public static VRCExpressionsMenu MergeMainMenus(
            List<VRCExpressionsMenu> mainMenus, 
            VRCExpressionsMenu mergedMenu
            )
        {
            // add menu item if it isn't already in there.
            foreach (var menu in mainMenus)
            {
                foreach (var control in mergedMenu.controls.ToList())
                {
                    if (!mergedMenu.controls.Contains(control))
                    {
                        mergedMenu.controls.Add(control);
                    }
                }
            }

            return mergedMenu;
        }
        
        public static Dictionary<HumanBodyBones, Transform> GetBonesMap(Avatar avatar, GameObject armatureRoot)
        {
            var dict = new Dictionary<HumanBodyBones, Transform>();

            // maps a *models* skeleton to standard bones.
            var human = avatar.humanDescription.human;
            // maps the standard bones to positions/other info.
            var skeleton = avatar.humanDescription.skeleton;


            foreach (var humanBone in human)
            {
                var boneTransform = armatureRoot.transform.parent.GetComponentsInChildren<Transform>()
                    .FirstOrDefault(t => t.name == humanBone.boneName);
                if (boneTransform == null)
                {
                    Debug.LogWarning(
                        $"Bone: {humanBone.boneName} is described in avatar but not found. Ignore if not part of the standard skeleton.");
                    continue;
                }

                string enumName = humanBone.humanName.Replace(" ", "");
                if (Enum.TryParse(enumName, true, out HumanBodyBones bone))
                {
                    dict.Add(bone, boneTransform);
                }
                else
                {
                    Debug.LogWarning(
                        $"unable to parse bone: humanName: {humanBone.humanName}, humanBoneName: {humanBone.boneName}");
                }
            }

            return dict;
        }
    }
}