using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    public interface Utils
    {
        public static void CreateDirectoryFromAssetPath(string assetPath)
        {
            string directoryPath = Path.GetDirectoryName(assetPath);
            if (Directory.Exists(directoryPath))
                return;
            Directory.CreateDirectory(directoryPath);
            AssetDatabase.Refresh();
        }

        public static bool MoveAsset(string oldPath, string newPath)
        {
            CreateDirectoryFromAssetPath(newPath);
            string error = AssetDatabase.MoveAsset(oldPath, newPath);
            if (error != null)
            {
                Debug.LogError(error);
                return false;
            }
            return true;
        }
        
        public static T CopyComponent<T>(T original, GameObject destination) where T : Component
        {
            T copy = destination.AddComponent<T>();
            var type = typeof(T);
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields)
            {
                field.SetValue(copy, field.GetValue(original));
            }
            return copy;
        }
    }
}