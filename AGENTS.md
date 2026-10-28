# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
the Linux packages it needs; this file covers how the code is put together and the rules that keep it working.

## What this is

A recreation of Atari's 1983 vector arcade game Star Wars (space, the Death Star's surface, and the trench), in C#
on .NET 10 and Avalonia 12. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM).
Every model is a list of 3D lines drawn in code. The sound effects are synthesised by `tools/make_sounds.py`, and
the spoken lines are made by `tools/make_voice.sh`.

## Commands

```bash
dotnet build AVAWars.slnx                 # the solution holds the one project, AVAWars.csproj, at the root
dotnet run --project AVAWars.csproj       # play it (no command-line options)
python3 tools/make_sounds.py              # rebuild the effects in Sounds/ (standard-library Python only)
tools/make_voice.sh                       # rebuild Sounds/Voice*.wav: needs Piper and the en_GB-vctk-medium voice
```

There is no `global.json`, so any .NET 10 SDK builds it. There are no tests yet. `make_voice.sh` reads `PIPER`,
`VOICE` and `SPEAKER` to find the Piper binary, the voice model and the speaker, and runs each line through
`tools/crunch_voice.py` (`crunch_voice.py in.wav out.wav`) for the speech-chip sound.

## Layout

Everything is at the repository root; there is no `src/` folder.

- `Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract mode, play, high scores), the waves and shields, and the
    frame update.
  - `GameEngine.Space.cs`, `GameEngine.Surface.cs` and `GameEngine.Trench.cs` are the three phases of the attack
    run.
  - `AttractPilot.cs` flies the ship in attract mode.
  - `InputHandler.cs` (keys and mouse) and `ControllerManager.cs` (SDL2 gamepads) give the yoke and the fire
    buttons.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `Models/`: `Vec3`, `Camera` (the cockpit projection), `Wireframe` (line models, nose along +Z), `Entities` (TIE
  fighters and the other things in 3D) and `Explosion`.
- `Views/`: `GameCanvas`, split over partial files, draws the fixed 1024x768 screen and runs the 60 Hz
  `DispatcherTimer`. `GameCanvas.World.cs` draws the 3D world, `GameCanvas.Screens.cs` the HUD, crosshair and
  title screen, and `GameCanvas.Draw.cs` the drawing helpers.
- `Sounds/`: the WAV files, committed, and copied to the output folder by the `.csproj`.
- `tools/`: `make_sounds.py`, `make_voice.sh` and `crunch_voice.py`.

## Rules that keep it working

- **Variable time step.** `GameCanvas` passes the real elapsed time (capped at 0.1 s) to `GameEngine.Update`; there
  is no fixed-step loop. Randomness comes from unseeded `new Random()` instances, so play is not repeatable. Write
  any new movement in terms of `deltaTime`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/` and `Models/`. `Views/` reads the engine and
  draws it. The game and the screen share one projection, so a target is hit when its outline covers the crosshair.
- **Input goes through `InputHandler`.** `StickY` is +1 for up (W, Up, D-pad up); the gamepad stick's Y is negated
  for this, because SDL reports stick up as negative. Menus read the pad through `PadUp` (`LeftStickY < -0.5`).
  Keep keys, stick and mouse agreeing when adding controls.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`.
- **Audio.** Sound plays through NetCoreAudio, which uses `aplay` on Linux. Without it the game runs silent.
- **High scores** live in the app-data folder, in `AVAWars/highscores.txt`.
- **Line endings.** Keep each file's existing line endings (`App.axaml`, `App.axaml.cs`, `MainWindow.axaml.cs`,
  `Program.cs`, `app.manifest` and `Engine/ControllerManager.cs` are CRLF) so diffs show only real changes.

## Making a change

- Build with no new warnings, and play the change through; there are no automated tests to catch mistakes.
- Update `README.md` (features, controls, project structure) to match.
- New sounds go into `tools/make_sounds.py`, and new spoken lines into `tools/make_voice.sh`, not as hand-edited WAV
  files.

## CI and releases

`.github/workflows/build.yml` builds every push (it runs no tests). It then publishes self-contained single-file
builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow
artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper
release.
