# Asterion — original presentation-inspired vehicle

Original, editable Blender geometry for Drive Roadster. This is an independently authored game vehicle, not a Tesla model extraction, licensed replica, or manufacturer-endorsed product. The game proportions, physics, seating anchors and controls are design choices, not real vehicle specifications.

## Files
- `DR_Asterion_LOD0.fbx` and `.glb`: detailed external and cockpit model
- `DR_Asterion_LOD1/2.fbx` and `.glb`: reduced visual models, same anchors and pivots
- `DR_Asterion_Collider.fbx`: independent simple convex-friendly chassis proxy; never use the detailed vehicle as its collision mesh
- `Textures/`: original generated tileable carbon base-color and roughness maps; other surfaces use PBR scalar material settings
- `vehicle_manifest.json`: independently re-imported GLB geometry measurements
- `vehicle_source_manifest.json`: source scene metadata

Editable source and deterministic generation script: `SourceAssets/Vehicle/DR_Asterion.blend`, `build_vehicle.py`. Review-only scene includes a studio floor, lights and camera, which are excluded from the vehicle exports. The cockpit revision was exported and re-imported with Blender 5.1.0; earlier exterior review images used the previous authoring environment. Add `-- --export-only` to skip studio rendering. Run `blender -b -t 8 --python SourceAssets/Vehicle/build_vehicle.py`, then `blender -b --python SourceAssets/Vehicle/validate_vehicle.py`.

## Coordinates / assembly
1 Blender unit = 1 metre. Blender +Z is up, -Y is forward. FBX is exported with -Z forward / +Y up and GLB with its standard Y-up conversion. The intended Unity model convention is +Z forward, +Y up. The root is at ground level under the vehicle centre. Unity import orientation remains a required integration check.

Wheel pivots: `Wheel_FL/FR/RL/RR`; Unity positions x ±0.88, y 0.35, z +1.42 front / -1.38 rear. Nominal visual radius 0.35 m. All meshes belonging to a wheel are parented to its pivot. Rotate around its local axle, not world space. Steering and wheel-spin logic are intentionally external.

Unity driver anchor: (-0.395, 0.53, -0.12); eye reference: (-0.395, 1.08, -0.08); exit: (-1.4, 0.10, -0.12). These are initial art integration markers, not verified VRCStation avatar positions. Passenger is artwork plus anchor only. `SteeringWheel`, `Pedal_Throttle`, `Pedal_Brake` and `AeroWing` are separate transforms. The first three are animated from driving input in Unity, not hand/foot tracking. Thruster markers identify rear and downward FX attachment locations.

## Reference and interpretation
The current official Roadster page was visually inspected on 2026-10-17: https://www.tesla.com/ja_jp/roadster (browser redirected to the English regional page). Observed design cues: silver/white angular wedge body, a low copper-tinted canopy, narrow diagonal headlamps, black lower aero surfaces, thin wide red taillight, white/grey cabin, squared yoke, central display, large rear wing and rear-facing circular mechanisms. Original geometry interprets these cues rather than tracing images or copying mesh data. No Tesla logos or official imagery are included as distributed textures.

The earlier red open-top study was a provisional design and is superseded. The 2017 event URL was a research lead, not evidence of the current vehicle. Video frame observations and their limits are documented separately when available. Unseen underside, tyre construction, mechanical internals, cabin instruments and game FX details are creative interpretation. Flight/wall-ride performance is not inferred from still images.

## Materials and performance
Nine PBR materials cover alloy body, carbon, rubber, metal, ivory cabin, copper glass, white LED, red LED and cyan instrument displays. Transparency/transmission and emission need explicit Unity shader setup; Blender material equivalence is not guaranteed by FBX. Carbon maps are 512 px tileable textures rather than full-body 2K atlases. Normal detail is mostly actual geometry. LODs are geometric reductions; materials/renderer counts are not automatically reduced. Check draw calls and LOD transition quality in Unity before adding four vehicles.

## Verification boundaries
Blender export, re-import and rendered appearance checks are distinct from Unity validation. Unity import, Udon compilation and scripted ClientSim section tests are documented in `docs/validation/UNITY_VALIDATION.ja.md`. Actual VRChat upload, avatar-specific station alignment, multiple-client ownership, VR comfort and GPU budget remain unverified. Never describe these assets as VRChat-tested or the whole world as playable solely on these exports.

### Actual video observations
The vehicle artist also visually reviewed browser-captured frames from these official Tesla posts, in addition to the official page stills:
- https://x.com/Tesla/status/2110913438293647789 at 17.704 s: silver vehicle at ramp departure, narrow pale blue-white jets from the forward area angled outward; at approximately 25.7 s: a large white landing gas cloud.
- https://x.com/Tesla/status/2110926164306931860 at 21.608 s: vehicle airborne beside a truck, with multiple downward white/pale-blue jets. Silver body, dark copper canopy, red rear light and rear wing agree with the page stills.

These are observations of footage, not independently measured performance, proof of a propulsion mechanism, or exact nozzle coordinates. Forward vector and underbody nozzle locations in this original asset are interpreted game attachment points. Renderable flame meshes are intentionally absent: Unity should use restrained white/pale-blue jets plus a diffuse mist; verify VR visibility and performance. Official frames are reference material only and are not included in the distributable asset package.

The live cockpit UI and five Interact controls are created by `RoadsterCockpitSetup.cs`; they are not baked labels in the FBX. The source model includes game-designed dashboard housings, console, vents and trim. Historical `DR_Asterion_Review.blend` and studio renders predate this interior revision.
