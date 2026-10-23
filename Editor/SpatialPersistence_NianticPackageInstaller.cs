// Copyright (c) Reality Collective. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using RealityCollective.ServiceFramework.Editor;
using RealityCollective.ServiceFramework.Editor.Packages;
using RealityCollective.Utilities.Editor;
using RealityCollective.Utilities.Extensions;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RealityToolkit.SpatialPersistence.Niantic.Editor
{
    /// <summary>
    /// Copies the package's default profiles (Assets~) into the project the first time the package loads.
    /// </summary>
    [InitializeOnLoad]
    internal static class SpatialPersistence_NianticPackageInstaller
    {
        private static readonly string destinationPath = Path.Combine(Application.dataPath, "SpatialPersistence_Niantic");
        private static readonly string sourcePath = Path.GetFullPath($"{PathFinderUtility.ResolvePath<IPathFinder>(typeof(SpatialPersistence_NianticPackagePathFinder)).ForwardSlashes()}{Path.DirectorySeparatorChar}{"Assets~"}");

        static SpatialPersistence_NianticPackageInstaller()
        {
            EditorApplication.delayCall += CheckPackage;
        }

        [MenuItem("Tools/Reality Toolkit/Packages/Install SpatialPersistence_Niantic Package Assets...", true)]
        private static bool ImportPackageAssetsValidation()
        {
            return !Directory.Exists($"{destinationPath}{Path.DirectorySeparatorChar}");
        }

        [MenuItem("Tools/Reality Toolkit/Packages/Install SpatialPersistence_Niantic Package Assets...")]
        private static void ImportPackageAssets()
        {
            EditorPreferences.Set($"{nameof(SpatialPersistence_NianticPackageInstaller)}.Assets", false);
            EditorApplication.delayCall += CheckPackage;
        }

        private static void CheckPackage()
        {
            if (!EditorPreferences.Get($"{nameof(SpatialPersistence_NianticPackageInstaller)}.Assets", false))
            {
                EditorPreferences.Set($"{nameof(SpatialPersistence_NianticPackageInstaller)}.Assets", AssetsInstaller.TryInstallAssets(sourcePath, destinationPath));
            }
        }
    }
}
