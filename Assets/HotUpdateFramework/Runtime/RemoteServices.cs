using YooAsset;

namespace HotUpdateFramework
{
    public sealed class RemoteServices : IRemoteServices
    {
        private readonly HotUpdateConfig _config;
        private readonly string _packageName;
        private readonly string _platformName;
        
        private string remoteMainRoot;
        private string remoteFallbackRoot;

        public RemoteServices(HotUpdateConfig config, string packageName)
        {
            _config = config;
            _packageName = packageName;
            _platformName = HotUpdateUtility.GetPlatformName(config.PlatformNameOverride);

            remoteMainRoot = HotUpdateUtility.GetRemoteRootByPriority(config.RemoteRoots, 0);
            remoteFallbackRoot = HotUpdateUtility.GetRemoteRootByPriority(config.RemoteRoots, 1);

            HotUpdateLogger.Log($"Remote URLs: main={remoteMainRoot}, fallback={remoteFallbackRoot}");
        }

        string IRemoteServices.GetRemoteMainURL(string fileName)
        {
            return BuildUrl(remoteMainRoot, fileName);
        }

        string IRemoteServices.GetRemoteFallbackURL(string fileName)
        {
            string fallbackRoot = string.IsNullOrWhiteSpace(remoteFallbackRoot) ? remoteMainRoot : remoteFallbackRoot;
            return BuildUrl(fallbackRoot, fileName);
        }

        private string BuildUrl(string root, string fileName)
        {
            string url = _config.RemoteUrlTemplate
                .Replace("{Root}", (root ?? string.Empty).TrimEnd('/'))
                .Replace("{Platform}", _platformName)
                .Replace("{PackageName}", _packageName)
                .Replace("{FileName}", fileName ?? string.Empty);

            return HotUpdateUtility.RemoveDuplicateSlashesAfterScheme(url);
        }
    }
}
