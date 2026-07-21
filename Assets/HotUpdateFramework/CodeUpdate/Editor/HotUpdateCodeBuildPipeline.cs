using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Settings;
using HotUpdateFramework.Editor;
using UnityEditor;
using UnityEngine;

namespace HotUpdateFramework.Code.Editor
{
    public static class HotUpdateCodeBuildPipeline
    {
        public static void PrepareAllProcess()
        {
            PrebuildCommand.GenerateAll();
            SyncAotMetadataList();
            CopyAotMetadataAndHotUpdateDlls();
        }

        public static void PrepareHotUpdateProcess()
        {
            CompileDllCommand.CompileDll(EditorUserBuildSettings.activeBuildTarget, EditorUserBuildSettings.development);
            CopyAotMetadataAndHotUpdateDlls();
        }

        public static void SyncAotMetadataList()
        {
            HotUpdateCodeConfig config = HotUpdateCodeEditorUtility.GetOrCreateConfig();
            string aotGenericReferencesPath = GetAotGenericReferencesFullPath();
            string[] aotMetadataAssemblyNames = ReadPatchedAotAssemblyList(aotGenericReferencesPath);
            if (aotMetadataAssemblyNames.Length == 0)
            {
                HotUpdateLogger.Warning($"No AOT metadata assembly found in " + $"{aotGenericReferencesPath}");
                return;
            }

            string[] hybridClrPatchAotAssemblyNames = aotMetadataAssemblyNames.Select(HotUpdateUtility.RemoveDllExtension).ToArray();

            bool configChanged = SyncStringArrayProperty(config, "aotMetadataAssemblyNames", aotMetadataAssemblyNames);
            bool hybridClrSettingsChanged = SyncStringArrayProperty(SettingsUtil.HybridCLRSettings, "patchAOTAssemblies", hybridClrPatchAotAssemblyNames);
            if (hybridClrSettingsChanged)
                HybridCLRSettings.Save();

            if (configChanged || hybridClrSettingsChanged)
            {
                AssetDatabase.SaveAssets();
                HotUpdateLogger.Log($"Synced AOT metadata list: " + $"{string.Join(", ", aotMetadataAssemblyNames)}");
                return;
            }

            HotUpdateLogger.Log($"AOT metadata list is already synced: " + $"{string.Join(", ", aotMetadataAssemblyNames)}");
        }

        public static void CopyAotMetadataAndHotUpdateDlls()
        {
            HotUpdateCodeConfig config = HotUpdateCodeEditorUtility.GetOrCreateConfig();
            string hotUpdateDllOutputDirectory = GetHotUpdateDllOutputDirectory();
            CopyAotMetadataAndHotUpdateDlls(config, hotUpdateDllOutputDirectory);
        }

        public static void CopyAotMetadataAndHotUpdateDlls(HotUpdateCodeConfig config, string hotUpdateDllOutputDirectory)
        {
            int copiedCount = CopyHotUpdateDlls(config, hotUpdateDllOutputDirectory);
            copiedCount += CopyAotMetadataDlls(config);

            AssetDatabase.Refresh();
            HotUpdateLogger.Log($"AOT metadata and hot update DLL assets " + $"copied: {copiedCount}");
        }

        public static string GetHotUpdateDllOutputDirectory()
        {
            return HotUpdateEditorUtility.GetProjectFullPath(SettingsUtil.GetHotUpdateDllsOutputDirByTarget(EditorUserBuildSettings.activeBuildTarget));
        }

        public static string GetAotMetadataDllOutputDirectory()
        {
            return HotUpdateEditorUtility.GetProjectFullPath(SettingsUtil.GetAssembliesPostIl2CppStripDir(EditorUserBuildSettings.activeBuildTarget));
        }

        public static List<string> GetHotUpdateDllPaths(HotUpdateCodeConfig config, string hotUpdateDllOutputDirectory)
        {
            var dllPaths = new List<string>();
            foreach (string location in config.HotUpdateAssemblyAssetLocations)
            {
                if (string.IsNullOrWhiteSpace(location))
                    continue;

                string dllFileName = HotUpdateUtility.GetDllFileNameFromAssetLocation(location);
                string sourcePath = Path.GetFullPath(Path.Combine(hotUpdateDllOutputDirectory, dllFileName));
                if (File.Exists(sourcePath) == false)
                {
                    throw new FileNotFoundException($"Hot update DLL not found: " + $"{sourcePath}", sourcePath);
                }

                dllPaths.Add(sourcePath);
            }

            if (dllPaths.Count == 0)
            {
                throw new InvalidOperationException("No hot update DLL configured in " + "HotUpdateCodeConfig.");
            }

            return dllPaths;
        }

        public static int CopyHotUpdateDlls(HotUpdateCodeConfig config, string hotUpdateDllOutputDirectory)
        {
            int copiedCount = 0;
            foreach (string location in config.HotUpdateAssemblyAssetLocations)
            {
                if (string.IsNullOrWhiteSpace(location))
                    continue;

                string sourcePath = Path.GetFullPath(Path.Combine(hotUpdateDllOutputDirectory, HotUpdateUtility.GetDllFileNameFromAssetLocation(location)));
                if (HotUpdateEditorUtility.CopyIfExists(sourcePath, location))
                {
                    copiedCount++;
                }
            }

            return copiedCount;
        }

        public static int CopyAotMetadataDlls(HotUpdateCodeConfig config)
        {
            int copiedCount = 0;
            string aotMetadataDirectory = GetAotMetadataDllOutputDirectory();
            foreach (string location in config.AotMetadataAssetLocations)
            {
                if (string.IsNullOrWhiteSpace(location))
                    continue;

                string sourcePath = Path.GetFullPath(Path.Combine(aotMetadataDirectory, HotUpdateUtility.GetDllFileNameFromAssetLocation(location)));
                if (HotUpdateEditorUtility.CopyIfExists(sourcePath, location))
                {
                    copiedCount++;
                }
            }

            return copiedCount;
        }

        private static string GetAotGenericReferencesFullPath()
        {
            string outputFile = SettingsUtil.HybridCLRSettings.outputAOTGenericReferenceFile;
            return Path.GetFullPath(Path.Combine(Application.dataPath, outputFile.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string[] ReadPatchedAotAssemblyList(string filePath)
        {
            if (File.Exists(filePath) == false)
            {
                throw new FileNotFoundException($"AOTGenericReferences file not found: " + $"{filePath}", filePath);
            }

            string content = File.ReadAllText(filePath);
            Match listMatch = Regex.Match(content, @"PatchedAOTAssemblyList\s*=\s*new\s+" + @"List<string>\s*\{(?<body>.*?)\};", RegexOptions.Singleline);

            if (listMatch.Success == false)
            {
                throw new Exception($"Can not parse PatchedAOTAssemblyList " + $"in {filePath}");
            }

            var assemblyNames = new List<string>();
            MatchCollection matches = Regex.Matches(listMatch.Groups["body"].Value, "\"(?<name>[^\"]+)\"");
            foreach (Match match in matches)
            {
                string assemblyName = HotUpdateUtility.NormalizeDllAssemblyName(match.Groups["name"].Value);
                if (string.IsNullOrWhiteSpace(assemblyName))
                    continue;

                if (assemblyNames.Any(item => string.Equals(item, assemblyName, StringComparison.OrdinalIgnoreCase)) == false)
                {
                    assemblyNames.Add(assemblyName);
                }
            }

            return assemblyNames.ToArray();
        }

        private static bool SyncStringArrayProperty(UnityEngine.Object target, string propertyName, string[] values)
        {
            var serializedObject = new SerializedObject(target);
            serializedObject.Update();
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new Exception($"Can not find serialized property: " + $"{propertyName}");
            }

            if (StringArrayPropertyEquals(property, values))
                return false;

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).stringValue = values[i];
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            return true;
        }

        private static bool StringArrayPropertyEquals(SerializedProperty property, string[] values)
        {
            if (property.arraySize != values.Length)
                return false;

            for (int i = 0; i < values.Length; i++)
            {
                string propertyValue = property.GetArrayElementAtIndex(i).stringValue;
                if (string.Equals(propertyValue, values[i], StringComparison.OrdinalIgnoreCase) == false)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
