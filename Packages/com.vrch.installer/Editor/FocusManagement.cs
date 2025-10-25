using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    public partial class HapticsInstaller
    {
        private int _currentIndex = 0;
        private Vector2 _scrollPosition;
        private float _selectedScale = 1.0f;
        
        private void DrawFocusTools(GameObject prefabRoot)
        {
            EditorGUILayout.LabelField($"Fine Tune Individual Nodes:", EditorStyles.boldLabel);
            if (Nodes.Count > 0)
            {
                // Navigation controls
                EditorGUILayout.BeginHorizontal();

                GUI.enabled = _currentIndex > 0;
                if (GUILayout.Button("<- Previous"))
                {
                    _currentIndex--;
                    FocusCurrentObject();
                }

                GUI.enabled = true;

                EditorGUILayout.LabelField($"{_currentIndex + 1} / {Nodes.Count}", EditorStyles.centeredGreyMiniLabel,
                    GUILayout.Width(80));

                GUI.enabled = _currentIndex < Nodes.Count - 1;
                if (GUILayout.Button("Next ->"))
                {
                    _currentIndex++;
                    FocusCurrentObject();
                }

                GUI.enabled = true;

                EditorGUILayout.EndHorizontal();

                // Current object display
                if (_currentIndex < Nodes.Count && Nodes[_currentIndex] != null)
                {
                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.ObjectField(Nodes[_currentIndex], typeof(GameObject), true);

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Focus in Scene"))
                    {
                        FocusCurrentObject();
                    }
                    
                    // Toggle flag for current node
                    bool isFlagged = FlaggedIndices.Contains(_currentIndex);
                    Color buttonColor = isFlagged ? Color.yellow : Color.white;
                    GUI.backgroundColor = buttonColor;
            
                    if (GUILayout.Button(isFlagged ? "★ Unflag" : "☆ Flag", GUILayout.Width(80)))
                    {
                        if (isFlagged)
                            FlaggedIndices.Remove(_currentIndex);
                        else
                            FlaggedIndices.Add(_currentIndex);
                    }
                    GUI.backgroundColor = Color.white;
            
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    
                    // start the editor
                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Node Scale", GUILayout.Width(80));
                    EditorGUI.BeginChangeCheck();
                    _selectedScale = EditorGUILayout.Slider(_selectedScale, 0.1f, 2.5f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        GameObject node = Nodes[_currentIndex];
                        node.transform.localScale = Vector3.one * _selectedScale;
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    
                    
                    EditorGUILayout.EndVertical();
                }

                // Show full list
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("All Nodes:", EditorStyles.boldLabel);
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(150));
                for (int i = 0; i < Nodes.Count; i++)
                {
                    var original = GUI.color;
                    if (FlaggedIndices.Contains(i))
                    {
                        GUI.color = Color.red;
                        
                    }
                    EditorGUILayout.BeginHorizontal();

                    // Highlight current item
                    if (i == _currentIndex) GUI.backgroundColor = Color.yellow;

                    if (GUILayout.Button($"{i + 1}.", GUILayout.Width(40)))
                    {
                        _currentIndex = i;
                        FocusCurrentObject();
                    }

                    GUI.backgroundColor = Color.white;

                    Nodes[i] = (GameObject)EditorGUILayout.ObjectField(Nodes[i], typeof(GameObject), true);

                    bool isFlagged = FlaggedIndices.Contains(i);
                    if (GUILayout.Button(isFlagged ? "★" : "☆", GUILayout.Width(25)))
                    {
                        if (isFlagged)
                            FlaggedIndices.Remove(i);
                        else
                            FlaggedIndices.Add(i);
                    }
                    
                    EditorGUILayout.EndHorizontal();
                    if (FlaggedIndices.Contains(i))
                    {
                        GUI.color = original;

                    }
                }
                EditorGUILayout.EndScrollView();
                EditorGUILayout.Space();
                EditorGUILayout.BeginHorizontal();
                
                if (FlaggedIndices.Count > 0)
                {
                    if (GUILayout.Button($"Select Flagged ({FlaggedIndices.Count})", GUILayout.Width(120)))
                    {
                        // Select all flagged nodes in Unity
                        List<Object> flaggedObjects = new List<Object>();
                        foreach (int index in FlaggedIndices)
                        {
                            if (index < Nodes.Count && Nodes[index] != null)
                            {
                                flaggedObjects.Add(Nodes[index]);
                            }
                        }
                        Selection.objects = flaggedObjects.ToArray();
                    }
                    
                    if (GUILayout.Button($"Clear Flags", GUILayout.Width(80)))
                    {
                        FlaggedIndices.Clear();
                    }
                }
                
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("Fill \"Prefab to Edit\" to see nodes here.",
                    MessageType.Info);
            }
        }

        void FocusCurrentObject()
        {
            if (Nodes.Count == 0 || _currentIndex >= Nodes.Count) return;
            
            GameObject obj = Nodes[_currentIndex];
            if (obj == null) return;

            // Select the object (shows in Inspector)
            Selection.activeGameObject = obj;
            _selectedScale = (obj.transform.localScale.x + obj.transform.localScale.y + obj.transform.localScale.z) / 3;

            // Ping it in the hierarchy
            EditorGUIUtility.PingObject(obj);

            // Frame it in the Scene view
            SceneView.lastActiveSceneView?.FrameSelected();
        }
    }
}