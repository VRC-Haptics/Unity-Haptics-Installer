using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

public class UploadHook : IVRCSDKBuildRequestedCallback
{
    public int callbackOrder => 0;
    
    public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
    {
        switch (requestedBuildType)
        {
            case VRCSDKRequestedBuildType.Avatar: return avatarBuild(); 
            case VRCSDKRequestedBuildType.Scene: return true;
            default:
            {
                Debug.LogError("Unknown VRCSDKRequestedBuildType, Haptics Package likely out of date.");
                return true;
            };
        }
    }

    bool avatarBuild()
    {
        Debug.Log("Avatar build");
        return true;
    }
}
