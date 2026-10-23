# Living Room VR — Godot 4 version

All 120 Blender/Cycles-baked living rooms, playable in Godot 4.4+ (written for 4.4 and 4.5).

## Run it
1. Install **Godot 4.4 or newer** (standard build, not .NET) from https://godotengine.org/download
2. Open Godot → **Import** → pick `project.godot` in this folder → **Import & Edit**
3. Press **F5** (Play).

Rooms are streamed at runtime from `rooms/` (`LRxxx.glbdata` = plain glTF binary, `LRxxx_lightmap.lmjpg` = Cycles lightmap),
so there is no long import step. The rooms are included in the **LivingRoomVR-Godot-Project.zip** download (the source in the repo omits them).

## Export (Windows / Android APK / Web / Linux)
`Project → Export…` — presets are already set up and include the `rooms/*` files.
Godot asks you to download its **export templates** the first time (Editor → Manage Export Templates → Download).
Android export also needs the Android SDK path and a debug keystore set in Editor Settings.

## Controls
| | Keyboard / mouse | Gamepad | Touch |
|---|---|---|---|
| Move | WASD / arrows (Shift = run) | left stick | drag on left side |
| Look | mouse (click to capture) | right stick | drag on right side |
| Do chore | E / Space / click | A | tap the object |
| Next / previous room | N / B | RB / LB | **Next ▶** button |
| Room list | M / Tab | Start | **Rooms** button |

## How it works
* `main.gd` – loads a room with `GLTFDocument`, swaps the baked shell (`STATIC`) to `shaders/baked.gdshader`
  (albedo × Cycles lightmap on UV2), adds floor/furniture colliders from the Blender footprints stored in glTF extras,
  turns `INT_*` objects into chores and builds the HUD.
* Chores: put pillows/books back, straighten frames, turn off lamps/TV, water plants, pick up trash.
