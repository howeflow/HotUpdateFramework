using System;
using System.IO;
using UnityEditor;
using YooAsset;
using YooAsset.Editor;

namespace HotUpdateFramework.Editor
{
    public static class HotUpdateBuildPipeline
    {
        public static void BuildPackage()
        {
            HotUpdateConfig config = HotUpdateEditorUtility.GetOrCreateConfig();

            string packageVersion = string.IsNullOrWhiteSpace(config.PackageVersionOverride) ? DateTime.Now.ToString("yyyyMMddHHmm") : config.PackageVersionOverride;
            EBuildinFileCopyOption buildinFileCopyOption = config.UseBuildinFileSystemInHostMode ? EBuildinFileCopyOption.ClearAndCopyAll : EBuildinFileCopyOption.None;
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

            HotUpdateLogger.Log($"Buildin file copy option: {buildinFileCopyOption}");
            HotUpdateLogger.Log($"Bundle encryption: " + $"{(encryptionServices != null ? "Enabled" : "Disabled")}");

            var pipeline = new ScriptableBuildPipeline();
            BuildResult buildResult = pipeline.Run(buildParameters, true);
            if (buildResult.Success)
            {
                EditorUtility.RevealInFinder(buildResult.OutputPackageDirectory);
                HotUpdateLogger.Log($"YooAsset package built: " + $"{buildResult.OutputPackageDirectory}");
                return;
            }

            throw new Exception($"YooAsset build failed: " + $"{buildResult.FailedTask}, {buildResult.ErrorInfo}");
        }

        public static void ClearBuildCache()
        {
            HotUpdateConfig config = HotUpdateEditorUtility.GetOrCreateConfig();
            string packageRootDirectory = Path.GetFullPath(Path.Combine(AssetBundleBuilderHelper.GetDefaultBuildOutputRoot(), EditorUserBuildSettings.activeBuildTarget.ToString(), config.PackageName));
            HotUpdateEditorUtility.EnsureProjectChildPath(packageRootDirectory);

            bool confirmed = EditorUtility.DisplayDialog("Clear YooAsset Build Cache", "This will purge Scriptable Build Pipeline cache " + "and delete YooAsset build directory." + $"\n\n{packageRootDirectory}", "Clear", "Cancel");
            if (confirmed == false)
                return;

            UnityEditor.Build.Pipeline.Utilities.BuildCache.PurgeCache(false);
            HotUpdateEditorUtility.DeleteDirectoryIfExists(packageRootDirectory);
            AssetDatabase.Refresh();
            HotUpdateLogger.Log($"Cleared YooAsset build cache: " + $"{packageRootDirectory}");
        }

        public static void ClearEditorRuntimeCache()
        {
            string yooFolderName = YooAssetSettingsData.GetDefaultYooFolderName();
            if (string.IsNullOrWhiteSpace(yooFolderName))
            {
                HotUpdateLogger.Warning("YooAsset DefaultYooFolderName is empty. " + "Skip clearing editor runtime cache to avoid " + "deleting project root.");
                return;
            }

            string cacheDirectory = Path.GetFullPath(Path.Combine(HotUpdateEditorUtility.GetProjectRoot(), yooFolderName));
            DeleteProjectDirectoryWithConfirm(cacheDirectory, "Clear YooAsset Editor Runtime Cache", "This will delete YooAsset editor runtime cache " + "under the project root.");
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
                HotUpdateLogger.Log($"Directory does not exist: {fullPath}");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(title, $"{message}\n\n{fullPath}", "Clear", "Cancel");
            if (confirmed == false)
                return;

            HotUpdateEditorUtility.DeleteDirectoryIfExists(fullPath);
            AssetDatabase.Refresh();
            HotUpdateLogger.Log($"Deleted directory: {fullPath}");
        }
    }
}
