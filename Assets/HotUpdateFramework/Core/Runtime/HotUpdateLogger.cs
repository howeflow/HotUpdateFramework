using UnityEngine;

namespace HotUpdateFramework
{
    public interface IHotUpdateLogger
    {
        void Log(string message);
        void Warning(string message);
        void Error(string message);
    }

    public class EmptyHotUpdateLogger : IHotUpdateLogger
    {
        public void Log(string message)
        {
        }

        public void Warning(string message)
        {
        }

        public void Error(string message)
        {
        }
    }


    public class DefaultUpdateLogger:IHotUpdateLogger
    {
        private const string Prefix = "[HotUpdate]";
        
        public void Log(string message)
        {
            Debug.Log(Format(message));
        }
        public void Warning(string message)
        {
            Debug.LogWarning(Format(message));
        }
        public void Error(string message)
        {
            Debug.LogError(Format(message));
        }
        private string Format(string message)
        {
            return $"{Prefix} {message}";
        }
    }
    
    public static class HotUpdateLogger
    {
        public static IHotUpdateLogger Logger { get; set; } = new DefaultUpdateLogger();

        public static void Log(string message)
        {
            Logger?.Log(message);
        }

        public static void Warning(string message)
        {
            Logger?.Warning(message);
        }

        public static void Error(string message)
        {
            Logger?.Error(message);
        }
    }
}
