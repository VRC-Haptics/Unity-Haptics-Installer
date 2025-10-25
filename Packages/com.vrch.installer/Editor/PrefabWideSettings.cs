using UnityEditor;
using UnityEngine;

namespace Editor
{
    public partial class HapticsInstaller
    {
        static float _prefabScale = 1.0f;

        private void DrawPrefabWideSettings(GameObject prefabRoot)
        {
            // slider for visualizer size
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Prefab Scale", GUILayout.Width(80));
            EditorGUI.BeginChangeCheck();
            _prefabScale = EditorGUILayout.Slider(_prefabScale, 0.1f, 2.5f);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var node in Nodes)
                {
                    node.transform.localScale = Vector3.one * _prefabScale;
                }
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}