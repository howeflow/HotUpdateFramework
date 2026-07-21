using System;
using System.Collections.Generic;
using System.IO;
using HotUpdateFramework.Editor;
using UnityEditor;
using UnityEngine;

namespace HotUpdateFramework.Code.Editor
{
    public static class HotUpdateCodeEditorUtility
    {
        public const string DefaultConfigAssetPath = "Assets/Resources/HotUpdateCodeConfig.asset";

        public static HotUpdateCodeConfig CreateDefaultConfigAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<HotUpdateCodeConfig>(DefaultConfigAssetPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                return existing;
            }

            HotUpdateEditorUtility.EnsureDirectory(Path.GetDirectoryName(DefaultConfigAssetPath));
            var config = ScriptableObject.CreateInstance<HotUpdateCodeConfig>();
            AssetDatabase.CreateAsset(config, DefaultConfigAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = config;
            HotUpdateLogger.Log($"Created config: {DefaultConfigAssetPath}");
            return config;
        }

        public static HotUpdateCodeConfig GetOrCreateConfig()
        {
            return HotUpdateCodeConfig.LoadDefault() ?? CreateDefaultConfigAsset();
        }
    }
}
