using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Settings;
using HotUpdateFramework.Crypto.Editor;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace HotUpdateFramework.Editor
{
    public static class HotUpdateHelper
    {
        public const string DefaultConfigAssetPath = "Assets/Resources/HotUpdateConfig.asset";

        public static HotUpdateConfig CreateDefaultConfigAsset()
        {
            HotUpdateConfig existing = AssetDatabase.LoadAssetAtPath<HotUpdateConfig>(DefaultConfigAssetPath);
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
            Debug.Log($"[HotUpdate] Created config: {DefaultConfigAssetPath}");
            return config;
        }

        public static HotUpdateConfig GetOrCreateConfig()
        {
            return HotUpdateConfig.LoadDefault() ?? CreateDefaultConfigAsset();
        }

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

        public static void CopyAotMetadataAndHotUpdateDlls() {
            HotUpdateConfig config = GetOrCreateConfig();
            var directory = GetHotUpdateDllOutputDirectory();
            CopyAotMetadataAndHotUpdateDlls(config, directory);
        }
        public static void CopyAotMetadataAndHotUpdateDlls(HotUpdateConfig config,string inputDirectory) {
            int copiedCount = CopyHotUpdateDlls(config, inputDirectory);
            copiedCount += CopyAotMetadataDlls(config);

            AssetDatabase.Refresh();
            Debug.Log($"[HotUpdate] AotMetadataAndHotUpdateDlls assets. Copied: {copiedCount}");
        }

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
                EncryptionServices = config.EnableBundleEncryption ? new HotUpdateBundleEncryptionServices() : null,
                ClearBuildCacheFiles = false,
                UseAssetDependencyDB = true,
                BuiltinShadersBundleName = GetBuiltinShaderBundleName(config.PackageName)
            };

            Debug.Log($"[HotUpdate] Buildin file copy option: {buildinFileCopyOption}");
            Debug.Log($"[HotUpdate] Bundle encryption: {(config.EnableBundleEncryption ? "Enabled" : "Disabled")}");

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

        public static void ClearYooAssetBuildCache()
        {
            HotUpdateConfig config = GetOrCreateConfig();
            string packageRootDirectory = Path.GetFullPath(Path.Combine(
                AssetBundleBuilderHelper.GetDefaultBuildOutputRoot(),
                EditorUserBuildSettings.activeBuildTarget.ToString(),
                config.PackageName));
            EnsureProjectChildPath(packageRootDirectory);

            bool confirmed = EditorUtility.DisplayDialog(
                "Clear YooAsset Build Cache",
                $"This will purge Scriptable Build Pipeline cache and delete YooAsset build directory.\n\n{packageRootDirectory}",
                "Clear",
                "Cancel");
            if (confirmed == false)
                return;

            UnityEditor.Build.Pipeline.Utilities.BuildCache.PurgeCache(false);
            DeleteProjectDirectory(packageRootDirectory);
            AssetDatabase.Refresh();
            Debug.Log($"[HotUpdate] Cleared YooAsset build cache: {packageRootDirectory}");
        }

        public static void ClearYooAssetEditorRuntimeCache()
        {
            string yooFolderName = YooAssetSettingsData.GetDefaultYooFolderName();
            if (string.IsNullOrWhiteSpace(yooFolderName))
            {
                Debug.LogWarning("[HotUpdate] YooAsset DefaultYooFolderName is empty. Skip clearing editor runtime cache to avoid deleting project root.");
                return;
            }

            string cacheDirectory = Path.GetFullPath(Path.Combine(GetProjectRoot(), yooFolderName));
            DeleteProjectDirectoryWithConfirm(
                cacheDirectory,
                "Clear YooAsset Editor Runtime Cache",
                "This will delete YooAsset editor runtime cache under the project root.");
        }

        public static string GetHotUpdateDllOutputDirectory()
        {
            return GetProjectFullPath(SettingsUtil.GetHotUpdateDllsOutputDirByTarget(EditorUserBuildSettings.activeBuildTarget));
        }

        public static string GetAotMetadataDllOutputDirectory()
        {
            return GetProjectFullPath(SettingsUtil.GetAssembliesPostIl2CppStripDir(EditorUserBuildSettings.activeBuildTarget));
        }

        public static List<string> GetHotUpdateDllPaths(HotUpdateConfig config, string hotUpdateDllOutputDirectory)
        {
            var dllPaths = new List<string>();
            foreach (string location in config.HotUpdateAssemblyAssetLocations)
            {
                if (string.IsNullOrWhiteSpace(location))
                    continue;

                string dllFileName = GetDllFileNameFromAssetLocation(location);
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

                string sourcePath = Path.GetFullPath(Path.Combine(hotUpdateDllOutputDirectory, GetDllFileNameFromAssetLocation(location)));
                if (CopyIfExists(sourcePath, location))
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

                string sourcePath = Path.GetFullPath(Path.Combine(aotMetadataDirectory, GetDllFileNameFromAssetLocation(location)));
                if (CopyIfExists(sourcePath, location))
                    copiedCount++;
            }

            return copiedCount;
        }

        public static bool CopyIfExists(string sourcePath, string destinationAssetPath)
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

        public static string GetDllFileNameFromAssetLocation(string assetLocation)
        {
            string fileName = Path.GetFileName(assetLocation);
            if (fileName.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - ".bytes".Length);
            return fileName;
        }

        public static string GetProjectFullPath(string path)
        {
            return Path.GetFullPath(Path.Combine(SettingsUtil.ProjectDir, path.Replace('/', Path.DirectorySeparatorChar)));
        }

        public static void DeleteDirectoryIfExists(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
                Directory.Delete(fullPath, true);
        }

        private static string GetBuiltinShaderBundleName(string packageName)
        {
            bool uniqueBundleName = AssetBundleCollectorSettingData.Setting.UniqueBundleName;
            PackRuleResult packRuleResult = DefaultPackRule.CreateShadersPackRuleResult();
            return packRuleResult.GetBundleName(packageName, uniqueBundleName);
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

        private static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static void DeleteProjectDirectoryWithConfirm(string directoryPath, string title, string message)
        {
            string fullPath = Path.GetFullPath(directoryPath);
            EnsureProjectChildPath(fullPath);

            if (Directory.Exists(fullPath) == false)
            {
                Debug.Log($"[HotUpdate] Directory does not exist: {fullPath}");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(title, $"{message}\n\n{fullPath}", "Clear", "Cancel");
            if (confirmed == false)
                return;

            DeleteProjectDirectory(fullPath);
            AssetDatabase.Refresh();
            Debug.Log($"[HotUpdate] Deleted directory: {fullPath}");
        }

        private static void DeleteProjectDirectory(string directoryPath)
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

        private static void EnsureProjectChildPath(string path)
        {
            string projectRoot = EnsureTrailingSeparator(Path.GetFullPath(GetProjectRoot()));
            string fullPath = Path.GetFullPath(path);
            string fullPathWithSeparator = EnsureTrailingSeparator(fullPath);

            var comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (fullPathWithSeparator.StartsWith(projectRoot, comparison) == false)
                throw new InvalidOperationException($"Can not delete directory outside project: {fullPath}");

            string trimmedRoot = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string trimmedPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(trimmedRoot, trimmedPath, comparison))
                throw new InvalidOperationException("Can not delete project root.");
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                return path;

            return path + Path.DirectorySeparatorChar;
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
