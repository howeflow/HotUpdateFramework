namespace HotUpdateFramework
{
    public enum HotUpdateStage
    {
        None,
        InitializeYooAsset,
        RequestPackageVersion,
        UpdateManifest,
        DownloadFiles,
        LoadAotMetadata,
        LoadHotUpdateAssemblies,
        InvokeEntry,
        Completed,
        Failed
    }
}
