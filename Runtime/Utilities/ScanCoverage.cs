// Copyright (c) Reality Collective. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Generic;
using UnityEngine;

namespace RealityToolkit.SpatialPersistence.Niantic
{
    /// <summary>
    /// A rough measure of how complete a device-map scan is, to tell the user when to keep scanning and when it is good to save.
    /// </summary>
    /// <remarks>
    /// Coverage is the mean progress towards three targets that move while scanning:
    /// time scanned, distance walked, and AR mesh added since the scan began. The mesh keeps everything meshed since launch, so only the
    /// new area counts. Map points are reported but left out of the percentage: NSDK sends map updates too rarely to move a live readout
    /// (one update in an 8.5 s scan on a Galaxy S24). The default targets are a first guess, to tune on devices.
    /// </remarks>
    public sealed class ScanCoverage
    {
        private float startedAt;
        private float meshAreaAtStart;
        private Vector3? lastCameraPosition;
        private float errorSum;

        /// <summary>
        /// Seconds of scanning that count as complete.
        /// </summary>
        public float MinScanSeconds { get; set; } = 15f;

        /// <summary>
        /// Metres walked that count as complete.
        /// </summary>
        public float MinPathMetres { get; set; } = 3f;

        /// <summary>
        /// Square metres of new AR mesh that count as complete.
        /// </summary>
        public float MinNewMeshAreaSquareMetres { get; set; } = 3f;

        /// <summary>
        /// Metres walked since <see cref="Begin"/>.
        /// </summary>
        public float PathMetres { get; private set; }

        /// <summary>
        /// Total AR mesh area at the last <see cref="SetMeshArea"/>, in square metres.
        /// </summary>
        public float MeshAreaSquareMetres { get; private set; }

        /// <summary>
        /// AR mesh area added since <see cref="Begin"/>, in square metres.
        /// </summary>
        public float NewMeshAreaSquareMetres => Mathf.Max(0f, MeshAreaSquareMetres - meshAreaAtStart);

        /// <summary>
        /// Map feature points counted since <see cref="Begin"/>.
        /// </summary>
        public int MapPoints { get; private set; }

        /// <summary>
        /// Map updates counted since <see cref="Begin"/>.
        /// </summary>
        public int MapUpdates { get; private set; }

        /// <summary>
        /// Starts measuring a new scan.
        /// </summary>
        /// <param name="now">The current time, for example Time.realtimeSinceStartup.</param>
        /// <param name="meshAreaSquareMetres">The AR mesh area before the scan.</param>
        public void Begin(float now, float meshAreaSquareMetres)
        {
            startedAt = now;
            meshAreaAtStart = meshAreaSquareMetres;
            MeshAreaSquareMetres = meshAreaSquareMetres;
            PathMetres = 0f;
            lastCameraPosition = null;
            MapPoints = 0;
            MapUpdates = 0;
            errorSum = 0f;
        }

        /// <summary>
        /// Adds the distance from the last camera position. Call every frame while scanning.
        /// </summary>
        public void TrackCamera(Vector3 cameraPosition)
        {
            if (lastCameraPosition.HasValue)
            {
                PathMetres += Vector3.Distance(lastCameraPosition.Value, cameraPosition);
            }

            lastCameraPosition = cameraPosition;
        }

        /// <summary>
        /// Records the current AR mesh area, from <see cref="MeasureMeshArea"/>.
        /// </summary>
        public void SetMeshArea(float meshAreaSquareMetres) => MeshAreaSquareMetres = meshAreaSquareMetres;

        /// <summary>
        /// Counts the points of one map update and the sum of their errors.
        /// </summary>
        public void AddMapPoints(int count, float pointErrorSum)
        {
            MapUpdates++;
            MapPoints += count;
            errorSum += pointErrorSum;
        }

        /// <summary>
        /// Coverage from 0 to 1: the mean progress towards the time, distance and new mesh targets.
        /// </summary>
        public float Coverage(float now)
        {
            var time = Mathf.Clamp01((now - startedAt) / Mathf.Max(1f, MinScanSeconds));
            var path = Mathf.Clamp01(PathMetres / Mathf.Max(0.1f, MinPathMetres));
            var mesh = Mathf.Clamp01(NewMeshAreaSquareMetres / Mathf.Max(0.1f, MinNewMeshAreaSquareMetres));
            return (time + path + mesh) / 3f;
        }

        /// <summary>
        /// True once every target is met.
        /// </summary>
        public bool IsComplete(float now) => Coverage(now) >= 0.999f;

        /// <summary>
        /// One line for the log or a readout: seconds, metres walked, mesh and map points.
        /// </summary>
        public string Summary(float now)
        {
            var meanError = MapPoints > 0 ? errorSum / MapPoints : 0f;
            return $"{Coverage(now) * 100f:0}% coverage: {now - startedAt:0} s, {PathMetres:0.0} m walked, new mesh {NewMeshAreaSquareMetres:0.0} m² (total {MeshAreaSquareMetres:0.0}), {MapPoints} map points in {MapUpdates} updates (mean error {meanError:0.000})";
        }

        /// <summary>
        /// Total triangle area of a set of AR mesh chunks, in square metres. Cheap enough to call once a second while scanning.
        /// </summary>
        public static float MeasureMeshArea(IEnumerable<MeshFilter> meshFilters)
        {
            if (meshFilters == null)
            {
                return 0f;
            }

            var area = 0f;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            foreach (var meshFilter in meshFilters)
            {
                var mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                mesh.GetVertices(vertices);
                mesh.GetTriangles(triangles, 0);
                for (var i = 0; i + 2 < triangles.Count; i += 3)
                {
                    var a = vertices[triangles[i]];
                    area += Vector3.Cross(vertices[triangles[i + 1]] - a, vertices[triangles[i + 2]] - a).magnitude * 0.5f;
                }
            }

            return area;
        }
    }
}
