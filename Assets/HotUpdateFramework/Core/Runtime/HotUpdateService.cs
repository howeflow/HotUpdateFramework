using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using YooAsset;

namespace HotUpdateFramework
{
    public sealed class HotUpdateService
    {
        public static HotUpdateService Instance { get; } = new HotUpdateService();

        public ResourcePackage Package { get; private set; }
        public string PackageVersion { get; private set; } = string.Empty;

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
            var config = HotUpdateConfig.LoadDefault();
            if (config == null)
                throw new HotUpdateException("HotUpdateConfig is null.");

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                HotUpdateLogger.Log($"Start summary: package={config.PackageName}, playMode={config.PlayMode}, " + $"platform={HotUpdateUtility.GetPlatformName(config.PlatformNameOverride)}, " + $"versionOverride={(string.IsNullOrEmpty(config.PackageVersionOverride) ? "<empty>" : config.PackageVersionOverride)}");

                Report(progress, HotUpdateStage.InitializeYooAsset, "Initialize YooAsset");

                YooAssets.Initialize();

                bool useLocalOnly = ShouldUseLocalOnly(config);
                Package = await InitializePackageAsync(config, config.PackageName, progress, cancellationToken, useLocalOnly);

                try
                {
                    PackageVersion = await RequestAndUpdateManifestAsync(config, Package, useLocalOnly ? string.Empty : config.PackageVersionOverride, progress, cancellationToken);
                }
                catch (HotUpdateException ex)
                    when (CanFallbackToBuildin(config) && useLocalOnly == false)
                {
                    HotUpdateLogger.Warning($"Remote manifest unavailable, use buildin package: " + $"{ex.Message}");
                    Package = await ReinitializeOfflinePackageAsync(Package, progress, cancellationToken);
                    PackageVersion = await RequestAndUpdateManifestAsync(config, Package, string.Empty, progress, cancellationToken);
                }
                await DownloadPackageAsync(Package, config, progress, cancellationToken);

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

        private async UniTask<ResourcePackage> InitializePackageAsync(HotUpdateConfig config, string packageName, IProgress<HotUpdateProgress> progress, CancellationToken cancellationToken, bool forceOffline = false)
        {
            ResourcePackage package = YooAssets.TryGetPackage(packageName) ?? YooAssets.CreatePackage(packageName);

            if (package.InitializeStatus == EOperationStatus.Succeed && package.PackageValid)
            {
                YooAssets.SetDefaultPackage(package);
                return package;
            }

            InitializeParameters parameters = CreateInitializeParameters(config, packageName, forceOffline);
            InitializationOperation operation = package.InitializeAsync(parameters);
            await WaitOperationAsync(operation, HotUpdateStage.InitializeYooAsset, $"Initialize package {packageName}", progress, cancellationToken);
            EnsureSucceed(operation, $"Initialize YooAsset package {packageName}");

            YooAssets.SetDefaultPackage(package);
            return package;
        }

        private static InitializeParameters CreateInitializeParameters(HotUpdateConfig config, string packageName, bool forceOffline = false)
        {
            IDecryptionServices decryptionServices = HotUpdateCryptoProvider.DecryptionServices;

            if (forceOffline)
            {
                return CreateOfflineInitializeParameters(decryptionServices);
            }

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
                        BuildinFileSystemParameters = config.UseBuildinFileSystemInHostMode ? FileSystemParameters.CreateDefaultBuildinFileSystemParameters(decryptionServices) : null,
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

        private static bool CanFallbackToBuildin(HotUpdateConfig config)
        {
            return config.PlayMode == EPlayMode.HostPlayMode && config.UseBuildinFileSystemInHostMode;
        }

        private static bool ShouldUseLocalOnly(HotUpdateConfig config)
        {
            if (CanFallbackToBuildin(config) == false)
                return false;

            IReadOnlyList<string> roots = config.RemoteRoots;
            for (int i = 0; i < roots.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(roots[i]) == false)
                    return false;
            }

            HotUpdateLogger.Warning("Remote roots are empty, use buildin package.");
            return true;
        }

        private async UniTask<string> RequestAndUpdateManifestAsync(HotUpdateConfig config, ResourcePackage package, string packageVersionOverride, IProgress<HotUpdateProgress> progress, CancellationToken cancellationToken)
        {
            string packageVersion = packageVersionOverride;

            if (string.IsNullOrWhiteSpace(packageVersion))
            {
                Report(progress, HotUpdateStage.RequestPackageVersion, $"Request package version {package.PackageName}");
                RequestPackageVersionOperation versionOperation = package.RequestPackageVersionAsync(true, config.ManifestTimeout);
                await WaitOperationAsync(versionOperation, HotUpdateStage.RequestPackageVersion, $"Request package version {package.PackageName}", progress, cancellationToken);
                EnsureSucceed(versionOperation, $"Request YooAsset package version {package.PackageName}");
                packageVersion = versionOperation.PackageVersion;
            }

            if (string.IsNullOrWhiteSpace(packageVersion))
                throw new HotUpdateException($"YooAsset package version is empty: {package.PackageName}");

            Report(progress, HotUpdateStage.UpdateManifest, $"Update manifest {package.PackageName} {packageVersion}");
            UpdatePackageManifestOperation manifestOperation = package.UpdatePackageManifestAsync(packageVersion, config.ManifestTimeout);
            await WaitOperationAsync(manifestOperation, HotUpdateStage.UpdateManifest, $"Update manifest {package.PackageName} {packageVersion}", progress, cancellationToken);
            EnsureSucceed(manifestOperation, $"Update YooAsset package manifest {package.PackageName}");

            return packageVersion;
        }

        private async UniTask DownloadPackageAsync(ResourcePackage package, HotUpdateConfig config, IProgress<HotUpdateProgress> progress, CancellationToken cancellationToken)
        {
            Report(progress, HotUpdateStage.DownloadFiles, $"Create downloader {package.PackageName}");

            ResourceDownloaderOperation downloader = package.CreateResourceDownloader(config.DownloadingMaxNumber, config.FailedTryAgain);

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
