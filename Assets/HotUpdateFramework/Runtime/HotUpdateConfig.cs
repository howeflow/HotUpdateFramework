using System;
using System.Collections.Generic;
using HybridCLR;
using UnityEngine;
using YooAsset;

namespace HotUpdateFramework
{
    [CreateAssetMenu(fileName = "HotUpdateConfig", menuName = "Hot Update/Config")]
    public sealed class HotUpdateConfig : ScriptableObject
    {
        public const string ResourcesPath = "HotUpdateConfig";
        public const string DefaultHotUpdateAssemblyAssetDirectory = "Assets/HotUpdateAssets/Assemblies";
        public const string DefaultAotMetadataAssetDirectory = "Assets/HotUpdateAssets/Assemblies/AOT";

        [Header("YooAsset")]
        [SerializeField] private string packageName = "DefaultPackage";
        [SerializeField] private EPlayMode playMode = EPlayMode.HostPlayMode;
        [SerializeField] private bool setAsDefaultPackage = true;
        [SerializeField] private bool useBuildinFileSystemInHostMode = false;
        [SerializeField] private string packageVersionOverride = string.Empty;
        [SerializeField] private int manifestTimeout = 10;

        [Header("CDN")]
        [SerializeField] private string[] remoteRoots =
        {
            "https://your-cdn-domain.example.com/hotupdate/release",
            "https://your-cdn-domain.example.com/hotupdate/dev",
            "http://127.0.0.1:8080"
        };
        [SerializeField] private string remoteUrlTemplate = "{Root}/{Platform}/{PackageName}/{FileName}";
        [SerializeField] private string platformNameOverride = string.Empty;

        [Header("Download")]
        [SerializeField] private bool downloadPackage = true;
        [SerializeField] private int downloadingMaxNumber = 8;
        [SerializeField] private int failedTryAgain = 3;

        [Header("HybridCLR")]
        [SerializeField] private HomologousImageMode homologousImageMode = HomologousImageMode.SuperSet;
        [SerializeField] private string aotMetadataAssetDirectory = DefaultAotMetadataAssetDirectory;
        [SerializeField] private string[] aotMetadataAssemblyNames =
        {
            "mscorlib.dll",
            "System.dll",
            "System.Core.dll",
            "UnityEngine.CoreModule.dll"
        };
        [SerializeField] private string hotUpdateAssemblyAssetDirectory = DefaultHotUpdateAssemblyAssetDirectory;
        [SerializeField] private string[] hotUpdateAssemblyNames =
        {
            "HotUpdate.dll"
        };

        [Header("Entry")]
        [SerializeField] private bool invokeHotUpdateEntry = true;
        [SerializeField] private string entryTypeName = "HotUpdate.HotUpdateEntry";
        [SerializeField] private string entryMethodName = "Start";

        public string PackageName => NormalizePackageName(packageName, "DefaultPackage");
        public EPlayMode PlayMode => playMode;
        public bool SetAsDefaultPackage => setAsDefaultPackage;
        public bool UseBuildinFileSystemInHostMode => useBuildinFileSystemInHostMode;
        public string PackageVersionOverride => packageVersionOverride?.Trim() ?? string.Empty;
        public int ManifestTimeout => Mathf.Max(1, manifestTimeout);
        public IReadOnlyList<string> RemoteRoots => remoteRoots ?? Array.Empty<string>();
        public string RemoteMainRoot => GetRemoteRootByPriority(0);
        public string RemoteFallbackRoot => GetRemoteRootByPriority(1);
        public string RemoteUrlTemplate => string.IsNullOrWhiteSpace(remoteUrlTemplate) ? "{Root}/{Platform}/{PackageName}/{FileName}" : remoteUrlTemplate.Trim();
        public string PlatformNameOverride => platformNameOverride?.Trim() ?? string.Empty;
        public bool IsRemoteMainRootConfigured => string.IsNullOrWhiteSpace(RemoteMainRoot) == false && RemoteMainRoot.Contains("your-cdn-domain") == false;
        public bool DownloadPackage => downloadPackage;
        public int DownloadingMaxNumber => Mathf.Max(1, downloadingMaxNumber);
        public int FailedTryAgain => Mathf.Max(0, failedTryAgain);
        public HomologousImageMode HomologousImageMode => homologousImageMode;
        public string AotMetadataAssetDirectory => NormalizeAssetDirectory(aotMetadataAssetDirectory, DefaultAotMetadataAssetDirectory);
        public IReadOnlyList<string> AotMetadataAssemblyNames => aotMetadataAssemblyNames ?? Array.Empty<string>();
        public IReadOnlyList<string> AotMetadataAssetLocations => BuildAotMetadataAssetLocations();
        public string HotUpdateAssemblyAssetDirectory => NormalizeAssetDirectory(hotUpdateAssemblyAssetDirectory, DefaultHotUpdateAssemblyAssetDirectory);
        public IReadOnlyList<string> HotUpdateAssemblyNames => hotUpdateAssemblyNames ?? Array.Empty<string>();
        public IReadOnlyList<string> HotUpdateAssemblyAssetLocations => BuildHotUpdateAssemblyAssetLocations();
        public bool InvokeHotUpdateEntry => invokeHotUpdateEntry;
        public string EntryTypeName => entryTypeName?.Trim() ?? string.Empty;
        public string EntryMethodName => entryMethodName?.Trim() ?? string.Empty;

        public static HotUpdateConfig LoadDefault()
        {
            return Resources.Load<HotUpdateConfig>(ResourcesPath);
        }

        private static string NormalizePackageName(string packageName, string fallback)
        {
            return string.IsNullOrWhiteSpace(packageName) ? fallback : packageName.Trim();
        }

        private static string NormalizeAssetDirectory(string assetDirectory, string fallback)
        {
            string value = string.IsNullOrWhiteSpace(assetDirectory) ? fallback : assetDirectory.Trim();
            return value.Replace('\\', '/').TrimEnd('/');
        }

        private string GetRemoteRootByPriority(int priorityIndex)
        {
            if (remoteRoots == null || priorityIndex < 0)
                return string.Empty;

            int validIndex = 0;
            foreach (string remoteRoot in remoteRoots)
            {
                if (string.IsNullOrWhiteSpace(remoteRoot))
                    continue;

                if (validIndex == priorityIndex)
                    return remoteRoot.Trim();

                validIndex++;
            }

            return string.Empty;
        }

        private string[] BuildAotMetadataAssetLocations()
        {
            return BuildAssemblyAssetLocations(aotMetadataAssemblyNames, AotMetadataAssetDirectory);
        }

        private string[] BuildHotUpdateAssemblyAssetLocations()
        {
            return BuildAssemblyAssetLocations(hotUpdateAssemblyNames, HotUpdateAssemblyAssetDirectory);
        }

        private static string[] BuildAssemblyAssetLocations(string[] assemblyNames, string assetDirectory)
        {
            if (assemblyNames == null || assemblyNames.Length == 0)
                return Array.Empty<string>();

            var locations = new List<string>(assemblyNames.Length);
            foreach (string assemblyName in assemblyNames)
            {
                if (string.IsNullOrWhiteSpace(assemblyName))
                    continue;

                string fileName = NormalizeAssemblyAssetFileName(assemblyName);
                if (fileName.StartsWith("Assets/", StringComparison.Ordinal))
                    locations.Add(fileName);
                else
                    locations.Add($"{assetDirectory}/{fileName}");
            }

            return locations.ToArray();
        }

        private static string NormalizeAssemblyAssetFileName(string assemblyName)
        {
            string fileName = assemblyName.Trim().Replace('\\', '/');
            if (fileName.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                return fileName;

            if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) == false)
                fileName += ".dll";

            return $"{fileName}.bytes";
        }
    }
}
