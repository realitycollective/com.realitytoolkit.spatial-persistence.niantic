// Copyright (c) Reality Collective. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace RealityToolkit.SpatialPersistence.Niantic.Definitions
{
    /// <summary>
    /// The anchors stored against one device map: their IDs and their poses relative to the map's root anchor.
    /// </summary>
    /// <remarks>Saved with <see cref="JsonUtility"/> beside the map file, so the package needs no JSON library.</remarks>
    [Serializable]
    public class DeviceMapAnchorIndex
    {
        /// <summary>
        /// The ID the module gave the map.
        /// </summary>
        public string mapId;

        /// <summary>
        /// The anchors placed against the map.
        /// </summary>
        public List<DeviceMapAnchor> anchors = new List<DeviceMapAnchor>();
    }

    /// <summary>
    /// One anchor placed against a device map.
    /// </summary>
    [Serializable]
    public class DeviceMapAnchor
    {
        /// <summary>
        /// The anchor ID, as reported by CreateAnchorSucceeded and searched for with TryFindAnchors.
        /// </summary>
        public string id;

        /// <summary>
        /// Position relative to the map's root anchor.
        /// </summary>
        public Vector3 position;

        /// <summary>
        /// Rotation relative to the map's root anchor.
        /// </summary>
        public Quaternion rotation = Quaternion.identity;

        /// <summary>
        /// When the anchor stops being valid, as Unix seconds, or 0 for never.
        /// </summary>
        public long expiresUnixSeconds;
    }
}
