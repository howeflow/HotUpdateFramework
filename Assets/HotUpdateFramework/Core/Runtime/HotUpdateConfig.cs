using System;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

namespace HotUpdateFramework
{
    [CreateAssetMenu(fileName = "HotUpdateConfig", menuName = "Hot Update/Asset Config")]
    public sealed class HotUpdateConfig : ScriptableObject
    {
        public const string ResourcesPath = "HotUpdateConfig";

        [Header("CDN")]
        [SerializeField] private string platformNameOverride = string.Empty;
        [SerializeField] private string remoteUrlTemplate = "{Root}/{Platform}/{PackageName}/{FileName}";
        [SerializeField] private string[] remoteRoots =
        {
            "https://your-cdn-domain.example.com/hotupdate/release",
            "https://your-cdn-domain.example.com/hotupdate/dev",
            "http://127.0.0.1:8080"
        };

        [Header("YooAsset")]
        [SerializeField] private string packageName = "DefaultPackage";
        [SerializeField] private EPlayMode playMode = EPlayMode.HostPlayMode;
        [SerializeField] private bool useBuildinFileSystemInHostMode;
        [SerializeField] private string packageVersionOverride = string.Empty;
        [SerializeField] private int manifestTimeout = 10;
        [SerializeField] private int downloadingMaxNumber = 8;
        [SerializeField] private int failedTryAgain = 3;

        public string PackageName => HotUpdateUtility.NormalizePackageName(packageName, "DefaultPackage");
        public EPlayMode PlayMode => playMode;
        public bool UseBuildinFileSystemInHostMode => useBuildinFileSystemInHostMode;
        public bool UseBuildinFileSystem => useBuildinFileSystemInHostMode;
        public string PackageVersionOverride => packageVersionOverride?.Trim() ?? string.Empty;
        public int ManifestTimeout => Mathf.Max(1, manifestTimeout);
        public IReadOnlyList<string> RemoteRoots => remoteRoots ?? Array.Empty<string>();
        public string RemoteUrlTemplate => string.IsNullOrWhiteSpace(remoteUrlTemplate) ? "{Root}/{Platform}/{PackageName}/{FileName}" : remoteUrlTemplate.Trim();
        public string PlatformNameOverride => platformNameOverride?.Trim() ?? string.Empty;
        public int DownloadingMaxNumber => Mathf.Max(1, downloadingMaxNumber);
        public int FailedTryAgain => Mathf.Max(0, failedTryAgain);

        public static HotUpdateConfig LoadDefault()
        {
            return Resources.Load<HotUpdateConfig>(ResourcesPath);
        }
    }
}
