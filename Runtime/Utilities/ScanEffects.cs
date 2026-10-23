// Copyright (c) Reality Collective. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace RealityToolkit.SpatialPersistence.Niantic
{
    /// <summary>
    /// The looks of the scan overlay drawn on the AR mesh while scanning, plus an optional layer of the map's feature points.
    /// </summary>
    /// <remarks>
    /// An effect is only the materials set on the AR mesh chunks, so switching costs nothing. The package's neutral look is the
    /// silhouette pair in Runtime/Visuals/Materials; apps add their own effects (Ethar Explorer adds its Ethar Spatial ones).
    /// The default effect is <see cref="defaultEffect"/>, or the first in the list when that is empty.
    /// The points layer draws the map's feature points as camera-facing dots with GPU instancing; give it an instancing material.
    /// </remarks>
    public class ScanEffects : MonoBehaviour
    {
        /// <summary>
        /// One look: a name to show and the materials each mesh chunk draws with.
        /// </summary>
        [Serializable]
        public struct Effect
        {
            /// <summary>
            /// The name to show, for example "Silhouette".
            /// </summary>
            public string Name;

            /// <summary>
            /// The materials set on each AR mesh chunk.
            /// </summary>
            public Material[] Materials;
        }

        // Legacy (Ethar Explorer, 23 October 2026): public const string DefaultEffectName = "Ethar Pulse";
        // The default is now the serialized defaultEffect, or the first effect in the list.

        private const int MaxPoints = 30000;

        // 1023 instances per draw is the safe limit for instanced arrays on mobile.
        private const int InstancesPerDraw = 1023;

        [SerializeField]
        private XROrigin origin;

        [SerializeField]
        private List<Effect> effects = new List<Effect>();

        [Tooltip("The effect to start with. Leave empty to use the first in the list.")]
        [SerializeField]
        private string defaultEffect = string.Empty;

        [Tooltip("Draws the map's feature points: an instancing material that turns each quad to face the camera.")]
        [SerializeField]
        private Material pointsMaterial;

        private readonly List<Matrix4x4> points = new List<Matrix4x4>();
        private Mesh pointQuad;
        private int current;

        /// <summary>
        /// The effects to choose from.
        /// </summary>
        public IReadOnlyList<Effect> Effects => effects;

        /// <summary>
        /// The name of the effect in use.
        /// </summary>
        public string CurrentName => effects.Count > 0 ? effects[current].Name : "None";

        /// <summary>
        /// Draw the map's feature points over the camera image.
        /// </summary>
        public bool ShowPoints { get; set; }

        /// <summary>
        /// The number of map points collected since the last <see cref="ClearPoints"/>.
        /// </summary>
        public int PointCount => points.Count;

        private void Awake()
        {
            if (origin == null)
            {
                origin = FindFirstObjectByType<XROrigin>();
            }

            // Legacy (Ethar Explorer): Select(DefaultEffectName);
            if (string.IsNullOrEmpty(defaultEffect) || !Select(defaultEffect))
            {
                current = 0;
            }

            pointQuad = CreateQuad();
        }

        private void OnDestroy()
        {
            if (pointQuad != null)
            {
                Destroy(pointQuad);
            }
        }

        /// <summary>
        /// Uses the named effect and applies it to every mesh chunk.
        /// </summary>
        /// <returns>False if there is no effect with that name.</returns>
        public bool Select(string effectName, ARMeshManager meshManager = null)
        {
            var index = effects.FindIndex(e => string.Equals(e.Name, effectName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return false;
            }

            current = index;
            ApplyAll(meshManager);
            return true;
        }

        /// <summary>
        /// Moves to the next effect and applies it to every mesh chunk.
        /// </summary>
        public void Next(ARMeshManager meshManager)
        {
            if (effects.Count == 0)
            {
                return;
            }

            current = (current + 1) % effects.Count;
            ApplyAll(meshManager);
        }

        /// <summary>
        /// Gives one mesh chunk the current effect's materials.
        /// </summary>
        public void Apply(MeshFilter mesh)
        {
            if (effects.Count > 0 && mesh != null && mesh.TryGetComponent<MeshRenderer>(out var renderer) && effects[current].Materials is { Length: > 0 } materials)
            {
                renderer.sharedMaterials = materials;
            }
        }

        /// <summary>
        /// Adds feature points from a map update. Their positions are relative to the map's root anchor.
        /// </summary>
        /// <param name="pointsRelativeToRoot">The points from <see cref="NianticHelper.TryGetMapPoints"/>.</param>
        /// <param name="rootAnchor">The tracked root anchor, or null while scanning.</param>
        /// <remarks>
        /// While scanning, the root anchor is "located at the origin of the current AR session" (DeviceMapAccessController.CreateRootAnchor),
        /// so session space stands in for it.
        /// </remarks>
        public void AddPoints(Vector3[] pointsRelativeToRoot, Transform rootAnchor)
        {
            if (pointsRelativeToRoot == null)
            {
                return;
            }

            var toWorld = rootAnchor != null
                ? rootAnchor.localToWorldMatrix
                : (origin != null && origin.TrackablesParent != null ? origin.TrackablesParent.localToWorldMatrix : Matrix4x4.identity);
            foreach (var point in pointsRelativeToRoot)
            {
                if (points.Count >= MaxPoints)
                {
                    break;
                }

                points.Add(Matrix4x4.Translate(toWorld.MultiplyPoint3x4(point)));
            }
        }

        /// <summary>
        /// Forgets the collected map points.
        /// </summary>
        public void ClearPoints() => points.Clear();

        private void LateUpdate()
        {
            if (!ShowPoints || points.Count == 0 || pointsMaterial == null || pointQuad == null)
            {
                return;
            }

            var renderParams = new RenderParams(pointsMaterial) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            for (var start = 0; start < points.Count; start += InstancesPerDraw)
            {
                Graphics.RenderMeshInstanced(renderParams, pointQuad, 0, points, Mathf.Min(InstancesPerDraw, points.Count - start), start);
            }
        }

        private void ApplyAll(ARMeshManager meshManager)
        {
            if (meshManager == null)
            {
                return;
            }

            foreach (var mesh in meshManager.meshes)
            {
                Apply(mesh);
            }
        }

        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "ScanPointQuad" };
            mesh.SetVertices(new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) });
            mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            // A loose bound so the quad is never culled; the vertex shader places it.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
            return mesh;
        }
    }
}
