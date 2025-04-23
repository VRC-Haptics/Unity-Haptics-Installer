# Unity-Haptics-Installer

This is a VPM (VRC Package Manager) package for integrating haptics into an avatar. 

[Add to VCC](vcc://vpm/addRepo?url=https://vrc-haptics.github.io/Unity-Haptics-Installer/index.json)

Currently depends on VRCFury, but uses minimal components hopefully for quick enough compile times.

# Technical Info

Most performance related details scale with the number of bones the optimized prefab is attached to. For example a Vest + Tail Pull combination results in a multiplier of 3x: Vests:(hip, spine, chest) + Tail Pull:(hip) -> Optimized:(hip, spine, chest)

 - Triangles: Nodes(non-external) x 20 tri's/node
 - Mesh Renderers: Bone Multiplier
 - Material Slots: Bone Multiplier
 - Synced Bits: 1 bit
 - Non-Local Contacts: 0
 - Skinned Mesh Renderers: 0
 - Animators: 0

 These numbers means that it is completely feasible to have a 40 node haptic vest on an avatar with Excellent rating on pc, and a Poor rating on the quest representation.

## Requirements  

- [VCC](https://vrchat.com/home/download) or [ALCOM](https://vrc-get.anatawa12.com/alcom/)
- Unity: 2022.3.22f1 (possibly other versions?)
- [VRCFury](https://vrcfury.com/download): Any semi-modern version
- [Configuration Map](https://github.com/VRC-Haptics/Unity-Config-Generator): Multiple maps can be baked into one prefab.

## Basic Usage:

**Three steps:** 
1. Generate Basic Layout.
2. Conform to Avatar.
3. Bake For Performance.

**IMPORTANT**: This package uses small scripts attached to the objects to store critical information. Please do not `Auto-Fix` without the optimized versions created, and the unoptimized versions removed from the avatar.

### Generate Basic Layout:
Once a project is opened with the required packages installed, the first step is transforming the json formatted config into objects in the scene.  

1. On the top bar: `Haptics -> Start Installer`.
2. Drag Avatar root to the slot on the installer.
3. Click: `Select Configuration File`, Select your desired config file
4. Click: `Create Prefab`.

### Conform to Avatar:
Drag nodes to desired spots. **DO NOT JUST DRAG THE GREEN THINGS** 

**GREEN THINGS**: Nodes Consist of two things; a visualizer and a contact reciever. The visualizer is parented to the contact, so just moving it wouldn't influence the contact at all. Select the entire node from the Heirarchy instead. 

The goal is to move the node as close as possible to where it represents on the avatar. Striking a balance between what you see/feel and what others interact with is your own equation to work out. As for scaling, adjust the `Radius` parameter on the Contact Reciever directly. The visualizers will automatically scale to fit the contact's radius when baking the prefab.

Group editing is very useful during this time. Just be aware that editing the Reciever Parameter, Reciever Type, or Filtering will cause issues.

Once that is completed, you can propogate your changes to the prefab by:
 - Selecting the prefab root
 - `Inspector -> Overrides -> Apply All`


Repeat for every prefab that is going to be on this avatar before moving to the next step.

### Bake Prefabs:

Your generated prefabs are saved under the `Assets/Haptics` Directory, you can safely delete the prefabs from the avatar root now. 

Baking prefabs consolidates multiple prefabs into the most condensed form without damaging the previous prefabs. Simply make sure each of the haptics prefabs has an entry on the `Prefabs to Optimize` list and press the `Bake Prefabs` button. 

YOUR DONE! 
This produces the final prefab that integrates into your avatar on upload.