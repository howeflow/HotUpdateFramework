using System.IO;
using YooAsset;

namespace HotUpdateFramework.Crypto.Editor
{
    public sealed class HotUpdateBundleEncryptionServices : IEncryptionServices
    {
        private readonly int _offset = HotUpdateOffsetCrypto.DefaultOffset;

        public EncryptResult Encrypt(EncryptFileInfo fileInfo)
        {
            byte[] bytes = File.ReadAllBytes(fileInfo.FileLoadPath);
            return new EncryptResult
            {
                Encrypted = true,
                EncryptedData = HotUpdateOffsetCrypto.AddOffset(bytes, _offset)
            };
        }
    }
}
