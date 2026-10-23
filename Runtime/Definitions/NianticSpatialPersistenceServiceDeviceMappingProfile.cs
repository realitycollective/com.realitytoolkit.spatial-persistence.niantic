// Copyright (c) Reality Collective. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using RealityCollective.ServiceFramework.Definitions;
using RealityToolkit.SpatialPersistence.Interfaces;
using UnityEngine;

namespace RealityToolkit.SpatialPersistence.Niantic.Definitions
{
    /// <summary>
    /// Configuration profile for the <see cref="NianticSpatialPersistenceServiceDeviceMappingModule"/>.
    /// </summary>
    public class NianticSpatialPersistenceServiceDeviceMappingProfile : BaseServiceProfile<ISpatialPersistenceServiceModule>
    {
        [Header("Mapping")]
        [Tooltip("Distance walked, in metres, before the scan starts a new map node.")]
        [SerializeField]
        private float splitterMaxDistanceMeters = 10f;

        [Tooltip("Time, in seconds, before the scan starts a new map node.")]
        [SerializeField]
        private float splitterMaxDurationSeconds = 1000f;

        [Header("Localisation")]
        [Tooltip("Localisation attempts per second against the loaded device map. Device maps only: the Niantic cloud is never asked.")]
        [SerializeField]
        private int localisationFramerate = 10;

        [Tooltip("Seconds to wait for localisation before reporting AnchorLocatedError. The module keeps trying afterwards.")]
        [SerializeField]
        private float localisationTimeoutSeconds = 30f;

        [Header("Storage")]
        [Tooltip("File name of a stored map in Application.persistentDataPath, where {0} is the map ID the module gives it. No folders.")]
        [SerializeField]
        private string mapFileNameFormat = "{0}_SerializedDeviceMapData";

        [Tooltip("File name of a map's anchor index (anchor IDs and their poses relative to the map's root anchor), where {0} is the map ID. No folders.")]
        [SerializeField]
        private string anchorIndexFileNameFormat = "{0}_DeviceMapAnchors.json";

        [Header("Scan coverage")]
        [Tooltip("Seconds of scanning that count as complete.")]
        [SerializeField]
        private float minScanSeconds = 15f;

        [Tooltip("Metres walked that count as complete.")]
        [SerializeField]
        private float minPathMetres = 3f;

        [Tooltip("Square metres of new AR mesh that count as complete.")]
        [SerializeField]
        private float minNewMeshAreaSquareMetres = 3f;

        /// <summary>
        /// Distance walked, in metres, before the scan starts a new map node.
        /// </summary>
        public float SplitterMaxDistanceMeters => splitterMaxDistanceMeters;

        /// <summary>
        /// Time, in seconds, before the scan starts a new map node.
        /// </summary>
        public float SplitterMaxDurationSeconds => splitterMaxDurationSeconds;

        /// <summary>
        /// Localisation attempts per second against the loaded device map.
        /// </summary>
        public int LocalisationFramerate => localisationFramerate;

        /// <summary>
        /// Seconds to wait for localisation before reporting an error.
        /// </summary>
        public float LocalisationTimeoutSeconds => localisationTimeoutSeconds;

        /// <summary>
        /// File name of a stored map, where {0} is the map ID.
        /// </summary>
        public string MapFileNameFormat => string.IsNullOrEmpty(mapFileNameFormat) || !mapFileNameFormat.Contains("{0}") ? "{0}_SerializedDeviceMapData" : mapFileNameFormat;

        /// <summary>
        /// File name of a map's anchor index, where {0} is the map ID.
        /// </summary>
        public string AnchorIndexFileNameFormat => string.IsNullOrEmpty(anchorIndexFileNameFormat) || !anchorIndexFileNameFormat.Contains("{0}") ? "{0}_DeviceMapAnchors.json" : anchorIndexFileNameFormat;

        /// <summary>
        /// Seconds of scanning that count as complete.
        /// </summary>
        public float MinScanSeconds => minScanSeconds;

        /// <summary>
        /// Metres walked that count as complete.
        /// </summary>
        public float MinPathMetres => minPathMetres;

        /// <summary>
        /// Square metres of new AR mesh that count as complete.
        /// </summary>
        public float MinNewMeshAreaSquareMetres => minNewMeshAreaSquareMetres;
    }
}
