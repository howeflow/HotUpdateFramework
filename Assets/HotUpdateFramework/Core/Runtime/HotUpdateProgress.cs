namespace HotUpdateFramework
{
    public readonly struct HotUpdateProgress
    {
        public readonly HotUpdateStage Stage;
        public readonly string Message;
        public readonly float Progress;
        public readonly int CurrentDownloadCount;
        public readonly int TotalDownloadCount;
        public readonly long CurrentDownloadBytes;
        public readonly long TotalDownloadBytes;

        public HotUpdateProgress(HotUpdateStage stage, string message, float progress = 0f, int currentDownloadCount = 0, int totalDownloadCount = 0, long currentDownloadBytes = 0L, long totalDownloadBytes = 0L)
        {
            Stage = stage;
            Message = message ?? string.Empty;
            Progress = progress;
            CurrentDownloadCount = currentDownloadCount;
            TotalDownloadCount = totalDownloadCount;
            CurrentDownloadBytes = currentDownloadBytes;
            TotalDownloadBytes = totalDownloadBytes;
        }
    }
}
