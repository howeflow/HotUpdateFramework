using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace HotUpdateFramework
{
    public sealed class HotUpdateService
    {
        private const string LastKnownGoodVersionKeyPrefix = "HotUpdateFramework.LastKnownGoodPackageVersion";

        public static HotUpdateService Instance { get; } = new HotUpdateService();

        public ResourcePackage Package { get; private set; }
        public string PackageVersion { get; private set; } = string.Empty;

        private HotUpdateConfig _config;

        private HotUpdateService()
        {
        }

        public async UniTask RunAsync(IProgress<HotUpdateProgress> progress = null, CancellationToken cancellationToken = default)
        {
            await PrepareResourcesAsync(progress, cancellationToken);
            Report(progress, HotUpdateStage.Completed, "Resource update completed", 1f);
        }

        public async UniTask PrepareResourcesAsync(IProgress<HotUpdateProgress> progress = null, CancellationToken cancellationToken = default)
        {
            _config = HotUpdateConfig.LoadDefault();
            HotUpdateConfig config = _config;
            if (config == null)
                throw new HotUpdateException("HotUpdateConfig is null.");

            PackageVersion = string.Empty;
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                HotUpdateLogger.Log($"Start summary: package={config.PackageName}, playMode={config.PlayMode}, " + $"platform={HotUpdateUtility.GetPlatformName()}");

                Report(progress, HotUpdateStage.InitializeYooAsset, "Initialize YooAsset");

                YooAssets.Initialize();

                Package = await InitializePackageAsync(config, config.PackageName, progress, cancellationToken);

                try
                {
                    PackageVersion = await RequestAndUpdateManifestAsync(config, Package, progress, cancellationToken);
                    if (config.PlayMode == EPlayMode.HostPlayMode)
                        SaveLastKnownGoodPackageVersion(config, PackageVersion);
                }
                catch (HotUpdateException ex) when (config.PlayMode == EPlayMode.HostPlayMode)
                {
                    HotUpdateLogger.Warning($"Remote manifest unavailable, try last known good manifest: {ex.Message}");
                    PackageVersion = await TryLoadLastKnownGoodManifestAsync(config, Package, progress, cancellationToken);

                    if (string.IsNullOrEmpty(PackageVersion))
                    {
                        HotUpdateLogger.Warning("Cached manifest unavailable, use buildin package.");
                        Package = await ReinitializeOfflinePackageAsync(Package, progress, cancellationToken);
                        PackageVersion = await RequestAndUpdateManifestAsync(config, Package, progress, cancellationToken);
                    }
                }

                await DownloadByTagsAsync(config.DownloadTag, progress, cancellationToken);

                HotUpdateLogger.Log($"Resource summary: package={Package.PackageName}, version={PackageVersion}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Report(progress, HotUpdateStage.Failed, ex.Message, 1f);
                throw;
            }
        }

        public bool IsNeedDownload(string location)
        {
            ResourcePackage package = GetReadyPackage();
            string normalizedLocation = ValidateLocation(package, location);
            return package.IsNeedDownloadFromRemote(normalizedLocation);
        }

        public UniTask DownloadByLocationAsync(string location, IProgress<HotUpdateProgress> progress = null, CancellationToken cancellationToken = default)
        {
            return DownloadByLocationsAsync(new[] { location }, progress, cancellationToken);
        }

        public async UniTask DownloadByLocationsAsync(IReadOnlyList<string> locations, IProgress<HotUpdateProgress> progress = null, CancellationToken cancellationToken = default)
        {
            ResourcePackage package = GetReadyPackage();
            HotUpdateConfig config = GetReadyConfig();
            string[] normalizedLocations = GetDownloadLocations(package, locations);

            if (normalizedLocations.Length == 0)
            {
                Report(progress, HotUpdateStage.DownloadFiles, $"No download locations configured {package.PackageName}", 1f);
                return;
            }

            ResourceDownloaderOperation downloader = package.CreateBundleDownloader(normalizedLocations, false, config.DownloadingMaxNumber, config.FailedTryAgain);
            await RunDownloaderAsync(package, downloader, $"locations={normalizedLocations.Length}", progress, cancellationToken);
        }

        public async UniTask DownloadByTagsAsync(IReadOnlyList<string> tags, IProgress<HotUpdateProgress> progress = null, CancellationToken cancellationToken = default)
        {
            ResourcePackage package = GetReadyPackage();
            HotUpdateConfig config = GetReadyConfig();
            string[] normalizedTags = GetDownloadTags(tags);

            if (normalizedTags.Length == 0)
            {
                Report(progress, HotUpdateStage.DownloadFiles, $"No download tags configured {package.PackageName}", 1f);
                return;
            }

            ResourceDownloaderOperation downloader = package.CreateResourceDownloader(normalizedTags, config.DownloadingMaxNumber, config.FailedTryAgain);
            await RunDownloaderAsync(package, downloader, $"tags={string.Join(", ", normalizedTags)}", progress, cancellationToken);
        }

        private async UniTask<ResourcePackage> InitializePackageAsync(HotUpdateConfig config, string packageName, IProgress<HotUpdateProgress> progress, CancellationToken cancellationToken)
        {
            ResourcePackage package = YooAssets.TryGetPackage(packageName) ?? YooAssets.CreatePackage(packageName);

            if (package.InitializeStatus == EOperationStatus.Succeed && package.PackageValid)
            {
                YooAssets.SetDefaultPackage(package);
                return package;
            }

            InitializeParameters parameters = CreateInitializeParameters(config, packageName);
            InitializationOperation operation = package.InitializeAsync(parameters);
            await WaitOperationAsync(operation, HotUpdateStage.InitializeYooAsset, $"Initialize package {packageName}", progress, cancellationToken);
            EnsureSucceed(operation, $"Initialize YooAsset package {packageName}");

            YooAssets.SetDefaultPackage(package);
            return package;
        }

        private static InitializeParameters CreateInitializeParameters(HotUpdateConfig config, string packageName)
        {
            IDecryptionServices decryptionServices = HotUpdateCryptoProvider.DecryptionServices;

            switch (config.PlayMode)
            {
                case EPlayMode.EditorSimulateMode:
                    var simulateResult = EditorSimulateModeHelper.SimulateBuild(packageName);
                    return new EditorSimulateModeParameters
                    {
                        EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(simulateResult.PackageRootDirectory)
                    };

                case EPlayMode.OfflinePlayMode:
                    return CreateOfflineInitializeParameters(decryptionServices);

                case EPlayMode.HostPlayMode:
                    IRemoteServices remoteServices = new RemoteServices(config, packageName);
                    return new HostPlayModeParameters
                    {
                        BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(decryptionServices),
                        CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices, decryptionServices)
                    };

                default:
                    throw new HotUpdateException($"Unsupported play mode: {config.PlayMode}");
            }
        }

        private static OfflinePlayModeParameters CreateOfflineInitializeParameters(IDecryptionServices decryptionServices)
        {
            return new OfflinePlayModeParameters
            {
                BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(decryptionServices)
            };
        }

        private async UniTask<ResourcePackage> ReinitializeOfflinePackageAsync(ResourcePackage package, IProgress<HotUpdateProgress> progress, CancellationToken cancellationToken)
        {
            Report(progress, HotUpdateStage.InitializeYooAsset, $"Switch to buildin package {package.PackageName}");

            DestroyOperation destroyOperation = package.DestroyAsync();
            await WaitOperationAsync(destroyOperation, HotUpdateStage.InitializeYooAsset, $"Destroy remote package {package.PackageName}", progress, cancellationToken);
            EnsureSucceed(destroyOperation, $"Destroy YooAsset package {package.PackageName}");

            InitializationOperation initializeOperation = package.InitializeAsync(CreateOfflineInitializeParameters(HotUpdateCryptoProvider.DecryptionServices));
            await WaitOperationAsync(initializeOperation, HotUpdateStage.InitializeYooAsset, $"Initialize buildin package {package.PackageName}", progress, cancellationToken);
            EnsureSucceed(initializeOperation, $"Initialize buildin YooAsset package " + $"{package.PackageName}");

            YooAssets.SetDefaultPackage(package);
            return package;
        }

        private async UniTask<string> TryLoadLastKnownGoodManifestAsync(HotUpdateConfig config, ResourcePackage package, IProgress<HotUpdateProgress> progress, CancellationToken cancellationToken)
        {
            string packageVersion = LoadLastKnownGoodPackageVersion(config);
            if (string.IsNullOrEmpty(packageVersion))
            {
                HotUpdateLogger.Warning($"Last known good manifest version not found: package={package.PackageName}");
                return string.Empty;
            }

            Report(progress, HotUpdateStage.UpdateManifest, $"Load cached manifest {package.PackageName} {packageVersion}");
            UpdatePackageManifestOperation operation = package.UpdatePackageManifestAsync(packageVersion, config.ManifestTimeout);
            await WaitOperationAsync(operation, HotUpdateStage.UpdateManifest, $"Load cached manifest {package.PackageName} {packageVersion}", progress, cancellationToken);

            if (operation.Status != EOperationStatus.Succeed)
            {
                HotUpdateLogger.Warning($"Load cached YooAsset manifest failed: package={package.PackageName}, version={packageVersion}, error={operation.Error}");
                return string.Empty;
            }

            HotUpdateLogger.Warning($"Using last known good manifest: package={package.PackageName}, version={packageVersion}");
            return packageVersion;
        }

        private static string LoadLastKnownGoodPackageVersion(HotUpdateConfig config)
        {
            string key = GetLastKnownGoodPackageVersionKey(config);
            return PlayerPrefs.GetString(key, string.Empty)?.Trim() ?? string.Empty;
        }

        private static void SaveLastKnownGoodPackageVersion(HotUpdateConfig config, string packageVersion)
        {
            if (string.IsNullOrWhiteSpace(packageVersion))
                return;

            string normalizedVersion = packageVersion.Trim();
            try
            {
                string key = GetLastKnownGoodPackageVersionKey(config);
                PlayerPrefs.SetString(key, normalizedVersion);
                PlayerPrefs.Save();
                HotUpdateLogger.Log($"Saved last known good manifest version: package={config.PackageName}, version={normalizedVersion}");
            }
            catch (Exception ex)
            {
                HotUpdateLogger.Warning($"Save last known good manifest version failed: package={config.PackageName}, error={ex.Message}");
            }
        }

        private static string GetLastKnownGoodPackageVersionKey(HotUpdateConfig config)
        {
            string platformName = HotUpdateUtility.GetPlatformName();
            return $"{LastKnownGoodVersionKeyPrefix}.{platformName}.{config.PackageName}";
        }

        private async UniTask<string> RequestAndUpdateManifestAsync(HotUpdateConfig config, ResourcePackage package, IProgress<HotUpdateProgress> progress, CancellationToken cancellationToken)
        {
            Report(progress, HotUpdateStage.RequestPackageVersion, $"Request package version {package.PackageName}");
            RequestPackageVersionOperation versionOperation = package.RequestPackageVersionAsync(true, config.ManifestTimeout);
            await WaitOperationAsync(versionOperation, HotUpdateStage.RequestPackageVersion, $"Request package version {package.PackageName}", progress, cancellationToken);
            EnsureSucceed(versionOperation, $"Request YooAsset package version {package.PackageName}");
            string packageVersion = versionOperation.PackageVersion;

            if (string.IsNullOrWhiteSpace(packageVersion))
                throw new HotUpdateException($"YooAsset package version is empty: {package.PackageName}");

            Report(progress, HotUpdateStage.UpdateManifest, $"Update manifest {package.PackageName} {packageVersion}");
            UpdatePackageManifestOperation manifestOperation = package.UpdatePackageManifestAsync(packageVersion, config.ManifestTimeout);
            await WaitOperationAsync(manifestOperation, HotUpdateStage.UpdateManifest, $"Update manifest {package.PackageName} {packageVersion}", progress, cancellationToken);
            EnsureSucceed(manifestOperation, $"Update YooAsset package manifest {package.PackageName}");

            return packageVersion;
        }

        private async UniTask RunDownloaderAsync(ResourcePackage package, ResourceDownloaderOperation downloader, string selection, IProgress<HotUpdateProgress> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, HotUpdateStage.DownloadFiles, $"Create downloader {package.PackageName}, {selection}");

            if (downloader.TotalDownloadCount == 0)
            {
                Report(progress, HotUpdateStage.DownloadFiles, $"No files need download {package.PackageName}", 1f);
                return;
            }

            string totalSizeText = HotUpdateUtility.FormatBytes(downloader.TotalDownloadBytes);
            Report(progress, HotUpdateStage.DownloadFiles, $"Need download {package.PackageName}: " + $"{downloader.TotalDownloadCount} files, {totalSizeText}");

            downloader.DownloadUpdateCallback = data =>
            {
                string currentSizeText = HotUpdateUtility.FormatBytes(data.CurrentDownloadBytes);
                string downloadMessage = $"Download {package.PackageName} " + $"{data.CurrentDownloadCount}/{data.TotalDownloadCount} " + $"{currentSizeText}/{HotUpdateUtility.FormatBytes(data.TotalDownloadBytes)}";
                progress?.Report(new HotUpdateProgress(HotUpdateStage.DownloadFiles, downloadMessage, data.Progress, data.CurrentDownloadCount, data.TotalDownloadCount, data.CurrentDownloadBytes, data.TotalDownloadBytes));
            };
            downloader.DownloadErrorCallback = data =>
            {
                HotUpdateLogger.Error($"Download failed: package={package.PackageName}, " + $"file={data.FileName}, error={data.ErrorInfo}");
            };
            downloader.DownloadFinishCallback = data =>
            {
                if (data.Succeed)
                {
                    Report(progress, HotUpdateStage.DownloadFiles, $"Download completed {data.PackageName}: " + $"{downloader.TotalDownloadCount} files, {totalSizeText}", 1f);
                }
            };

            downloader.BeginDownload();
            try
            {
                await WaitOperationAsync(downloader, HotUpdateStage.DownloadFiles, $"Download files {package.PackageName}", null, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                downloader.CancelDownload();
                throw;
            }

            EnsureSucceed(downloader, $"Download YooAsset files {package.PackageName}");
        }

        private ResourcePackage GetReadyPackage()
        {
            if (Package == null || Package.PackageValid == false || Package.InitializeStatus != EOperationStatus.Succeed || string.IsNullOrWhiteSpace(PackageVersion))
                throw new HotUpdateException("Resource package is not ready.");

            return Package;
        }

        private HotUpdateConfig GetReadyConfig()
        {
            if (_config == null)
                throw new HotUpdateException("HotUpdateConfig is not ready.");

            return _config;
        }

        private static string ValidateLocation(ResourcePackage package, string location)
        {
            if (string.IsNullOrWhiteSpace(location))
                throw new ArgumentException("Location is empty.", nameof(location));

            string normalizedLocation = location.Trim();
            if (package.CheckLocationValid(normalizedLocation) == false)
                throw new HotUpdateException($"YooAsset location is invalid: {normalizedLocation}");

            return normalizedLocation;
        }

        private static string[] GetDownloadLocations(ResourcePackage package, IReadOnlyList<string> configuredLocations)
        {
            if (configuredLocations == null)
                throw new ArgumentNullException(nameof(configuredLocations));

            var locations = new List<string>(configuredLocations.Count);
            for (int i = 0; i < configuredLocations.Count; i++)
            {
                string location = ValidateLocation(package, configuredLocations[i]);
                if (locations.Contains(location) == false)
                    locations.Add(location);
            }

            return locations.ToArray();
        }

        private static string[] GetDownloadTags(IReadOnlyList<string> configuredTags)
        {
            if (configuredTags == null || configuredTags.Count == 0)
                return Array.Empty<string>();

            var tags = new List<string>(configuredTags.Count);
            for (int i = 0; i < configuredTags.Count; i++)
            {
                string tag = configuredTags[i]?.Trim();
                if (string.IsNullOrEmpty(tag) || tags.Contains(tag))
                    continue;

                tags.Add(tag);
            }

            return tags.ToArray();
        }

        private static async UniTask WaitOperationAsync(AsyncOperationBase operation, HotUpdateStage stage, string message, IProgress<HotUpdateProgress> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (operation.IsDone == false)
            {
                progress?.Report(new HotUpdateProgress(stage, message, operation.Progress));
                await UniTask.Yield(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        private static void EnsureSucceed(AsyncOperationBase operation, string action)
        {
            if (operation.Status != EOperationStatus.Succeed)
            {
                throw new HotUpdateException($"{action} failed: {operation.Error}");
            }
        }

        private static void Report(IProgress<HotUpdateProgress> progress, HotUpdateStage stage, string message, float value = 0f)
        {
            progress?.Report(new HotUpdateProgress(stage, message, value));

            string logMessage = $"{stage}: {message}";
            if (stage == HotUpdateStage.Failed)
                HotUpdateLogger.Error(logMessage);
            else
                HotUpdateLogger.Log(logMessage);
        }
    }
}
