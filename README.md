# Living Room VR

A cross-platform VR / desktop / mobile chore game set in 120 procedurally generated, Cycles-baked living rooms.

## What's in this folder

| Path | What it is |
|---|---|
| `index.html` | The whole game (Three.js r170, WebXR) |
| `rooms/LRxxx.glb` | 120 game-ready rooms (Draco-compressed, PBR textures, interactable objects tagged) |
| `rooms/LRxxx_lightmap.jpg` | Cycles-baked lighting for each room (direct + bounced light, soft shadows) |
| `rooms.json` | Room index (style, size, door, windows, lightmap scale) |
| `thumbs/` | Room preview images for the menu |
| `lib/` | Three.js, Draco decoder and three-mesh-bvh, bundled so the game works offline |

The editable Blender scenes (`blend/`), full-resolution Cycles renders (`renders/`) and the generator
scripts (`tools/lrgen.py`, `tools/run_bake.py`) live one folder up in `Documents/LivingRoomVR`.

## Play

**On this PC:** double-click `Play on this PC.bat` (uses Blender's bundled Python to serve the folder), then
play with keyboard + mouse or a plugged-in Xbox / PlayStation / Switch Pro controller.

**On a Meta Quest / phone / Xbox / smart TV:** the game has to be served over **https**. The easiest free
option is GitHub Pages:

1. Create a public repo (e.g. `living-room-vr`) and upload the contents of this folder.
2. Repo → Settings → Pages → Source: `main` branch, `/ (root)`.
3. Open `https://<your-username>.github.io/living-room-vr/` in the Quest Browser and press **Enter VR**.

The same URL works in phone browsers, the Xbox Edge browser (with a controller) and most smart-TV browsers
that support WebGL 2.

URL shortcuts: `?house=7` jumps straight into house 7, `?room=LR045` loads a single room.

## Controls

| Device | Move | Look / turn | Grab · use | Drop · throw | Tasks |
|---|---|---|---|---|---|
| VR controllers | left stick | right stick (snap), push forward to teleport | grip (also at a distance) / trigger | release grip | X/Y or A/B (wrist panel) |
| VR hand tracking | — | — | pinch | release pinch | — |
| Keyboard + mouse | WASD | mouse | E / left click | Q / right click | Tab |
| Gamepad | left stick | right stick | A / ✕ | B / ○ | Y / △ |
| Phone / tablet | left joystick | drag right side | USE | DROP | TASKS |
| TV remote | ↑ ↓ | ← → | OK | Back | — |

## Chores

Pillows knocked onto the floor, books pulled off the shelves, crooked frames, closed curtains, TVs and lamps
left on, thirsty plants, trash, dirty floor spots and unlit fireplaces. Tools (vacuum, watering can) are on the
cart by the front door; each room has its own bin. Money and finished rooms are saved in the browser.

## Regenerating / re-baking rooms

```
blender -b --python tools/run_bake.py -- 0 120 main --force
```

Each room: build → Cycles render (1600×1000, OptiX denoise) → save editable `.blend` → join static meshes,
smart-UV lightmap, bake diffuse direct+indirect at 2048² → export Draco `.glb` + 1024² lightmap.
About 30 s per room on an RTX 5080.

## Console stores

Native PlayStation / Xbox / Nintendo releases need an approved developer account with each platform holder
(PlayStation Partners, ID@Xbox, Nintendo Developer Portal), their dev kits and certification. The `.glb`
rooms import directly into Unity or Unreal, which is the usual route to those stores.
