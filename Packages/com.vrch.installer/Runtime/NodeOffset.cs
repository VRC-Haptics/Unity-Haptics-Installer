using System;
using System.Collections.Generic;
using UnityEngine;

namespace HapticsInstaller.Runtime
{
    [Serializable]
    public class NodeOffset
    {
        public int configIndex;
        public Vector3 positionOffset;
        public Vector3 rotationOffset;
        public float radiusScale = 1f;

        // ray overrides
        public bool rayEnabled = false;
        public Vector3 rayOffsetDelta = Vector3.zero;
        public Vector3 rayRotationDelta = Vector3.zero;
        public float rayLengthScale = 1f;

        public Vector3 ApplyPosition(Vector3 configPos) => configPos + positionOffset;
        public float ApplyRadius(float configRadius) => configRadius * radiusScale;

        public Vector3 ApplyRayOffset(Vector3 baseOffset) => baseOffset + rayOffsetDelta;
        public Vector3 ApplyRayRotation(Vector3 baseRot) => baseRot + rayRotationDelta;
        public float ApplyRayLength(float baseLength) => baseLength * rayLengthScale;

        public NodeOffset Mirrored()
        {
            return new NodeOffset
            {
                configIndex = configIndex,
                positionOffset = new Vector3(-positionOffset.x, positionOffset.y, positionOffset.z),
                rotationOffset = new Vector3(rotationOffset.x, -rotationOffset.y, -rotationOffset.z),
                radiusScale = radiusScale,
                rayEnabled = rayEnabled,
                rayOffsetDelta = new Vector3(-rayOffsetDelta.x, rayOffsetDelta.y, rayOffsetDelta.z),
                rayRotationDelta = new Vector3(rayRotationDelta.x, -rayRotationDelta.y, -rayRotationDelta.z),
                rayLengthScale = rayLengthScale
            };
        }
    }

    [Serializable]
    public class MirrorPair
    {
        public int indexA;
        public int indexB;
        public bool linked = true;
    }

    [Serializable]
    public class OffsetCollection
    {
        public string sourceConfigPath;
        public int configHash;
        public List<NodeOffset> offsets = new();
        public List<MirrorPair> mirrorPairs = new();

        public MirrorPair GetPairFor(int offsetIndex)
        {
            foreach (var pair in mirrorPairs)
            {
                if (pair.indexA == offsetIndex || pair.indexB == offsetIndex)
                    return pair;
            }
            return null;
        }

        public int GetLinkedPartner(int offsetIndex)
        {
            var pair = GetPairFor(offsetIndex);
            if (pair == null || !pair.linked) return -1;
            return pair.indexA == offsetIndex ? pair.indexB : pair.indexA;
        }
    }
}