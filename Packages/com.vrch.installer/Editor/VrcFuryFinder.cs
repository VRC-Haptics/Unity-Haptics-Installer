using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Editor
{
    public class VrcFuryFinder
    {
        private static Type _vrcFuryType;
        private static Type _armatureLink;
        
        public VrcFuryFinder()
        {
            _vrcFuryType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .FirstOrDefault(t => t.Name == "VRCFury");
        }

        public HumanBodyBones GetTargetBone(GameObject obj)
        {
            Component vrcFuryComponent = GetVrcFuryComponent(obj);
            
            if (vrcFuryComponent != null)
            {
                // Get the content field
                FieldInfo contentField = vrcFuryComponent.GetType().GetField(
                    "content", 
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                Debug.LogError(contentField);
                if (contentField != null)
                {
                    Debug.LogError($"{contentField.GetValue(vrcFuryComponent)}");
                    object contentValue = contentField.GetValue(vrcFuryComponent);
                    if (contentValue != null)
                    {
                        // Now get the 'propBone' from the content (assumed to be an instance of ArmatureLink).
                        FieldInfo propBoneField = contentValue.GetType().GetField(
                            "propBone", 
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                        if (propBoneField != null)
                        {
                            object propBone = propBoneField.GetValue(contentValue);
                            Debug.Log("propBone: " + propBone);
                        }
                        else
                        {
                            Debug.LogWarning("Could not find 'propBone' field on content.");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("Content field is null.");
                    }
                }
                else
                {
                    Debug.LogWarning("Could not find the 'content' field on VRCFury component.");
                }
            }

            return HumanBodyBones.Head;
        }
        
        private static Component GetVrcFuryComponent(GameObject obj)
        {
            // Attempt to retrieve the type named "VRCFury" from all loaded assemblies.
            
            if (_vrcFuryType != null)
            {
                return obj.GetComponents<Component>()
                    .FirstOrDefault(comp => comp.GetType() == _vrcFuryType);
            }
            else
            {
                Debug.LogWarning("VRCFury type not found");
                return null;
            }
        }
    }
}