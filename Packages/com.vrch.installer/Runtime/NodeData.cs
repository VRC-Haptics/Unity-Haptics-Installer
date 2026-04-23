using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using UnityEngine;
using UnityEngine.Serialization;
using VRC.SDKBase;
using Vector3 = UnityEngine.Vector3;

namespace HapticsInstaller.Runtime
{
    /// <summary>
    /// Small data class that keeps track of the reference data for this node.
    /// </summary>
    [Serializable]
    public class NodeData : MonoBehaviour, IEditorOnly
    {
        // Version of this node struct, used to migrate and get default values.
        [SerializeField] private int version = 1;
        
        [Tooltip("The bone that this node should be parented to.")]
        public HumanBodyBones targetBone;
        public bool isExternal;
        public float originalRadius;
        public List<String> interactionTags; 
        public Vector3 originalPosition;
        
        [Tooltip("OSC address is required to be unique across all avatars.")]
        public String address;

        [Tooltip("The Referenced node will be mirrored along the X axis.")]
        public NodeData mirrorSource = null;
        
    }
}