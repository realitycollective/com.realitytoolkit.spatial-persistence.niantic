// Copyright (c) Reality Collective. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NianticSpatial.NSDK.AR.Meshing;
using NianticSpatial.NSDK.AR.SceneSegmentation;
using RealityCollective.Utilities.Logging;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace RealityToolkit.SpatialPersistence.Niantic
{
    /// <summary>
    /// Turns Niantic meshing and scene segmentation on only while a flow needs them, so they do not run (and heat the device) all the time.
    /// </summary>
    /// <remarks>
    /// Ship the scene with the ARMeshManager, its NsdkMeshingExtension and any ARSceneSegmentationManager disabled.
    /// Ethar Explorer measured both running from launch in every mode before this gate (startup review P6, October 2026).
    /// A flow that needs them calls <see cref="Request"/> with its reason and <see cref="Release"/> when it ends; they stay on while any reason holds.
    /// The managers are looked up when the state changes, not every frame.
    /// </remarks>
    public static class NianticFeatureGate
    {
        /// <summary>
        /// The user is scanning an environment into a device map.
        /// </summary>
        public const string VPS_SCANNING = "vps-scanning";

        /// <summary>
        /// The app is showing content anchored to a device map or another VPS trackable.
        /// </summary>
        public const string VPS_DEPLOYMENT = "vps-deployment";

        private static readonly HashSet<string> reasons = new();

        /// <summary>
        /// True while at least one flow has the Niantic features on.
        /// </summary>
        public static bool IsEnabled => reasons.Count > 0;

        /// <summary>
        /// The flows holding the features on.
        /// </summary>
        public static IReadOnlyCollection<string> Reasons => reasons;

        /// <summary>
        /// Turns the features on for a flow. A repeated request for the same reason changes nothing.
        /// </summary>
        /// <param name="reason">Why the features are needed, for example <see cref="VPS_SCANNING"/>.</param>
        public static void Request(string reason)
        {
            if (string.IsNullOrEmpty(reason) || !reasons.Add(reason))
            {
                return;
            }

            StaticLogger.Log($"{nameof(NianticFeatureGate)} - Niantic features requested for {reason}");
            if (reasons.Count == 1)
            {
                Apply(true);
            }
        }

        /// <summary>
        /// Releases a flow's hold. The features turn off when no flow holds them.
        /// </summary>
        /// <param name="reason">The reason passed to <see cref="Request"/>.</param>
        public static void Release(string reason)
        {
            if (string.IsNullOrEmpty(reason) || !reasons.Remove(reason))
            {
                return;
            }

            StaticLogger.Log($"{nameof(NianticFeatureGate)} - Niantic features released by {reason}");
            if (reasons.Count == 0)
            {
                Apply(false);
            }
        }

        private static void Apply(bool enabled)
        {
            var meshExtensions = Object.FindObjectsByType<NsdkMeshingExtension>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var meshManagers = Object.FindObjectsByType<ARMeshManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var semanticsManagers = Object.FindObjectsByType<ARSceneSegmentationManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (meshManagers.Length == 0 && semanticsManagers.Length == 0)
            {
                StaticLogger.LogWarning($"{nameof(NianticFeatureGate)} - no ARMeshManager or ARSceneSegmentationManager found in the loaded scenes");
                return;
            }

            if (enabled)
            {
                // The meshing extension configures the NSDK meshing subsystem, so it is enabled before the mesh manager starts it.
                SetEnabled(meshExtensions, true);
                SetEnabled(semanticsManagers, true);
                SetEnabled(meshManagers, true);
            }
            else
            {
                SetEnabled(meshManagers, false);
                SetEnabled(semanticsManagers, false);
                SetEnabled(meshExtensions, false);
            }

            StaticLogger.Log($"{nameof(NianticFeatureGate)} - Niantic features {(enabled ? "on" : "off")}: {meshManagers.Length} mesh, {semanticsManagers.Length} semantics");
        }

        private static void SetEnabled(Behaviour[] behaviours, bool enabled)
        {
            foreach (var behaviour in behaviours)
            {
                if (behaviour != null)
                {
                    behaviour.enabled = enabled;
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() => reasons.Clear();
    }
}
