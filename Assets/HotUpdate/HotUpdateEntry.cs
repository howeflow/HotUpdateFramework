using Cysharp.Threading.Tasks;
using HotUpdateFramework;
using HotUpdateFramework.Code;
using UnityEngine;
using UnityEngine.UI;

namespace HotUpdate
{
    public static class HotUpdateEntry
    {
        public static async UniTask Start(HotUpdateContext context)
        {
            Debug.Log("[HotUpdate] HotUpdateEntry.Start invoked.");

            var handle = YooAsset.YooAssets.LoadAssetAsync<GameObject>("Assets/HotUpdateAssets/Res/GameObject.prefab");
            await handle.ToUniTask();

            var go = handle.InstantiateSync();
            var text = go.GetComponentInChildren<Text>();
            text.text = go.name;

            var image = go.GetComponentInChildren<Image>();
            image.color = Color.green;
            
            context?.Complete();
        }
    }
}
