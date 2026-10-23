// Copyright (c) Reality Collective. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NUnit.Framework;
using UnityEngine;

namespace RealityToolkit.SpatialPersistence.Niantic.Editor.Tests
{
    /// <summary>
    /// Offline checks of the scan coverage measure.
    /// </summary>
    public class ScanCoverageTests
    {
        [Test]
        public void ScanCoverage_IsTheMeanOfTimeDistanceAndNewMesh()
        {
            var coverage = new ScanCoverage { MinScanSeconds = 10f, MinPathMetres = 2f, MinNewMeshAreaSquareMetres = 4f };
            coverage.Begin(100f, 5f);
            coverage.TrackCamera(Vector3.zero);
            coverage.TrackCamera(new Vector3(1f, 0f, 0f));
            coverage.SetMeshArea(7f);

            // 5 of 10 s, 1 of 2 m, 2 of 4 m² of new mesh: half way on each target.
            Assert.AreEqual(0.5f, coverage.Coverage(105f), 1e-4f);
            Assert.AreEqual(1f, coverage.PathMetres, 1e-4f);
            Assert.AreEqual(2f, coverage.NewMeshAreaSquareMetres, 1e-4f);
            Assert.IsFalse(coverage.IsComplete(105f));
        }

        [Test]
        public void ScanCoverage_CapsEachTargetAndCountsOnlyNewMesh()
        {
            var coverage = new ScanCoverage { MinScanSeconds = 10f, MinPathMetres = 2f, MinNewMeshAreaSquareMetres = 4f };
            coverage.Begin(0f, 50f);
            coverage.TrackCamera(Vector3.zero);
            coverage.TrackCamera(new Vector3(0f, 0f, 10f));

            // Time and distance are well past their targets but count once each; mesh meshed before the scan does not count.
            coverage.SetMeshArea(50f);
            Assert.AreEqual(2f / 3f, coverage.Coverage(60f), 1e-4f);

            coverage.SetMeshArea(54f);
            Assert.IsTrue(coverage.IsComplete(60f));
        }

        [Test]
        public void ScanCoverage_CountsMapPointsWithoutChangingTheCoverage()
        {
            var coverage = new ScanCoverage();
            coverage.Begin(0f, 0f);
            var before = coverage.Coverage(5f);
            coverage.AddMapPoints(300, 6f);
            coverage.AddMapPoints(100, 2f);

            Assert.AreEqual(400, coverage.MapPoints);
            Assert.AreEqual(2, coverage.MapUpdates);
            Assert.AreEqual(before, coverage.Coverage(5f));
            StringAssert.Contains("400 map points in 2 updates (mean error 0.020)", coverage.Summary(5f));
        }

        [Test]
        public void ScanCoverage_MeasuresMeshAreaFromTriangles()
        {
            var mesh = new Mesh();
            mesh.SetVertices(new[] { Vector3.zero, new Vector3(2f, 0f, 0f), new Vector3(0f, 0f, 3f), new Vector3(2f, 0f, 3f) });
            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            var holder = new GameObject("ScanCoverageTestMesh");
            try
            {
                var filter = holder.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;

                // A 2 m by 3 m quad made of two triangles.
                Assert.AreEqual(6f, ScanCoverage.MeasureMeshArea(new[] { filter, null }), 1e-4f);
                Assert.AreEqual(0f, ScanCoverage.MeasureMeshArea(null));
            }
            finally
            {
                Object.DestroyImmediate(holder);
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
