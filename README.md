# HybridCLR + YooAsset + UniTask 热更新框架

这个项目在 `Assets/HotUpdateFramework` 下提供了一套轻量热更新框架，用来串联 HybridCLR、YooAsset 和 UniTask，并支持任意 HTTP/HTTPS CDN。CDN 可以是对象存储、云厂商 CDN、自建静态服务器、Nginx、OSS/COS/S3 兼容源站等，只要 YooAsset 生成的文件能通过 URL 访问即可。

框架代码分为两个独立程序集模块：

- `Core`：负责配置、YooAsset 初始化、版本清单和资源下载，不引用 HybridCLR。
- `HybridCLR`：直接负责 AOT 元数据、热更 DLL、`HotUpdateContext` 和入口调用；编辑器生成与复制菜单也位于独立 Editor 程序集，不使用接口或运行时注册。

配置资源也按模块拆分：`HotUpdateConfig.asset` 只保存 YooAsset、CDN 和下载配置；`HotUpdateCodeConfig.asset` 保存 HybridCLR 元数据、热更程序集和入口配置。移除 HybridCLR 模块时不会影响 Core 配置。

## 单包结构

框架采用单 YooAsset 包结构：

- `DefaultPackage`：同时放 HybridCLR 热更 DLL、AOT 元数据 DLL 和普通热更新资源。

启动场景手动调用热更后，流程为整包下载：

1. 初始化 YooAsset。
2. 初始化 `DefaultPackage`，请求版本并更新清单。
3. 对 `DefaultPackage` 创建整包下载器并下载所有需要更新的文件。
4. 从 `DefaultPackage` 加载 AOT 元数据 DLL 和热更 DLL。
5. 将 `DefaultPackage` 设置为默认资源包。
6. 反射调用 `HotUpdate.HotUpdateEntry.Start`。

运行时日志通过 `HotUpdateLogger.Logger` 控制，默认使用 `DefaultUpdateLogger`。如果不需要框架日志，可以设置为 `EmptyHotUpdateLogger` 或提供自己的 `IHotUpdateLogger` 实现。

自定义加解密时，直接实现 YooAsset 官方接口，并在构建热更包前和运行热更前注册到 `HotUpdateCryptoProvider`：

```csharp
public sealed class MyBundleEncryptionServices : IEncryptionServices
{
    //encrytion
}
public sealed class MyBundleDecryptionServices : IDecryptionServices
{
    //decryption
}

public static class MyBundleCryptoRegister
{
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void InitEditor()
    {
        HotUpdateCryptoProvider.SetEncryptionServices(new MyBundleEncryptionServices());
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitRuntime()
    {
        HotUpdateCryptoProvider.SetDecryptionServices(new MyBundleDecryptionServices());
    }
}
```

运行时解密服务必须在调用 `HotUpdateCodeService.Instance.RunAsync` 之前注册。编辑器加密服务可以放在 Editor 代码里，用 `[InitializeOnLoadMethod]` 注册。构建热更包和运行时加载必须使用同一套算法和参数，否则 AssetBundle 会加载失败。

如果自定义算法只是给 AssetBundle 文件头部增加固定偏移，解密服务里可以直接使用 `AssetBundle.LoadFromFile(fileInfo.FileLoadPath, fileInfo.FileLoadCRC, offset)`，运行时会走更省内存的文件偏移加载。AES、异或、压缩包头等需要还原完整字节的算法，可以在解密服务里读取文件、解密后再从 `AssetBundle.LoadFromMemory` 或 `AssetBundle.LoadFromMemoryAsync` 加载。

## 运行时接入

建议在 BootScene 里完成隐私协议、基础 SDK、网络检查、强更检查之后，再手动调用：

```csharp
using HotUpdateFramework;
using HotUpdateFramework.Code;

var progress = Progress.Create<HotUpdateProgress>(value =>
{
    //进度处理
});

var context = new HotUpdateContext
{
    OnComplete = OnHotUpdateComplete,
    OnProgress = OnHotUpdateContextProgress,
    UserData = this
};

var cancellationToken = this.GetCancellationTokenOnDestroy();
await HotUpdateCodeService.Instance.RunAsync(
    progress,
    context,
    cancellationToken);
```

不使用代码热更模块时，Core 不需要 `HotUpdateContext`，直接调用 `await HotUpdateService.Instance.RunAsync(progress, cancellationToken)` 即可。

`HotUpdateProgress.Progress` 表示当前阶段进度。Loading 进度条如果需要完整 `0-1` 流程进度，建议在启动层根据 `HotUpdateProgress.Stage` 自己做映射，示例可参考 `Assets/Sample/BootController.cs`。

热更入口可以按需声明框架支持的参数，`HotUpdateContext` 会由 AOT 启动侧传入：

```csharp
public static async UniTask Start(HotUpdateContext context)
{
    await InitGameAsync();
    context?.Complete();
}
```

## 默认目录

项目内资源位置：

- 热更 DLL：`Assets/HotUpdateAssets/Assemblies/HotUpdate.dll.bytes`
- AOT 元数据 DLL：`Assets/HotUpdateAssets/Assemblies/AOT/*.dll.bytes`
- 普通热更新资源：`Assets/HotUpdateAssets/Res`
- 热更新配置：`Assets/Resources/HotUpdateConfig.asset`
- 代码热更配置：`Assets/Resources/HotUpdateCodeConfig.asset`

`HotUpdateCodeConfig.asset` 里的程序集目录配置：

- `hotUpdateAssemblyAssetDirectory`：热更 DLL 的目标目录，默认是 `Assets/HotUpdateAssets/Assemblies`
- `aotMetadataAssetDirectory`：AOT 元数据 DLL 的目标目录，默认是 `Assets/HotUpdateAssets/Assemblies/AOT`

目录需要位于 `Assets` 下。程序集列表可以填写 `HotUpdate`、`HotUpdate.dll` 或 `HotUpdate.dll.bytes`，框架会转换为 YooAsset 使用的 `.dll.bytes` 资源路径。调整目录或程序集列表后，执行 `Hot Update/Prepare HotUpdate Process` 和 `Hot Update/Asset/Build YooAsset Package`。

YooAsset Collector 默认配置：

- `DefaultPackage` 收集 `Assets/HotUpdateAssets`
- DLL 和 AOT 文件作为普通 `TextAsset` 打进 AssetBundle，并在运行时读取 `bytes`
- 资源定位使用完整资源路径，例如 `Assets/HotUpdateAssets/Assemblies/HotUpdate.dll.bytes`

`ProjectSettings/HybridCLRSettings.asset` 配置内容：

- 热更程序集：`HotUpdate`
- AOT 元数据程序集：执行 `Hot Update/Prepare All Process` 后，会从 `Assets/HybridCLRGenerate/AOTGenericReferences.cs` 自动同步到 `ProjectSettings/HybridCLRSettings.asset` 和 `HotUpdateCodeConfig.asset`

## 编辑器流程

1. 如果项目尚未安装 HybridCLR，先执行 `HybridCLR/Installer...`。
2. 在 YooAsset Collector 里配置 `DefaultPackage`，并收集 `Assets/HotUpdateAssets`。
3. 首次出包、AOT 代码变化、切平台或 `Development Build` 开关变化时，执行 `Hot Update/Prepare All Process`。
4. 只修改热更代码时，执行 `Hot Update/Prepare HotUpdate Process`。
5. 在 YooAsset Collector 中给需要进入首包的资源设置标签，并将标签配置到 `HotUpdateConfig.asset` 的 `builtinTag`。
6. 执行 `Hot Update/Asset/Build YooAsset Package`，构建单个热更新包。
7. 重新构建 App 包，让按 `builtinTag` 筛选出的 `Assets/StreamingAssets/DefaultPackage` 资源进入首包。
8. 将生成的 YooAsset 包目录发布到 CDN 源站。

`HostPlayMode` 固定同时启用 Buildin 与 Cache 文件系统。构建菜单使用 `ClearAndCopyByTags`，只把 `builtinTag` 匹配的 Bundle 以及包清单复制到 `Assets/StreamingAssets/DefaultPackage`，其余资源从本地缓存或 CDN 获取。

初始化阶段只主动下载 `downloadTag` 配置的资源；列表为空时跳过主动下载，其他资源在实际加载时按需获取。YooAsset 的标签下载规则会同时包含没有配置任何标签的基础 Bundle。

运行时可通过 `HotUpdateService.IsNeedDownload` 判断指定 location 是否缺少主 Bundle 或依赖 Bundle，并使用 `DownloadByLocationAsync`、`DownloadByLocationsAsync` 或 `DownloadByTagsAsync` 主动下载；资源加载和句柄释放仍直接使用 YooAsset API。

### 步骤操作

首次出包 / AOT 代码变化 / 切平台 / `Development Build` 开关变化：

```text
Hot Update/Prepare All Process
Hot Update/Asset/Build YooAsset Package
Build Player
```

`Prepare All Process` 会执行 HybridCLR `Generate All`，同步 AOT 元数据程序集列表，并把热更 DLL 和 AOT 元数据 DLL 复制到配置的资源目录。

修改AOT启动流程 / CDN 地址 / 播放模式：

```text
Build Player
```

改了 `builtinTag` / 加解密服务代码：

```text
Hot Update/Build Package
Build Player
```

改了 `HotUpdate` 热更代码：

```text
Hot Update/Prepare HotUpdate Process
Hot Update/Build Package
```

`Prepare HotUpdate Process` 只编译热更 DLL，并复制热更 DLL 和已有的 AOT 元数据 DLL。它不会重新生成 AOT 元数据列表。

如果热更代码新增了需要 AOT 补充元数据支持的泛型调用，执行 `Hot Update/Prepare All Process` 后再继续后续步骤。

改了 `Assets/HotUpdateAssets/Res` 下的热更资源：

```text
Hot Update/Build Package
```


## CDN 远程目录

默认 URL 模板是：

```text
{Root}/{Platform}/{PackageName}/{FileName}
```

`HotUpdateConfig.asset` 使用 `Environment` 选择 `Local`、`Dev` 或 `Release` 环境。每个环境分别配置 YooAsset 的 `Main Root` 和 `Fallback Root`：

```text
Environment: Dev

Local Remote
  Main Root: http://127.0.0.1:8080
  Fallback Root:
Dev Remote
  Main Root: https://cdn.example.com/hotupdate/dev
  Fallback Root: https://backup.example.com/hotupdate/dev
Release Remote
  Main Root: https://cdn.example.com/hotupdate/release
  Fallback Root: https://backup.example.com/hotupdate/release
```

如果选择 `Dev`，并将 `Dev Remote/Main Root` 设置为：

```text
https://cdn.example.com/hotupdate/dev
```

Android 平台会请求：

```text
https://cdn.example.com/hotupdate/dev/Android/DefaultPackage/<YooAssetFileName>
```

所以 CDN 源站目录应该类似这样：

```text
<CDN源站根目录>/hotupdate/
  dev/
    Android/
      DefaultPackage/
        <YooAsset输出文件>
  release/
    Android/
      DefaultPackage/
        <YooAsset输出文件>
```

本地发布默认读取 `Tools/local_cdn_server.env`：

```powershell
python .\Tools\local_cdn_server.py
```

默认配置如下：

```ini
CdnRootDirectory=LocalCdn
BuildOutputRoot=Bundles
Platform=Android
PackageName=DefaultPackage
CleanDestination=true
StartLocalServer=true
LocalServerHost=0.0.0.0
LocalServerPort=8080
PauseOnExit=true
```

脚本会根据 `BuildOutputRoot`、`Platform` 和 `PackageName` 推导 YooAsset 输出目录 `<BuildOutputRoot>/<Platform>/<PackageName>`，从里面寻找最新的 YooAsset 版本目录，然后复制到：

```text
LocalCdn/Android/DefaultPackage
```

本地模拟 CDN 可以在配置里开启自动启动服务：

```ini
StartLocalServer=true
LocalServerHost=0.0.0.0
LocalServerPort=8080
```

开启后执行发布脚本会直接启动 HTTP 服务，终端保持运行，按 `Ctrl+C` 停止。命令行可以临时开启：

```powershell
python .\Tools\local_cdn_server.py --start-local-server
```

此时在 `HotUpdateConfig.asset` 中选择 `Local`，并设置：

```text
Local Remote/Main Root = http://127.0.0.1:8080
```

发布到真实源站时，调整配置里的 `CdnRootDirectory`，例如：

```ini
CdnRootDirectory=D:/CdnOrigin/hotupdate
```

然后选择对应环境并填写公网地址，例如：

```text
Environment = Dev
Dev Remote/Main Root = https://cdn.example.com/hotupdate/dev
```

命令行参数可临时覆盖配置：

```powershell
python .\Tools\local_cdn_server.py --platform iOS --package-name DefaultPackage --cdn-root-directory "D:\CdnOrigin\hotupdate"
```

脚本结束后会等待回车关闭终端。命令行连续执行时可以关闭等待：

```powershell
python .\Tools\local_cdn_server.py --no-pause-on-exit
```

脚本会复制到：

```text
<CdnRootDirectory>/<Platform>/<PackageName>
```

之后用你的 CDN/对象存储/服务器同步工具把 `CdnRootDirectory` 发布到公网。默认配置会清理目标目录，适合本地模拟；如果线上需要保留旧文件，可以把 `CleanDestination` 改成 `false`。

## Cloudflare R2 模拟

`Tools/r2_cdn_sync.py` 用 Python 将 `LocalCdn` 同步到 Cloudflare R2。密钥不写入项目配置，默认使用环境变量或运行时输入。

安装 Python 依赖：

```powershell
python -m pip install -r .\Tools\requirements-r2.txt
```

配置 `Tools/r2_cdn_sync.env`：

```ini
CdnRootDirectory=LocalCdn
BucketName=your-r2-bucket
AccountId=your-cloudflare-account-id
Prefixes=dev,release
DeleteRemote=false
RefreshLocalCdn=true
LocalCdnConfigPath=Tools/local_cdn_server.env
PublicBaseUrl=https://pub-xxxx.r2.dev
DryRun=false
IncrementalUpload=true
SyncManifestFileName=.r2-sync-manifest.json
InteractiveCredentials=true
PauseOnExit=true
```

执行同步时，脚本会在当前终端提示输入 R2 S3 API 密钥：

```powershell
python .\Tools\r2_cdn_sync.py
```

如果 `Prefixes` 配置了多个前缀，脚本会先让你选择要同步到哪个目录：

```text
Select R2 prefix:
  1. dev
  2. release
Prefix [1-2, default 1]:
```

也可以在命令行里直接指定，适合接 CI 或固定发布目标：

```powershell
python .\Tools\r2_cdn_sync.py --prefix release
```

也可以提前在终端设置环境变量，脚本检测到后不会再次询问：

```powershell
$env:AWS_ACCESS_KEY_ID="R2 Access Key ID"
$env:AWS_SECRET_ACCESS_KEY="R2 Secret Access Key"
python .\Tools\r2_cdn_sync.py
```

脚本会先按 `local_cdn_server.env` 刷新 `LocalCdn`，再同步到：

```text
s3://<BucketName>/<SelectedPrefix>
```

`IncrementalUpload` 开启时，脚本会在本地生成同步清单，并上传到 R2 的 `<SelectedPrefix>/.r2-sync-manifest.json`。下次同步会先读取所选前缀下的远程清单，按文件相对路径、大小和 MD5 判断是否变化，只上传新增或变化的文件。第一次远程没有同步清单时，脚本会退回使用 R2 对象列表做比对；执行后会写入同步清单，后续同步就会走清单比对。命令行可以临时关闭增量上传：

```powershell
python .\Tools\r2_cdn_sync.py --upload-all
```

R2 公开访问地址对应填到 `HotUpdateConfig.asset`：

```text
Dev Remote/Main Root = https://pub-xxxx.r2.dev/dev
Release Remote/Main Root = https://pub-xxxx.r2.dev/release
```

如果发布正式环境，运行脚本时选择 `release` 或传入 `--prefix release`，远端文件路径为 `release/Android/DefaultPackage/...`。要让当前包访问正式环境，将配置切换为：

```text
Environment = Release
```

`Fallback Root` 应指向同一环境的备用 CDN；留空时会自动使用该环境的 `Main Root`，不要把 `Dev` 和 `Release` 互相作为备用地址。

## 注意事项

- 真机联机更新建议使用 `HostPlayMode`，并确保源站目录结构和 URL 模板一致。
- `HostPlayMode` 成功更新远端清单后会立即记录最后可用版本，不要求所有资源下载完成。之后远端版本或清单请求失败时，会依次尝试 YooAsset 本地缓存清单和首包内置资源；使用缓存清单时仍保持 Buildin 与 Cache 文件系统同时生效。
- 修改 `builtinTag` 后需要重新执行 `Hot Update/Asset/Build YooAsset Package` 并重新打 App 包，才会改变真机首包内置资源。
- 资源加密由 `HotUpdateCryptoProvider` 注册 YooAsset `IEncryptionServices` 和 `IDecryptionServices` 决定。
- Editor 模拟模式依赖 `AssetBundleCollectorSetting.asset` 里存在 `DefaultPackage`。
