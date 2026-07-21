using System;
using UnityEngine;

namespace HotUpdateFramework.Code
{
    public sealed class HotUpdateContext
    {
        private bool _completed;

        public Action OnComplete { get; set; }
        public Action<float, string> OnProgress { get; set; }
        public object UserData { get; set; }

        public void ReportProgress(float progress, string message = null)
        {
            OnProgress?.Invoke(Mathf.Clamp01(progress), message ?? string.Empty);
        }

        public void Complete()
        {
            if (_completed)
                return;

            _completed = true;
            OnComplete?.Invoke();
        }
    }
}
