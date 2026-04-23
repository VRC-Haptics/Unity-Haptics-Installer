using System.Collections.Generic;
using Common;
using UnityEngine;

namespace Scripts
{
    public class HapticEditor : MonoBehaviour
    {
        public OffsetsAsset offsets;
        public GameObject snapTarget;
        public Transform AvatarRoot => transform.parent;

        public Animator AvatarAnimator
        {
            get
            {
                var root = AvatarRoot;
                return root != null ? root.GetComponent<Animator>() : null;
            }
        }

        public Avatar GetAvatar()
        {
            var anim = AvatarAnimator;
            return anim != null ? anim.avatar : null;
        }

        public Transform GetBoneTransform(HumanBodyBones bone)
        {
            var anim = AvatarAnimator;
            if (anim == null || !anim.isHuman) return null;
            return anim.GetBoneTransform(bone);
        }
    }
}