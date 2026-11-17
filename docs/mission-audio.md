# Dalek mission intro (0.12.35)

The original `assets/sounds/Dalek-Exterminate.mp3` is converted to mono 48 kHz signed PCM16 WAV and embedded as `MikuGlider.Audio.DalekExterminate`. The runtime needs no external file, decoder process or media player.

## Native trigger

Both the exported game prefab `Regulated Rampage Objective.prefab` and the `AmalgamationObjective` component in `Scenes/Mission Levels/Desert/DesertAmalg.unity` set their first `ObjectiveInfoList.title` to the localization key `o_kill` (KILL in English). The latter component's script GUID is `7f10f83bd6c8f1fb220ec87af3e949ce`, matching `AmalgamationObjective.cs.meta`. The call path is:

`MissionManager.ActivateObjective` → the objective's `Activate` → `ObjectiveBase.Activate` → `MoveNext` → `MissionHUD.AddObjective` → `MissionHUD.ShowNewObjective`.

The patch observes an accepted `ShowNewObjective` call, preserving the native empty-text and `overrideCurrent` early exits. It uses `IObjective` without an objective-class allowlist and requires the first main objective's first localized title to equal both the displayed popup and `o_kill`. Side objectives, completion messages, later objectives, ended missions and `MissionManager.LateJoined` are excluded. Matching the main title also excludes a later nested `SanitationKillObjective` popup under a different main objective. It does not inspect rendered pixels or translate literal English text.

Version 0.12.34 incorrectly restricted this to `RegulatedRampageObjective`, so Amalgamation Hunt had no chance of playing. A regression test failed against that implementation and passes after removing the type restriction. It also covers an unrelated future objective class to prevent replacing the old restriction with another finite list.

Each eligible `MissionManager` instance gets one attempt, including failed rolls and disabled configuration. A separate `System.Random.Next(2)` selects one of two equally likely outcomes; gameplay random generators are untouched. This is local cosmetic playback, with independent rolls on each modded client, and no network broadcast. It works with any selected skin. Every first eligible attempt logs its objective class and `roll=play (50%)`, `roll=skip (50%)`, or `disabled by config`; repeated popups neither reroll nor repeat the log.

## Native playback

`AkAudioInputManager.PostAudioInputEvent("Play_VideoAudio", PlayerLook.Instance.gameObject, ...)` feeds PCM samples directly to Wwise. The installed `SoundbanksInfo.json` identifies this existing event in both Main and Menu banks with the **Wwise Audio Input** source plugin (`LibId=13107202`). The existing native game component `VideoAudioToWwise` also uses this event; `DiscordWwiseBridge` confirms the Audio Input callback and mono format contract.

The callback reads immutable decoded samples without Unity calls, allocations or disk access. The last partial buffer is zero-padded and delivered before returning end-of-stream. Shutdown stops only the playing ID created by this feature. Audio errors are logged and do not interrupt objective creation. Existing mission text and announcement audio are retained.

## Configuration

In `BepInEx/config/horse.mycopunk.mikuglider.cfg`:

```ini
[Audio]
EnableDalekMissionIntro = true
```

Set it to `false` and restart the game to disable the sound. The chance is fixed at 50%.

## Reproduction and checks

```powershell
ffmpeg -nostdin -y -i assets/sounds/Dalek-Exterminate.mp3 -map_metadata -1 -ac 1 -ar 48000 -c:a pcm_s16le assets/sounds/Dalek-Exterminate.wav
dotnet build MikuGlider.csproj -c Release
dotnet build tests/MissionAudio/MissionAudio.csproj -c Release
tests/MissionAudio/bin/Release/net48/MissionAudio.exe scratch/mission-audio/test.cfg
```

The test directory must exist. The suite checks actual BepInEx configuration persistence, Harmony dispatch against a native-method contract fixture, successful and failed coin tosses, duplicate suppression, all excluded conditions, localization, corrupt WAV rejection, and sample-exact playback including the last buffer. Game/Wwise fixtures are explicitly marked; this is not a live game audio test.

Release build: zero warnings/errors. The six model/correction resources retain their 0.12.33 hashes and the embedded WAV retains its 0.12.34 hash. The converted audio is approximately 2.19 seconds. The user's 0.12.34 log contains a selected Rampage event with Wwise playing ID 2030; it cannot establish audibility on its own. No live mission was run by the agent for 0.12.35. A selected roll now logs, for example, `Dalek mission intro: KILL (AmalgamationObjective); roll=play (50%); Wwise playing ID ...`.
