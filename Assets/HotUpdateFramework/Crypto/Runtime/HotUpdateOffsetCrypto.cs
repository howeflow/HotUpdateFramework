using System;

namespace HotUpdateFramework.Crypto
{
    public static class HotUpdateOffsetCrypto
    {
        public const int DefaultOffset = 32;

        public static int NormalizeOffset(int offset)
        {
            return offset <= 0 ? DefaultOffset : offset;
        }

        public static byte[] AddOffset(byte[] data, int offset)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            int normalizedOffset = NormalizeOffset(offset);
            byte[] result = new byte[data.Length + normalizedOffset];
            Buffer.BlockCopy(data, 0, result, normalizedOffset, data.Length);
            return result;
        }

        public static byte[] RemoveOffset(byte[] data, int offset)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            int normalizedOffset = NormalizeOffset(offset);
            if (data.Length <= normalizedOffset)
                return Array.Empty<byte>();

            byte[] result = new byte[data.Length - normalizedOffset];
            Buffer.BlockCopy(data, normalizedOffset, result, 0, result.Length);
            return result;
        }
    }
}
