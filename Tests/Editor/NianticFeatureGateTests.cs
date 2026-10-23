// Copyright (c) Reality Collective. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NUnit.Framework;

namespace RealityToolkit.SpatialPersistence.Niantic.Editor.Tests
{
    /// <summary>
    /// Offline checks of the reference counting in <see cref="NianticFeatureGate"/>. No scene is loaded, so applying the state finds no managers.
    /// </summary>
    public class NianticFeatureGateTests
    {
        [Test]
        public void NianticFeatureGate_StaysOnWhileAnyFlowHoldsIt()
        {
            try
            {
                Assert.IsFalse(NianticFeatureGate.IsEnabled);
                NianticFeatureGate.Request(NianticFeatureGate.VPS_SCANNING);
                NianticFeatureGate.Request(NianticFeatureGate.VPS_DEPLOYMENT);
                NianticFeatureGate.Request(NianticFeatureGate.VPS_SCANNING);
                Assert.AreEqual(2, NianticFeatureGate.Reasons.Count);

                NianticFeatureGate.Release(NianticFeatureGate.VPS_SCANNING);
                Assert.IsTrue(NianticFeatureGate.IsEnabled, "The deployment still needs it");
                NianticFeatureGate.Release(NianticFeatureGate.VPS_DEPLOYMENT);
                Assert.IsFalse(NianticFeatureGate.IsEnabled);
            }
            finally
            {
                NianticFeatureGate.Release(NianticFeatureGate.VPS_SCANNING);
                NianticFeatureGate.Release(NianticFeatureGate.VPS_DEPLOYMENT);
            }
        }

        [Test]
        public void NianticFeatureGate_IgnoresEmptyAndRepeatedReleases()
        {
            NianticFeatureGate.Release(NianticFeatureGate.VPS_SCANNING);
            NianticFeatureGate.Request(string.Empty);
            NianticFeatureGate.Request(null);
            Assert.IsFalse(NianticFeatureGate.IsEnabled);
        }
    }
}
