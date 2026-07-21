using HotUpdateFramework.Editor;
using UnityEditor;

namespace HotUpdateFramework.Code.Editor
{
    public static class HotUpdateCodeBuildMenu
    {
        private const string MenuRoot = HotUpdateBuildMenu.MenuRoot;
        
        [MenuItem(MenuRoot + "Create CodeUpdate Config", priority = 2)]
        public static HotUpdateCodeConfig CreateDefaultConfigAsset()
        {
            return HotUpdateCodeEditorUtility.CreateDefaultConfigAsset();
        }

        [MenuItem(MenuRoot + "Prepare All Process", priority = 101)]
        public static void PrepareAllProcess()
        {
            HotUpdateCodeBuildPipeline.PrepareAllProcess();
        }

        [MenuItem(MenuRoot + "Prepare HotUpdate Process", priority = 102)]
        public static void PrepareHotUpdateProcess()
        {
            HotUpdateCodeBuildPipeline.PrepareHotUpdateProcess();
        }
    }
}
