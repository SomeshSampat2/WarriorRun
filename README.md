# WarriorRun

**WarriorRun** is a free and open-source endless-runner game built with Unity 6 (URP).
Sprint through forests, canyons, city streets, temples and lagoons — dodge barriers,
leap boulders, slide under fences — while the world is dressed with real
photogrammetry-scanned assets instead of cartoon props.

> This project is open source. Read it, fork it, learn from it, ship your own runner.

## Download & Play

- **Android:** grab the APK from
  [Releases](../../releases/latest) → `WarriorRun.apk` (~168 MB).
  Sideload it: copy to the device, tap, allow "install unknown apps" when asked.
- **iOS:** no downloadable build — iOS apps can't be sideloaded without Apple
  signing. To run it on an iPhone/iPad, build from source: install Unity's
  **iOS Build Support** module, switch platform to iOS, and build an Xcode
  project, then archive/sign it with your Apple ID in Xcode.

## Gameplay

- Classic 3-lane endless runner: swipe to switch lanes, jump, and slide
- Zones with distinct moods — Forest, Canyon, City, Temple, Vault, Water
- Zone-specific obstacles and scenery, difficulty ramps with distance
- Coins, power-ups, score chasing — runs until you crash
- Menu diorama with animated scenery, parallax, and ambient audio
- Animated loading screen between scenes — the game world streams in
  asynchronously behind a spinner + progress bar, so transitions never hitch

## Tech Stack

- **Engine:** Unity `6000.6.3f1`
- **Render pipeline:** Universal Render Pipeline (URP 17.6.0)
- **Backend:** IL2CPP, Android (arm64-v8a)
- **Assets pipeline:** glTFast 6.20.0 runtime imports of `.gltf` models
- **Input:** Unity Input System (touch swipe gestures + keyboard in editor)
- **Audio:** Unity Audio + TextMeshPro UI

## Realistic Art Direction

Every obstacle and environment prop is a real-world-scanned or procedurally
built realistic asset — no cartoon primitives, no googly eyes:

- **PolyHaven CC0 photogrammetry** — trees, rocks, stumps, benches, hydrants,
  street lamps, crates, statues (2K→1K PBR texture sets)
- **Blender-generated obstacles** — jersey barrier, police barrier, fallen log,
  boulder, sandstone block, spikes, traffic cone, dumpster, stump, broken pillar
  (`blender_make_assets.py` — procedural geometry + scanned PBR textures)
- **HDRI sky** — PolyHaven `kloppenheim_02` pure-sky HDRI drives skybox + ambient
- **Post-processing** — SSAO + URP bloom/color grading

### Performance notes

The photogrammetry source meshes were aggressively optimized for mobile:

- Mega-scans (a 900 MB pine tree!) replaced with light stand-ins
- All remaining scans decimated in Blender (`blender_decimate.py`) —
  e.g. the shared tree went 874K → 119K vertices
- Decor footprints are programmatically clamped so nothing spills onto the lanes
- Textures capped at 1K at import

APK size: **~168 MB**.

## Project Layout

```
Assets/WarriorRun/
  Scripts/        runtime gameplay — TrackManager recycling, player, input, audio
  Editor/         WarriorRunBuilder (procedural pipeline) + RealisticUpgrade
                  (asset swap / footprint / light build tooling)
  Prefabs/        tiles, obstacles, decor, player
  Scenes/         Menu.unity + Game.unity
  Art/Realistic/  PolyHaven scans + Blender-generated glTF models, 1K PBR textures
blender_make_assets.py   generates the realistic obstacle set (headless Blender)
blender_decimate.py      decimates scan meshes to mobile budgets
```

## Building

1. Install Unity Hub + Unity `6000.6.3f1` with the **Android Build Support** module
   (SDK/NDK + OpenJDK).
2. Clone the repo and open it in Unity.
3. Menu ▸ **WarriorRun → Rebuild All** regenerates procedural content
   (scenes, materials, prefabs) if needed.
4. **Build ▸ Build** targeting Android produces `Builds/Android/WarriorRun.apk`.

Headless/CI build:

```bash
Unity -batchmode -nographics -projectPath <repo> \
  -executeMethod WarriorRun.EditorTools.WarriorRunBuilder.BuildAndroid -quit
```

## Asset Credits & Licenses

- **Code:** MIT — see [LICENSE](LICENSE).
- **PolyHaven assets** (models/textures/HDRIs): **CC0 1.0** — public domain,
  no attribution required. https://polyhaven.com
- **Music:** "Action Chiptunes" (Levels 1–3 + Title) — Juhani Junkala /
  SubspaceAudio, **CC0 1.0** — no attribution required.
  https://opengameart.org/content/5-chiptunes-action
- Fonts and remaining template art ship with their original licenses.

## Contributing

Issues and PRs welcome — new zones, obstacles, runner mechanics, perf work, audio.
Keep the visual bar realistic: scanned or convincingly procedural assets only.
