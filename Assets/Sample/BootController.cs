using Cysharp.Threading.Tasks;
using HotUpdateFramework;
using UnityEngine;
using UnityEngine.UI;

public class BootController : MonoBehaviour
{
    [SerializeField] private GameObject loadingView;
    [SerializeField] private Slider slider;

    private float _loadingProgress;

    private void Start()
    {
        HotUpdateOffsetCrypto.Register();

        var config = HotUpdateConfig.LoadDefault();
        var progress = Progress.Create<HotUpdateProgress>(OnHotUpdateProgress);
        var context = new HotUpdateContext
        {
            OnComplete = OnHotUpdateComplete,
            OnProgress = OnHotUpdateContextProgress,
            UserData = this
        };

        HotUpdateService.Instance.RunAsync(config, progress, context, this.GetCancellationTokenOnDestroy()).Forget();
    }

    private void OnHotUpdateProgress(HotUpdateProgress value)
    {
        MapHotUpdateProgress(value);
    }

    private void OnHotUpdateContextProgress(float progress, string message)
    {
        SetLoadingProgress(Mathf.Lerp(0.95f, 1f, Mathf.Clamp01(progress)));
        Debug.Log($"[Boot] Hot update context progress {progress:P0} {message}");
    }

    private void OnHotUpdateComplete()
    {
        if (loadingView != null)
            loadingView.SetActive(false);

        if (slider != null)
            slider.value = 1f;

        Debug.Log("[Boot] Hot update entry completed.");
    }

    private float MapHotUpdateProgress(HotUpdateProgress value)
    {
        float mappedProgress;
        switch (value.Stage)
        {
            case HotUpdateStage.InitializeYooAsset:
                mappedProgress = Mathf.Lerp(0f, 0.1f, value.Progress);
                break;
            case HotUpdateStage.RequestPackageVersion:
                mappedProgress = Mathf.Lerp(0.1f, 0.15f, value.Progress);
                break;
            case HotUpdateStage.UpdateManifest:
                mappedProgress = Mathf.Lerp(0.15f, 0.2f, value.Progress);
                break;
            case HotUpdateStage.DownloadFiles:
                mappedProgress = Mathf.Lerp(0.2f, 0.75f, value.Progress);
                break;
            case HotUpdateStage.LoadAotMetadata:
                mappedProgress = Mathf.Lerp(0.75f, 0.85f, value.Progress);
                break;
            case HotUpdateStage.LoadHotUpdateAssemblies:
                mappedProgress = Mathf.Lerp(0.85f, 0.9f, value.Progress);
                break;
            case HotUpdateStage.InvokeEntry:
                mappedProgress = Mathf.Lerp(0.9f, 0.95f, value.Progress);
                break;
            case HotUpdateStage.Completed:
                mappedProgress = 1f;
                break;
            default:
                mappedProgress = value.Progress;
                break;
        }

        return SetLoadingProgress(mappedProgress);
    }

    private float SetLoadingProgress(float progress)
    {
        _loadingProgress = Mathf.Max(_loadingProgress, Mathf.Clamp01(progress));
        if (slider != null)
            slider.value = _loadingProgress;

        return _loadingProgress;
    }
}
