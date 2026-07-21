using System;
using System.Collections.Generic;
using HybridCLR;
using UnityEngine;

namespace HotUpdateFramework.Code
{
    [CreateAssetMenu(fileName = "HotUpdateCodeConfig", menuName = "Hot Update/Code Config")]
    public sealed class HotUpdateCodeConfig : ScriptableObject
    {
        public const string ResourcesPath = "HotUpdateCodeConfig";
        
        public const string DefaultHotUpdateAssemblyAssetDirectory = "Assets/HotUpdateAssets/Assemblies";
        public const string DefaultAotMetadataAssetDirectory = "Assets/HotUpdateAssets/Assemblies/AOT";

        [Header("HybridCLR")]
        [SerializeField] private HomologousImageMode homologousImageMode = HomologousImageMode.SuperSet;
        [SerializeField] private string aotMetadataAssetDirectory = DefaultAotMetadataAssetDirectory;
        [SerializeField] private string[] aotMetadataAssemblyNames =
        {
            "mscorlib.dll",
            "System.dll",
            "System.Core.dll",
            "UnityEngine.CoreModule.dll"
        };
        [SerializeField] private string hotUpdateAssemblyAssetDirectory = DefaultHotUpdateAssemblyAssetDirectory;
        [SerializeField] private string[] hotUpdateAssemblyNames =
        {
            "HotUpdate.dll"
        };

        [Header("Entry")]
        [SerializeField] private bool invokeHotUpdateEntry = true;
        [SerializeField] private string entryTypeName = "HotUpdate.HotUpdateEntry";
        [SerializeField] private string entryMethodName = "Start";

        public HomologousImageMode HomologousImageMode => homologousImageMode;
        public string AotMetadataAssetDirectory => HotUpdateUtility.NormalizeAssetDirectory(aotMetadataAssetDirectory, DefaultAotMetadataAssetDirectory);
        public IReadOnlyList<string> AotMetadataAssemblyNames => aotMetadataAssemblyNames ?? Array.Empty<string>();
        public IReadOnlyList<string> AotMetadataAssetLocations => HotUpdateUtility.BuildAssemblyAssetLocations(AotMetadataAssemblyNames, AotMetadataAssetDirectory);
        public string HotUpdateAssemblyAssetDirectory => HotUpdateUtility.NormalizeAssetDirectory(hotUpdateAssemblyAssetDirectory, DefaultHotUpdateAssemblyAssetDirectory);
        public IReadOnlyList<string> HotUpdateAssemblyNames => hotUpdateAssemblyNames ?? Array.Empty<string>();
        public IReadOnlyList<string> HotUpdateAssemblyAssetLocations => HotUpdateUtility.BuildAssemblyAssetLocations(HotUpdateAssemblyNames, HotUpdateAssemblyAssetDirectory);
        public bool InvokeHotUpdateEntry => invokeHotUpdateEntry;
        public string EntryTypeName => entryTypeName?.Trim() ?? string.Empty;
        public string EntryMethodName => entryMethodName?.Trim() ?? string.Empty;
        
        public static HotUpdateCodeConfig LoadDefault()
        {
            return Resources.Load<HotUpdateCodeConfig>(ResourcesPath);
        }
    }
}
