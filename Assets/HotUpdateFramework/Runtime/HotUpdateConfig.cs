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

        [Header("Log")]
        [SerializeField] private bool enableRuntimeLog = true;

        [Header("YooAsset")]
        [SerializeField] private string packageName = "DefaultPackage";
        [SerializeField] private EPlayMode playMode = EPlayMode.HostPlayMode;
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

        public string PackageName => HotUpdateUtility.NormalizePackageName(packageName, "DefaultPackage");
        public EPlayMode PlayMode => playMode;
        public bool UseBuildinFileSystemInHostMode => useBuildinFileSystemInHostMode;
        public string PackageVersionOverride => packageVersionOverride?.Trim() ?? string.Empty;
        public int ManifestTimeout => Mathf.Max(1, manifestTimeout);
        public IReadOnlyList<string> RemoteRoots => remoteRoots ?? Array.Empty<string>();

        public string RemoteUrlTemplate => string.IsNullOrWhiteSpace(remoteUrlTemplate) ? "{Root}/{Platform}/{PackageName}/{FileName}" : remoteUrlTemplate.Trim();
        public string PlatformNameOverride => platformNameOverride?.Trim() ?? string.Empty;
        public int DownloadingMaxNumber => Mathf.Max(1, downloadingMaxNumber);
        public int FailedTryAgain => Mathf.Max(0, failedTryAgain);
        public bool EnableRuntimeLog => enableRuntimeLog;
        public HomologousImageMode HomologousImageMode => homologousImageMode;
        public string AotMetadataAssetDirectory => HotUpdateUtility.NormalizeAssetDirectory(aotMetadataAssetDirectory, DefaultAotMetadataAssetDirectory);
        public IReadOnlyList<string> AotMetadataAssemblyNames => aotMetadataAssemblyNames ?? Array.Empty<string>();
        public IReadOnlyList<string> AotMetadataAssetLocations => BuildAotMetadataAssetLocations();
        public string HotUpdateAssemblyAssetDirectory => HotUpdateUtility.NormalizeAssetDirectory(hotUpdateAssemblyAssetDirectory, DefaultHotUpdateAssemblyAssetDirectory);
        public IReadOnlyList<string> HotUpdateAssemblyNames => hotUpdateAssemblyNames ?? Array.Empty<string>();
        public IReadOnlyList<string> HotUpdateAssemblyAssetLocations => BuildHotUpdateAssemblyAssetLocations();
        public bool InvokeHotUpdateEntry => invokeHotUpdateEntry;
        public string EntryTypeName => entryTypeName?.Trim() ?? string.Empty;
        public string EntryMethodName => entryMethodName?.Trim() ?? string.Empty;

        public static HotUpdateConfig LoadDefault()
        {
            return Resources.Load<HotUpdateConfig>(ResourcesPath);
        }

        private string[] BuildAotMetadataAssetLocations()
        {
            return HotUpdateUtility.BuildAssemblyAssetLocations(aotMetadataAssemblyNames, AotMetadataAssetDirectory);
        }

        private string[] BuildHotUpdateAssemblyAssetLocations()
        {
            return HotUpdateUtility.BuildAssemblyAssetLocations(hotUpdateAssemblyNames, HotUpdateAssemblyAssetDirectory);
        }
    }
}
