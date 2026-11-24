# Luminous Tides

Single-player, first-person alien shooter vertical slice. Unity 6 (6000.3) with URP.

## Play

Open the project in Unity 6000.3, open `Assets/Scenes/Main.unity` and press Play,
or run `Build/LuminousTides.exe` after building (menu **Luminous Tides > 4. Build Windows Player**).

| Input | Action |
|---|---|
| WASD / mouse | Move / look |
| Left Shift | Sprint |
| Space | Jump |
| Left Ctrl or C | Crouch |
| Left mouse | Fire |
| Right mouse | Aim down sights |
| R | Reload (restart after winning) |
| Q | Dash: a short burst of speed in the direction you are moving (4 s cooldown) |
| E | Pulse: shockwave that damages and staggers nearby enemies (12 s) |
| G | Grenade: bounces once, bursts on a timer or on hitting an enemy (9 s) |
| F | Tidal Surge (super): charges from kills and damage; a huge shockwave that also restores you |
| 1 / 2, mouse wheel | Swap between the pulse carbine and the breaker shotgun |
| Esc | Release the cursor |

The mission has three parts: fight through to the rock arch, hold the survey station's uplink pad while it
charges and waves of enemies arrive, then kill the Tide Alpha. Dying returns you to the start of the current part.

Movement: Space again in the air is a double jump; crouch while sprinting to slide, and jump out of a slide to keep its speed.
Glowing orange spore pods explode when shot and hurt enemies far more than you.

Enemies: Tide Bursters sprint in and explode (shoot them early and they take their pack with them). Tide Stalkers charge and claw, Tide Spitters keep their distance and spit bolts you can sidestep or dash
through, and yellow-barred elites are tougher. Enemies sometimes drop ammunition; walk near it to collect it.

## How the level is made

The scene is generated from code, so it is never edited by hand:

1. **Luminous Tides > 1. Setup Project** creates the URP pipeline asset.
2. **Luminous Tides > 2. Build Level** (`Assets/Editor/LevelBuilder.cs`) builds terrain, props, enemies,
   player, HUD and navmesh, and saves `Assets/Scenes/Main.unity`.
   Any model at `Assets/Art/Models/<name>.fbx` replaces its primitive placeholder.
3. **Luminous Tides > 3. Capture Screenshots** renders previews into `Screenshots/`.

From a terminal: `Unity.exe -batchmode -quit -projectPath . -executeMethod ProjectSetup.SetupAndBuild`

## Art pipeline (`ArtSource/`)

| Step | Tool | Script |
|---|---|---|
| Reference images in the style of `refs/style_sheet.webp` | ChatGPT via Codex CLI | `gen_refs.sh` |
| Image to textured 3D mesh (Smart Topology) | Meshy API | `meshy_gen.py` (needs `MESHY_API_KEY`) |
| Scale, ground and export static props | Blender | `blender/process_static.py` |
| Rig, skin and animate the enemy (Idle, Run, Attack, Hit, Death) | Blender | `blender/rig_enemy.py` |
| Pose the arms on the carbine for the first-person view model | Blender | `blender/build_fp.py` |
| Hand-made rock, grass, coral and planet | Blender | `blender/make_props.py` |

Run Blender scripts headless: `blender -b --python ArtSource/blender/<script>.py`
(`make_props.py` also takes the output folder: `-- Assets/Art/Models`).

## Smoke test

`Build/LuminousTides.exe -autotest` plays a scripted fight, walks into the objective, writes
`Screenshots/play_*.png` and exits with code 0 when the mission completes.
