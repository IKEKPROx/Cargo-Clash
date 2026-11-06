# Cargo Clash — Unity 6.5

Open this folder with Unity `6000.5.5f1`, then open:

`Assets/CargoRover/Scenes/CargoRover.unity`

## Deliverables

- `Assets/CargoRover/Prefabs/Vehicle.prefab` — compact cabin with a fully visible windshield, raised ladder-frame chassis, three axles, simplified suspension without control arms, and six low-poly wheels with shallow staggered tread rows, hub rings, center caps, and lug details. The cargo bed uses its existing floor, side panels, rear panel, and front support as a continuous open-top enclosure joined to the cab.
- `Assets/CargoRover/Prefabs/CargoBox.prefab` — reusable independent cargo box with lid, straps, corner guards, and inset side panels. It intentionally has no top handle.
- `Assets/CargoRover/Scenes/CargoRover.unity` — assembled preview scene with one vehicle and two independent cargo-box prefab instances.
- `StartMenuCanvas` and `StartMenuController` in the scene — heavy, slightly irregular white **Cargo Clash** title, square-cut **GO** button, and a high camera that follows the rover while holding one angle. The title uses the Mac's Impact font at runtime when present (with fallbacks); no OS font file is redistributed.
- `Assets/CargoRover/Scripts/RoverTerrainAnimator.cs` — animation-oriented six-wheel terrain-following rig. Each wheel uses a tire-radius sphere cast against the ground collider, keeping its tread above uneven terrain; the body eases into heave, pitch, and roll, while wheels rotate and the visible axles/shocks follow their positions.
- `Assets/CargoRover/Scripts/CargoRoverIntroCamera.cs` — route-synchronised cinematic camera. It opens high and wide with the rover occupying only a small part of the frame, follows a smooth curved descent and changing lens, and settles into a tight elevated side view of both cargo boxes, deliberately cropping most of the vehicle. Its focus point is attached to the vehicle body rather than either box, leaving room for a future cargo ejection shot.
- `Assets/CargoRover/Scripts/CargoClashEndlessTerrain.cs` — streams nine 64-metre terrain chunks, each with a MeshCollider, around the moving rover. Chunks are recycled as the rover advances; there is no route-end teleport or visible X edge. The static `PreviewEnvironment/TestTerrain_ReplaceWithYourTerrain` remains visible for scene editing and is hidden during Play. Use **Tools > Cargo Rover > Regenerate Preview Terrain** after changing the shared height function.
- `Assets/Editor/CargoRoverBuilder.cs` — deterministic editor builder available from **Tools > Cargo Rover > Build Final Model**.
- `Assets/Editor/CargoClashStartMenuBuilder.cs` — adds or repairs the menu through **Tools > Cargo Rover > Build Start Menu UI**. Rebuilding the full model also adds the menu.

## Preview the start menu and terrain response

Open the scene and press **Play**. Until **GO** is clicked, the rover drives straight while the menu camera translates with it and keeps the same high angle. Terrain chunks stream around the rover as it advances; the vehicle does not jump back to a starting line. Wheel rotation is driven by actual travel distance, and each tire follows ground contact without the previous downward lag. Press **GO** to hide the menu and hand off to the existing route-synchronised cinematic preview: the rover drives from world Z = 5 to Z = -5 (about 8.3 seconds at the default speed), and the camera descends toward the cargo. This is a UI-to-CG hand-off only; actual gameplay still needs to be implemented. `StartMenuController` exposes an **On Go Pressed** event for that future transition. Press Play again to restart the preview. Select `Vehicle` and adjust `Rover Terrain Animator` in the Inspector to tune speed, suspension travel, body response, and maximum tilt. Select `PreviewEnvironment/Main Camera` to adjust the four camera position and focus keys, starting/ending field of view, and settling response. For a separately animated route, turn off **Animate Route**; the terrain-following rig will still run as the vehicle root moves, but this preview camera uses route progress and will need another progress source for a custom route.

The final terrain is **not required** to set up the rig. When replacing this procedural terrain with a level map, disable `CargoClashEndlessTerrain` on `PreviewEnvironment` and disable or remove `TestTerrain_ReplaceWithYourTerrain`; add the new Unity Terrain or mesh with an enabled TerrainCollider/MeshCollider, and set **Terrain Layers** on `Vehicle` to include that ground layer. Keep the rover above the ground and tune the travel and body response values for the new terrain. If a custom route changes heading, animate the `Vehicle` root's rotation as well.

The two cargo boxes remain separate scene roots and prefabs. The preview script follows them with the body visually; disable that attachment in the script before adding independent cargo physics.

## Physics status

No Rigidbody or WheelCollider components are included. This is a cinematic/visual terrain response, not a dynamic driving simulation. The vehicle and cargo boxes are separate root objects so Rigidbody, colliders, wheel physics, and cargo attachment logic can be added later without restructuring the model. At that point, replace or disable the preview animator's wheel and cargo positioning to avoid two systems driving the same transforms.

Suggested future setup:

1. Add one Rigidbody and compound colliders to the `Vehicle` root.
2. Add a Rigidbody and BoxCollider to each cargo-box root.
3. Use joints or a scripted attachment system if cargo should be secured to or released from the bed.
