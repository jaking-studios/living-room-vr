# Living Room VR — Unity 6 version

All 120 Blender/Cycles-baked living rooms, playable in **Unity 6** (built and tested with 6000.0.47f1, Built-in render pipeline).

## Run it
1. Unity Hub → **Add project from disk** → pick this folder → open with Unity 6 (6000.0.x or newer).
2. First open installs **glTFast** automatically and runs *Living Room VR → Set Up Project* (linear colour, scene, build settings).
3. Open `Assets/LivingRoomVR/Main.unity` and press **Play**.

Rooms load at runtime from `Assets/StreamingAssets/rooms/` (`LRxxx.glb` + `LRxxx_lightmap.jpg`). They are included in the **LivingRoomVR-Unity-Project.zip** download (the source in this repo omits them to keep it small).

## Build
Menu **Living Room VR → Build Windows / Build Android APK / Build WebGL** (needs the matching platform module in Unity Hub).
Command line: `Unity.exe -batchmode -projectPath . -executeMethod LivingRoomVR.EditorTools.ProjectSetup.BuildWindows -quit`

## Controls
| | Keyboard / mouse | Gamepad | Touch |
|---|---|---|---|
| Move | WASD / arrows (Shift = run) | left stick | drag on left side |
| Look | mouse (click to capture, Esc to release) | right stick | drag on right side |
| Do chore | E / Space / click | A | tap the object |
| Next / previous room | N / B | RB / LB | **Next ▶** button |
| Room list | M / Tab | Start | **Rooms** button |

## How it works
* `LivingRoomGame.cs` – loads a room with glTFast, swaps the baked shell (`STATIC`) to `LRVR/Baked`
  (albedo × Cycles lightmap on UV2), adds floor/furniture colliders from the Blender footprints in glTF extras,
  turns `INT_*` objects into chores and draws the HUD.
* `LRMaterialGenerator.cs` – builds glTF materials with the project's own small shaders, so player builds don't depend on glTFast shader variants.
