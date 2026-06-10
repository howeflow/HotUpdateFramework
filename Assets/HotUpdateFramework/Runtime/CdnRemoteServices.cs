using YooAsset;

namespace HotUpdateFramework
{
    public sealed class CdnRemoteServices : IRemoteServices
    {
        private readonly HotUpdateConfig _config;
        private readonly string _packageName;
        private readonly string _platformName;
        
        private string remoteMainRoot;
        private string remoteFallbackRoot;

        public CdnRemoteServices(HotUpdateConfig config, string packageName)
        {
            _config = config;
            _packageName = packageName;
            _platformName = HotUpdatePlatformUtility.GetPlatformName(config.PlatformNameOverride);

            remoteMainRoot = GetRemoteRootByPriority(0);
            remoteFallbackRoot = GetRemoteRootByPriority(1);
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

            return RemoveDuplicateSlashesAfterScheme(url);
        }

        private static string RemoveDuplicateSlashesAfterScheme(string url)
        {
            int schemeIndex = url.IndexOf("://", System.StringComparison.Ordinal);
            if (schemeIndex < 0)
                return url.Replace("//", "/");

            string scheme = url.Substring(0, schemeIndex + 3);
            string rest = url.Substring(schemeIndex + 3);
            while (rest.Contains("//"))
                rest = rest.Replace("//", "/");
            return scheme + rest;
        }
        
        private string GetRemoteRootByPriority(int priorityIndex) {
            var remoteRoots = _config.RemoteRoots;
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
    }
}
