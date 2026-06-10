using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using HybridCLR;
using HotUpdateFramework.Crypto;
using UnityEngine;
using YooAsset;

namespace HotUpdateFramework
{
    public sealed class HotUpdateService
    {
        public static HotUpdateService Instance { get; } = new HotUpdateService();

        private readonly List<Assembly> _loadedAssemblies = new List<Assembly>();

        public ResourcePackage Package { get; private set; }
        public string PackageVersion { get; private set; } = string.Empty;
        public IReadOnlyList<Assembly> LoadedAssemblies => _loadedAssemblies;

        private HotUpdateService()
        {
        }

        public UniTask RunAsync(HotUpdateConfig config, HotUpdateContext context, IProgress<HotUpdateProgress> progress = null, CancellationToken cancellationToken = default)
        {
            return RunAsync(config, progress, cancellationToken, context);
        }

        public async UniTask RunAsync(HotUpdateConfig config, IProgress<HotUpdateProgress> progress = null, CancellationToken cancellationToken = default, HotUpdateContext context = null)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            try
            {
                Report(progress, HotUpdateStage.InitializeYooAsset, "Initialize YooAsset");

                if (YooAssets.Initialized == false)
                    YooAssets.Initialize();

                Package = await InitializePackageAsync(config, config.PackageName, config.SetAsDefaultPackage, progress, cancellationToken);
                PackageVersion = await RequestAndUpdateManifestAsync(config, Package, config.PackageVersionOverride, progress, cancellationToken);
                if (config.DownloadPackage)
                    await DownloadPackageAsync(Package, config, progress, cancellationToken);

                await LoadAotMetadataAsync(config, Package, progress, cancellationToken);
                await LoadHotUpdateAssembliesAsync(config, Package, progress, cancellationToken);
                await InvokeEntryAsync(config, context, progress, cancellationToken);

                Report(progress, HotUpdateStage.Completed, "Hot update completed", 1f);
            }
            catch (Exception ex)
            {
                Report(progress, HotUpdateStage.Failed, ex.Message, 1f);
                throw;
            }
        }

        private async UniTask<ResourcePackage> InitializePackageAsync(HotUpdateConfig config, string packageName, bool setAsDefaultPackage, IProgress<HotUpdateProgress> progress, CancellationToken cancellationToken)
        {
            ResourcePackage package = YooAssets.TryGetPackage(packageName) ?? YooAssets.CreatePackage(packageName);

            if (package.InitializeStatus == EOperationStatus.Succeed && package.PackageValid)
            {
                if (setAsDefaultPackage)
                    YooAssets.SetDefaultPackage(package);
                return package;
            }

            InitializeParameters parameters = CreateInitializeParameters(config, packageName);
            InitializationOperation operation = package.InitializeAsync(parameters);
            await WaitOperationAsync(operation, HotUpdateStage.InitializeYooAsset, $"Initialize package {packageName}", progress, cancellationToken);
            EnsureSucceed(operation, $"Initialize YooAsset package {packageName}");

            if (setAsDefaultPackage)
                YooAssets.SetDefaultPackage(package);

            return package;
        }

        private static InitializeParameters CreateInitializeParameters(HotUpdateConfig config, string packageName)
        {
            IDecryptionServices decryptionServices = CreateDecryptionServices(config);

            switch (config.PlayMode)
            {
                case EPlayMode.EditorSimulateMode:
                    var simulateResult = EditorSimulateModeHelper.SimulateBuild(packageName);
                    return new EditorSimulateModeParameters
                    {
                        EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(simulateResult.PackageRootDirectory)
                    };

                case EPlayMode.OfflinePlayMode:
                    return new OfflinePlayModeParameters
                    {
                        BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(decryptionServices)
                    };

                case EPlayMode.HostPlayMode:
                    IRemoteServices remoteServices = new CdnRemoteServices(config, packageName);
                    return new HostPlayModeParameters
                    {
                        BuildinFileSystemParameters = config.UseBuildinFileSystemInHostMode
                            ? FileSystemParameters.CreateDefaultBuildinFileSystemParameters(decryptionServices)
                            : null,
                        CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices, decryptionServices)
                    };

                case EPlayMode.WebPlayMode:
                    if (config.EnableBundleEncryption)
                        throw new HotUpdateException("Bundle encryption does not support WebPlayMode.");

                    IRemoteServices webRemoteServices = new CdnRemoteServices(config, packageName);
                    return new WebPlayModeParameters
                    {
                        WebServerFileSystemParameters = FileSystemParameters.CreateDefaultWebServerFileSystemParameters(),
                        WebRemoteFileSystemParameters = FileSystemParameters.CreateDefaultWebRemoteFileSystemParameters(webRemoteServices)
                    };

                default:
                    throw new HotUpdateException($"Unsupported YooAsset play mode: {config.PlayMode}");
            }
        }

        private static IDecryptionServices CreateDecryptionServices(HotUpdateConfig config)
        {
            return config.EnableBundleEncryption
                ? new HotUpdateBundleDecryptionServices()
                : null;
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

            string totalSizeText = FormatBytes(downloader.TotalDownloadBytes);
            Report(progress, HotUpdateStage.DownloadFiles, $"Need download {package.PackageName}: {downloader.TotalDownloadCount} files, {totalSizeText}");

            downloader.DownloadUpdateCallback = data =>
            {
                string currentSizeText = FormatBytes(data.CurrentDownloadBytes);
                string downloadMessage = $"Download {package.PackageName} {data.CurrentDownloadCount}/{data.TotalDownloadCount} {currentSizeText}/{FormatBytes(data.TotalDownloadBytes)}";
                progress?.Report(new HotUpdateProgress(
                    HotUpdateStage.DownloadFiles,
                    downloadMessage,
                    data.Progress,
                    data.CurrentDownloadCount,
                    data.TotalDownloadCount,
                    data.CurrentDownloadBytes,
                    data.TotalDownloadBytes));
            };
            downloader.DownloadErrorCallback = data =>
            {
                Debug.LogError($"[HotUpdate] Download failed: {data.FileName}, {data.ErrorInfo}");
            };
            downloader.DownloadFinishCallback = data =>
            {
                if (data.Succeed)
                    Report(progress, HotUpdateStage.DownloadFiles, $"Download completed {data.PackageName}: {downloader.TotalDownloadCount} files, {totalSizeText}", 1f);
            };

            downloader.BeginDownload();
            await WaitOperationAsync(downloader, HotUpdateStage.DownloadFiles, $"Download files {package.PackageName}", null, cancellationToken);
            EnsureSucceed(downloader, $"Download YooAsset files {package.PackageName}");
        }

        private async UniTask LoadAotMetadataAsync(HotUpdateConfig config, ResourcePackage package, IProgress<HotUpdateProgress> progress, CancellationToken cancellationToken)
        {
            Report(progress, HotUpdateStage.LoadAotMetadata, "Load AOT metadata");

#if UNITY_EDITOR
            await UniTask.Yield(cancellationToken);
            Report(progress, HotUpdateStage.LoadAotMetadata, "Skip AOT metadata in Editor", 1f);
#else
            int totalCount = config.AotMetadataAssetLocations.Count;
            for (int i = 0; i < totalCount; i++)
            {
                string location = config.AotMetadataAssetLocations[i];
                if (string.IsNullOrWhiteSpace(location))
                    continue;

                byte[] dllBytes = await LoadAssetBytesAsync(package, location, cancellationToken);
                LoadImageErrorCode errorCode = RuntimeApi.LoadMetadataForAOTAssembly(dllBytes, config.HomologousImageMode);
                if (errorCode != LoadImageErrorCode.OK && errorCode != LoadImageErrorCode.HOMOLOGOUS_ASSEMBLY_HAS_LOADED)
                    throw new HotUpdateException($"Load AOT metadata failed: {location}, {errorCode}");

                Report(progress, HotUpdateStage.LoadAotMetadata, $"Load AOT metadata {i + 1}/{totalCount}", (i + 1f) / Mathf.Max(1, totalCount));
            }
#endif
        }

        private async UniTask LoadHotUpdateAssembliesAsync(HotUpdateConfig config, ResourcePackage package, IProgress<HotUpdateProgress> progress, CancellationToken cancellationToken)
        {
            Report(progress, HotUpdateStage.LoadHotUpdateAssemblies, "Load hot update assemblies");
            _loadedAssemblies.Clear();

            int totalCount = config.HotUpdateAssemblyAssetLocations.Count;
            for (int i = 0; i < totalCount; i++)
            {
                string location = config.HotUpdateAssemblyAssetLocations[i];
                if (string.IsNullOrWhiteSpace(location))
                    continue;

                string assemblyName = GetAssemblyNameFromLocation(location);
                Assembly assembly = TryGetLoadedEditorAssembly(assemblyName);
                if (assembly == null)
                {
                    byte[] dllBytes = await LoadAssetBytesAsync(package, location, cancellationToken);
                    assembly = Assembly.Load(dllBytes);
                }

                _loadedAssemblies.Add(assembly);
                Report(progress, HotUpdateStage.LoadHotUpdateAssemblies, $"Load assembly {assemblyName}", (i + 1f) / Mathf.Max(1, totalCount));
            }
        }

        private async UniTask InvokeEntryAsync(HotUpdateConfig config, HotUpdateContext context, IProgress<HotUpdateProgress> progress, CancellationToken cancellationToken)
        {
            if (config.InvokeHotUpdateEntry == false)
                return;

            if (string.IsNullOrWhiteSpace(config.EntryTypeName) || string.IsNullOrWhiteSpace(config.EntryMethodName))
                throw new HotUpdateException("Hot update entry type or method is empty.");

            Report(progress, HotUpdateStage.InvokeEntry, $"Invoke {config.EntryTypeName}.{config.EntryMethodName}");

            MethodInfo method = FindEntryMethod(config.EntryTypeName, config.EntryMethodName);
            if (method == null)
                throw new HotUpdateException($"Can not find hot update entry: {config.EntryTypeName}.{config.EntryMethodName}");

            object[] args = BuildEntryArguments(method, config, context, cancellationToken);

            try
            {
                object result = method.Invoke(null, args);
                if (result is UniTask uniTask)
                    await uniTask;
                else if (result is System.Threading.Tasks.Task task)
                    await task;
            }
            catch (TargetInvocationException ex)
            {
                throw new HotUpdateException($"Hot update entry threw an exception: {ex.InnerException?.Message}", ex.InnerException ?? ex);
            }
        }

        private MethodInfo FindEntryMethod(string typeName, string methodName)
        {
            IEnumerable<Assembly> assemblies = _loadedAssemblies.Count > 0
                ? _loadedAssemblies
                : AppDomain.CurrentDomain.GetAssemblies();

            foreach (Assembly assembly in assemblies)
            {
                Type type = assembly.GetType(typeName, false);
                if (type == null)
                    continue;

                return type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }

            return null;
        }

        private object[] BuildEntryArguments(MethodInfo method, HotUpdateConfig config, HotUpdateContext context, CancellationToken cancellationToken)
        {
            ParameterInfo[] parameters = method.GetParameters();
            object[] args = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;
                if (Package != null && type.IsInstanceOfType(Package))
                    args[i] = Package;
                else if (type.IsInstanceOfType(this))
                    args[i] = this;
                else if (type.IsInstanceOfType(config))
                    args[i] = config;
                else if (type == typeof(HotUpdateContext))
                    args[i] = context;
                else if (type == typeof(CancellationToken))
                    args[i] = cancellationToken;
                else if (parameters[i].HasDefaultValue)
                    args[i] = parameters[i].DefaultValue;
                else
                    throw new HotUpdateException($"Unsupported hot update entry parameter: {parameters[i].Name} ({type.FullName})");
            }

            return args;
        }

        private static async UniTask<byte[]> LoadAssetBytesAsync(ResourcePackage package, string location, CancellationToken cancellationToken)
        {
            if (package.CheckLocationValid(location) == false)
                throw new HotUpdateException($"YooAsset location is invalid: {location}");

            AssetHandle handle = package.LoadAssetAsync<TextAsset>(location);
            try
            {
                await WaitHandleAsync(handle, cancellationToken);
                if (handle.Status != EOperationStatus.Succeed)
                    throw new HotUpdateException($"Load asset failed: {location}, {handle.LastError}");

                TextAsset asset = handle.GetAssetObject<TextAsset>();
                byte[] bytes = asset == null ? null : asset.bytes;
                if (bytes == null || bytes.Length == 0)
                    throw new HotUpdateException($"Asset bytes are empty: {location}");

                return bytes;
            }
            finally
            {
                handle.Release();
            }
        }

        private static async UniTask WaitOperationAsync(AsyncOperationBase operation, HotUpdateStage stage, string message, IProgress<HotUpdateProgress> progress, CancellationToken cancellationToken)
        {
            while (operation.IsDone == false)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new HotUpdateProgress(stage, message, operation.Progress));
                await UniTask.Yield(cancellationToken);
            }
        }

        private static async UniTask WaitHandleAsync(HandleBase handle, CancellationToken cancellationToken)
        {
            while (handle.IsDone == false)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.Yield(cancellationToken);
            }
        }

        private static void EnsureSucceed(AsyncOperationBase operation, string action)
        {
            if (operation.Status != EOperationStatus.Succeed)
                throw new HotUpdateException($"{action} failed: {operation.Error}");
        }

        private static string GetAssemblyNameFromLocation(string location)
        {
            string fileName = Path.GetFileName(location);
            if (fileName.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - ".bytes".Length);
            if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - ".dll".Length);
            return fileName;
        }

        private static string FormatBytes(long bytes)
        {
            const double unit = 1024d;
            if (bytes < unit)
                return $"{bytes} B";

            double value = bytes;
            string[] units = { "KB", "MB", "GB" };
            for (int i = 0; i < units.Length; i++)
            {
                value /= unit;
                if (value < unit || i == units.Length - 1)
                    return $"{value:0.##} {units[i]}";
            }

            return $"{bytes} B";
        }

        private static Assembly TryGetLoadedEditorAssembly(string assemblyName)
        {
#if UNITY_EDITOR
            return AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal));
#else
            return null;
#endif
        }

        private static void Report(IProgress<HotUpdateProgress> progress, HotUpdateStage stage, string message, float value = 0f)
        {
            progress?.Report(new HotUpdateProgress(stage, message, value));
            Debug.Log($"[HotUpdate] {stage}: {message}");
        }
    }
}
