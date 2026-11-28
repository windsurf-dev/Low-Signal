# Low Signal

Open this folder as a Unity project. The editor version is recorded in `ProjectSettings/ProjectVersion.txt`.

## Where to work

```text
Assets/
  _LowSignal/                  Game-owned content
    Scenes/                   UnderwaterCity, the playable city scene
    Scripts/
      Player/                 Movement and the shared energy pool
      Abilities/              Flashlight and future powered abilities
      UI/                     Robot HUD
    World/
      UnderwaterCity/
        Editor/               City construction and repair tools
        Materials/            City materials
        Meshes/               Shared city geometry
        Prefabs/Buildings/    Editable building prefabs and their combined mesh assets
      SeaFloor/               Seafloor models and materials
    Settings/                 Input actions and rendering settings
  Resources/RobotHUD/          Fonts and icons loaded by Resources.Load
  ThirdParty/UnityTemplate/    Starter scene and Unity tutorial content
Packages/                     Package dependency definitions
ProjectSettings/              Shared Unity configuration
```

The HUD is built by `RobotHUD.cs`; its fonts and icons stay under `Resources/RobotHUD` because the script loads them by resource path. `PlayerEnergy.cs` owns shared energy; each ability owns its consumption settings.

Start from `Assets/_LowSignal/Scenes/TitleScreen.unity`. Start Game creates a fresh single-slot save after confirmation if needed; Load Game restores the saved area, player, abilities, items, and guardian puzzle. During gameplay, **Escape** (or controller Start) opens Resume / Load Last Save / Return to Title. New games and area entries autosave as checkpoints. There is no quick-save or pause-menu saving. Manual saving will be available at future recharge stations; additional checkpoints will be placed explicitly later. Returning to title discards progress since the last checkpoint.

A new game opens with the crash intro (`ShallowsCrashIntro`): the scout ship dives through the surface and skids into its wreck, the robot reboots to SIGNAL: LOW, climbs out of the hatch and backs away to the start, where play begins. It plays only after Start Game, never on Load Game, and can't be skipped. It adds itself at runtime and works out its shots from the wreck and the player's start, so the Shallows scene needs no setup; to tune its timing or reboot text, add the component to any object in the Shallows. **Low Signal > Shallows > Preview crash intro** plays it without touching the save. Because the ship moves, `ShallowsCrashIntroBuild` takes static batching off the ship's parts whenever the scene is built or played.

Save files are versioned JSON at `Application.persistentDataPath/savegame.json`, with the previous good save at `savegame.json.bak`. `SaveGameService` starts automatically, so scenes require no new components. Use `SaveGameService.Instance.TravelTo(...)` for future area transitions to preserve the departing area's state and autosave at the destination. Scene props remain in their own area; transient motion and sonar cooldowns restart on load. Object identities currently use scene hierarchy paths, so reordering or renaming saved props can invalidate their saved state during development.

City authoring tools are available under **Low Signal > City**. Surface cleanup stores unique geometry in `CitySurfaceMeshes.asset`, tracked through Git LFS, and reuses identical meshes. Do not generate a separate mesh asset for every scene object.

UnderwaterCity's 65 buildings are prefab instances. Each prefab groups its static visuals into Shell, Facade, Roof, and (where present) Interior meshes. New generated city copies reuse this library.

## Local files and samples

`Library`, `Temp`, `Logs`, and `UserSettings` are local Unity files ignored by Git. `LocalArchive` is also ignored: it holds unused terrain samples, unused terrain data, and the retired flashlight meter for recovery, outside Unity's asset imports. It is not required to open or build the game and is not distributed with clones.

The Unity template scene remains in Build Settings with its previous enabled state. It is separate from the playable city scene.

Move assets through Unity's Project window whenever possible. Preserve `.meta` files when moving assets outside the editor: their GUIDs connect scenes, scripts, and materials. City editor scripts also contain asset paths that must match any future folder moves.

Install Git LFS before cloning or pulling large assets (`git lfs install`).
