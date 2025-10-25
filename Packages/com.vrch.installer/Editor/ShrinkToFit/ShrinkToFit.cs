using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using HapticsInstaller.Runtime;
using HarmonyLib;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Editor.ShrinkToFit
{
    public static class ShrinkToFitUtils
    {
        /// <summary>
        /// Stores the global Transform for a given HumanBodyBones
        /// </summary>
        private static Dictionary<HumanBodyBones, Transform> _bones;

        /// <summary>
        /// Takes a single prefab and then shrinks it around the bones the nodes are designed to be parented to.
        /// </summary>
        /// <param name="avatarRoot">assumed to have a VRCAvatarDescriptor on the object</param>
        /// <param name="bodyObject">Assumed to have a skinned mesh renderer on the avatar that will be used to shrink the nodes around</param>
        /// <param name="prefabBase">The base of our haptics prefab</param>
        public static void SinglePrefab(
            GameObject avatarRoot,
            GameObject bodyObject,
            GameObject prefabBase)
        {
            
            if (avatarRoot == null || bodyObject == null || prefabBase == null)
            {
                Debug.LogError("Cannot have null avatar root/body object");
                return;
            }

            VRCAvatarDescriptor vrcDescriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            GameObject armature = avatarRoot.transform.Find("Armature").gameObject;
            var aviOrigin = avatarRoot.transform.position - new Vector3(0, avatarRoot.transform.position.y, 0);
            SkinnedMeshRenderer bodyRenderer = bodyObject.GetComponent<SkinnedMeshRenderer>();
            GameObject nodesParent = prefabBase.transform.Find("nodes").gameObject;

            // get models avatar information
            var animator = avatarRoot.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogError("No animator attached to avatar root");
                return;
            }

            var avatar = animator.avatar;
            if (avatar == null)
            {
                Debug.LogError("No avatar attached in avatar animator");
                return;
            }

            FillBones(avatar, armature);

            //Debug.Log("Human bones: " + string.Join(", ", avatar.humanDescription.human.Select(b => $"{b.humanName}->{b.boneName}")));
            //Debug.Log("Skeleton: " + string.Join(", ", avatar.humanDescription.skeleton.Select(b => $"N:{b.name}, {b.position}, {b.rotation}")));

            // duplicate mesh
            GameObject duplicatedMesh = Object.Instantiate(bodyObject, bodyObject.transform.parent);
            duplicatedMesh.name = bodyObject.name + "_Clone";

            // add collider to new mesh
            MeshCollider collider = duplicatedMesh.AddComponent<MeshCollider>();
            collider.sharedMesh = bodyRenderer.sharedMesh;
            collider.convex = false;

            foreach (Transform node in nodesParent.transform)
            {
                Undo.RecordObject(node.transform, $"Shrink to fit: {node.name}");
                
                // get target bone.
                TargetBone bone = node.GetComponent<TargetBone>();
                if (bone == null)
                {
                    Debug.LogError($"Could not find TargetBone component on node: {node.name}");
                    continue;
                }

                // global space transform containing bone position and rotation
                if (!_bones.TryGetValue(bone.targetBone, out var boneTransform))
                {
                    Debug.LogError($"Could not find TargetBone component on node: {node.name}");
                    continue;
                }

                // node location
                Vector3 nodePosition = node.transform.position;
                Vector3 boneToNode = nodePosition - boneTransform.position;

                // Project onto bone's forward axis to find closest point on the line
                float projectionDistance = Vector3.Dot(boneToNode, boneTransform.up);
                Vector3 closestPointOnAxis = boneTransform.position + boneTransform.up * projectionDistance;

                // Direction vector from node to intersection point
                Vector3 directionToAxis = closestPointOnAxis - nodePosition;
                Debug.DrawRay(nodePosition, directionToAxis, Color.red, 2f);
                float distanceToAxis = directionToAxis.magnitude;

                // if outside mesh
                if (Physics.Raycast(nodePosition, directionToAxis.normalized, out var raycastHit, distanceToAxis))
                {
                    node.transform.position = raycastHit.point;
                }
                // if inside mesh
                else if (Physics.Raycast(nodePosition, -directionToAxis.normalized, out raycastHit, 10000))
                {
                    node.transform.position = raycastHit.point;
                }
                else
                {
                    Debug.LogWarning($"Could not resolve shrink-to-fit: {node.name}");
                }
            }
            Object.DestroyImmediate(duplicatedMesh);
        }
        static void FillBones(Avatar avatar, GameObject armatureRoot)
        {
            _bones = new Dictionary<HumanBodyBones, Transform>();

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
                    _bones.Add(bone, boneTransform);
                }
                else
                {
                    Debug.LogWarning(
                        $"unable to parse bone: humanName: {humanBone.humanName}, humanBoneName: {humanBone.boneName}");
                }
            }
        }
    }
}