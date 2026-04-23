using System;
using UnityEngine;

namespace Common
{
    public class OffsetsAsset : ScriptableObject
    {
        public string mapAuthor;
        public string mapName;
        public int mapVersion;
        public bool useLowPoly = true;

        public NodeOffset[] nodeOffsets = Array.Empty<NodeOffset>();

        [Serializable]
        public class NodeOffset
        {
            public int mirrorIndex = -1;
            public string nodeId;

            public HumanBodyBones baseBone;
            public Vector3 basePosition;
            public Vector3 baseRotation;
            public float baseRadius;

            public bool hasRay;
            public Vector3 baseRayPositionOffset;
            public Vector3 baseRayRotationOffset;
            public float baseRayLength;

            public HumanBodyBones targetBone;
            public Vector3 positionOffset;
            public Vector3 rotationOffset;
            public float rayOffset;
            /// <summary>
            ///  Scales all axes of the nodes game object, this applies to raycast too.
            /// </summary>
            public float scaleMultiplier;
            /// <summary>
            ///  if ray length is zero (less than 0.0001) it doesn't get built.
            /// </summary>
            public float rayLenMultiplier;

            public Vector3 EffectivePosition => basePosition + positionOffset;
            public Vector3 EffectiveRotation => baseRotation + rotationOffset;
            public float EffectiveRadius => baseRadius * scaleMultiplier;
            public float EffectiveRayLen => baseRayLength * rayLenMultiplier;
            public float EffectiveRayPos => baseRayPositionOffset.z * rayOffset;
        }
    }
}