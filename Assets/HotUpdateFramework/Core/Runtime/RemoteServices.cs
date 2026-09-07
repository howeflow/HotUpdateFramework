using YooAsset;

namespace HotUpdateFramework
{
    public sealed class RemoteServices : IRemoteServices
    {
        private readonly string _packageName;
        private readonly string _platformName;
        private readonly string _remoteMainRoot;
        private readonly string _remoteFallbackRoot;

        public RemoteServices(HotUpdateConfig config, string packageName)
        {
            _packageName = packageName;
            _platformName = HotUpdateUtility.GetPlatformName();

            RemoteEndpoint endpoint = config.ActiveRemote;
            _remoteMainRoot = endpoint?.MainRoot ?? string.Empty;
            _remoteFallbackRoot = endpoint?.FallbackRoot ?? string.Empty;

            HotUpdateLogger.Log($"Remote environment: {config.Environment}, main={_remoteMainRoot}, fallback={_remoteFallbackRoot}");
        }

        string IRemoteServices.GetRemoteMainURL(string fileName)
        {
            return BuildUrl(_remoteMainRoot, fileName);
        }

        string IRemoteServices.GetRemoteFallbackURL(string fileName)
        {
            string fallbackRoot = string.IsNullOrWhiteSpace(_remoteFallbackRoot) ? _remoteMainRoot : _remoteFallbackRoot;
            return BuildUrl(fallbackRoot, fileName);
        }

        private string BuildUrl(string root, string fileName)
        {
            string url = $"{root}/{_platformName}/{_packageName}/{fileName}";
            return HotUpdateUtility.RemoveDuplicateSlashesAfterScheme(url);
        }
    }
}
