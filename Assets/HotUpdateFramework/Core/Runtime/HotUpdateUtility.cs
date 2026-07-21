using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HotUpdateFramework
{
    public static class HotUpdateUtility
    {
        public static string GetPlatformName(string overrideName)
        {
            if (string.IsNullOrWhiteSpace(overrideName) == false)
                return overrideName.Trim();

#if UNITY_EDITOR
            return EditorUserBuildSettings.activeBuildTarget.ToString();
#else
            switch (Application.platform)
            {
                case RuntimePlatform.Android:
                    return "Android";
                case RuntimePlatform.IPhonePlayer:
                    return "iOS";
                case RuntimePlatform.WebGLPlayer:
                    return "WebGL";
                case RuntimePlatform.OSXPlayer:
                    return "StandaloneOSX";
                case RuntimePlatform.LinuxPlayer:
                    return "StandaloneLinux64";
                case RuntimePlatform.WindowsPlayer:
                    return "StandaloneWindows64";
                default:
                    return Application.platform.ToString();
            }
#endif
        }
        
        public static string FormatBytes(long bytes)
        {
            const double unit = 1024d;
            if (bytes < unit)
                return $"{bytes} B";

            double value = bytes;
            string[] units = { "KB", "MB", "GB" };
            for (int i = 0; i < units.Length; i++)
            {
                value /= unit;
                if (value < unit || i == units.Length - 1)
                    return $"{value:0.##} {units[i]}";
            }

            return $"{bytes} B";
        }

        public static string NormalizePackageName(string packageName, string fallback)
        {
            return string.IsNullOrWhiteSpace(packageName) ? fallback : packageName.Trim();
        }

        public static string NormalizeAssetDirectory(string assetDirectory, string fallback)
        {
            string value = string.IsNullOrWhiteSpace(assetDirectory) ? fallback : assetDirectory.Trim();
            return value.Replace('\\', '/').TrimEnd('/');
        }

        public static string NormalizeAssemblyAssetFileName(string assemblyName)
        {
            string fileName = assemblyName.Trim().Replace('\\', '/');
            if (fileName.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                return fileName;

            if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) == false)
                fileName += ".dll";

            return $"{fileName}.bytes";
        }

        public static string[] BuildAssemblyAssetLocations(IReadOnlyList<string> assemblyNames, string assetDirectory)
        {
            if (assemblyNames == null || assemblyNames.Count == 0)
                return Array.Empty<string>();

            var locations = new List<string>(assemblyNames.Count);
            foreach (string assemblyName in assemblyNames)
            {
                if (string.IsNullOrWhiteSpace(assemblyName))
                    continue;

                string fileName = NormalizeAssemblyAssetFileName(assemblyName);
                if (fileName.StartsWith("Assets/", StringComparison.Ordinal))
                    locations.Add(fileName);
                else
                    locations.Add($"{assetDirectory}/{fileName}");
            }

            return locations.ToArray();
        }

        public static string GetAssemblyNameFromLocation(string location)
        {
            string fileName = Path.GetFileName(location);
            if (fileName.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - ".bytes".Length);
            if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - ".dll".Length);
            return fileName;
        }

        public static string GetDllFileNameFromAssetLocation(string assetLocation)
        {
            string fileName = Path.GetFileName(assetLocation);
            if (fileName.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - ".bytes".Length);
            return fileName;
        }

        public static string NormalizeDllAssemblyName(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
                return string.Empty;

            string fileName = Path.GetFileName(assemblyName.Trim().Replace('\\', '/'));
            if (fileName.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - ".bytes".Length);

            if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) == false)
                fileName += ".dll";

            return fileName;
        }

        public static string RemoveDllExtension(string assemblyName)
        {
            if (assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return assemblyName.Substring(0, assemblyName.Length - ".dll".Length);

            return assemblyName;
        }

        public static string GetRemoteRootByPriority(IReadOnlyList<string> remoteRoots, int priorityIndex)
        {
            if (remoteRoots == null || priorityIndex < 0)
                return string.Empty;

            int validIndex = 0;
            foreach (string remoteRoot in remoteRoots)
            {
                if (string.IsNullOrWhiteSpace(remoteRoot))
                    continue;

                if (validIndex == priorityIndex)
                    return remoteRoot.Trim();

                validIndex++;
            }

            return string.Empty;
        }

        public static string RemoveDuplicateSlashesAfterScheme(string url)
        {
            int schemeIndex = url.IndexOf("://", StringComparison.Ordinal);
            if (schemeIndex < 0)
                return url.Replace("//", "/");

            string scheme = url.Substring(0, schemeIndex + 3);
            string rest = url.Substring(schemeIndex + 3);
            while (rest.Contains("//"))
                rest = rest.Replace("//", "/");
            return scheme + rest;
        }
    }
}
