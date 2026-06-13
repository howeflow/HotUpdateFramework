using UnityEngine;

namespace HotUpdateFramework
{
    public static class HotUpdateLogger
    {
        private const string Prefix = "[HotUpdate]";

        public static bool EnableLog { get; set; } = true;

        public static void Log(string message)
        {
            if (EnableLog)
                LogAlways(message);
        }

        public static void LogAlways(string message)
        {
            Debug.Log(Format(message));
        }

        public static void Warning(string message)
        {
            Debug.LogWarning(Format(message));
        }

        public static void Error(string message)
        {
            Debug.LogError(Format(message));
        }
        
        private static string Format(string message)
        {
            return $"{Prefix} {message}";
        }
    }
}
