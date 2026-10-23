// Copyright (c) Reality Collective. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;

namespace RealityToolkit.SpatialPersistence.Niantic.Definitions
{
    /// <summary>
    /// The format of device map anchor IDs: the ID of the map the anchor belongs to, a separator, and the anchor's own key.
    /// </summary>
    /// <remarks>
    /// Because the anchor ID names its map, a search with only the anchor ID knows which map to load, even on a device that has not
    /// seen the anchor before (once maps can be fetched from elsewhere). The ID stays the same wherever the map is stored.
    /// </remarks>
    public static class DeviceMapAnchorId
    {
        /// <summary>
        /// Separates the map ID from the anchor key.
        /// </summary>
        public const char Separator = '/';

        /// <summary>
        /// Builds an anchor ID from its map ID and its key.
        /// </summary>
        /// <param name="mapId">The ID the module gave the map.</param>
        /// <param name="anchorKey">The anchor's own key, unique within the map.</param>
        public static string Compose(string mapId, string anchorKey)
        {
            if (string.IsNullOrEmpty(mapId) || mapId.IndexOf(Separator) >= 0)
            {
                throw new ArgumentException($"A map ID must not be empty or contain '{Separator}'.", nameof(mapId));
            }

            if (string.IsNullOrEmpty(anchorKey))
            {
                throw new ArgumentException("An anchor key must not be empty.", nameof(anchorKey));
            }

            return $"{mapId}{Separator}{anchorKey}";
        }

        /// <summary>
        /// Reads the map ID from an anchor ID.
        /// </summary>
        /// <param name="anchorId">An anchor ID made by <see cref="Compose"/>.</param>
        /// <param name="mapId">The map the anchor belongs to, or null.</param>
        /// <returns>False if the ID is not in this format.</returns>
        public static bool TryGetMapId(string anchorId, out string mapId)
        {
            mapId = null;
            if (string.IsNullOrEmpty(anchorId))
            {
                return false;
            }

            var split = anchorId.IndexOf(Separator);
            if (split <= 0 || split == anchorId.Length - 1)
            {
                return false;
            }

            mapId = anchorId.Substring(0, split);
            return true;
        }
    }
}
