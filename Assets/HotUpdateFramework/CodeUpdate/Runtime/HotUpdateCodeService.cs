using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace HotUpdateFramework.Code
{
    public sealed class HotUpdateCodeService
    {
        public static HotUpdateCodeService Instance { get; } = new HotUpdateCodeService();

        private readonly List<Assembly> _loadedAssemblies = new List<Assembly>();

        public IReadOnlyList<Assembly> LoadedAssemblies => _loadedAssemblies;
        
        private HotUpdateCodeService()
        {
        }

        public async UniTask RunAsync(IProgress<HotUpdateProgress> progress = null, HotUpdateContext context = null, CancellationToken cancellationToken = default)
        {
            HotUpdateService resourceService = HotUpdateService.Instance;
            await resourceService.PrepareResourcesAsync(progress, cancellationToken);

            try
            {
                await RunCodeUpdateAsync(resourceService, context, progress, cancellationToken);

                progress?.Report(new HotUpdateProgress(HotUpdateStage.Completed, "Hot update completed", 1f));
                HotUpdateLogger.Log($"Completed summary: package={resourceService.Package.PackageName}, " + $"version={resourceService.PackageVersion}, " + $"assemblies={LoadedAssemblies.Count}");
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                progress?.Report(new HotUpdateProgress(HotUpdateStage.Failed, ex.Message, 1f));
                HotUpdateLogger.Error($"{HotUpdateStage.Failed}: {ex.Message}");
                throw;
            }
        }
        
        private async UniTask RunCodeUpdateAsync(HotUpdateService service, HotUpdateContext hotUpdateContext, IProgress<HotUpdateProgress> progress, CancellationToken cancellationToken)
        {
            var config = HotUpdateCodeConfig.LoadDefault();
            if (config == null)
            {
                throw new HotUpdateException($"Code hot update config not found: " + $"Resources/{HotUpdateCodeConfig.ResourcesPath}");
            }
            ResourcePackage package = service.Package;
            if (package == null)
                throw new HotUpdateException("YooAsset package is not prepared.");

            await LoadAotMetadataAsync(config, package, progress, cancellationToken);
            await LoadHotUpdateAssembliesAsync(config, package, progress, cancellationToken);
            await InvokeEntryAsync(config, service, package, hotUpdateContext, progress, cancellationToken);
        }

        private static async UniTask LoadAotMetadataAsync(HotUpdateCodeConfig config, ResourcePackage package, IProgress<HotUpdateProgress> progress, CancellationToken cancellationToken)
        {
            Report(progress, HotUpdateStage.LoadAotMetadata, "Load AOT metadata");

#if UNITY_EDITOR
            await UniTask.Yield(cancellationToken);
            Report(progress, HotUpdateStage.LoadAotMetadata, "Skip AOT metadata in Editor", 1f);
#else
            IReadOnlyList<string> locations = config.AotMetadataAssetLocations;
            int totalCount = locations.Count;
            HomologousImageMode imageMode = config.HomologousImageMode;

            for (int i = 0; i < totalCount; i++)
            {
                string location = locations[i];
                if (string.IsNullOrWhiteSpace(location))
                    continue;

                byte[] dllBytes = await LoadAssetBytesAsync(package, location, cancellationToken);
                LoadImageErrorCode errorCode = RuntimeApi.LoadMetadataForAOTAssembly(dllBytes, imageMode);
                if (errorCode != LoadImageErrorCode.OK && errorCode != LoadImageErrorCode.HOMOLOGOUS_ASSEMBLY_HAS_LOADED)
                {
                    throw new HotUpdateException($"Load AOT metadata failed: " + $"{location}, {errorCode}");
                }

                Report(progress, HotUpdateStage.LoadAotMetadata, $"Load AOT metadata {i + 1}/{totalCount}", (i + 1f) / Mathf.Max(1, totalCount));
            }
#endif
        }

        private async UniTask LoadHotUpdateAssembliesAsync(HotUpdateCodeConfig config, ResourcePackage package, IProgress<HotUpdateProgress> progress, CancellationToken cancellationToken)
        {
            Report(progress, HotUpdateStage.LoadHotUpdateAssemblies, "Load hot update assemblies");
            _loadedAssemblies.Clear();

            IReadOnlyList<string> locations = config.HotUpdateAssemblyAssetLocations;
            int totalCount = locations.Count;
            for (int i = 0; i < totalCount; i++)
            {
                string location = locations[i];
                if (string.IsNullOrWhiteSpace(location))
                    continue;

                string assemblyName = HotUpdateUtility.GetAssemblyNameFromLocation(location);
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

        private async UniTask InvokeEntryAsync(HotUpdateCodeConfig config, HotUpdateService service, ResourcePackage package, HotUpdateContext hotUpdateContext, IProgress<HotUpdateProgress> progress, CancellationToken cancellationToken)
        {
            if (config.InvokeHotUpdateEntry == false)
                return;

            if (string.IsNullOrWhiteSpace(config.EntryTypeName) || string.IsNullOrWhiteSpace(config.EntryMethodName))
            {
                throw new HotUpdateException("Hot update entry type or method is empty.");
            }

            Report(progress, HotUpdateStage.InvokeEntry, $"Invoke {config.EntryTypeName}." + $"{config.EntryMethodName}");

            MethodInfo method = FindEntryMethod(config.EntryTypeName, config.EntryMethodName);
            if (method == null)
            {
                throw new HotUpdateException($"Can not find hot update entry: " + $"{config.EntryTypeName}." + $"{config.EntryMethodName}");
            }

            object[] args = BuildEntryArguments(method, service, package, config, hotUpdateContext, cancellationToken);

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
                throw new HotUpdateException($"Hot update entry threw an exception: " + $"{ex.InnerException?.Message}", ex.InnerException ?? ex);
            }
        }

        private MethodInfo FindEntryMethod(string typeName, string methodName)
        {
            IEnumerable<Assembly> assemblies = _loadedAssemblies.Count > 0 ? (IEnumerable<Assembly>)_loadedAssemblies : AppDomain.CurrentDomain.GetAssemblies();

            foreach (Assembly assembly in assemblies)
            {
                Type type = assembly.GetType(typeName, false);
                if (type == null)
                    continue;

                return type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }

            return null;
        }

        private static object[] BuildEntryArguments(MethodInfo method, HotUpdateService service, ResourcePackage package, HotUpdateCodeConfig config, HotUpdateContext hotUpdateContext, CancellationToken cancellationToken)
        {
            ParameterInfo[] parameters = method.GetParameters();
            object[] args = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                Type type = parameters[i].ParameterType;
                if (type.IsInstanceOfType(package))
                    args[i] = package;
                else if (type.IsInstanceOfType(service))
                    args[i] = service;
                else if (type.IsInstanceOfType(config))
                    args[i] = config;
                else if (type == typeof(HotUpdateContext))
                    args[i] = hotUpdateContext;
                else if (type == typeof(CancellationToken))
                    args[i] = cancellationToken;
                else if (parameters[i].HasDefaultValue)
                    args[i] = parameters[i].DefaultValue;
                else
                {
                    throw new HotUpdateException($"Unsupported hot update entry parameter: " + $"{parameters[i].Name} ({type.FullName})");
                }
            }

            return args;
        }

        private static void Report(IProgress<HotUpdateProgress> progress, HotUpdateStage stage, string message, float value = 0f)
        {
            progress?.Report(new HotUpdateProgress(stage, message, value));
            HotUpdateLogger.Log($"{stage}: {message}");
        }

        private static async UniTask<byte[]> LoadAssetBytesAsync(ResourcePackage package, string location, CancellationToken cancellationToken)
        {
            if (package.CheckLocationValid(location) == false)
                throw new HotUpdateException($"YooAsset location is invalid: {location}");

            AssetHandle handle = package.LoadAssetAsync<TextAsset>(location);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (handle.IsDone == false)
                    await UniTask.Yield(cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                if (handle.Status != EOperationStatus.Succeed)
                {
                    throw new HotUpdateException($"Load asset failed: {location}, " + $"{handle.LastError}");
                }

                TextAsset asset = handle.GetAssetObject<TextAsset>();
                byte[] bytes = asset == null ? null : asset.bytes;
                if (bytes == null || bytes.Length == 0)
                {
                    throw new HotUpdateException($"Asset bytes are empty: {location}");
                }

                return bytes;
            }
            finally
            {
                handle.Release();
            }
        }

        private static Assembly TryGetLoadedEditorAssembly(string assemblyName)
        {
#if UNITY_EDITOR
            return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal));
#else
            return null;
#endif
        }
    }
}
