using System;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

namespace HotUpdateFramework
{
    public enum HotUpdateEnvironment
    {
        Local,
        Dev,
        Release
    }

    [Serializable]
    public sealed class RemoteEndpoint
    {
        [SerializeField] private string mainRoot = string.Empty;
        [SerializeField] private string fallbackRoot = string.Empty;

        public string MainRoot => mainRoot?.Trim() ?? string.Empty;
        public string FallbackRoot => fallbackRoot?.Trim() ?? string.Empty;
    }

    [CreateAssetMenu(fileName = "HotUpdateConfig", menuName = "Hot Update/Asset Config")]
    public sealed class HotUpdateConfig : ScriptableObject
    {
        public const string ResourcesPath = "HotUpdateConfig";

        [Header("CDN")]
        [SerializeField] private HotUpdateEnvironment environment = HotUpdateEnvironment.Local;
        [SerializeField] private RemoteEndpoint localRemote = new RemoteEndpoint();
        [SerializeField] private RemoteEndpoint devRemote = new RemoteEndpoint();
        [SerializeField] private RemoteEndpoint releaseRemote = new RemoteEndpoint();

        [Header("YooAsset")]
        [SerializeField] private string packageName = "DefaultPackage";
        [SerializeField] private EPlayMode playMode = EPlayMode.HostPlayMode;
        [SerializeField] private int manifestTimeout = 10;
        [SerializeField] private int downloadingMaxNumber = 8;
        [SerializeField] private int failedTryAgain = 3;
        [Tooltip("copy to StreamingAssets")]
        [SerializeField] private string[] builtinTag = { "builtin" };
        [Tooltip("download during initialization")]
        [SerializeField] private string[] downloadTag = Array.Empty<string>();

        public string PackageName => HotUpdateUtility.NormalizePackageName(packageName, "DefaultPackage");
        public EPlayMode PlayMode => playMode;
        public int ManifestTimeout => Mathf.Max(1, manifestTimeout);
        public HotUpdateEnvironment Environment => environment;
        public RemoteEndpoint ActiveRemote
        {
            get
            {
                switch (environment)
                {
                    case HotUpdateEnvironment.Local:
                        return localRemote;
                    case HotUpdateEnvironment.Dev:
                        return devRemote;
                    case HotUpdateEnvironment.Release:
                        return releaseRemote;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
        public int DownloadingMaxNumber => Mathf.Max(1, downloadingMaxNumber);
        public int FailedTryAgain => Mathf.Max(0, failedTryAgain);
        public IReadOnlyList<string> BuiltinTag => builtinTag ?? Array.Empty<string>();
        public string[] DownloadTag => downloadTag ?? Array.Empty<string>();
        
        public static HotUpdateConfig LoadDefault()
        {
            return Resources.Load<HotUpdateConfig>(ResourcesPath);
        }
    }
}
