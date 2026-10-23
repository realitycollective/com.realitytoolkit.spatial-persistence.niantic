# Reality Toolkit - Spatial Persistence Niantic Device Mapping Module

A [Spatial Persistence](https://github.com/realitycollective/com.realitytoolkit.spatial-persistence) module that scans a space into a map stored on the device and relocalises against it, using the device mapping of the [Niantic Spatial SDK](https://www.nianticspatial.com/docs/nsdk/) (NSDK 4).

This is Niantic device mapping, not the Niantic cloud. Maps are made and kept on the device. The module never turns on Niantic VPS maps or universal localisation, which use the Niantic cloud.

## Requirements

- Unity 6000.0 or later, with AR Foundation 6.4 or later.
- `com.nianticspatial.nsdk` 4.0 or later, added from its git URL. The module compiles only when NSDK is present: its assemblies define `NIANTIC_NSDK_4` from the NSDK package version.
- In the NSDK settings, device mapping and VPS2 turned on. VPS2 does the localisation against device maps.
- In the AR scene, an `ARDeviceMappingManager` and an `ARVps2Manager`, best left disabled. The module finds them, configures them and enables them when needed. An `ARMeshManager`, if present, adds new mesh area to the scan coverage.

## Installation

Add the package from its git URL or from OpenUPM:

```text
    openupm add com.realitytoolkit.spatial-persistence.niantic
```

The package installer adds the module to the Spatial Persistence service profile for Android and iOS. The default profile is in `Assets~/Profiles`.

## How it works

The module adds the `SpatialPersistenceTrackingType.DeviceMap` tracking type. Every operation goes through the Spatial Persistence service, with the tracking type overloads.

| Operation | Call | What happens |
|---|---|---|
| Create | `TryCreateAnchor(position, rotation, timeToLive, SpatialPersistenceTrackingType.DeviceMap)` | With no map in use, the first create scans the space into a new map. Progress goes out as `SpatialPersistenceStatusMessage`. The scan finishes by itself when its coverage is complete. `CancelAnchorOperation` finishes it early and keeps what was captured. The map is stored, the device localises, and the create completes with `CreateAnchorSucceeded`. With a map in use, a create completes straight away. |
| Find | `TryFindAnchors(new SpatialPersistenceSearchArgs(SpatialPersistenceTrackingType.DeviceMap, anchorId))` | Loads the anchor's map and raises `AnchorLocated` once the device localises. One map is in use at a time. |
| Move | `TryMoveAnchor(anchoredObject, position, rotation, anchorId, SpatialPersistenceTrackingType.DeviceMap)` | Stores the new pose. The map must be in use and localised. |
| Delete | `TryDeleteAnchors(SpatialPersistenceTrackingType.DeviceMap, anchorIds)` | Removes the anchors. Deleting a map's last anchor deletes the map. |
| Clear | `TryClearAnchorCache()` | Drops a scan in progress and stops using the current map. Stored maps are kept. |

Only the map's root anchor is tracked by NSDK. Every other anchor is a pose relative to it, kept in an index file beside the map. The anchor GameObjects are children of the root anchor, so content placed under them stays rigid with the map.

An anchor ID names its map: `{mapId}/{anchorKey}`. `DeviceMapAnchorId` builds and reads it. A search with only the anchor ID therefore knows which map it needs.

`INianticSpatialPersistenceServiceDeviceMappingModule` adds what the service interface has no place for:

- `IsScanning`, `IsLocalised`, `RootAnchor` and `Coverage` describe the scan and the map in use;
- `MapPointsAdded` gives each map update's feature points, for a scan overlay;
- `MapStored` reports each stored map's size and the scan's coverage as a `DeviceMapScanReport`;
- `IsAnchorStored` and `GetStoredAnchorIds` list what is on the device.

Get the module with `ISpatialPersistenceService.TryGetModulesByTrackingType(SpatialPersistenceTrackingType.DeviceMap, out var modules)`.

## Storage

Maps and anchor indexes are files in `Application.persistentDataPath`. Their names come from the profile (`{0}` is the map ID). The module writes each file beside its target first, so an interrupted write leaves the previous file intact.

Uninstalling the app or clearing its data deletes the maps. Maps stored by ARDK 3 still load, because NSDK converts them the first time.

## Also in the package

- `ScanCoverage` measures how complete a scan is from time, distance walked and new mesh area.
- `ScanEffects` draws a scan overlay on the AR mesh, with the silhouette materials in `Runtime/Visuals`.
- `NianticFeatureGate` keeps NSDK meshing and semantics on only while a flow needs them.
