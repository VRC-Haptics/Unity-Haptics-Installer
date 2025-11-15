using System;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace HapticsInstaller.Runtime
{
    /// <summary>
    /// Small data class to hold the target bone of the Menu. Since we can't pull data from VRCFury assets.
    /// </summary>
    [Serializable]
    public class MenuData : MonoBehaviour, IEditorOnly
    {
        /// <summary>
        /// The Menu for This Prefab that needs to be merged
        /// </summary>
        public VRCExpressionsMenu vrcMenu;
    }
}

