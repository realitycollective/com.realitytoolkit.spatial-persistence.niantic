// Copyright (c) Reality Collective. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NUnit.Framework;

namespace RealityToolkit.SpatialPersistence.Niantic.Editor.Tests
{
    /// <summary>
    /// Checks of the <see cref="SpatialPersistenceTrackingType.DeviceMap"/> tracking type this module adds to the spatial persistence service.
    /// </summary>
    public class DeviceMapSearchArgsTests
    {
        [Test]
        public void DeviceMapSearch_NeedsTheMapIdAsItsAnchorId()
        {
            Assert.IsTrue(new SpatialPersistenceSearchArgs(SpatialPersistenceTrackingType.DeviceMap, "office").IsValid);
            Assert.IsFalse(new SpatialPersistenceSearchArgs(SpatialPersistenceTrackingType.DeviceMap, string.Empty).IsValid);
            Assert.IsFalse(new SpatialPersistenceSearchArgs(SpatialPersistenceTrackingType.DeviceMap, null).IsValid);
        }

        [Test]
        public void DeviceMap_IsDistinctFromTheOtherTrackingTypes()
        {
            Assert.AreEqual(3, (int)SpatialPersistenceTrackingType.DeviceMap, "Serialized profiles and search data store the number");
            Assert.AreNotEqual(SpatialPersistenceTrackingType.CloudAnchor, SpatialPersistenceTrackingType.DeviceMap);
            Assert.AreNotEqual(SpatialPersistenceTrackingType.ImageTracking, SpatialPersistenceTrackingType.DeviceMap);
        }
    }
}
