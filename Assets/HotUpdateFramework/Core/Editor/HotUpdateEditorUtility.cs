using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HotUpdateFramework.Editor
{
    public static class HotUpdateEditorUtility
    {
        public const string DefaultConfigAssetPath = "Assets/Resources/HotUpdateConfig.asset";

        public static HotUpdateConfig CreateDefaultConfigAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<HotUpdateConfig>(DefaultConfigAssetPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                return existing;
            }

            EnsureDirectory(Path.GetDirectoryName(DefaultConfigAssetPath));
            var config = ScriptableObject.CreateInstance<HotUpdateConfig>();
            AssetDatabase.CreateAsset(config, DefaultConfigAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = config;
            HotUpdateLogger.Log($"Created config: {DefaultConfigAssetPath}");
            return config;
        }

        public static HotUpdateConfig GetOrCreateConfig()
        {
            return HotUpdateConfig.LoadDefault() ?? CreateDefaultConfigAsset();
        }

        public static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        public static string GetProjectFullPath(string path)
        {
            return Path.GetFullPath(Path.Combine(GetProjectRoot(), path.Replace('/', Path.DirectorySeparatorChar)));
        }

        public static bool CopyIfExists(string sourcePath, string destinationAssetPath)
        {
            if (File.Exists(sourcePath) == false)
            {
                HotUpdateLogger.Warning($"Missing source file: {sourcePath}");
                return false;
            }

            string destinationFullPath = GetProjectFullPath(destinationAssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFullPath));
            File.Copy(sourcePath, destinationFullPath, true);
            HotUpdateLogger.Log($"Copy {sourcePath} -> {destinationAssetPath}");
            return true;
        }

        public static void DeleteDirectoryIfExists(string directoryPath)
        {
            string fullPath = Path.GetFullPath(directoryPath);
            EnsureProjectChildPath(fullPath);

            if (Directory.Exists(fullPath) == false)
                return;

            Directory.Delete(fullPath, true);

            string metaPath = $"{fullPath}.meta";
            if (File.Exists(metaPath))
                File.Delete(metaPath);
        }

        public static void EnsureProjectChildPath(string path)
        {
            string projectRoot = EnsureTrailingSeparator(Path.GetFullPath(GetProjectRoot()));
            string fullPath = Path.GetFullPath(path);
            string fullPathWithSeparator = EnsureTrailingSeparator(fullPath);

            var comparison = Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            if (fullPathWithSeparator.StartsWith(projectRoot, comparison) == false)
                throw new InvalidOperationException($"Can not access directory outside project: {fullPath}");

            string trimmedRoot = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string trimmedPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(trimmedRoot, trimmedPath, comparison))
                throw new InvalidOperationException("Can not access project root.");
        }

        public static void EnsureDirectory(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return;

            string[] parts = assetPath.Replace("\\", "/").Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (AssetDatabase.IsValidFolder(next) == false)
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                return path;

            return path + Path.DirectorySeparatorChar;
        }
    }
}
