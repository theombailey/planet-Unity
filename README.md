# Terrarium Planet

A small living planet you can water, warm, seed, and watch over time. It is built in Unity 6 (6000.1.14f1) with URP and the new Input System, and every simulation runs on the GPU or in plain C#.

This README describes the **current state** of the project (end of Phase 11, with presets, stability check, sound and demo mode). Anything marked *not yet* is planned but does not work in the build.

---

## Contents

1. [Quick start](#quick-start)
2. [The two views](#the-two-views)
3. [Desk view controls](#desk-view-controls)
4. [Surface view controls](#surface-view-controls)
5. [Tools on the desk](#tools-on-the-desk)
6. [Dials](#dials)
7. [Keyboard keys](#keyboard-keys)
8. [The F1 debug panel](#the-f1-debug-panel)
9. [The field view and debug brush](#the-field-view-and-debug-brush)
10. [What happens in the world](#what-happens-in-the-world)
11. [Tuning and presets](#tuning-and-presets)
12. [Developer notes](#developer-notes)

---

## Quick start

1. Open `Assets/Scenes/Terrarium.unity` in Unity 6000.1.14f1 (URP 17.1, Forward+).
2. Press **Play**. The planet floats on the desk with the lamp overhead and the time already running at 1x.
3. Hover a tool on the desk, left-click to pick it up, and hold the left button over the planet to use it.
4. Press **F1** to see the simulation panel.

> The Unity window must be focused for Play mode to advance frames.

---

## The two views

The camera moves between two modes that blend smoothly:

| Mode | What it is for |
|---|---|
| **Desk view** | See the whole planet on the desk, orbit it, and read the tools. This is the default. |
| **Surface view** | Stand on the ground and walk over the terrain, trees, and fungus. Reached by zooming in past the closest desk distance. |

Zooming in past the closest desk distance flies the camera down to the surface point under the mouse. Zooming out past the highest surface height flies you back to the desk.

---

## Desk view controls

| Input | Action |
|---|---|
| **Right-drag** | Orbit the planet (yaw and pitch are clamped so the desk stays in frame). |
| **Scroll wheel** (up / in) | Zoom in. Each notch removes a fixed fraction of the distance. |
| **Scroll wheel** (down / out) | Zoom out. |
| **Scroll in past the closest distance** | Fly down to the surface under the cursor. |

---

## Surface view controls

| Input | Action |
|---|---|
| **W / A / S / D** | Walk forward, left, back, right. You walk faster when you are higher up. |
| **Right-drag** | Look around (turn left/right and tilt up/down). |
| **Left-drag** | Pan the ground like grabbing a map, unless you are holding a tool, turning a dial, or the field view is on. |
| **Scroll wheel** (up / down) | Change height above the ground. |
| **Scroll out past the top** | Fly back to the desk. |

The camera is locked to the planet's rotation, so the ground stays still under you while the lamp moves across the sky.

---

## Tools on the desk

Nine tools sit on the desk. Hover one to highlight it (it gets a thick ink outline), then left-click to pick it up.

**Using a tool**
- Hold the **left button** over the planet to use a continuous tool (watering can, compost scoop, heat lamp, frost spray, field lens).
- **Click** to use a one-shot tool (spore dish, meteor, seed jar, bird cage).
- After you pick a tool up, release the button once before it starts working, so the click that grabbed it does not also fire it.
- The brush size depends on your distance from the ground. From the desk a tool covers a wide area; up close it becomes small.
- Hold a tool and move the mouse: it floats over the cursor, or over the planet's surface point if the cursor is on the planet.
- A short **right-click** or **Esc** puts the tool back on the desk. A right-*drag* orbits the camera instead.

| Tool | What it does | How to use it |
|---|---|---|
| **Watering can** | Adds moisture to the ground. | Hold left button over the planet. |
| **Compost scoop** | Adds nutrients and organic matter. | Hold left button over the planet. |
| **Heat lamp** | Warms the ground. | Hold left button over the planet. |
| **Frost spray** | Chills the ground. | Hold left button over the planet. |
| **Spore dish** | Scatters fungal spores (physarum agents) that spread along the ground and carry nutrients. | Click on the planet. |
| **Meteor** | Digs a crater, throws up dust, and kills life inside the blast (fungus and trees). It has a short cooldown. | Click on the planet. |
| **Seed jar** | Plants a tree seed. It only grows if the spot allows (dry land, not too cold or hot). If the spot is wrong, a message tells you why. | Click on dry land. |
| **Field lens** | Reads the ground under the cursor (height, climate, soil, fungal trail). Hold the left button to see temperature shown over the planet. | Hover to read; hold to see the temperature overlay. |
| **Bird cage** | Releases a flock of birds (boids) at the clicked spot, all starting out heading the same way. They then follow their own rules. | Click on the planet. |

---

## Dials

Two dials sit on the desk. Hover one and **left-drag sideways** to turn it. Drag left or right; the simulation follows as you turn.

| Dial | Effect |
|---|---|
| **Time dial** | Sets simulation speed to one of five stops: **Pause**, **0.25x** (slow days), **1x**, **4x**, **16x**. |
| **Spin dial** | Sets how fast days and years turn: turn it one way for slower days and years, the other way for faster. The camera and lamp follow. |

The dials keep showing the real state, so pressing **Space** or re-seeding the world also moves them.

---

## Keyboard keys

| Key | Action |
|---|---|
| **F1** | Show or hide the debug panel. |
| **F2** | Show or hide the field view overlay on the planet. |
| **F3** | Demo mode: hides the panel, glides the camera in and tours the planet. Click or scroll to take over. |
| **Space** | Pause or resume the simulation. |
| **R** | Re-seed the world with a random seed. |
| **Esc** | Put down a held tool. |
| **W A S D** | Walk on the surface (see [Surface view controls](#surface-view-controls)). |
| **Shift** | While the field view is on and you hold the left button, **erase** instead of paint. |

There are no gamepad bindings.

---

## The F1 debug panel

Press **F1** to open the panel on the left side of the screen. It is for development and tuning, and it is visible by default. It shows:

- **Frame rate** and sim time.
- **Time scale** toolbar: Pause, 0.25x, 1x, 4x, 16x (same as the time dial).
- **Sky, Atmosphere, Clouds** switches (next to the sound slider) to turn each layer off and compare.
- **Quality preset** toolbar: switches between the Low, Medium and High presets. Fields are resampled, so the world does not reset when you change presets.
- **Painterly effects**: toggles for Bands, Hatching, Outlines, Paper and Boil (the hand-drawn look).
- **Terrain and climate**: sliders for the land/sea balance, climate settings, and the terrain tools.
- **Physarum network**: population readout.
- **Forest**: tree count, living and dead trees, leaf cover, and growth readout.
- **Boids**: birds alive out of the pool, births and deaths in the last step, seeds dropped so far, and a *Re-scatter boids* button.
- **Field view** and **debug brush** controls (see the next section).
- **World seed**: *Randomise seed* picks a new random seed; *Config seed* returns to the seed in the config asset.

---

## The field view and debug brush

The planet is built from several data layers (**fields**), such as moisture, temperature, nutrients, and height. You can see them by pressing **F2** (or using the F1 panel).

- With the field view on, the planet shows the chosen field and channel, with optional face boundaries and a texel grid.
- While the field view is on, hold **left button** over the planet to paint into the field being viewed. Hold **Shift** to erase.
- The left-drag camera pan is turned off while the field view is on, so painting does not move the camera.

The field view and brush are for checking the simulation. The real tools use the same brush underneath.

---

## What happens in the world

Everything in the world comes from local rules. Nothing is scripted to happen at a set time.

- **Climate.** The lamp warms the side of the planet it faces, and warmth spreads and fades slowly. The axial tilt means the seasons turn over the year.
- **Water and nutrients.** Moisture and nutrients move around and get used up or stored in the soil.
- **Fungus (physarum).** Fungal agents follow the soil's nutrients and moisture, make trails, and carry nutrients along those trails. Spores from the spore dish start new agents.
- **Trees.** A seed takes root where the soil and temperature allow. A tree grows a branch network from the space colonization method. Its trunk and branches thicken as it grows, and its crown leans toward the lamp.
  - Trees grow faster in warm, moist, nutrient-rich soil, and slower in shade from other trees.
  - Their leaves turn green in summer, orange and yellow in autumn, and fall in winter.
  - Mature trees drop seeds that may germinate nearby. Trees that starve, dry out, freeze, or overheat die, turn grey, and rot back into soil nutrients.
  - Meteors kill trees inside their blast.
- **Birds (boids).** Flocks of tiny birds fly just above the ground. Each bird follows three local rules (stay apart from close neighbours, match its neighbours' heading, drift toward their centre) plus two appetites: it climbs toward denser tree canopy, which is its food, and it drifts toward ground at a comfortable temperature. Nobody tells a flock to visit a forest or to leave for the warm side of the planet; they follow those slopes.
  - Birds eat from the canopy, pay energy to stay alive, fly and crowd each other, and starve if they find no food. A well-fed bird that is not crowded has a chick, so the population settles by itself.
  - A bird flying over a forest may pick up a seed and drop it some time later, somewhere else. The tree system then decides whether the seed can germinate there, so birds carry forests to new ground.
  - Dead birds feed the soil, and a meteor kills the birds in its blast.
- **Life stops at a cap.** The forest is capped (about 300 trees) so the frame rate stays steady.
- **Sky, air and clouds.** From the desk the room is dark and faintly starry, and the planet has a thin glowing rim of air (blue on the lamp's side, orange where day meets night). Dive to the surface and the room fades into the planet's own sky: blue by day, a sunset glow (orange toward the lamp, violet away from it) when the lamp is low, and stars at night.
  - **Clouds** form where the ground is wet, so they gather over seas and rainy land and thin out over deserts, and the wind carries them round the planet. They are lit by the lamp, turn orange at sunset, and cast shadows on the ground. From the desk they are a layer round the planet; from the surface you see them in the sky.
  - The sky and clouds are drawn by shaders only (nothing is painted by hand), and every colour and amount is in SimConfig under *Atmosphere and sky* and *Clouds*.

---

## Tuning and presets

- Every number you can tune lives in the **SimConfig** asset (`Assets/Settings/Terrarium/SimConfig.asset`), grouped by area (camera, climate, physarum, trees, tools, and so on). Edit them in the Inspector, then press Play to see the change.
- The **quality presets** (Low, Medium, High) scale things like resolution and particle counts for different machines. You can switch them in the F1 panel.
- The **climate presets** *Lush* (warm, wet, rich soil), *Harsh* (cold, dry, poor soil) and *Fresh start* (code defaults) are buttons in the F1 panel. They reset the config and start a new world with the same seed.
- The seed is fixed by default, so the same config gives the same planet. Use *Randomise seed* or **R** for a new one.

---

## Sound

All sound is generated live from maths (no audio files) and follows the ecosystem: wind (stronger when trees are bare, whistlier when cold), a fungus drone, pentatonic bird notes (busier with more birds, an octave lower at night), leaf rustle, and a sound for each tool. The F1 panel has a volume slider.

---

## Developer notes

- **Requirements:** Unity 6000.1.14f1, URP 17.1, Forward+, new Input System only. No asset-store packages.
- **Tests:** run them from the Unity Test Runner (EditMode); all 167 pass. The ecosystem **stability check** (F1 panel, *Run stability check*) runs 5 seeds for 10 sim years at 16x, logs fungus, trees and birds every season and reports PASS or FAIL.
- **Building the game:** in Unity choose **Terrarium > Build Windows Player**. It writes `Builds/Terrarium/Terrarium.exe` (about 94 MB). Press **F3** in the built game for demo mode.
- **Editor bridge:** a file-based command bridge (`Assets/Scripts/Editor/DevBridge.cs`) lets scripts drive the open editor (play, capture screenshots, snap the camera, use tools). It is editor-only and is not part of the game.
- **Scene and assets:** `SceneBuilder` (editor menu) rebuilds the desk, planet, tools, and dials from code.

### Project layout (short)

| Folder | Holds |
|---|---|
| `Assets/Scripts/Core` | Sim root, clock, config, quality presets, F1 panel |
| `Assets/Scripts/Sphere` | Cube-sphere fields and the debug view |
| `Assets/Scripts/Climate` | Lamp, temperature, moisture, terrain |
| `Assets/Scripts/Physarum` | Fungal agents and nutrient transport |
| `Assets/Scripts/Trees` | Space colonization, tree mesher, tree life, tree manager |
| `Assets/Scripts/Audio` | Generative sound (`OnAudioFilterRead`) |
| `Assets/Scripts/Boids` | Boid simulation (GPU buffers, spatial hash grid) and the scene glue that draws them and carries seeds to the trees |
| `Assets/Scripts/Interaction` | Input, camera rig, tools, dials, surface navigation |
| `Assets/Scripts/Rendering` | Painterly settings, ink pass, planet mesh, and the sky / atmosphere / cloud controller |
| `Assets/Shaders` | Compute shaders (fields, physarum, trees, boids), painterly surface shaders, and the Sky, Atmosphere and Clouds shaders |
| `Assets/Tests/EditMode` | Unit and GPU tests |

---

*Found something that does not match this README? Tell me and I will fix the README or the code.*
