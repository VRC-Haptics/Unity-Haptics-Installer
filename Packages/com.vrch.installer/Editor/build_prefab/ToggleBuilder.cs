using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Algolia.Search.Models.Common;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Editor.build_prefab
{
    public class ToggleBuilder
    {
        private GameObject _animator_root;
        private string _parameter;
        private string _save_dir;
        private string _name;
        private bool _default_on;

        private List<GameObject> _toggled = new List<GameObject>();

        /// <summary>
        /// Creates a new binary toggle group in reference to the `menu_root`
        /// </summary>
        public ToggleBuilder(GameObject animator_root, string parameter, bool enabled_default, string save_dir, string name)
        {
            _animator_root = animator_root;
            _parameter = parameter;
            _default_on = enabled_default;
            _save_dir = save_dir;
            _name = name;
        }

        public void AddObject(GameObject obj)
        {
            _toggled.Add(obj);
        }

        public AnimatorController Finalize()
        {
            if (_toggled.Count == 0)
            {
                Debug.LogWarning("ToggleBuilder: No objects to toggle.");
                return null;
            }

            // Ensure save directory exists
            if (!Directory.Exists(_save_dir))
            {
                Directory.CreateDirectory(_save_dir);
                AssetDatabase.Refresh();
            }

            // Create animation clips
            AnimationClip onClip = new AnimationClip { name = _name + "_On" };
            AnimationClip offClip = new AnimationClip { name = _name + "_Off" };

            foreach (var obj in _toggled)
            {
                string path = AnimationUtility.CalculateTransformPath(obj.transform, _animator_root.transform);

                AnimationCurve curveOn = AnimationCurve.Constant(0f, 0f, 1f);
                AnimationCurve curveOff = AnimationCurve.Constant(0f, 0f, 0f);

                onClip.SetCurve(path, typeof(GameObject), "m_IsActive", curveOn);
                offClip.SetCurve(path, typeof(GameObject), "m_IsActive", curveOff);
            }

            AssetDatabase.CreateAsset(onClip, Path.Combine(_save_dir, _name + "_On.anim"));
            AssetDatabase.CreateAsset(offClip, Path.Combine(_save_dir, _name + "_Off.anim"));

            // Create animator controller
            string controllerPath = Path.Combine(_save_dir, _name + "_Controller.controller");
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            // Add bool parameter
            controller.AddParameter(_parameter, AnimatorControllerParameterType.Bool);

            // Get or add layer
            AnimatorControllerLayer layer = controller.layers[0];
            layer.name = "Haptic/Toggle";
            controller.layers = new[] { layer };

            AnimatorStateMachine stateMachine = layer.stateMachine;

            // Create states
            AnimatorState onState = stateMachine.AddState("On");
            onState.motion = onClip;
            onState.writeDefaultValues = true;

            AnimatorState offState = stateMachine.AddState("Off");
            offState.motion = offClip;
            offState.writeDefaultValues = true;

            // Set default state
            stateMachine.defaultState = _default_on ? onState : offState;

            // Transition: Off -> On (when parameter is true)
            AnimatorStateTransition toOn = offState.AddTransition(onState);
            toOn.AddCondition(AnimatorConditionMode.If, 0, _parameter);
            toOn.hasExitTime = false;
            toOn.duration = 0f;

            // Transition: On -> Off (when parameter is false)
            AnimatorStateTransition toOff = onState.AddTransition(offState);
            toOff.AddCondition(AnimatorConditionMode.IfNot, 0, _parameter);
            toOff.hasExitTime = false;
            toOff.duration = 0f;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"ToggleBuilder: Created toggle '{_name}' with {_toggled.Count} objects at {_save_dir}");
            return controller;
        }
    }
}