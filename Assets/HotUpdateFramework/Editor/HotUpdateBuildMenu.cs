using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HotUpdateFramework;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Settings;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace HotUpdateFramework.Editor
{
    public static class HotUpdateBuildMenu
    {
        private const string MenuRoot = "Hot Update/";
        private const string ConfigAssetPath = "Assets/Resources/HotUpdateConfig.asset";

        [MenuItem(MenuRoot + "Create Default Config", priority = 1)]
        public static HotUpdateConfig CreateDefaultConfigAsset()
        {
            HotUpdateConfig existing = AssetDatabase.LoadAssetAtPath<HotUpdateConfig>(ConfigAssetPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                return existing;
            }

            EnsureDirectory(Path.GetDirectoryName(ConfigAssetPath));
            var config = ScriptableObject.CreateInstance<HotUpdateConfig>();
            AssetDatabase.CreateAsset(config, ConfigAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = config;
            Debug.Log($"[HotUpdate] Created config: {ConfigAssetPath}");
            return config;
        }

        [MenuItem(MenuRoot + "Generate HybridCLR/Compile HotUpdate DLLs", priority = 20)]
        public static void CompileHotUpdateDlls()
        {
            CompileDllCommand.CompileDll(EditorUserBuildSettings.activeBuildTarget, EditorUserBuildSettings.development);
        }

        [MenuItem(MenuRoot + "Generate HybridCLR/Generate All", priority = 21)]
        public static void GenerateHybridClrAll()
        {
            PrebuildCommand.GenerateAll();
            SyncAotMetadataList();
        }

        [MenuItem(MenuRoot + "Generate HybridCLR/Sync AOT Metadata List", priority = 30)]
        public static void SyncAotMetadataList()
        {
            HotUpdateConfig config = GetOrCreateConfig();
            string aotGenericReferencesPath = GetAotGenericReferencesFullPath();
            string[] aotMetadataAssemblyNames = ReadPatchedAotAssemblyList(aotGenericReferencesPath);
            if (aotMetadataAssemblyNames.Length == 0)
            {
                Debug.LogWarning($"[HotUpdate] No AOT metadata assembly found in {aotGenericReferencesPath}");
                return;
            }

            string[] hybridClrPatchAotAssemblyNames = aotMetadataAssemblyNames
                .Select(RemoveDllExtension)
                .ToArray();

            bool configChanged = SyncStringArrayProperty(config, "aotMetadataAssemblyNames", aotMetadataAssemblyNames);
            bool hybridClrSettingsChanged = SyncStringArrayProperty(SettingsUtil.HybridCLRSettings, "patchAOTAssemblies", hybridClrPatchAotAssemblyNames);
            if (hybridClrSettingsChanged)
                HybridCLRSettings.Save();

            if (configChanged || hybridClrSettingsChanged)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[HotUpdate] Synced AOT metadata list: {string.Join(", ", aotMetadataAssemblyNames)}");
                return;
            }

            Debug.Log($"[HotUpdate] AOT metadata list is already synced: {string.Join(", ", aotMetadataAssemblyNames)}");
        }

        [MenuItem(MenuRoot + "Prepare YooAsset DLL Assets", priority = 40)]
        public static void PrepareYooAssetRawFiles()
        {
            HotUpdateConfig config = GetOrCreateConfig();
            int copiedCount = 0;

            foreach (string location in config.HotUpdateAssemblyAssetLocations)
            {
                if (string.IsNullOrWhiteSpace(location))
                    continue;

                string dllFileName = GetDllFileNameFromAssetLocation(location);
                string sourcePath = GetFullPath(SettingsUtil.GetHotUpdateDllsOutputDirByTarget(EditorUserBuildSettings.activeBuildTarget), dllFileName);
                if (CopyIfExists(sourcePath, location))
                    copiedCount++;
            }

            foreach (string location in config.AotMetadataAssetLocations)
            {
                if (string.IsNullOrWhiteSpace(location))
                    continue;

                string dllFileName = GetDllFileNameFromAssetLocation(location);
                string sourcePath = GetFullPath(SettingsUtil.GetAssembliesPostIl2CppStripDir(EditorUserBuildSettings.activeBuildTarget), dllFileName);
                if (CopyIfExists(sourcePath, location))
                    copiedCount++;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[HotUpdate] Prepared YooAsset DLL assets. Copied: {copiedCount}");
        }

        [MenuItem(MenuRoot + "Build YooAsset Package", priority = 60)]
        public static void BuildYooAssetPackage()
        {
            HotUpdateConfig config = GetOrCreateConfig();

            string packageVersion = string.IsNullOrWhiteSpace(config.PackageVersionOverride)
                ? DateTime.Now.ToString("yyyyMMddHHmmss")
                : config.PackageVersionOverride;
            EBuildinFileCopyOption buildinFileCopyOption = config.UseBuildinFileSystemInHostMode
                ? EBuildinFileCopyOption.ClearAndCopyAll
                : EBuildinFileCopyOption.None;

            ScriptableBuildParameters buildParameters = new ScriptableBuildParameters
            {
                BuildOutputRoot = AssetBundleBuilderHelper.GetDefaultBuildOutputRoot(),
                BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot(),
                BuildPipeline = EBuildPipeline.ScriptableBuildPipeline.ToString(),
                BuildBundleType = (int)EBuildBundleType.AssetBundle,
                BuildTarget = EditorUserBuildSettings.activeBuildTarget,
                PackageName = config.PackageName,
                PackageVersion = packageVersion,
                PackageNote = "Hot update package",
                EnableSharePackRule = true,
                SingleReferencedPackAlone = false,
                VerifyBuildingResult = true,
                FileNameStyle = EFileNameStyle.HashName,
                BuildinFileCopyOption = buildinFileCopyOption,
                BuildinFileCopyParams = string.Empty,
                CompressOption = ECompressOption.LZ4,
                ClearBuildCacheFiles = false,
                UseAssetDependencyDB = true,
                BuiltinShadersBundleName = GetBuiltinShaderBundleName(config.PackageName)
            };

            Debug.Log($"[HotUpdate] Buildin file copy option: {buildinFileCopyOption}");

            var pipeline = new ScriptableBuildPipeline();
            BuildResult buildResult = pipeline.Run(buildParameters, true);
            if (buildResult.Success)
            {
                EditorUtility.RevealInFinder(buildResult.OutputPackageDirectory);
                Debug.Log($"[HotUpdate] YooAsset package built: {buildResult.OutputPackageDirectory}");
            }
            else
            {
                throw new Exception($"YooAsset build failed: {buildResult.FailedTask}, {buildResult.ErrorInfo}");
            }
        }

        private static string GetBuiltinShaderBundleName(string packageName)
        {
            bool uniqueBundleName = AssetBundleCollectorSettingData.Setting.UniqueBundleName;
            PackRuleResult packRuleResult = DefaultPackRule.CreateShadersPackRuleResult();
            return packRuleResult.GetBundleName(packageName, uniqueBundleName);
        }

        private static HotUpdateConfig GetOrCreateConfig()
        {
            return HotUpdateConfig.LoadDefault() ?? CreateDefaultConfigAsset();
        }

        private static string GetAotGenericReferencesFullPath()
        {
            string outputFile = SettingsUtil.HybridCLRSettings.outputAOTGenericReferenceFile;
            return Path.GetFullPath(Path.Combine(Application.dataPath, outputFile.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string[] ReadPatchedAotAssemblyList(string filePath)
        {
            if (File.Exists(filePath) == false)
                throw new FileNotFoundException($"AOTGenericReferences file not found: {filePath}", filePath);

            string content = File.ReadAllText(filePath);
            Match listMatch = Regex.Match(
                content,
                @"PatchedAOTAssemblyList\s*=\s*new\s+List<string>\s*\{(?<body>.*?)\};",
                RegexOptions.Singleline);

            if (listMatch.Success == false)
                throw new Exception($"Can not parse PatchedAOTAssemblyList in {filePath}");

            var assemblyNames = new List<string>();
            MatchCollection matches = Regex.Matches(listMatch.Groups["body"].Value, "\"(?<name>[^\"]+)\"");
            foreach (Match match in matches)
            {
                string assemblyName = NormalizeDllAssemblyName(match.Groups["name"].Value);
                if (string.IsNullOrWhiteSpace(assemblyName))
                    continue;

                if (assemblyNames.Any(item => string.Equals(item, assemblyName, StringComparison.OrdinalIgnoreCase)) == false)
                    assemblyNames.Add(assemblyName);
            }

            return assemblyNames.ToArray();
        }

        private static bool SyncStringArrayProperty(UnityEngine.Object target, string propertyName, string[] values)
        {
            var serializedObject = new SerializedObject(target);
            serializedObject.Update();
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
                throw new Exception($"Can not find serialized property: {propertyName}");

            if (StringArrayPropertyEquals(property, values))
                return false;

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).stringValue = values[i];

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
                    return false;
            }

            return true;
        }

        private static bool CopyIfExists(string sourcePath, string destinationAssetPath)
        {
            if (File.Exists(sourcePath) == false)
            {
                Debug.LogWarning($"[HotUpdate] Missing source file: {sourcePath}");
                return false;
            }

            string destinationFullPath = GetProjectFullPath(destinationAssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFullPath));
            File.Copy(sourcePath, destinationFullPath, true);
            Debug.Log($"[HotUpdate] Copy {sourcePath} -> {destinationAssetPath}");
            return true;
        }

        private static string GetDllFileNameFromAssetLocation(string assetLocation)
        {
            string fileName = Path.GetFileName(assetLocation);
            if (fileName.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - ".bytes".Length);
            return fileName;
        }

        private static string RemoveDllExtension(string assemblyName)
        {
            if (assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return assemblyName.Substring(0, assemblyName.Length - ".dll".Length);

            return assemblyName;
        }

        private static string NormalizeDllAssemblyName(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
                return string.Empty;

            string fileName = Path.GetFileName(assemblyName.Trim().Replace('\\', '/'));
            if (fileName.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - ".bytes".Length);

            if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) == false)
                fileName += ".dll";

            return fileName;
        }

        private static string GetFullPath(string relativeDirectory, string fileName)
        {
            return Path.GetFullPath(Path.Combine(SettingsUtil.ProjectDir, relativeDirectory.Replace('/', Path.DirectorySeparatorChar), fileName));
        }

        private static string GetProjectFullPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(SettingsUtil.ProjectDir, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void EnsureDirectory(string assetPath)
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
    }
}
