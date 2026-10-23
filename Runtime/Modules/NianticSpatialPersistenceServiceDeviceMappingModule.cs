// Copyright (c) Reality Collective. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NianticSpatial.NSDK.AR.Mapping;
using NianticSpatial.NSDK.AR.VPS2;
using RealityCollective.Utilities.Async;
using RealityToolkit.SpatialPersistence.Interfaces;
using RealityToolkit.SpatialPersistence.Niantic.Definitions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace RealityToolkit.SpatialPersistence.Niantic
{
    /// <summary>
    /// Spatial persistence against device maps scanned with the Niantic Spatial SDK (NSDK 4): anchors are placed against a map of the space
    /// that is stored on the device, and found again by relocalising against it. Device mapping only: VPS maps and universal localisation,
    /// which use the Niantic cloud, stay off.
    /// </summary>
    /// <remarks>
    /// <para>Creating: <c>TryCreateAnchor(position, rotation, timeToLive, SpatialPersistenceTrackingType.DeviceMap)</c>. With no map localised,
    /// the first create scans the space into a new map, as a cloud anchor service captures the space before it creates an anchor:
    /// progress goes out as <c>SpatialPersistenceStatusMessage</c>, and the scan finishes when <see cref="Coverage"/> is complete, or at once
    /// on <see cref="CancelAnchorOperation"/>, which keeps what was captured. The map is stored, the device localises against it, and every
    /// create made meanwhile completes with <c>CreateAnchorSucceeded</c>. With a map localised, a create completes straight away.</para>
    /// <para>Anchors are poses relative to the map's root anchor, kept in an index beside the map, so only the root anchor is tracked by
    /// NSDK and content stays rigid with the map. The anchor GameObjects are children of the root anchor; place content under them.
    /// An anchor ID names its map (<see cref="DeviceMapAnchorId"/>), and <see cref="MapStored"/> reports each stored map's size.</para>
    /// <para>Finding: a <see cref="SpatialPersistenceTrackingType.DeviceMap"/> search for anchor IDs loads the map they belong to and
    /// raises <c>AnchorLocated</c> for each once the device localises. One map is in use at a time. Maps stored by ARDK 3 load too:
    /// NSDK's AddMap converts them.</para>
    /// <para>Moving and deleting use the tracking type overloads of <c>TryMoveAnchor</c> and <c>TryDeleteAnchors</c>. Deleting a map's
    /// last anchor deletes the map. <see cref="TryClearAnchorCache"/> drops a scan in progress and the map in use; stored maps are kept.</para>
    /// <para>The scene needs an ARDeviceMappingManager and an ARVps2Manager, best left disabled (the module enables them when needed),
    /// and NSDK device mapping and VPS2 turned on in the NSDK settings. An ARMeshManager, if present, adds new mesh area to the coverage.
    /// The managers are looked up when first needed, so the service can start before the AR scene loads.</para>
    /// </remarks>
    [System.Runtime.InteropServices.Guid("64F41A9F-5C8E-4A52-B61B-553F485BE959")]
    public class NianticSpatialPersistenceServiceDeviceMappingModule : BaseSpatialPersistenceServiceModule, INianticSpatialPersistenceServiceDeviceMappingModule
    {
        /// <summary>
        /// A create waiting for the map to be stored or localised.
        /// </summary>
        private sealed class PendingCreate
        {
            public string Key;
            public Pose WorldPose;
            public long ExpiresUnixSeconds;
            public TaskCompletionSource<string> Result;
        }

        #region Private Properties
        private readonly NianticSpatialPersistenceServiceDeviceMappingProfile profile;
        private readonly ScanCoverage coverage = new ScanCoverage();
        private readonly Dictionary<string, DeviceMapAnchorIndex> indexes = new Dictionary<string, DeviceMapAnchorIndex>();
        private readonly Dictionary<string, string> mapOfAnchor = new Dictionary<string, string>();
        private readonly Dictionary<string, GameObject> anchorObjects = new Dictionary<string, GameObject>();
        private readonly List<PendingCreate> pendingCreates = new List<PendingCreate>();
        private readonly HashSet<string> pendingFinds = new HashSet<string>();
        private ARDeviceMappingManager mappingManager;
        private ARVps2Manager vps2Manager;
        private ARMeshManager meshManager;
        private NianticHelper niantic;
        private bool isStarted;
        private bool indexesLoaded;
        private bool awaitingMap;
        private string currentMapId;
        private GameObject rootAnchor;
        private bool isLocalised;
        private bool timeoutReported;
        private float trackingStartedAt;
        private float scanStartedAt;
        private float nextScanSampleAt;
        #endregion Private Properties

        #region Constructor
        /// <summary>
        /// Creates the module from its profile, under the spatial persistence service.
        /// </summary>
        public NianticSpatialPersistenceServiceDeviceMappingModule(string name, uint priority, NianticSpatialPersistenceServiceDeviceMappingProfile profile, ISpatialPersistenceService parentService)
            : base(name, priority, profile, parentService)
        {
            this.profile = profile;
            if (profile != null)
            {
                coverage.MinScanSeconds = profile.MinScanSeconds;
                coverage.MinPathMetres = profile.MinPathMetres;
                coverage.MinNewMeshAreaSquareMetres = profile.MinNewMeshAreaSquareMetres;
            }
        }
        #endregion Constructor

        #region ISpatialPersistenceServiceModule
        /// <inheritdoc />
        public override bool IsRunning => isStarted && niantic != null && mappingManager != null && vps2Manager != null;

        /// <inheritdoc />
        public override SpatialPersistenceTrackingType TrackingType => SpatialPersistenceTrackingType.DeviceMap;

        /// <inheritdoc />
        public override Task StartSpatialPersistenceModule()
        {
            if (IsRunning)
            {
                return Task.CompletedTask;
            }

            LoadIndexes();
            if (!TryResolveManagers())
            {
                // Not an error yet: the AR scene may not be loaded. Every operation tries again.
                OnSpatialPersistenceStatusMessage($"[{nameof(NianticSpatialPersistenceServiceDeviceMappingModule)}] Waiting for an {nameof(ARDeviceMappingManager)} and an {nameof(ARVps2Manager)} in the scene.");
                return Task.CompletedTask;
            }

            isStarted = true;
            OnSessionInitialized();
            OnSessionStarted();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override void StopSpatialPersistenceModule()
        {
            FailPending("The module was stopped.");
            ReleaseHelper();
            isStarted = false;
            OnSessionEnded();
        }

        /// <inheritdoc />
        public override async void TryCreateAnchor(Vector3 position, Quaternion rotation, DateTimeOffset timeToLive)
            => await TryCreateAnchorAsync(position, rotation, timeToLive);

        /// <inheritdoc />
        /// <returns>The anchor ID. Completes at once while a map is localised; otherwise once the scan is stored and localised.</returns>
        public override Task<string> TryCreateAnchorAsync(Vector3 position, Quaternion rotation, DateTimeOffset timeToLive)
        {
            if (!EnsureStarted())
            {
                OnSpatialPersistenceError($"[{nameof(TryCreateAnchor)}] Cannot create an anchor: no {nameof(ARDeviceMappingManager)} and {nameof(ARVps2Manager)} in the scene.");
                OnCreateAnchorFailed();
                return Task.FromResult(string.Empty);
            }

            var pending = new PendingCreate
            {
                // The anchor ID is made when the anchor is stored, because it names the map, which a scan has not made yet.
                Key = Guid.NewGuid().ToString("N"),
                WorldPose = new Pose(position, rotation),
                ExpiresUnixSeconds = DateTimeOffset.Compare(timeToLive, DateTimeOffset.Now) > 0 ? timeToLive.ToUnixTimeSeconds() : 0,
                Result = new TaskCompletionSource<string>(),
            };

            if (currentMapId != null && isLocalised && rootAnchor != null)
            {
                CompleteCreate(pending);
                return pending.Result.Task;
            }

            OnCreateAnchorStarted();
            pendingCreates.Add(pending);
            if (!niantic.IsMapping && currentMapId == null)
            {
                // Nothing to place the anchor against yet: scan the space into a new map, as a cloud service captures the space first.
                StartScan();
            }

            return pending.Result.Task;
        }

        /// <inheritdoc />
        /// <remarks>Loads the map of the first anchor found in the index; one map is in use at a time.</remarks>
        public override void TryFindAnchors(params SpatialPersistenceSearchArgs[] searchCriteria)
        {
            var (mapId, ids) = PrepareFind(searchCriteria);
            if (mapId != null)
            {
                Find(mapId, ids, null);
            }
        }

        /// <inheritdoc />
        /// <returns>True once the anchors' map is loaded and being localised against; <c>AnchorLocated</c> reports each anchor.</returns>
        public override Task<bool> TryFindAnchorsAsync(params SpatialPersistenceSearchArgs[] searchCriteria)
        {
            var (mapId, ids) = PrepareFind(searchCriteria);
            if (mapId == null)
            {
                return Task.FromResult(false);
            }

            var result = new TaskCompletionSource<bool>();
            Find(mapId, ids, result);
            return result.Task;
        }

        /// <inheritdoc />
        public override bool HasAnchor(GameObject anchoredObject)
            => anchoredObject != null && anchorObjects.Values.Any(anchor => anchor != null && (anchoredObject == anchor || anchoredObject.transform.IsChildOf(anchor.transform)));

        /// <inheritdoc />
        /// <remarks>Stores the new pose relative to the map's root anchor; the map must be localised.</remarks>
        public override bool TryMoveAnchor(GameObject anchoredObject, Vector3 position, Quaternion rotation, string vendorAnchorID)
        {
            if (string.IsNullOrEmpty(vendorAnchorID) || !mapOfAnchor.TryGetValue(vendorAnchorID, out var mapId))
            {
                return false;
            }

            if (mapId != currentMapId || !isLocalised || rootAnchor == null)
            {
                OnSpatialPersistenceError($"[{nameof(TryMoveAnchor)}] Anchor [{vendorAnchorID}] can only move while its map is localised.");
                return false;
            }

            var relative = ToRootRelative(new Pose(position, rotation));
            var stored = FindStored(vendorAnchorID);
            stored.position = relative.position;
            stored.rotation = relative.rotation;
            SaveIndex(mapId);

            var anchor = GetOrCreateAnchorObject(vendorAnchorID);
            anchor.transform.localPosition = relative.position;
            anchor.transform.localRotation = relative.rotation;
            OnAnchorUpdated(vendorAnchorID, anchor);
            return true;
        }

        /// <inheritdoc />
        /// <remarks>Deleting a map's last anchor deletes the map too.</remarks>
        public override void DeleteAnchors(params string[] ids)
        {
            if (ids == null)
            {
                return;
            }

            LoadIndexes();
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id) || !mapOfAnchor.TryGetValue(id, out var mapId))
                {
                    continue;
                }

                indexes[mapId].anchors.RemoveAll(anchor => anchor.id == id);
                mapOfAnchor.Remove(id);
                DestroyAnchorObject(id);
                pendingFinds.Remove(id);
                if (indexes[mapId].anchors.Count > 0)
                {
                    SaveIndex(mapId);
                }
                else
                {
                    DeleteMap(mapId);
                }

                OnAnchorDeleted(id);
            }
        }

        /// <inheritdoc />
        /// <remarks>Forgets these anchors' GameObjects so a search finds them again. Stored anchors are kept.</remarks>
        public override void ResetAnchors(params string[] ids)
        {
            if (ids == null)
            {
                return;
            }

            foreach (var id in ids)
            {
                DestroyAnchorObject(id);
            }
        }

        /// <inheritdoc />
        /// <remarks>Drops a scan in progress (nothing is stored), fails waiting creates, and stops using the current map. Stored maps are kept.</remarks>
        public override bool TryClearAnchorCache()
        {
            if (!EnsureStarted())
            {
                return false;
            }

            awaitingMap = false;
            FailPending("The anchor cache was cleared.");
            StopTrackingMap();
            niantic.ClearMaps();
            currentMapId = null;
            return true;
        }

        /// <inheritdoc />
        /// <remarks>
        /// While scanning, finishes the scan now and keeps what was captured, so the waiting creates complete (or fail if nothing usable was
        /// captured). While localising, gives up: waiting creates fail and waiting searches report <c>AnchorLocatedError</c>.
        /// </remarks>
        public override void CancelAnchorOperation()
        {
            if (niantic == null)
            {
                return;
            }

            if (niantic.IsMapping)
            {
                OnSpatialPersistenceStatusMessage("Finishing the scan with what was captured.");
                niantic.StopMapping();
            }
            else if (currentMapId != null && !isLocalised)
            {
                FailPending("Localisation was cancelled.");
                StopTrackingMap();
                currentMapId = null;
            }
        }
        #endregion ISpatialPersistenceServiceModule

        #region INianticSpatialPersistenceServiceDeviceMappingModule
        /// <inheritdoc />
        public bool IsScanning => niantic != null && niantic.IsMapping;

        /// <inheritdoc />
        public bool IsLocalised => isLocalised;

        /// <inheritdoc />
        public GameObject RootAnchor => rootAnchor;

        /// <inheritdoc />
        public ScanCoverage Coverage => coverage;

        /// <inheritdoc />
        public event Action<Vector3[]> MapPointsAdded;

        /// <inheritdoc />
        public event Action<DeviceMapScanReport> MapStored;

        /// <inheritdoc />
        public bool IsAnchorStored(string anchorId)
        {
            LoadIndexes();
            return !string.IsNullOrEmpty(anchorId) && mapOfAnchor.ContainsKey(anchorId);
        }

        /// <inheritdoc />
        public string[] GetStoredAnchorIds()
        {
            LoadIndexes();
            return mapOfAnchor.Keys.ToArray();
        }
        #endregion INianticSpatialPersistenceServiceDeviceMappingModule

        #region BaseService
        /// <inheritdoc />
        public override void Update()
        {
            base.Update();
            if (niantic == null)
            {
                return;
            }

            if (niantic.IsMapping && awaitingMap)
            {
                if (Camera.main != null)
                {
                    coverage.TrackCamera(Camera.main.transform.position);
                }

                // Once a second: mesh area, progress, and whether the scan is good enough to finish.
                if (Time.realtimeSinceStartup >= nextScanSampleAt)
                {
                    nextScanSampleAt = Time.realtimeSinceStartup + 1f;
                    coverage.SetMeshArea(MeasureMeshArea());
                    OnSpatialPersistenceStatusMessage(coverage.Summary(Time.realtimeSinceStartup));
                    if (coverage.IsComplete(Time.realtimeSinceStartup))
                    {
                        niantic.StopMapping();
                    }
                }
            }

            if (currentMapId != null && rootAnchor != null && !isLocalised && !timeoutReported && Time.realtimeSinceStartup - trackingStartedAt > LocalisationTimeout)
            {
                timeoutReported = true;
                foreach (var id in pendingFinds)
                {
                    OnAnchorLocatedError(id, $"Not localised within {LocalisationTimeout:0} s. Still trying: point the camera at the scanned area.");
                }
            }
        }

        /// <inheritdoc />
        public override void Destroy()
        {
            FailPending("The module was destroyed.");
            ReleaseHelper();
            base.Destroy();
        }
        #endregion BaseService

        #region Private Methods
        private float SplitterMaxDistance => profile != null ? profile.SplitterMaxDistanceMeters : 10f;

        private float SplitterMaxDuration => profile != null ? profile.SplitterMaxDurationSeconds : 1000f;

        private int LocalisationFramerate => profile != null ? profile.LocalisationFramerate : 10;

        private float LocalisationTimeout => profile != null ? profile.LocalisationTimeoutSeconds : 30f;

        private string MapPath(string mapId) => Path.Combine(Application.persistentDataPath, string.Format(profile != null ? profile.MapFileNameFormat : "{0}_SerializedDeviceMapData", mapId));

        private string IndexFormat => profile != null ? profile.AnchorIndexFileNameFormat : "{0}_DeviceMapAnchors.json";

        private string IndexPath(string mapId) => Path.Combine(Application.persistentDataPath, string.Format(IndexFormat, mapId));

        /// <summary>
        /// Starts the module if it is not running, finding the scene's managers again if the scene was reloaded.
        /// </summary>
        private bool EnsureStarted()
        {
            if (!IsRunning)
            {
                StartSpatialPersistenceModule();
            }

            return IsRunning;
        }

        private bool TryResolveManagers()
        {
            if (niantic != null && mappingManager != null && vps2Manager != null)
            {
                return true;
            }

            // The old helper belongs to managers that were destroyed with their scene.
            ReleaseHelper();
            mappingManager = UnityEngine.Object.FindFirstObjectByType<ARDeviceMappingManager>(FindObjectsInactive.Include);
            vps2Manager = UnityEngine.Object.FindFirstObjectByType<ARVps2Manager>(FindObjectsInactive.Include);
            if (mappingManager == null || vps2Manager == null)
            {
                return false;
            }

            niantic = new NianticHelper(mappingManager, vps2Manager);
            niantic.MapFinalized += OnMapFinalized;
            niantic.MapUpdated += OnMapUpdated;
            niantic.AnchorTracking += OnAnchorTracking;
            // Configured while disabled: both managers read their settings when their subsystems start.
            mappingManager.enabled = false;
            vps2Manager.enabled = false;
            niantic.ConfigureMapping(SplitterMaxDistance, SplitterMaxDuration);
            niantic.ConfigureDeviceMapLocalisation(LocalisationFramerate);
            return true;
        }

        private void ReleaseHelper()
        {
            if (niantic != null)
            {
                niantic.MapFinalized -= OnMapFinalized;
                niantic.MapUpdated -= OnMapUpdated;
                niantic.AnchorTracking -= OnAnchorTracking;
                if (mappingManager != null && vps2Manager != null)
                {
                    niantic.ClearMaps();
                }

                niantic.Dispose();
                niantic = null;
            }

            awaitingMap = false;
            currentMapId = null;
            rootAnchor = null;
            isLocalised = false;
            anchorObjects.Clear();
        }

        #region Scanning
        private void StartScan()
        {
            StopTrackingMap();
            niantic.ClearMaps();
            niantic.ConfigureMapping(SplitterMaxDistance, SplitterMaxDuration);
            awaitingMap = true;
            scanStartedAt = Time.realtimeSinceStartup;
            coverage.Begin(Time.realtimeSinceStartup, MeasureMeshArea());
            nextScanSampleAt = 0f;
            OnSpatialPersistenceStatusMessage("Scanning the space into a new device map. Move slowly and look at surfaces from different angles.");
            AwaiterExtensions.RunCoroutine(niantic.StartMapping());
        }

        private void OnMapFinalized(byte[] lastMapUpdate)
        {
            if (!awaitingMap)
            {
                // A scan dropped by TryClearAnchorCache.
                return;
            }

            awaitingMap = false;
            OnSpatialPersistenceStatusMessage($"Scan finished. {coverage.Summary(Time.realtimeSinceStartup)}");

            // MapFinalized carries only the last update; the whole map comes from TryGetMapData.
            if (!niantic.TryGetMapData(out var mapData))
            {
                FailPending("No map was created. Scan for longer and try again.");
                return;
            }

            var mapId = Guid.NewGuid().ToString();
            try
            {
                WriteSafely(MapPath(mapId), mapData);
            }
            catch (Exception e)
            {
                FailPending($"Could not store the map: {e.Message}");
                return;
            }

            indexes[mapId] = new DeviceMapAnchorIndex { mapId = mapId };
            // The scan's root anchor exists from its first map update; mapping can stop.
            var anchorPayload = niantic.RootAnchorPayload;
            mappingManager.enabled = false;
            if (string.IsNullOrEmpty(anchorPayload) && !niantic.TryCreateRootAnchor(out anchorPayload))
            {
                FailPending("The map was stored, but it has no root anchor to place anchors against.");
                DeleteMap(mapId);
                return;
            }

            if (!TrackRootAnchor(mapId, anchorPayload))
            {
                FailPending("NSDK could not track the new map's root anchor.");
                DeleteMap(mapId);
                return;
            }

            var report = new DeviceMapScanReport
            {
                mapId = mapId,
                mapBytes = mapData.LongLength,
                scanSeconds = Time.realtimeSinceStartup - scanStartedAt,
                pathMetres = coverage.PathMetres,
                newMeshAreaSquareMetres = coverage.NewMeshAreaSquareMetres,
                mapPoints = coverage.MapPoints,
                mapUpdates = coverage.MapUpdates,
                coverage = coverage.Coverage(Time.realtimeSinceStartup),
                storedUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };
            OnSpatialPersistenceStatusMessage($"Stored a new device map ({mapData.Length / 1024f:0} KB). Localising...");
            MapStored?.Invoke(report);
        }

        private void OnMapUpdated(byte[] mapUpdate)
        {
            if (!niantic.IsMapping || !niantic.TryGetMapPoints(mapUpdate, out var points, out var errors))
            {
                return;
            }

            var errorSum = 0f;
            if (errors != null)
            {
                foreach (var error in errors)
                {
                    errorSum += error;
                }
            }

            coverage.AddMapPoints(points.Length, errorSum);
            MapPointsAdded?.Invoke(points);
        }

        private float MeasureMeshArea()
        {
            if (meshManager == null)
            {
                meshManager = UnityEngine.Object.FindFirstObjectByType<ARMeshManager>(FindObjectsInactive.Include);
            }

            return meshManager != null ? ScanCoverage.MeasureMeshArea(meshManager.meshes) : 0f;
        }
        #endregion Scanning

        #region Finding and localising
        private (string mapId, List<string> ids) PrepareFind(SpatialPersistenceSearchArgs[] searchCriteria)
        {
            if (searchCriteria == null || searchCriteria.Length == 0)
            {
                OnSpatialPersistenceError($"[{nameof(TryFindAnchors)}] No search criteria were given.");
                return (null, null);
            }

            LoadIndexes();
            string mapId = null;
            var ids = new List<string>();
            foreach (var searchArg in searchCriteria)
            {
                if (searchArg.spatialPersistenceTrackingType != TrackingType)
                {
                    OnSpatialPersistenceStatusMessage($"Skipped a search that is not for a device map anchor - [{searchArg.spatialPersistenceTrackingType}]");
                    continue;
                }

                if (!searchArg.IsValid)
                {
                    OnAnchorLocatedError(searchArg.anchorID, "A device map search needs the anchor ID.");
                    continue;
                }

                if (!mapOfAnchor.TryGetValue(searchArg.anchorID, out var anchorMapId))
                {
                    OnAnchorLocatedError(searchArg.anchorID, DeviceMapAnchorId.TryGetMapId(searchArg.anchorID, out var missingMapId)
                        ? $"The device map [{missingMapId}] of anchor [{searchArg.anchorID}] is not on this device. It was scanned on another device, or the app's data was cleared."
                        : $"No anchor [{searchArg.anchorID}] is stored on this device.");
                    continue;
                }

                if (IsExpired(searchArg.anchorID))
                {
                    OnAnchorLocatedError(searchArg.anchorID, $"Anchor [{searchArg.anchorID}] has expired.");
                    DeleteAnchors(searchArg.anchorID);
                    continue;
                }

                mapId ??= anchorMapId;
                if (anchorMapId == mapId)
                {
                    ids.Add(searchArg.anchorID);
                }
                else
                {
                    OnAnchorLocatedError(searchArg.anchorID, $"Anchor [{searchArg.anchorID}] belongs to another device map; one map is in use at a time.");
                }
            }

            return (mapId, ids);
        }

        private void Find(string mapId, List<string> ids, TaskCompletionSource<bool> result)
        {
            if (!EnsureStarted())
            {
                foreach (var id in ids)
                {
                    OnAnchorLocatedError(id, $"Cannot load its map: no {nameof(ARDeviceMappingManager)} and {nameof(ARVps2Manager)} in the scene.");
                }

                result?.TrySetResult(false);
                return;
            }

            OnFindAnchorStarted();
            if (mapId == currentMapId && rootAnchor != null)
            {
                // The map is already in use: report what is localised now, the rest when it localises.
                foreach (var id in ids)
                {
                    if (isLocalised)
                    {
                        OnAnchorLocated(id, GetOrCreateAnchorObject(id));
                    }
                    else
                    {
                        pendingFinds.Add(id);
                    }
                }

                result?.TrySetResult(true);
                return;
            }

            AwaiterExtensions.RunCoroutine(LoadAndTrack(mapId, ids, result));
        }

        /// <summary>
        /// Loads a stored map and tracks its root anchor. The anchors are reported when the device localises.
        /// </summary>
        private IEnumerator LoadAndTrack(string mapId, List<string> ids, TaskCompletionSource<bool> result)
        {
            var ok = false;
            try
            {
                if (niantic.IsMapping)
                {
                    ReportFindFailure(ids, "Cannot load a map while scanning.");
                    yield break;
                }

                byte[] mapData = null;
                var path = MapPath(mapId);
                try
                {
                    mapData = File.Exists(path) ? File.ReadAllBytes(path) : null;
                }
                catch (Exception e)
                {
                    ReportFindFailure(ids, $"Could not read the stored map: {e.Message}");
                    yield break;
                }

                if (mapData == null || mapData.Length == 0)
                {
                    ReportFindFailure(ids, "The anchor's device map is missing or empty.");
                    yield break;
                }

                FailPending("Another device map was loaded.");
                StopTrackingMap();
                niantic.ClearMaps();
                // Tracking needs a moment after the previous anchors are removed (as in the NSDK 4 device-map flow).
                yield return new WaitForSeconds(0.2f);

                if (!niantic.TryLoadMap(mapData, out var anchorPayload))
                {
                    ReportFindFailure(ids, "NSDK could not load the stored map.");
                    yield break;
                }

                foreach (var id in ids)
                {
                    pendingFinds.Add(id);
                }

                ok = TrackRootAnchor(mapId, anchorPayload);
                if (!ok)
                {
                    pendingFinds.Clear();
                    ReportFindFailure(ids, "NSDK could not track the map's root anchor.");
                }
            }
            finally
            {
                result?.TrySetResult(ok);
            }
        }

        private bool TrackRootAnchor(string mapId, string anchorPayload)
        {
            currentMapId = mapId;
            isLocalised = false;
            timeoutReported = false;
            trackingStartedAt = Time.realtimeSinceStartup;
            if (!niantic.TryTrackAnchor(anchorPayload, out var anchor))
            {
                currentMapId = null;
                return false;
            }

            rootAnchor = anchor.gameObject;
            return true;
        }

        private void OnAnchorTracking(ARVps2Anchor anchor)
        {
            if (currentMapId == null || anchor == null || anchor.gameObject != rootAnchor || isLocalised)
            {
                return;
            }

            isLocalised = true;
            foreach (var pending in pendingCreates.ToArray())
            {
                CompleteCreate(pending);
            }

            pendingCreates.Clear();
            foreach (var id in pendingFinds.ToArray())
            {
                OnAnchorLocated(id, GetOrCreateAnchorObject(id));
            }

            pendingFinds.Clear();
        }

        private void StopTrackingMap()
        {
            // Disabling the VPS2 manager removes its anchors, so the root anchor GameObject and its children go with it.
            niantic?.StopTracking();
            rootAnchor = null;
            isLocalised = false;
            anchorObjects.Clear();
        }
        #endregion Finding and localising

        #region Anchors
        private void CompleteCreate(PendingCreate pending)
        {
            var id = DeviceMapAnchorId.Compose(currentMapId, pending.Key);
            var relative = ToRootRelative(pending.WorldPose);
            indexes[currentMapId].anchors.Add(new DeviceMapAnchor
            {
                id = id,
                position = relative.position,
                rotation = relative.rotation,
                expiresUnixSeconds = pending.ExpiresUnixSeconds,
            });
            mapOfAnchor[id] = currentMapId;
            SaveIndex(currentMapId);
            var anchor = GetOrCreateAnchorObject(id);
            OnCreateAnchorSucceeded(id, anchor);
            pending.Result.TrySetResult(id);
        }

        private void FailPending(string reason)
        {
            if (pendingCreates.Count > 0)
            {
                OnSpatialPersistenceError(reason);
                foreach (var pending in pendingCreates)
                {
                    OnCreateAnchorFailed();
                    pending.Result.TrySetResult(string.Empty);
                }

                pendingCreates.Clear();
            }

            ReportFindFailure(pendingFinds.ToList(), reason);
            pendingFinds.Clear();
        }

        private void ReportFindFailure(IEnumerable<string> ids, string reason)
        {
            foreach (var id in ids)
            {
                OnAnchorLocatedError(id, reason);
            }
        }

        private Pose ToRootRelative(Pose worldPose)
        {
            var root = rootAnchor.transform;
            var inverse = Quaternion.Inverse(root.rotation);
            return new Pose(inverse * (worldPose.position - root.position), inverse * worldPose.rotation);
        }

        private GameObject GetOrCreateAnchorObject(string id)
        {
            if (anchorObjects.TryGetValue(id, out var existing) && existing != null)
            {
                return existing;
            }

            var stored = FindStored(id);
            var anchor = new GameObject($"Device Map Anchor [{id}]");
            anchor.transform.SetParent(rootAnchor.transform, false);
            anchor.transform.localPosition = stored != null ? stored.position : Vector3.zero;
            anchor.transform.localRotation = stored != null ? stored.rotation : Quaternion.identity;
            anchorObjects[id] = anchor;
            return anchor;
        }

        private void DestroyAnchorObject(string id)
        {
            if (anchorObjects.TryGetValue(id, out var anchor))
            {
                if (anchor != null)
                {
                    UnityEngine.Object.Destroy(anchor);
                }

                anchorObjects.Remove(id);
            }
        }

        private DeviceMapAnchor FindStored(string id)
            => mapOfAnchor.TryGetValue(id, out var mapId) && indexes.TryGetValue(mapId, out var index) ? index.anchors.Find(anchor => anchor.id == id) : null;

        private bool IsExpired(string id)
        {
            var stored = FindStored(id);
            return stored != null && stored.expiresUnixSeconds > 0 && stored.expiresUnixSeconds < DateTimeOffset.Now.ToUnixTimeSeconds();
        }
        #endregion Anchors

        #region Storage
        private void LoadIndexes()
        {
            if (indexesLoaded)
            {
                return;
            }

            indexesLoaded = true;
            var format = IndexFormat;
            var split = format.IndexOf("{0}", StringComparison.Ordinal);
            var pattern = $"{format.Substring(0, split)}*{format.Substring(split + 3)}";
            try
            {
                foreach (var file in Directory.GetFiles(Application.persistentDataPath, pattern))
                {
                    var index = JsonUtility.FromJson<DeviceMapAnchorIndex>(File.ReadAllText(file));
                    if (index == null || string.IsNullOrEmpty(index.mapId) || !File.Exists(MapPath(index.mapId)))
                    {
                        continue;
                    }

                    indexes[index.mapId] = index;
                    foreach (var anchor in index.anchors)
                    {
                        mapOfAnchor[anchor.id] = index.mapId;
                    }
                }
            }
            catch (Exception e)
            {
                OnSpatialPersistenceError($"Could not read the stored device map anchors: {e.Message}");
            }
        }

        private void SaveIndex(string mapId)
        {
            try
            {
                WriteSafely(IndexPath(mapId), System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(indexes[mapId])));
            }
            catch (Exception e)
            {
                OnSpatialPersistenceError($"Could not store the anchors of map [{mapId}]: {e.Message}");
            }
        }

        private void DeleteMap(string mapId)
        {
            if (mapId == currentMapId)
            {
                StopTrackingMap();
                niantic?.ClearMaps();
                currentMapId = null;
            }

            if (indexes.TryGetValue(mapId, out var index))
            {
                foreach (var anchor in index.anchors)
                {
                    mapOfAnchor.Remove(anchor.id);
                }

                indexes.Remove(mapId);
            }

            try
            {
                File.Delete(MapPath(mapId));
                File.Delete(IndexPath(mapId));
            }
            catch (Exception e)
            {
                OnSpatialPersistenceError($"Could not delete the stored map [{mapId}]: {e.Message}");
            }
        }

        /// <summary>
        /// Writes beside the target first, so an interrupted write leaves the previous file intact.
        /// </summary>
        private static void WriteSafely(string path, byte[] data)
        {
            var temporaryPath = path + ".tmp";
            File.WriteAllBytes(temporaryPath, data);
            File.Copy(temporaryPath, path, true);
            File.Delete(temporaryPath);
        }
        #endregion Storage

        #endregion Private Methods
    }
}
