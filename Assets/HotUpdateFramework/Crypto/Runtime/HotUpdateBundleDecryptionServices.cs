using System.IO;
using System.Text;
using UnityEngine;
using YooAsset;

namespace HotUpdateFramework.Crypto
{
    public sealed class HotUpdateBundleDecryptionServices : IDecryptionServices
    {
        private readonly int _offset = HotUpdateOffsetCrypto.DefaultOffset;

        DecryptResult IDecryptionServices.LoadAssetBundle(DecryptFileInfo fileInfo)
        {
            return new DecryptResult
            {
                Result = AssetBundle.LoadFromFile(fileInfo.FileLoadPath, fileInfo.FileLoadCRC, (ulong)_offset)
            };
        }

        DecryptResult IDecryptionServices.LoadAssetBundleAsync(DecryptFileInfo fileInfo)
        {
            return new DecryptResult
            {
                CreateRequest = AssetBundle.LoadFromFileAsync(fileInfo.FileLoadPath, fileInfo.FileLoadCRC, (ulong)_offset)
            };
        }

        DecryptResult IDecryptionServices.LoadAssetBundleFallback(DecryptFileInfo fileInfo)
        {
            byte[] bytes = ReadFileData(fileInfo.FileLoadPath);
            return new DecryptResult
            {
                Result = AssetBundle.LoadFromMemory(bytes, fileInfo.FileLoadCRC)
            };
        }

        byte[] IDecryptionServices.ReadFileData(DecryptFileInfo fileInfo)
        {
            return ReadFileData(fileInfo.FileLoadPath);
        }

        string IDecryptionServices.ReadFileText(DecryptFileInfo fileInfo)
        {
            return Encoding.UTF8.GetString(ReadFileData(fileInfo.FileLoadPath));
        }

        private byte[] ReadFileData(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            byte[] fileData = HotUpdateOffsetCrypto.RemoveOffset(bytes, _offset);
            if (fileData.Length == 0)
                throw new InvalidDataException($"Encrypted bundle file is smaller than offset: {filePath}, offset: {_offset}");

            return fileData;
        }
    }
}
