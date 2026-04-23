using System.Collections.Generic;
using Common;
using HapticsInstaller.Runtime;
using UnityEngine;

namespace Editor
{
    public static class MirrorDetector
    {
        private const float PositionTolerance = 0.005f;

        /// <summary>
        /// Builds an OffsetCollection from a Config, auto-detecting mirror pairs.
        /// Nodes with |x| below tolerance are centerline (no pair).
        /// </summary>
        public static OffsetCollection BuildFromConfig(Config conf, string configPath)
        {
            var collection = new OffsetCollection
            {
                sourceConfigPath = configPath,
                configHash = conf.GetHashCode()
            };

            // create one offset entry per config node
            for (int i = 0; i < conf.nodes.Length; i++)
            {
                collection.offsets.Add(new NodeOffset { configIndex = i });
            }

            // detect mirror pairs by matching (x, y, z) to (-x, y, z)
            var paired = new HashSet<int>();
            for (int i = 0; i < conf.nodes.Length; i++)
            {
                if (paired.Contains(i)) continue;
                if (conf.nodes[i].is_external_address) continue;

                Vector3 posA = conf.nodes[i].GetNodePosition();

                // centerline node — no mirror
                if (Mathf.Abs(posA.x) < PositionTolerance) continue;

                for (int j = i + 1; j < conf.nodes.Length; j++)
                {
                    if (paired.Contains(j)) continue;
                    if (conf.nodes[j].is_external_address) continue;
                    if (conf.nodes[i].target_bone != conf.nodes[j].target_bone) continue;

                    Vector3 posB = conf.nodes[j].GetNodePosition();
                    Vector3 mirrored = new Vector3(-posA.x, posA.y, posA.z);

                    if (Vector3.Distance(mirrored, posB) < PositionTolerance &&
                        Mathf.Abs(conf.nodes[i].radius - conf.nodes[j].radius) < PositionTolerance)
                    {
                        collection.mirrorPairs.Add(new MirrorPair
                        {
                            indexA = i,
                            indexB = j,
                            linked = true
                        });
                        paired.Add(i);
                        paired.Add(j);
                        break;
                    }
                }
            }

            return collection;
        }
    }
}