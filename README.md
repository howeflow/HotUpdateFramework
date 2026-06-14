# HybridCLR + YooAsset + UniTask 热更新框架

这个项目在 `Assets/HotUpdateFramework` 下提供了一套轻量热更新框架，用来串联 HybridCLR、YooAsset 和 UniTask，并支持任意 HTTP/HTTPS CDN。CDN 可以是对象存储、云厂商 CDN、自建静态服务器、Nginx、OSS/COS/S3 兼容源站等，只要 YooAsset 生成的文件能通过 URL 访问即可。

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

运行时框架普通日志由 `HotUpdateConfig.asset` 里的 `enableRuntimeLog` 控制，默认开启。关闭后只隐藏热更阶段类的普通日志，下载失败和热更失败仍然会输出错误日志。

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

运行时解密服务必须在调用 `HotUpdateService.Instance.RunAsync` 之前注册。编辑器加密服务可以放在 Editor 代码里，用 `[InitializeOnLoadMethod]` 注册。构建热更包和运行时加载必须使用同一套算法和参数，否则 AssetBundle 会加载失败。

如果自定义算法只是给 AssetBundle 文件头部增加固定偏移，解密服务里可以直接使用 `AssetBundle.LoadFromFile(fileInfo.FileLoadPath, fileInfo.FileLoadCRC, offset)`，运行时会走更省内存的文件偏移加载。AES、异或、压缩包头等需要还原完整字节的算法，可以在解密服务里读取文件、解密后再从 `AssetBundle.LoadFromMemory` 或 `AssetBundle.LoadFromMemoryAsync` 加载。

## 运行时接入

建议在 BootScene 里完成隐私协议、基础 SDK、网络检查、强更检查之后，再手动调用：

```csharp
HotUpdateConfig config = HotUpdateConfig.LoadDefault();
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
await HotUpdateService.Instance.RunAsync(config, progress, context, cancellationToken);
```

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

`HotUpdateConfig.asset` 里的程序集目录配置：

- `hotUpdateAssemblyAssetDirectory`：热更 DLL 的目标目录，默认是 `Assets/HotUpdateAssets/Assemblies`
- `aotMetadataAssetDirectory`：AOT 元数据 DLL 的目标目录，默认是 `Assets/HotUpdateAssets/Assemblies/AOT`

目录需要位于 `Assets` 下。程序集列表可以填写 `HotUpdate`、`HotUpdate.dll` 或 `HotUpdate.dll.bytes`，框架会转换为 YooAsset 使用的 `.dll.bytes` 资源路径。调整目录或程序集列表后，执行 `Hot Update/Prepare HotUpdate Process` 和 `Hot Update/Build YooAsset Package`。

YooAsset Collector 默认配置：

- `DefaultPackage` 收集 `Assets/HotUpdateAssets`
- DLL 和 AOT 文件作为普通 `TextAsset` 打进 AssetBundle，并在运行时读取 `bytes`
- 资源定位使用完整资源路径，例如 `Assets/HotUpdateAssets/Assemblies/HotUpdate.dll.bytes`

`ProjectSettings/HybridCLRSettings.asset` 配置内容：

- 热更程序集：`HotUpdate`
- AOT 元数据程序集：执行 `Hot Update/Prepare All Process` 后，会从 `Assets/HybridCLRGenerate/AOTGenericReferences.cs` 自动同步到 `ProjectSettings/HybridCLRSettings.asset` 和 `HotUpdateConfig.asset`

## 编辑器流程

1. 如果项目尚未安装 HybridCLR，先执行 `HybridCLR/Installer...`。
2. 在 YooAsset Collector 里配置 `DefaultPackage`，并收集 `Assets/HotUpdateAssets`。
3. 首次出包、AOT 代码变化、切平台或 `Development Build` 开关变化时，执行 `Hot Update/Prepare All Process`。
4. 只修改热更代码时，执行 `Hot Update/Prepare HotUpdate Process`。
5. 如果首包需要内置一份热更资源，勾选 `HotUpdateConfig.asset` 里的 `useBuildinFileSystemInHostMode`。
6. 执行 `Hot Update/Build YooAsset Package`，构建单个热更新包。
7. 开启内置文件时，重新构建 App 包，让 `Assets/StreamingAssets/DefaultPackage` 进入首包。
8. 将生成的 YooAsset 包目录发布到 CDN 源站。

`useBuildinFileSystemInHostMode` 关闭时，`HostPlayMode` 只使用远端 CDN 和本地缓存；开启时，构建菜单会使用 `ClearAndCopyAll` 把本次 YooAsset 构建结果拷到 `Assets/StreamingAssets/DefaultPackage`，运行时会先启用 Buildin 文件系统，再配合 CDN 检查更新。

### 步骤操作

首次出包 / AOT 代码变化 / 切平台 / `Development Build` 开关变化：

```text
Hot Update/Prepare All Process
Hot Update/Build YooAsset Package
Build Player
```

`Prepare All Process` 会执行 HybridCLR `Generate All`，同步 AOT 元数据程序集列表，并把热更 DLL 和 AOT 元数据 DLL 复制到配置的资源目录。

修改AOT启动流程 / CDN 地址 / 播放模式：

```text
Build Player
```

改了 `useBuildinFileSystemInHostMode` / 加解密服务代码：

```text
Hot Update/Build YooAsset Package
Build Player
```

改了 `HotUpdate` 热更代码：

```text
Hot Update/Prepare HotUpdate Process
Hot Update/Build YooAsset Package
```

`Prepare HotUpdate Process` 只编译热更 DLL，并复制热更 DLL 和已有的 AOT 元数据 DLL。它不会重新生成 AOT 元数据列表。

如果热更代码新增了需要 AOT 补充元数据支持的泛型调用，执行 `Hot Update/Prepare All Process` 后再继续后续步骤。

改了 `Assets/HotUpdateAssets/Res` 下的热更资源：

```text
Hot Update/Build YooAsset Package
```


## CDN 远程目录

默认 URL 模板是：

```text
{Root}/{Platform}/{PackageName}/{FileName}
```

`HotUpdateConfig.asset` 使用 `remoteRoots` 列表配置远端根地址。列表从上往下表示优先级：

- 第一个非空地址：YooAsset main URL
- 第二个非空地址：YooAsset fallback URL
- 第三个及之后：作为备用地址记录，运行时不会被 YooAsset 自动逐个尝试，需要使用时把它拖到列表前面

可以把本地、测试、正式地址都放在列表里，通过调整顺序决定当前包访问哪条 CDN：

```text
http://127.0.0.1:8080
https://cdn.example.com/hotupdate/dev
https://cdn.example.com/hotupdate/release
```

如果 `remoteRoots` 的第一项设置为：

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

此时可以把本地地址放到 `HotUpdateConfig.asset` 的 `remoteRoots` 第一位：

```text
http://127.0.0.1:8080
```

发布到真实源站时，调整配置里的 `CdnRootDirectory`，例如：

```ini
CdnRootDirectory=D:/CdnOrigin/hotupdate
```

然后把公网地址放到 `remoteRoots` 第一位，例如：

```text
https://cdn.example.com/hotupdate/dev
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
PublishLocalFirst=true
PublishConfigPath=Tools/local_cdn_server.env
PublicRoot=https://pub-xxxx.r2.dev
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
```[local_cdn_server.env](Tools/local_cdn_server.env)

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
remoteRoots[0] = https://pub-xxxx.r2.dev/dev
remoteRoots[1] = https://pub-xxxx.r2.dev/release
```

如果发布正式环境，运行脚本时选择 `release` 或传入 `--prefix release`，远端文件路径为 `release/Android/DefaultPackage/...`。要让当前包访问正式环境，把正式地址放到 `remoteRoots` 第一位：

```text
remoteRoots[0] = https://pub-xxxx.r2.dev/release
remoteRoots[1] = https://pub-xxxx.r2.dev/dev
```

## 注意事项

- 真机联机更新建议使用 `HostPlayMode`，并确保源站目录结构和 URL 模板一致。
- 项目按强联网流程处理热更。没有网络或源站不可用时，版本和清单请求会按 `manifestTimeout` 超时失败，启动层应显示重试、退出或检查网络，不走离线缓存进游戏。
- `useBuildinFileSystemInHostMode` 只有在重新执行 `Hot Update/Build YooAsset Package` 并重新打 App 包后才对真机首包生效；只在运行时勾选但没有生成 `StreamingAssets/DefaultPackage`，会导致内置 catalog 或清单缺失。
- 资源加密由 `HotUpdateCryptoProvider` 注册 YooAsset `IEncryptionServices` 和 `IDecryptionServices` 决定。
- Editor 模拟模式依赖 `AssetBundleCollectorSetting.asset` 里存在 `DefaultPackage`。
