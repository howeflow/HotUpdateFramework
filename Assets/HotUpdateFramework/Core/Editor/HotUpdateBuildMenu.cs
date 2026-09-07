using UnityEditor;

namespace HotUpdateFramework.Editor
{
    public static class HotUpdateBuildMenu
    {
        public const string MenuRoot = "Hot Update/";

        [MenuItem(MenuRoot + "Create HotUpdate Config", priority = 1)]
        public static HotUpdateConfig CreateDefaultConfigAsset()
        {
            return HotUpdateEditorUtility.CreateDefaultConfigAsset();
        }

        [MenuItem(MenuRoot + "Build Package", priority = 201)]
        public static void BuildYooAssetPackage()
        {
            HotUpdateBuildPipeline.BuildPackage(EditorUserBuildSettings.activeBuildTarget);
        }

        [MenuItem(MenuRoot + "Clear/Build Cache", priority = 202)]
        public static void ClearYooAssetBuildCache()
        {
            HotUpdateBuildPipeline.ClearBuildCache();
        }

        [MenuItem(MenuRoot + "Clear/Runtime Cache", priority = 203)]
        public static void ClearYooAssetEditorRuntimeCache()
        {
            HotUpdateBuildPipeline.ClearEditorRuntimeCache();
        }
    }
}
