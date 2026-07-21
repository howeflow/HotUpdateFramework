using System;

namespace HotUpdateFramework
{
    public sealed class HotUpdateException : Exception
    {
        public HotUpdateException(string message) : base(message)
        {
        }

        public HotUpdateException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
