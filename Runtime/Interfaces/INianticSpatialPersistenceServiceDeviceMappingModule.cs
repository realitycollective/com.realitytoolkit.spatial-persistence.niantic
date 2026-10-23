// Copyright (c) Reality Collective. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using RealityToolkit.SpatialPersistence.Interfaces;
using RealityToolkit.SpatialPersistence.Niantic.Definitions;
using System;
using UnityEngine;

namespace RealityToolkit.SpatialPersistence.Niantic
{
    /// <summary>
    /// The state a <see cref="SpatialPersistenceTrackingType.DeviceMap"/> module exposes beyond <see cref="ISpatialPersistenceServiceModule"/>:
    /// scanning progress and the map in use. Every operation goes through the spatial persistence interface.
    /// </summary>
    /// <remarks>
    /// Get it with <c>ISpatialPersistenceService.TryGetModulesByTrackingType(SpatialPersistenceTrackingType.DeviceMap, out var modules)</c>.
    /// Create anchors with <c>TryCreateAnchor(..., SpatialPersistenceTrackingType.DeviceMap)</c> (the first one scans the space into a map),
    /// find them with a <see cref="SpatialPersistenceTrackingType.DeviceMap"/> search (which loads their map), and move and delete them
    /// with the tracking type overloads.
    /// </remarks>
    public interface INianticSpatialPersistenceServiceDeviceMappingModule : ISpatialPersistenceServiceModule
    {
        /// <summary>
        /// True while the space is being scanned into a new map.
        /// </summary>
        bool IsScanning { get; }

        /// <summary>
        /// True once the device has localised against the current map.
        /// </summary>
        bool IsLocalised { get; }

        /// <summary>
        /// The current map's root anchor, or null. Anchors created or found are its children.
        /// </summary>
        GameObject RootAnchor { get; }

        /// <summary>
        /// How complete the current or last scan is. The scan finishes by itself when this is complete.
        /// </summary>
        ScanCoverage Coverage { get; }

        /// <summary>
        /// Raised for each map update while scanning, with the update's feature points relative to the map's root anchor.
        /// </summary>
        event Action<Vector3[]> MapPointsAdded;

        /// <summary>
        /// Raised once a scan's map is stored, with its size and the scan's coverage, before the device localises against it.
        /// </summary>
        event Action<DeviceMapScanReport> MapStored;

        /// <summary>
        /// True if an anchor with this ID is stored on the device. Anchor IDs name their map, see <see cref="DeviceMapAnchorId"/>.
        /// </summary>
        bool IsAnchorStored(string anchorId);

        /// <summary>
        /// The IDs of every anchor stored on the device.
        /// </summary>
        string[] GetStoredAnchorIds();
    }
}
