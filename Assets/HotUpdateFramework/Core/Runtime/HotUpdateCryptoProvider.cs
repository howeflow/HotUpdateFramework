using System;
using YooAsset;

namespace HotUpdateFramework
{
    public static class HotUpdateCryptoProvider
    {
        public static IEncryptionServices EncryptionServices { get; private set; }
        public static IDecryptionServices DecryptionServices { get; private set; }

        public static void SetServices(IEncryptionServices encryptionServices, IDecryptionServices decryptionServices)
        {
            SetEncryptionServices(encryptionServices);
            SetDecryptionServices(decryptionServices);
        }

        public static void SetEncryptionServices(IEncryptionServices encryptionServices)
        {
            EncryptionServices = encryptionServices ?? throw new ArgumentNullException(nameof(encryptionServices));
        }

        public static void SetDecryptionServices(IDecryptionServices decryptionServices)
        {
            DecryptionServices = decryptionServices ?? throw new ArgumentNullException(nameof(decryptionServices));
        }

        public static void Reset()
        {
            EncryptionServices = null;
            DecryptionServices = null;
        }
    }
}
