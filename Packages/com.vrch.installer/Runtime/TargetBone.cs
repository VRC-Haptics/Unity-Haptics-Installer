using UnityEngine;

namespace HapticsInstaller.Runtime
{
    /// <summary>
    /// Small data class to hold the target bone of each Node. Since we can't pull data from VRCFury assets.
    /// </summary>
    public class TargetBone : MonoBehaviour
    {
        public HumanBodyBones targetBone;
    }
}

