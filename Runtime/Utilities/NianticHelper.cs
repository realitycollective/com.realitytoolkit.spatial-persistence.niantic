// Copyright (c) Reality Collective. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NianticSpatial.NSDK.AR.Mapping;
using NianticSpatial.NSDK.AR.VPS2;
using RealityCollective.Utilities.Logging;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace RealityToolkit.SpatialPersistence.Niantic
{
    /// <summary>
    /// The Niantic Spatial SDK (NSDK 4) calls the app makes for on-device maps: scan a device map, read it to save, load a saved one,
    /// and track its root anchor to localise.
    /// </summary>
    /// <remarks>
    /// The flow was proven on device with NSDK 4.1 (October 2026). Used by <see cref="NianticSpatialPersistenceServiceDeviceMappingModule"/>.
    /// ARDK 3 to NSDK 4: ARPersistentAnchorManager became <see cref="ARVps2Manager"/>, ARDeviceMap became the raw map bytes,
    /// DeviceMapFinalized became <see cref="ARDeviceMappingManager.MapFinalized"/> (the last update only, so the whole map comes from
    /// <see cref="TryGetMapData"/>), and a loaded map is tracked through <see cref="DeviceMapAccessController.AddMap"/> then
    /// <see cref="DeviceMapAccessController.CreateRootAnchor"/>. AddMap also converts maps saved by ARDK 3.
    /// Only on-device features are used: universal and VPS map localisation default to on in NSDK 4 and go to the cloud, so they are turned off.
    /// Both managers read their settings when their subsystems start, which is when they are enabled, so configure them while disabled.
    /// </remarks>
    public sealed class NianticHelper : IDisposable
    {
        private DeviceMapAccessController accessController;
        private ARVps2Anchor trackedAnchor;
        private bool trackedAnchorReported;

        /// <summary>
        /// Wraps the two NSDK managers from the scene.
        /// </summary>
        /// <param name="mappingManager">Scans device maps.</param>
        /// <param name="vps2Manager">Tracks anchors against the loaded device maps.</param>
        public NianticHelper(ARDeviceMappingManager mappingManager, ARVps2Manager vps2Manager)
        {
            MappingManager = mappingManager;
            Vps2Manager = vps2Manager;
            accessController = DeviceMapAccessController.Acquire();
            if (accessController == null)
            {
                StaticLogger.LogError($"{nameof(NianticHelper)} - could not acquire the NSDK device map access controller");
            }

            if (MappingManager != null)
            {
                MappingManager.MapUpdated += OnMapUpdated;
                MappingManager.MapFinalized += OnMapFinalized;
            }

            if (Vps2Manager != null)
            {
                Vps2Manager.trackablesChanged.AddListener(OnAnchorsChanged);
            }
        }

        /// <summary>
        /// Scans device maps.
        /// </summary>
        public ARDeviceMappingManager MappingManager { get; }

        /// <summary>
        /// Tracks anchors against the loaded device maps.
        /// </summary>
        public ARVps2Manager Vps2Manager { get; }

        /// <summary>
        /// True between <see cref="StartMapping"/> and <see cref="StopMapping"/>.
        /// </summary>
        public bool IsMapping { get; private set; }

        /// <summary>
        /// The anchor passed to <see cref="TryTrackAnchor"/>, or null.
        /// </summary>
        public ARVps2Anchor TrackedAnchor => trackedAnchor;

        /// <summary>
        /// The payload of the scanned map's root anchor, created with the first map update. Null before then.
        /// </summary>
        public string RootAnchorPayload => MappingManager != null ? MappingManager.RootAnchorPayload : null;

        /// <summary>
        /// Raised for each map update while scanning. The bytes hold only what changed since the previous update.
        /// </summary>
        public event Action<byte[]> MapUpdated;

        /// <summary>
        /// Raised once after <see cref="StopMapping"/> (NSDK forces it after 2 s). Read the whole map with <see cref="TryGetMapData"/>.
        /// </summary>
        public event Action<byte[]> MapFinalized;

        /// <summary>
        /// Raised once when the anchor from <see cref="TryTrackAnchor"/> first reaches <see cref="TrackingState.Tracking"/>: the device has localised.
        /// </summary>
        public event Action<ARVps2Anchor> AnchorTracking;

        /// <summary>
        /// Sets how the scan splits into map nodes. Call while the mapping manager is disabled.
        /// </summary>
        /// <param name="splitterMaxDistanceMeters">Distance walked before a new node.</param>
        /// <param name="splitterMaxDurationSeconds">Time before a new node.</param>
        public void ConfigureMapping(float splitterMaxDistanceMeters, float splitterMaxDurationSeconds)
        {
            if (MappingManager == null)
            {
                return;
            }

            MappingManager.MappingSplitterMaxDistanceMeters = splitterMaxDistanceMeters;
            MappingManager.MappingSplitterMaxDurationSeconds = splitterMaxDurationSeconds;
        }

        /// <summary>
        /// Localises against device maps only, never the cloud. Call while the VPS2 manager is disabled.
        /// </summary>
        /// <param name="framerate">Localisation attempts per second. ARDK 3's 0.1 s request interval was 10.</param>
        public void ConfigureDeviceMapLocalisation(int framerate = 10)
        {
            if (Vps2Manager == null)
            {
                return;
            }

            Vps2Manager.DeviceMapLocalizationEnabled = true;
            Vps2Manager.VpsMapLocalizationEnabled = false;
            Vps2Manager.UniversalLocalizationEnabled = false;
            Vps2Manager.DeviceMapLocalizationFramerate = framerate;
        }

        /// <summary>
        /// Enables the mapping manager, waits a frame for its subsystem to start, then starts scanning a new map.
        /// </summary>
        public IEnumerator StartMapping()
        {
            if (MappingManager == null)
            {
                yield break;
            }

            MappingManager.enabled = true;
            yield return null;
            MappingManager.StartMapping();
            IsMapping = true;
        }

        /// <summary>
        /// Stops scanning. <see cref="MapFinalized"/> follows.
        /// </summary>
        public void StopMapping()
        {
            if (MappingManager == null || !IsMapping)
            {
                return;
            }

            MappingManager.StopMapping();
            IsMapping = false;
        }

        /// <summary>
        /// Reads the whole scanned map, ready to save.
        /// </summary>
        public bool TryGetMapData(out byte[] mapData)
        {
            mapData = null;
            return MappingManager != null && MappingManager.TryGetMapData(out mapData) && mapData != null && mapData.Length > 0;
        }

        /// <summary>
        /// Reads the feature points in a map update, relative to the map's root anchor.
        /// </summary>
        public bool TryGetMapPoints(byte[] mapUpdate, out Vector3[] points, out float[] errors)
        {
            points = null;
            errors = null;
            return MappingManager != null && mapUpdate != null && MappingManager.ExtractMapMetadataFromRootAnchor(mapUpdate, out points, out errors) && points != null;
        }

        /// <summary>
        /// Loads a saved map and creates the root anchor to track against it.
        /// </summary>
        /// <param name="mapData">Bytes from <see cref="TryGetMapData"/>, or an ARDK 3 map file (AddMap converts it).</param>
        /// <param name="anchorPayload">The root anchor to pass to <see cref="TryTrackAnchor"/>.</param>
        public bool TryLoadMap(byte[] mapData, out string anchorPayload)
        {
            anchorPayload = null;
            if (accessController == null || mapData == null || mapData.Length == 0)
            {
                return false;
            }

            if (!accessController.AddMap(mapData))
            {
                StaticLogger.LogError($"{nameof(NianticHelper)} - AddMap refused the map ({mapData.Length} bytes)");
                return false;
            }

            if (!accessController.CreateRootAnchor(out anchorPayload) || string.IsNullOrEmpty(anchorPayload))
            {
                StaticLogger.LogError($"{nameof(NianticHelper)} - CreateRootAnchor failed on the loaded map");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Creates a root anchor on the maps in memory, for a scan that finished without one.
        /// </summary>
        public bool TryCreateRootAnchor(out string anchorPayload)
        {
            anchorPayload = null;
            return accessController != null && accessController.CreateRootAnchor(out anchorPayload) && !string.IsNullOrEmpty(anchorPayload);
        }

        /// <summary>
        /// Starts device-map localisation and tracks an anchor. <see cref="AnchorTracking"/> is raised once it tracks.
        /// </summary>
        /// <param name="anchorPayload">A root anchor payload from a scan or a loaded map.</param>
        /// <param name="anchor">The anchor GameObject, which follows the map once localised.</param>
        public bool TryTrackAnchor(string anchorPayload, out ARVps2Anchor anchor)
        {
            anchor = null;
            if (Vps2Manager == null || string.IsNullOrEmpty(anchorPayload))
            {
                return false;
            }

            if (!Vps2Manager.enabled)
            {
                ConfigureDeviceMapLocalisation(Vps2Manager.DeviceMapLocalizationFramerate);
                Vps2Manager.enabled = true;
            }

            trackedAnchorReported = false;
            var ok = Vps2Manager.TryTrackAnchor(anchorPayload, out trackedAnchor);
            anchor = trackedAnchor;
            return ok && anchor != null;
        }

        /// <summary>
        /// Stops localising. Disabling the VPS2 manager removes every anchor it made, so the anchor GameObject is destroyed with it.
        /// </summary>
        public void StopTracking()
        {
            trackedAnchor = null;
            trackedAnchorReported = false;
            if (Vps2Manager != null)
            {
                Vps2Manager.enabled = false;
            }
        }

        /// <summary>
        /// Stops scanning and tracking and forgets every map in memory. Saved files are untouched.
        /// </summary>
        public void ClearMaps()
        {
            StopMapping();
            StopTracking();
            if (MappingManager != null)
            {
                MappingManager.ClearMap();
                MappingManager.enabled = false;
            }

            accessController?.ClearDeviceMaps();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (MappingManager != null)
            {
                MappingManager.MapUpdated -= OnMapUpdated;
                MappingManager.MapFinalized -= OnMapFinalized;
            }

            if (Vps2Manager != null)
            {
                Vps2Manager.trackablesChanged.RemoveListener(OnAnchorsChanged);
            }

            accessController?.Release();
            accessController = null;
        }

        private void OnMapUpdated(byte[] mapUpdate) => MapUpdated?.Invoke(mapUpdate);

        private void OnMapFinalized(byte[] mapUpdate)
        {
            IsMapping = false;
            MapFinalized?.Invoke(mapUpdate);
        }

        private void OnAnchorsChanged(ARTrackablesChangedEventArgs<ARVps2Anchor> args)
        {
            if (trackedAnchorReported || trackedAnchor == null)
            {
                return;
            }

            foreach (var anchor in args.updated)
            {
                if (anchor.trackableId == trackedAnchor.trackableId && anchor.trackingState == TrackingState.Tracking)
                {
                    trackedAnchorReported = true;
                    StaticLogger.Log($"{nameof(NianticHelper)} - localised (confidence {anchor.trackingConfidence:0.00})");
                    AnchorTracking?.Invoke(anchor);
                    return;
                }
            }
        }
    }
}
