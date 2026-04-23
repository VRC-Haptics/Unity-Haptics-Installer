using System.Collections.Generic;
using Common;
using UnityEngine;

namespace Editor
{
    public static class MirrorUtils
    {
        public static HumanBodyBones MirrorBone(HumanBodyBones bone)
        {
            return BoneMirrorMap.TryGetValue(bone, out var mirrored) ? mirrored : bone;
        }

        public static float MirrorScale(float scale)
        {
            return scale; // scale is symmetric, but kept as a method for future overrides
        }
        
        private static readonly Dictionary<HumanBodyBones, HumanBodyBones> BoneMirrorMap = BuildBoneMirrorMap();

        private static Dictionary<HumanBodyBones, HumanBodyBones> BuildBoneMirrorMap()
        {
            var map = new Dictionary<HumanBodyBones, HumanBodyBones>();
            var all = (HumanBodyBones[])System.Enum.GetValues(typeof(HumanBodyBones));
            var byName = new Dictionary<string, HumanBodyBones>();

            foreach (var b in all)
            {
                if (b == HumanBodyBones.LastBone) continue;
                byName[b.ToString()] = b;
            }

            foreach (var b in all)
            {
                if (b == HumanBodyBones.LastBone) continue;
                string name = b.ToString();
                if (!name.Contains("Left")) continue;
                string mirror = name.Replace("Left", "Right");
                if (byName.TryGetValue(mirror, out var right))
                {
                    map[b] = right;
                    map[right] = b;
                }
            }

            return map;
        }

        public static void DetectMirrorPairs(OffsetsAsset asset, float tolerance = 0.01f)
        {
            var offsets = asset.nodeOffsets;
            for (int i = 0; i < offsets.Length; i++)
                offsets[i].mirrorIndex = -1;

            for (int i = 0; i < offsets.Length; i++)
            {
                if (offsets[i].mirrorIndex >= 0) continue;

                var pos = offsets[i].basePosition;
                var bone = offsets[i].baseBone;

                HumanBodyBones mirrorBone;
                if (BoneMirrorMap.TryGetValue(bone, out var mb))
                {
                    mirrorBone = mb;
                }
                else
                {
                    // Center bone — only pair if noticeably off-center
                    if (Mathf.Abs(pos.x) < tolerance) continue;
                    mirrorBone = bone;
                }

                var mirroredPos = new Vector3(-pos.x, pos.y, pos.z);

                for (int j = i + 1; j < offsets.Length; j++)
                {
                    if (offsets[j].mirrorIndex >= 0) continue;
                    if (offsets[j].baseBone != mirrorBone) continue;

                    if ((offsets[j].basePosition - mirroredPos).sqrMagnitude <= tolerance * tolerance)
                    {
                        offsets[i].mirrorIndex = j;
                        offsets[j].mirrorIndex = i;
                        break;
                    }
                }
            }
        }

        public static Vector3 MirrorPosition(Vector3 localPos)
        {
            return new Vector3(-localPos.x, localPos.y, localPos.z);
        }

        public static Quaternion MirrorRotation(Quaternion q)
        {
            // Mirror across YZ plane (negate X axis)
            return new Quaternion(-q.x, q.y, q.z, -q.w);
        }

        public static Vector3 MirrorEuler(Vector3 euler)
        {
            return MirrorRotation(Quaternion.Euler(euler)).eulerAngles;
        }
    }
}