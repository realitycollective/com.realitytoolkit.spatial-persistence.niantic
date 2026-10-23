// Copyright (c) Reality Collective. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;

namespace RealityToolkit.SpatialPersistence.Niantic.Definitions
{
    /// <summary>
    /// What a finished scan produced: the stored map's size and how much of the space the scan covered.
    /// </summary>
    /// <remarks>Raised once for each map the module stores. Useful to learn how large maps get before choosing where to keep them.</remarks>
    [Serializable]
    public class DeviceMapScanReport
    {
        /// <summary>
        /// The ID the module gave the stored map.
        /// </summary>
        public string mapId;

        /// <summary>
        /// Size of the stored map, in bytes.
        /// </summary>
        public long mapBytes;

        /// <summary>
        /// How long the scan ran, in seconds.
        /// </summary>
        public float scanSeconds;

        /// <summary>
        /// Distance the device moved while scanning, in metres.
        /// </summary>
        public float pathMetres;

        /// <summary>
        /// AR mesh area added while scanning, in square metres. 0 without an ARMeshManager.
        /// </summary>
        public float newMeshAreaSquareMetres;

        /// <summary>
        /// Feature points in the map updates received while scanning.
        /// </summary>
        public int mapPoints;

        /// <summary>
        /// Map updates received while scanning.
        /// </summary>
        public int mapUpdates;

        /// <summary>
        /// How complete the scan was when it finished, from 0 to 1. Below 1 when it was finished early.
        /// </summary>
        public float coverage;

        /// <summary>
        /// When the map was stored, as Unix seconds.
        /// </summary>
        public long storedUnixSeconds;

        /// <inheritdoc />
        public override string ToString()
            => $"map {mapId}: {mapBytes / 1024f:0} KB, {scanSeconds:0} s, {pathMetres:0.0} m walked, {newMeshAreaSquareMetres:0.0} m2 new mesh, {mapPoints} points in {mapUpdates} updates, coverage {coverage:P0}";
    }
}
