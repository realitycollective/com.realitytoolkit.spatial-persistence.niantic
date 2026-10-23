// Copyright (c) Reality Collective. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NUnit.Framework;
using RealityToolkit.SpatialPersistence.Niantic.Definitions;
using System;

namespace RealityToolkit.SpatialPersistence.Niantic.Editor.Tests
{
    /// <summary>
    /// Checks of <see cref="DeviceMapAnchorId"/>, the anchor ID format that names the anchor's map.
    /// </summary>
    public class DeviceMapAnchorIdTests
    {
        [Test]
        public void Compose_ThenTryGetMapId_GivesTheMapBack()
        {
            var id = DeviceMapAnchorId.Compose("3f2c", "a1b2");

            Assert.AreEqual("3f2c/a1b2", id);
            Assert.IsTrue(DeviceMapAnchorId.TryGetMapId(id, out var mapId));
            Assert.AreEqual("3f2c", mapId);
        }

        [Test]
        public void TryGetMapId_RefusesIdsInAnotherFormat()
        {
            Assert.IsFalse(DeviceMapAnchorId.TryGetMapId(null, out _));
            Assert.IsFalse(DeviceMapAnchorId.TryGetMapId(string.Empty, out _));
            Assert.IsFalse(DeviceMapAnchorId.TryGetMapId(Guid.NewGuid().ToString(), out _), "A plain GUID names no map");
            Assert.IsFalse(DeviceMapAnchorId.TryGetMapId("/a1b2", out _));
            Assert.IsFalse(DeviceMapAnchorId.TryGetMapId("3f2c/", out _));
        }

        [Test]
        public void Compose_RefusesAMapIdThatCouldNotBeReadBack()
        {
            Assert.Throws<ArgumentException>(() => DeviceMapAnchorId.Compose(string.Empty, "a1b2"));
            Assert.Throws<ArgumentException>(() => DeviceMapAnchorId.Compose("3f/2c", "a1b2"));
            Assert.Throws<ArgumentException>(() => DeviceMapAnchorId.Compose("3f2c", null));
        }

        [Test]
        public void ScanReport_DescribesSizeAndCoverage()
        {
            var report = new DeviceMapScanReport { mapId = "3f2c", mapBytes = 2048, scanSeconds = 20f, coverage = 1f };

            StringAssert.Contains("2 KB", report.ToString());
            StringAssert.Contains("3f2c", report.ToString());
        }
    }
}
