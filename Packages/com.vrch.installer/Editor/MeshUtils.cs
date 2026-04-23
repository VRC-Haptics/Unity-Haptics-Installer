using System.Collections.Generic;
using UnityEngine;

namespace Editor
{
    public static class MeshUtils
    {
        public struct MeshHit
        {
            public Vector3 worldPoint;
            public Vector3 worldNormal;
        }

        public static MeshHit FindClosestPoint(
            Mesh mesh, Transform meshTransform, Vector3 worldPoint,
            BoneWeight[] boneWeights = null, HashSet<int> boneIndices = null,
            float weightThreshold = 0.15f)
        {
            var localPoint = meshTransform.InverseTransformPoint(worldPoint);
            var verts = mesh.vertices;
            var tris = mesh.triangles;
            bool filter = boneWeights != null && boneIndices != null && boneIndices.Count > 0;

            float bestDist = float.MaxValue;
            var bestPoint = localPoint;
            var bestNormal = Vector3.up;

            for (int i = 0; i < tris.Length; i += 3)
            {
                int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];

                if (filter && !TriTouchesBone(boneWeights, i0, i1, i2, boneIndices, weightThreshold))
                    continue;

                Vector3 a = verts[i0], b = verts[i1], c = verts[i2];
                var pt = ClosestPointOnTriangle(localPoint, a, b, c);
                float d = (pt - localPoint).sqrMagnitude;

                if (d < bestDist)
                {
                    bestDist = d;
                    bestPoint = pt;
                    bestNormal = Vector3.Cross(b - a, c - a).normalized;
                }
            }

            // If bone filter matched nothing, retry unfiltered
            if (filter && bestDist == float.MaxValue)
                return FindClosestPoint(mesh, meshTransform, worldPoint);

            return new MeshHit
            {
                worldPoint = meshTransform.TransformPoint(bestPoint),
                worldNormal = meshTransform.TransformDirection(bestNormal).normalized
            };
        }

        private static bool TriTouchesBone(BoneWeight[] w, int i0, int i1, int i2,
            HashSet<int> bones, float t)
        {
            return VtxTouchesBone(w[i0], bones, t) ||
                   VtxTouchesBone(w[i1], bones, t) ||
                   VtxTouchesBone(w[i2], bones, t);
        }

        private static bool VtxTouchesBone(BoneWeight w, HashSet<int> bones, float t)
        {
            return (w.weight0 >= t && bones.Contains(w.boneIndex0)) ||
                   (w.weight1 >= t && bones.Contains(w.boneIndex1)) ||
                   (w.weight2 >= t && bones.Contains(w.boneIndex2)) ||
                   (w.weight3 >= t && bones.Contains(w.boneIndex3));
        }

        private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
                return a + ab * (d1 / (d1 - d3));

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
                return a + ac * (d2 / (d2 - d6));

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }
    }
}