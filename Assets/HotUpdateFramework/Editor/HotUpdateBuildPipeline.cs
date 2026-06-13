using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Settings;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace HotUpdateFramework.Editor
{
    public static class HotUpdateBuildPipeline
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
            HotUpdateConfig config = HotUpdateEditorUtility.GetOrCreateConfig();
            string aotGenericReferencesPath = GetAotGenericReferencesFullPath();
            string[] aotMetadataAssemblyNames = ReadPatchedAotAssemblyList(aotGenericReferencesPath);
            if (aotMetadataAssemblyNames.Length == 0)
            {
                HotUpdateLogger.Warning($"No AOT metadata assembly found in {aotGenericReferencesPath}");
                return;
            }

            string[] hybridClrPatchAotAssemblyNames = aotMetadataAssemblyNames
                .Select(HotUpdateUtility.RemoveDllExtension)
                .ToArray();

            bool configChanged = SyncStringArrayProperty(config, "aotMetadataAssemblyNames", aotMetadataAssemblyNames);
            bool hybridClrSettingsChanged = SyncStringArrayProperty(SettingsUtil.HybridCLRSettings, "patchAOTAssemblies", hybridClrPatchAotAssemblyNames);
            if (hybridClrSettingsChanged)
                HybridCLRSettings.Save();

            if (configChanged || hybridClrSettingsChanged)
            {
                AssetDatabase.SaveAssets();
                HotUpdateLogger.LogAlways($"Synced AOT metadata list: {string.Join(", ", aotMetadataAssemblyNames)}");
                return;
            }

            HotUpdateLogger.LogAlways($"AOT metadata list is already synced: {string.Join(", ", aotMetadataAssemblyNames)}");
        }

        public static void CopyAotMetadataAndHotUpdateDlls()
        {
            HotUpdateConfig config = HotUpdateEditorUtility.GetOrCreateConfig();
            string hotUpdateDllOutputDirectory = GetHotUpdateDllOutputDirectory();
            CopyAotMetadataAndHotUpdateDlls(config, hotUpdateDllOutputDirectory);
        }

        public static void CopyAotMetadataAndHotUpdateDlls(HotUpdateConfig config, string hotUpdateDllOutputDirectory)
        {
            int copiedCount = CopyHotUpdateDlls(config, hotUpdateDllOutputDirectory);
            copiedCount += CopyAotMetadataDlls(config);

            AssetDatabase.Refresh();
            HotUpdateLogger.LogAlways($"AotMetadataAndHotUpdateDlls assets. Copied: {copiedCount}");
        }

        public static string GetHotUpdateDllOutputDirectory()
        {
            return HotUpdateEditorUtility.GetProjectFullPath(SettingsUtil.GetHotUpdateDllsOutputDirByTarget(EditorUserBuildSettings.activeBuildTarget));
        }

        public static string GetAotMetadataDllOutputDirectory()
        {
            return HotUpdateEditorUtility.GetProjectFullPath(SettingsUtil.GetAssembliesPostIl2CppStripDir(EditorUserBuildSettings.activeBuildTarget));
        }

        public static List<string> GetHotUpdateDllPaths(HotUpdateConfig config, string hotUpdateDllOutputDirectory)
        {
            var dllPaths = new List<string>();
            foreach (string location in config.HotUpdateAssemblyAssetLocations)
            {
                if (string.IsNullOrWhiteSpace(location))
                    continue;

                string dllFileName = HotUpdateUtility.GetDllFileNameFromAssetLocation(location);
                string sourcePath = Path.GetFullPath(Path.Combine(hotUpdateDllOutputDirectory, dllFileName));
                if (File.Exists(sourcePath) == false)
                    throw new FileNotFoundException($"Hot update DLL not found: {sourcePath}", sourcePath);

                dllPaths.Add(sourcePath);
            }

            if (dllPaths.Count == 0)
                throw new InvalidOperationException("No hot update DLL configured in HotUpdateConfig.");

            return dllPaths;
        }

        public static int CopyHotUpdateDlls(HotUpdateConfig config, string hotUpdateDllOutputDirectory)
        {
            int copiedCount = 0;
            foreach (string location in config.HotUpdateAssemblyAssetLocations)
            {
                if (string.IsNullOrWhiteSpace(location))
                    continue;

                string sourcePath = Path.GetFullPath(Path.Combine(hotUpdateDllOutputDirectory, HotUpdateUtility.GetDllFileNameFromAssetLocation(location)));
                if (HotUpdateEditorUtility.CopyIfExists(sourcePath, location))
                    copiedCount++;
            }

            return copiedCount;
        }

        public static int CopyAotMetadataDlls(HotUpdateConfig config)
        {
            int copiedCount = 0;
            string aotMetadataDirectory = GetAotMetadataDllOutputDirectory();
            foreach (string location in config.AotMetadataAssetLocations)
            {
                if (string.IsNullOrWhiteSpace(location))
                    continue;

                string sourcePath = Path.GetFullPath(Path.Combine(aotMetadataDirectory, HotUpdateUtility.GetDllFileNameFromAssetLocation(location)));
                if (HotUpdateEditorUtility.CopyIfExists(sourcePath, location))
                    copiedCount++;
            }

            return copiedCount;
        }

        public static string GetDllFileNameFromAssetLocation(string assetLocation)
        {
            return HotUpdateUtility.GetDllFileNameFromAssetLocation(assetLocation);
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
                string assemblyName = HotUpdateUtility.NormalizeDllAssemblyName(match.Groups["name"].Value);
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

        public static void BuildPackage()
        {
            HotUpdateConfig config = HotUpdateEditorUtility.GetOrCreateConfig();

            string packageVersion = string.IsNullOrWhiteSpace(config.PackageVersionOverride)
                ? DateTime.Now.ToString("yyyyMMddHHmmss")
                : config.PackageVersionOverride;
            EBuildinFileCopyOption buildinFileCopyOption = config.UseBuildinFileSystemInHostMode
                ? EBuildinFileCopyOption.ClearAndCopyAll
                : EBuildinFileCopyOption.None;
            IEncryptionServices encryptionServices = HotUpdateCryptoProvider.EncryptionServices;

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
                EncryptionServices = encryptionServices,
                ClearBuildCacheFiles = false,
                UseAssetDependencyDB = true,
                BuiltinShadersBundleName = GetBuiltinShaderBundleName(config.PackageName)
            };

            HotUpdateLogger.LogAlways($"Buildin file copy option: {buildinFileCopyOption}");
            HotUpdateLogger.LogAlways($"Bundle encryption: {(encryptionServices != null ? "Enabled" : "Disabled")}");

            var pipeline = new ScriptableBuildPipeline();
            BuildResult buildResult = pipeline.Run(buildParameters, true);
            if (buildResult.Success)
            {
                EditorUtility.RevealInFinder(buildResult.OutputPackageDirectory);
                HotUpdateLogger.LogAlways($"YooAsset package built: {buildResult.OutputPackageDirectory}");
                return;
            }

            throw new Exception($"YooAsset build failed: {buildResult.FailedTask}, {buildResult.ErrorInfo}");
        }

        public static void ClearBuildCache()
        {
            HotUpdateConfig config = HotUpdateEditorUtility.GetOrCreateConfig();
            string packageRootDirectory = Path.GetFullPath(Path.Combine(
                AssetBundleBuilderHelper.GetDefaultBuildOutputRoot(),
                EditorUserBuildSettings.activeBuildTarget.ToString(),
                config.PackageName));
            HotUpdateEditorUtility.EnsureProjectChildPath(packageRootDirectory);

            bool confirmed = EditorUtility.DisplayDialog(
                "Clear YooAsset Build Cache",
                $"This will purge Scriptable Build Pipeline cache and delete YooAsset build directory.\n\n{packageRootDirectory}",
                "Clear",
                "Cancel");
            if (confirmed == false)
                return;

            UnityEditor.Build.Pipeline.Utilities.BuildCache.PurgeCache(false);
            HotUpdateEditorUtility.DeleteDirectoryIfExists(packageRootDirectory);
            AssetDatabase.Refresh();
            HotUpdateLogger.LogAlways($"Cleared YooAsset build cache: {packageRootDirectory}");
        }

        public static void ClearEditorRuntimeCache()
        {
            string yooFolderName = YooAssetSettingsData.GetDefaultYooFolderName();
            if (string.IsNullOrWhiteSpace(yooFolderName))
            {
                HotUpdateLogger.Warning("YooAsset DefaultYooFolderName is empty. Skip clearing editor runtime cache to avoid deleting project root.");
                return;
            }

            string cacheDirectory = Path.GetFullPath(Path.Combine(HotUpdateEditorUtility.GetProjectRoot(), yooFolderName));
            DeleteProjectDirectoryWithConfirm(
                cacheDirectory,
                "Clear YooAsset Editor Runtime Cache",
                "This will delete YooAsset editor runtime cache under the project root.");
        }

        private static string GetBuiltinShaderBundleName(string packageName)
        {
            bool uniqueBundleName = AssetBundleCollectorSettingData.Setting.UniqueBundleName;
            PackRuleResult packRuleResult = DefaultPackRule.CreateShadersPackRuleResult();
            return packRuleResult.GetBundleName(packageName, uniqueBundleName);
        }

        private static void DeleteProjectDirectoryWithConfirm(string directoryPath, string title, string message)
        {
            string fullPath = Path.GetFullPath(directoryPath);
            HotUpdateEditorUtility.EnsureProjectChildPath(fullPath);

            if (Directory.Exists(fullPath) == false)
            {
                HotUpdateLogger.LogAlways($"Directory does not exist: {fullPath}");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(title, $"{message}\n\n{fullPath}", "Clear", "Cancel");
            if (confirmed == false)
                return;

            HotUpdateEditorUtility.DeleteDirectoryIfExists(fullPath);
            AssetDatabase.Refresh();
            HotUpdateLogger.LogAlways($"Deleted directory: {fullPath}");
        }
    }
}
