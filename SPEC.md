

<pasted_content id="85ad">
# TERRARIUM PLANET: Claude Code Hand-off Spec

> Read this whole document before writing any code. Work through the phases in order. Do not start a phase until the previous one's acceptance criteria pass. Commit at the end of each phase.

---

## 0. How to work on this project

**Owner:** Theo, first-year CS student, solo developer. This is a university Unity project and is assessed on **a playable demo, a video, and the code**. There is no written report. The demo has to be impressive and interactive, and the code has to be readable, because it is marked.

**Theo's non-negotiables (these are his working style, so follow them strictly):**
1. **Build from first principles.** Write the algorithms yourself: noise, physarum, boids, space colonization, audio synthesis, sphere mapping. Do not pull in asset-store packages, third-party frameworks, DOTween, Cinemachine, Shader Graph or FMOD. Use only what ships with Unity plus the URP package.
2. **Interpretable code.** Every compute kernel, shader and simulation class starts with a comment block giving the idea, the equations, and what each parameter means. Prefer clear code over clever code. If an optimisation costs readability, say so in a comment and keep the simple version available behind a flag.
3. **Emergent behaviour over scripted behaviour.** Do not hard-code outcomes such as "trees appear here" or "boids go there". Define local rules and let the patterns appear.
4. **Hand-drawn look, achieved procedurally** in shaders you write. No pre-drawn art assets.
5. **Explain as you go.** At the end of each phase, give Theo a short plain-English summary of what you built, how it works, and what to look at in the editor to confirm it. He wants to understand every part.

**Working agreement:**
- You probably cannot see the Unity editor's rendered output. Where something can be tested with code (maths, mapping round-trips, determinism, buffer sizes), write **EditMode/PlayMode tests**. Where it needs eyes, add a debug view and list a **"Theo, please check:"** checklist at the end of the phase, then wait for his confirmation before continuing.
- Put every tunable number in a `SimConfig` ScriptableObject (section 4.2). Do not leave magic numbers in code.
- Use a fixed seed (config field) so runs are reproducible, and a "randomise seed" button.
- Keep per-frame managed allocations at zero in the hot paths (no LINQ, no `new` in `Update`).
- Use the Unity version and input system the project already has. Check `ProjectSettings/ProjectVersion.txt` and the packages manifest first, and don't change the version.

**Starting state:** Fresh start. Ignore the old `UnityGame2` space demo. Render pipeline is **URP**. The Unity version and the demo GPU are **not yet known**, so Phase 0 detects them and every scale parameter (agent counts, field resolution) lives in quality presets (section 4.3).

---

## 1. Vision

A tiny living planet is **suspended in mid-air above a wooden desk in a dim lab**, slowly spinning, lit from one side by a desk lamp. Around it on the desk is a scatter of **physical tools** (a watering can, a seed jar, a magnifying glass, a rock for meteors, and so on). The player is a god of a very small world.

The player picks up a tool from the desk and applies it to the planet. They can **scroll to zoom in from the desk view right down to the surface**, where they interact more locally and watch individual trees, fungal networks and flocks.

All life on the planet is **emergent**:
- **Fungal / root networks** grow by a **physarum (slime mould)** algorithm. They find nutrients and build transport networks.
- **Trees** grow by a **space colonization** algorithm, which is a tree-growing algorithm.
- **Flocks of small flying creatures** move by **boids** rules. They pollinate and carry seeds, and they migrate with the seasons.
- A **shared nutrient / moisture / temperature world** couples all three, so they affect each other.
- **Seasons and climate** drive everything. There is no scripted "event" in the sim. Winter and spring just happen.

There is also **generative ambient audio** synthesised from the state of the simulation.

**The feel:** a hand-drawn, inky, watercolour-and-hatching look, like a naturalist's field-notebook illustration that has come alive. The scene is a warm, dark workshop with one pool of lamp light.

### Explicitly out of scope (don't build unless Theo asks)
Orbiting-in-space camera, microphone or webcam input, genetics and evolution, multiplayer, glass dome. See section 14 for stretch goals.

---

## 2. Scene & camera design

### 2.1 The desk scene
- A **wooden desk** (procedural wood-grain shader, painterly) fills the bottom of the view. It is dim and warm, with a dark lab behind it.
- The **planet floats** about 0.4 world-units above the desk surface, centred. It has a gentle sine bob, and a faint hand-drawn "levitation ring" glyph on the desk below it (a decal or thin torus, purely decorative). It spins around a tilted axis.
- A **desk lamp** stands to one side as a visible model (simple low-poly, shaded in the same painterly style). It is the **only strong light source**, a point/spot light aimed at the planet, so one hemisphere is lit and the other is in shadow. A very dim cool ambient fill keeps the night side readable.
- **Tools sit on the desk** around the planet (section 8). They are simple low-poly props. Hovering highlights one with an ink outline.
- **Scale constants** (put in config): `PlanetRadius = 1.0`, desk props sized relative to it, trees `0.02–0.06` tall at maturity, boids about `0.004` long. Use a small camera near-clip (about `0.001`) and check depth precision at close zoom.

### 2.2 Camera modes (one continuous system, not two scenes)
1. **Desk view** (default): the camera sits at a comfortable distance. The player can orbit a limited amount around the planet with RMB-drag (clamped so the desk stays in frame). Scroll zooms.
2. **Zoom-to-surface:** scrolling in dollies the camera toward the **surface point under the cursor** (raycast to the planet). Below a distance threshold the camera blends into **surface mode**.
3. **Surface mode:** the camera is **parented to the planet's rotating frame**, so the ground stays still under the player while the lamp visibly sweeps across the sky (day/night). Controls: LMB-drag or WASD to pan along the surface (moving over the sphere with the camera's up = surface normal), scroll to change height, RMB-drag to look around. Scrolling back out blends smoothly to desk view.
4. The transitions must be smooth (critically-damped spring or smoothstep on a blend factor), with no pops.
5. Tools work in **both** modes. The brush radius is specified in world units, so it covers more in desk view and less up close (and is clamped to sensible limits).

---

## 3. Core architecture overview

```
Assets/
  Scripts/
    Core/        SimConfig, QualityPreset, SimClock, Rng, Stats
    Sphere/      CubeSphere.cs (+ tests), FieldSet.cs, FieldDebugView.cs
    Climate/     ClimateController.cs (season, insolation driver)
    Physarum/    PhysarumSystem.cs
    Trees/       TreeManager.cs, Tree.cs, SpaceColonization.cs, TreeMesher.cs
    Boids/       BoidSystem.cs
    Interaction/ CameraRig.cs, ToolController.cs, Tool*.cs, SurfacePicker.cs
    Audio/       GenerativeAudio.cs, Synth voices
    Rendering/   Renderer features (outline, paper), PlanetMeshBuilder.cs
  Shaders/
    Include/     CubeSphere.hlsl, Noise.hlsl, Painterly.hlsl
    Compute/     FieldInit.compute, Climate.compute, Physarum.compute,
                 Nutrients.compute, Boids.compute, Brush.compute,
                 TreeSplat.compute, Stats.compute
    Surface/     PlanetSurface.shader, Leaves.shader, Wood.shader, Desk.shader,
                 Props.shader, Water.shader
  Tests/         EditMode + PlayMode tests
```

**Tick order each sim step** (single orchestrator `SimLoop`, one fixed timestep, scaled by time-dial):
1. `ClimateController` updates clock: spin angle, axis tilt, season phase, and lamp-relative insolation.
2. Climate kernel: temperature, moisture evaporation / rain, weathering → nutrients.
3. Physarum step (sense/move/deposit) → trail diffuse/decay → nutrient transport.
4. Tree manager step (CPU): growth, seeding, death. Then splat canopy density to the field.
5. Boid step (GPU), seed-pickup/drop events read back asynchronously.
6. Stats reduction (GPU → async readback) feeding audio and debug HUD.

Keep a **GPU-side authoritative state** wherever possible, and use `AsyncGPUReadback` (never blocking `GetData`) for the few things the CPU needs.

---

## 4. Shared infrastructure

### 4.1 The cube-sphere field system (heart of the project)

The planet surface needs 2D simulation fields (trails, nutrients, temperature…) without the pole pinching of a lat/long map. Use a **cube-sphere**: six square faces projected onto a sphere.

- Faces indexed `0..5` = `+X, -X, +Y, -Y, +Z, -Z`. Each face is an `N×N` grid (`N` from the quality preset, default 256).
- Store each field as a **`Texture2DArray` with 6 slices** (`RenderTexture` with `dimension = Tex2DArray`, `volumeDepth = 6`, `enableRandomWrite = true`). Fields that change per step are **ping-ponged** (read buffer / write buffer).
- **Equiangular warp** to reduce area distortion: face coords `(s,t) ∈ [-1,1]` map to cube-plane coords `(tan(s·π/4), tan(t·π/4))`. Inverse uses `atan(x)·4/π`.
- Provide in BOTH C# (`CubeSphere.cs`) and HLSL (`Include/CubeSphere.hlsl`), kept in sync:
  - `DirToFaceUV(float3 dir) → (face, u, v)`: pick the face by the largest absolute component, then divide and apply the inverse warp.
  - `FaceUVToDir(face, u, v) → float3` (normalised).
  - `TexelCentreDir(face, x, y, N)`.
  - **`NeighbourTexel(face, x, y, dx, dy)`: seam-aware**. Compute the neighbour's cube point, normalise to the sphere, then re-map with `DirToFaceUV`. This one trick handles diffusion and sampling across face edges and corners without any adjacency tables.
- **Sampling:** `SampleField(Texture2DArray, dir)` does bilinear sampling by hand (4 texels via `NeighbourTexel`-style logic) so it is correct at seams. Don't rely on hardware filtering across slices.
- **Tests (EditMode, required):** (a) `DirToFaceUV` then `FaceUVToDir` round-trips to within 1e-5 for 100k random directions; (b) all 6 faces are reachable; (c) texel-centre directions map back to their own texel; (d) seam test: stepping `dx=+1` off a face edge lands on the correct adjacent face texel, checked against the direction-space neighbour; (e) C# and HLSL versions agree (write a tiny compute kernel that outputs results for fixed inputs and compare in a PlayMode test).
- **Debug view:** a toggleable visualiser that paints the sphere with any chosen field (greyscale or ramp) and can draw face boundaries. This is essential for checking seams.

### 4.2 Fields

All are on the cube-sphere. Suggested layout, with channels packed where sensible:

| Field | Channels | Meaning |
|---|---|---|
| `Terrain` | R height (−1..1, 0 = sea level), G unused/rock hardness | static-ish, meteors modify it |
| `Climate` | R temperature (°C-ish), G moisture 0..1, B insolation 0..1 | updated each step |
| `Soil` | R nutrients, G organic matter (dead stuff awaiting decomposition), B canopy density (written by trees), A reserved | updated each step |
| `Trail` | R physarum trail strength | ping-pong, diffuse + decay |
| `Brush` | transient tool paint | merged into the others by `Brush.compute` |

### 4.3 `SimConfig` and quality presets
- `SimConfig` is a ScriptableObject holding every tunable, grouped by system, with `[Range]`, tooltips, and units in the tooltip.
- `QualityPreset` ScriptableObject: `fieldResolution` (128/256/512), `physarumAgents` (default 400k, range 50k–2M), `boidCount` (default 15k, range 2k–100k), `maxTrees` (default 300), `maxSegmentsPerTree`, `meshRingSides`. Provide Low / Medium / High, plus a runtime selector. The demo-machine GPU is unknown, so make **Medium** safe for an integrated or mid-range GPU and let Theo scale up.
- Runtime debug panel using `OnGUI` (no packages): sliders for key parameters, toggles for debug views, FPS and sim stats, a pause button and a time-scale dial. Toggle with `F1`.
- **Time model:** `SimClock` has `dayLengthSeconds` (default 24), `yearLengthDays` (default 10), `timeScale` (0 / 1 / 4 / 16). Everything time-dependent uses sim-time deltas from this clock, never `Time.deltaTime` directly.

### 4.4 Utilities
- `Rng`: small deterministic PCG/xorshift in both C# and HLSL (hash-based per-thread), seeded from `SimConfig.seed`.
- `Noise.hlsl`: write your own value / gradient noise and fBm (3D, sampled at sphere directions so there are no seams). Mirror a C# copy if CPU needs it, or generate everything on GPU.

---

## 5. Climate & terrain

**Terrain (init):** 3D fBm noise sampled at each texel's sphere direction gives `height`. Sea level at 0, with a configurable land fraction (roughly 30–45%). Add mild ridged noise for mountain chains. Because the noise is 3D on the sphere there are no seams. A slider re-seeds and regenerates.

**Spin and axial tilt (this gives you seasons):**
- The planet spins around its own axis (period = day length).
- The axis is tilted relative to the lamp, with tilt angle `ε` (default ~23°). Over a year the axis's lean **toward vs. away from the lamp oscillates**, `lean(t) = ε · sin(2π · t / yearLength)`. So one hemisphere is tipped toward the lamp (summer) while the other tips away (winter), then they swap. Visually the planet actually tilts, and the sim is consistent with what you see.
- Pass the planet's current rotation matrix to compute shaders so they can convert a texel's local direction to world space.

**Insolation:** per texel, `insolation = max(0, dot(worldNormal, dirToLamp))`, where `worldNormal` is the texel direction rotated by the planet's rotation (just a lit-vs-unlit Lambert term; use the lamp's actual position so it matches the render).

**Temperature:** relax each texel toward a target: `T_target = T_base + k_sun·insolation − k_alt·max(0,height) − k_lat·(small term from axis latitude) ` with thermal inertia (`T += (T_target − T)·rate·dt`) plus slow diffusion so the day/night and seasonal swings smear realistically. Oceans have higher inertia (slower swing). Tune so there are visible polar ice / cold biomes and hot equatorial bands and the swing is readable at the default year length.

**Moisture:** ocean texels are saturated. Land moisture diffuses inland and decays with evaporation proportional to temperature. **Rain tool** adds moisture. Optionally a simple wind-advection term (stretch). Frozen (T < 0) texels hold moisture as ice and release it on thaw.

**Weathering / soil formation:** land texels slowly add nutrients at `rate ∝ moisture · warmth` (bare rock becomes fertile slowly). This is the planet's base energy input to life.

**Acceptance (Phase 4):** debug view shows plausible continents, a day/night terminator sweeping round, a temperature field with a visible latitudinal gradient that shifts with the seasons, and moisture concentrated near oceans. Over several sim-years the hemispheres visibly swap warm/cold.

---

## 6. Life systems

### 6.1 Physarum (fungal / root network): GPU

**Concept:** Jeff Jones' agent-based slime mould. Agents sense a chemical, steer toward the highest concentration, and deposit trail as they move. Positive feedback produces networks that find food and connect it. Here the network stands for fungal mycelium / roots under the surface.

**Agent state (StructuredBuffer):** `float3 pos` (unit sphere), `float3 heading` (tangent vector, ⟂ pos), `float energy`, `uint alive`.

**Per step, per agent:**
1. **Sense** at three points ahead, at arc-distance `sensorDist` (radians) and angles `−θ, 0, +θ` from heading. For a point at angle `a`: `d = rotate(heading, around pos, by a)` then `p = normalize(pos·cos(sd) + d·sin(sd))`. Sample `attractant(p) = trail(p) + wNutrient·nutrients(p) − wWater·isOcean(p)·big`.
2. **Steer** by the standard rule: if centre is the strongest, go straight; if both sides are stronger, random turn; else turn toward the stronger side by `turnSpeed·dt`.
3. **Move** on the sphere by arc `speed·dt`: `pos' = normalize(pos·cos(a) + heading·sin(a))` and parallel-transport the heading: `heading' = −pos·sin(a) + heading·cos(a)` then re-orthogonalise (`heading -= pos·dot(pos,heading)`, normalise).
4. **Deposit** trail at the texel under `pos'` (use an atomic add on a uint-encoded trail buffer, or a scatter pass followed by a resolve; pick whichever is simple and correct, and document it).
5. **Eat:** `eaten = min(nutrients, eatRate·dt)`. Subtract from the `Soil` field, `energy += eaten·efficiency − upkeep·dt`.
6. **Life cycle:** `energy ≤ 0` → agent dies, return a bit of mass to organic matter. `energy > reproduceThreshold` → spawn a child (find a dead slot in the pool), splitting energy. Agents on ocean or deep cold ground lose energy fast. **Do not scale the population by script.** It must emerge from the energy balance.

**Trail field:** each step, 3×3 **seam-aware** diffusion (via `NeighbourTexel`), then multiply by `(1 − decay·dt)`.

**Nutrient transport (the key coupling):** nutrients diffuse with a diffusion coefficient boosted along trails: `D = D0 + D1·trail`. So the network moves nutrients from rich spots to poor ones, which is how fungal networks really help trees. Conserve nutrients: no creating or destroying them except via weathering (source), rain wash-out (sink, optional) and organic-matter decomposition (organic → nutrients at a temperature and moisture-dependent rate).

**Rendering:** draw the trail as faint ink-line mycelium on the ground texture (thresholded and fuzzed, drawn like fine root hairs), more visible when the camera is close.

**Acceptance (Phase 5):** from a random scatter of agents on a fertile patch, a recognisable branching/network structure forms within ~20–60 s; networks connect nutrient blobs placed by brush; population stabilises (does not explode or collapse) under default config; seams show no visible artifacts as networks cross face edges; 400k agents run at 60 fps on the dev GPU (or report the achieved count).

### 6.2 Trees: space colonization (CPU, with GPU instanced leaves)

**Concept (Runions et al., 2007):** a tree is a graph of branch nodes. A cloud of **attractor points** fills the intended crown volume. Each growth iteration: every attractor finds its nearest branch node within an influence radius; each node with attractors grows a new child in the normalised average direction toward them; attractors within the kill distance of any node are removed. Growth stops when attractors are used up.

**Implementation notes:**
- Work in the tree's **local frame**: origin at the root on the surface, local up = planet normal (so trees grow radially outward and "up" is correct all over the sphere).
- **Crown envelope:** an ellipsoid above the root. Attractors are **added gradually over the tree's life**, rate gated by the tree's local resources. This is what makes trees emerge as different shapes in different places: rich soil gives fuller trees, poor soil gives sparse scraggly ones.
- Bias each new segment's direction with **gravitropism** (toward local up), **phototropism** (toward the lamp direction in the tree's frame, so trees lean slightly toward the light), and a small random wobble.
- **Pipe model radius:** tips have `r_min`; a parent's radius satisfies `r_parent^n = Σ r_child^n` with `n ≈ 2.5`. Recompute bottom-up after growth.
- **Spatial acceleration:** a simple uniform grid / hash for nearest-node queries, no external libraries. Cap segments per tree.
- **Meshing:** generalised cylinders (rings of `meshRingSides` vertices per node, smooth-blended at branch joints is nice-to-have but a simple approach is fine). Build or extend the mesh **incrementally** as the tree grows. Use one `Mesh` per tree and `Graphics.DrawMesh`, or merge into a pooled mesh. Document the choice.
- **Leaves:** terminal-region nodes spawn leaf quads stored in a `ComputeBuffer`, drawn with `Graphics.DrawMeshInstancedProcedural`. The leaf shader reads global `_Season`, `_LeafHealth` so leaves change colour in autumn, drop in winter, bud in spring.

**Resources & life cycle (emergent, not scripted):**
- Each tree samples nutrients, moisture, temperature, and light (insolation + shading by neighbours' canopy density) at its root and uses them to determine growth rate and attractor addition. Growth only when `T` is in range and moisture is adequate.
- Trees **draw nutrients** from the `Soil` field (a radius around the root), more efficiently where physarum `trail` is strong (mycorrhizal link).
- **Seasonal behaviour:** deciduous: leaf drop and growth pause below a temperature threshold. Resume in spring.
- **Reproduction:** mature trees in good health produce **seeds** (a count per season) that fall nearby, or are picked up by boids (section 6.3). A seed germinates only if the local conditions are good, and with low probability, so forests form where the environment supports them. Seed dispersal and germination are the only "placement" logic. No scripted planting.
- **Death:** drought, freezing, starvation, old age, or meteor impacts. A dead tree turns grey, then collapses, adding its mass to the `Organic` field (which decomposes into nutrients). Trees can collapse into the hollow of other trees' light, so competition emerges.
- **Canopy splat:** each step, write tree canopy density into `Soil.B` (via a small compute pass from a `ComputeBuffer` of tree positions and sizes). Used for shading and boid food.
- Cap: `maxTrees` (preset). If at cap, seeds fail to germinate.

**Acceptance (Phase 7):** a single seed grows into a recognisable, non-repetitive tree (different seeds give different shapes); trees visibly lean toward the lamp and grow thinner on poor soil; leaves change with the seasons; a patch left alone grows into a forest that thins out in winter and regrows in spring; trees die and decay into nutrients; no spikes in frame time when many trees grow at once (spread work over frames with a per-frame budget).

### 6.3 Boids: GPU

**Concept (Reynolds):** each boid steers by three local rules: **separation** (avoid crowding), **alignment** (match heading with neighbours), **cohesion** (move toward neighbours' centre). Flocks emerge.

**On a sphere:** boids live in a thin shell above the surface. State: `float3 pos` (world/planet-local, not unit), `float3 vel`, `float energy`, `uint flags` (alive, carrying seed), `float seedViability`.
- **Altitude control:** a radial spring keeps the boid at `surfaceRadius(pos) + hoverHeight`. Sample `Terrain.height` for the surface radius, and keep `vel` mostly tangential.
- **Neighbour search:** 3D **spatial hash grid** over the shell (cell size = perception radius). Build with the standard GPU pattern: count per cell → prefix sum (or atomic cell-list) → scatter → query the 27 neighbouring cells. Document it well; it's the hardest part. Start with a simple (slower) atomic linked-list-per-cell approach if a full prefix sum is too complex, then optimise.
- **Extra steering forces (all local, nothing scripted):**
  - **Food seeking:** attracted toward higher `canopy` density / flowers (sample the `Soil.B` gradient).
  - **Thermal comfort / migration:** bias toward warmer, with a gentle gradient-following term on `Climate.R`. So boids migrate toward the warm hemisphere as the seasons swap, with **no migration code**.
  - **Avoid the player's tools** briefly when something intense happens, optional.
- **Energy:** they eat when over food (taking from canopy / nectar abstraction, a small amount of nutrient conversion), lose energy to upkeep and flying. Zero energy → die and add to the `Organic` field. High energy and enough local density → reproduce into a free pool slot. Population balances from the energy economy.
- **Seed carrying (pollination / dispersal):** a boid passing a mature tree can pick up a seed with some probability (flag set). When it lands low / dies / its timer expires, it **drops the seed**, writing an event to an append buffer. The CPU reads the event buffer via `AsyncGPUReadback` and tells `TreeManager` to try germination at that point. This couples boids → trees.
- **Rendering:** instanced procedural quads / tiny bird-shaped sprites with hand-drawn flap animation in the vertex shader. Orient along velocity. Painterly ink outline at close range. Fade the outline with distance so flocks at desk-view distance read as a drifting ink "murmuration" smudge.

**Acceptance (Phase 8):** a flock visibly forms and moves coherently around the planet at hover height and follows terrain; flocks gravitate toward forests; a hemisphere swap makes boids drift to the other warm side over a season; seeds get carried and new trees appear where boids drop them; population is stable and not scripted; 15k boids at 60 fps on the dev GPU (or report the count achieved).

### 6.4 Coupling summary (this is what makes it an ecosystem)
```
Lamp → insolation → temperature ─┬→ weathering → nutrients ─→ physarum eat ─→ network
                                  ├→ tree growth/leaf season           │
                                  └→ boid migration                    ▼
 nutrients ←─ decomposition ←─ organic ←─ dead trees / boids / agents   nutrient transport
 trees ──seeds──▶ boids carry ──▶ new trees          trail ──▶ better nutrient uptake for trees
 trees ──canopy──▶ boid food, shading
```
Phase 9 is about tuning this loop to be **stable but lively** (no extinction in the first few minutes at default settings; no explosion; visible boom/bust with seasons is good).

---

## 7. Rendering: procedural hand-drawn look (URP, written HLSL)

Write custom URP shaders (ShaderLab + HLSL using URP includes). Do not use Shader Graph. All shading functions live in `Include/Painterly.hlsl` and are shared.

**Techniques to implement (each with a comment block explaining the maths):**
1. **Banded ("toon") lighting with wobbly bands:** `n·l` is quantised into 3–4 bands with smoothstep edges, and the band thresholds are perturbed by low-frequency noise so the boundaries look hand-painted rather than perfectly smooth.
2. **Hatching in shadow:** in shadow bands, overlay procedural pen hatching, i.e. line patterns whose direction follows the surface (or screen) and whose density increases with darkness. Cross-hatch in the darkest band. Use noise to break the lines and vary thickness.
3. **Ink outlines:** (a) inverted-hull outlines on props, trees and boids; (b) a **fullscreen Renderer Feature** for depth+normal edge detection on the planet and desk. Write the `ScriptableRendererFeature` yourself. Line width wobbles with noise.
4. **Paper grain + vignette:** a fullscreen overlay multiplying subtle paper fibre noise and darkening the corners. Also gives the "field-notebook" mood.
5. **Boil / line jitter:** quantise time (about 8–12 Hz) and use it to offset noise sampling, so outlines and hatches subtly "boil" like frame-by-frame hand-drawn animation. Keep it subtle and togglable.
6. **Colour:** watercolour-like palette ramps per biome, with soft edge bleeding (noise-displaced biome boundaries) and pigment pooling (darker at region edges). Warm lamp light, cool night ambient.
7. **Planet surface shader:** reads `Terrain`, `Climate`, `Soil`, `Trail` field arrays. Base colour from height + temperature + moisture: ocean (flat wash with a few animated ink wave strokes), sand, grass (greens that yellow with dryness), rock, snow/ice. Canopy and trail shown as ink marks. The vertex shader displaces a cube-sphere mesh by `Terrain.height` (vertex texture fetch).
8. **Water:** flat colour wash with slowly drifting ink wave lines and a thin foam outline at the shoreline (from height ≈ 0).
9. **Desk:** procedural wood grain (stretched noise + rings), plus a worn-varnish highlight. Lamp light falls off in a pool.

**Planet mesh:** build a 6-face cube-sphere mesh (`PlanetMeshBuilder`) with configurable subdivisions (default 128 per face) so it matches the field's UV layout (equiangular). Make the normals come from the height field (central differences, seam-aware) or compute in the pixel shader.
**LOD (stretch):** a per-face quadtree for finer close-up geometry. Not required for v1. If the close-up looks too faceted, bump subdivisions or add tessellation-free per-face chunks.

**Acceptance (Phase 2):** a greyboxed planet and desk look unmistakably "hand-inked": banded wobbly lighting, hatching in shadow, outlines, paper grain. Toggle-able per effect for debugging.

---

## 8. Tools & interaction

**Tools sit on the desk as props.** Click one to pick it up; it follows the cursor (smoothly, with a little swing). Hold LMB over the planet to **apply**. Right-click or Escape returns it to its place on the desk. The cursor-over-planet position comes from a ray-sphere test (+ terrain height refinement) → direction → `Brush.compute` paints into fields with a soft falloff brush.

| Tool (prop) | Effect (all via the fields; the sim does the rest) |
|---|---|
| **Watering can** | Adds moisture in the brush. Rain-drop particles fall from the can. |
| **Compost scoop** | Adds nutrients / organic matter. |
| **Seed jar** | Click-drops a single seed (tree) at the cursor. Optional second jar for **spore dish**: spawns a few physarum agents. |
| **Boid cage** | Releases a small flock at the cursor. |
| **Heat lamp / magnifier** | Raises temperature in the brush while held (a focused lamp beam). |
| **Frost spray / ice block** | Lowers temperature in the brush. |
| **Meteor (rock)** | Click to fire: impact at cursor: kills life in the radius (tree death, agent kill, boid kill), carves a crater (height change), throws a ring of nutrient-rich organic matter, drops a brief dust-ink-splash effect. Short cooldown. |
| **Field lens (magnifier)** | **Inspect** mode: hovering shows diegetic ink-handwriting readouts near the cursor (temperature, moisture, nutrients, local tree count, season), drawn in a handwriting-style procedural font or simple stroke glyphs. Also toggles field overlays on the planet while held. |
| **Dials on the desk** | A pause / time-speed dial (0, 1×, 4×, 16×) and a "spin" dial. Grab-and-turn with the mouse. Keeps UI diegetic. |

**General interaction rules:**
- A minimal on-screen hint (tool name + short key hints) can appear at screen edge, in the same ink style. No generic Unity UI look.
- A reset (R) re-seeds the world. `F1` toggles the debug panel.
- Every effect must go through the same field/agent systems as everything else. A tool must never directly script a creature.

**Acceptance (Phase 6):** each tool visibly affects the right field in the debug view (before life exists), the brush is seam-safe across cube-face edges, picking works at all camera distances, and the desk ↔ surface zoom transition is smooth.

---

## 9. Generative audio (from scratch)

Synthesise audio in code using `OnAudioFilterRead` on an `AudioSource` (no audio middleware, and ideally no recorded samples). Document the DSP in comments.

**Layers (each driven by sim stats, with parameter smoothing to avoid clicks):**
1. **Wind / air bed:** filtered noise (one-pole or state-variable filter). Cutoff and level rise with temperature difference (wind) and with the camera's closeness to the surface. Winter = thinner, colder timbre.
2. **Flock voices:** a handful of soft sine/triangle voices with slow attack, pitches picked from a **pentatonic scale** (so it always sounds consonant). Number of active voices and their rhythm follow boid count and flock density (e.g. Stats compute gives per-region density). Pitch register drifts with season (major-ish in spring/summer, more minor/sparse in winter).
3. **Mycelium drone:** a low, slowly evolving drone (a few detuned oscillators) whose brightness follows total physarum trail activity or network size.
4. **Tree rustle / growth ticks:** soft filtered-noise bursts or tiny plucks when trees put out new growth or leaves drop.
5. **Tool sounds:** synthesised cues: rain patter (granular filtered noise bursts), meteor impact (low sine thump + noise burst), seed plant (soft pluck).
6. **Day/night:** gentle shift in the overall filter and key as the lamp sweeps across the camera's surface point (in surface mode).

**Stats pipeline:** `Stats.compute` reduces key aggregates each ~0.25 s: total nutrients, mean temperature, boid count + mean speed, trail energy, tree count, population changes. Read via `AsyncGPUReadback` into a `Stats` struct consumed by the audio layer and the debug HUD.

**Acceptance (Phase 10):** audio is never harsh or clicky, it audibly responds to seasons and population changes, and it works in both camera modes with a master volume control.

---

## 10. Phased build plan (with acceptance criteria)

Work strictly in order. After each phase: run tests, commit (`phase-N: <summary>`), summarise for Theo, and if the phase has a "Theo, please check" list, stop and wait.

### Phase 0: Project setup & capability check
- Create the Unity project (use existing install; URP template/setup), `.gitignore` for Unity, folder structure from section 3.
- Add a startup `CapabilityReport` that logs: Unity version, graphics API, GPU name, VRAM, `SystemInfo.supportsComputeShaders`, `maxComputeWorkGroupSize`, `supportsAsyncGPUReadback`, max texture array size.
- Create `SimConfig`, `QualityPreset` (Low/Medium/High), `SimClock`.
- **Accept:** project opens clean with no console errors/warnings; report prints; presets selectable. *Theo, please check:* paste the capability report so presets can be tuned.

### Phase 1: Desk scene greybox & camera
- Desk plane, lamp model + light, floating spinning planet sphere (placeholder), placeholder tool props, `CameraRig` with desk view + limited orbit + zoom.
- **Accept:** scene reads correctly: dim lab, lamp on one side lighting one hemisphere, planet bobbing / spinning, tools laid out around it. Orbit clamps correctly; zoom is smooth.

### Phase 2: Painterly rendering
- Implement section 7 on the greybox: banded lighting, hatching, outlines (hull + fullscreen feature), paper grain, boil, desk wood shader.
- **Accept:** see section 7 acceptance. Each effect toggleable from the debug panel. *Theo, please check:* the look.

### Phase 3: Cube-sphere field infrastructure
- `CubeSphere.cs` / `.hlsl` + all tests from 4.1. `FieldSet` with ping-pong arrays. `FieldDebugView`. `Brush.compute` and a debug-click brush. `PlanetMeshBuilder`.
- **Accept:** all tests green; debug brush paints a smooth, seam-safe blob that crosses face edges and corners (including across the 8 cube corners); the face-boundary overlay lines up with the mesh. *Theo, please check:* paint across several edges.

### Phase 4: Terrain & climate
- Section 5. Init terrain, spin + tilt + seasons, insolation, temperature, moisture, weathering. Planet surface shader showing biomes.
- **Accept:** section 5 acceptance. *Theo, please check:* continents, day/night, seasonal temperature swap over a year at 16×.

### Phase 5: Physarum & nutrient transport
- Section 6.1. Agent buffer, kernels, trail diffuse/decay, nutrient transport, rendering of the network.
- **Accept:** section 6.1 acceptance.

### Phase 6: Tools & surface interaction
- `SurfacePicker`, tool props, `ToolController`, brushes for water / nutrients / heat / cold, spore dish, zoom-to-surface camera mode and surface-locked controls, dials.
- **Accept:** section 8 acceptance. Meteor tool can be a stub (crater + field changes) until trees and boids exist; finish its kill-radius effects in Phase 9.

### Phase 7: Trees
- Section 6.2. `SpaceColonization` (pure C#, unit-tested for determinism and termination), `TreeMesher`, `TreeManager`, leaf instancing, seasons, seeds, death, canopy splat.
- **Accept:** section 6.2 acceptance.

### Phase 8: Boids
- Section 6.3. Spatial hash, steering, energy economy, seed carry events, rendering.
- **Accept:** section 6.3 acceptance.

### Phase 9: Ecosystem coupling & tuning
- Connect everything per 6.4. Complete the meteor kill-radius across all systems. Add a **headless/accelerated stability test**: run the sim for N sim-years at 16× with default config and a few seeds, log populations per season, and assert nothing goes extinct or explodes (use `Stats`). Build a few named **presets** (e.g. "Lush", "Harsh", "Fresh start") on `SimConfig`.
- **Accept:** across 5 different seeds, default config sustains all three life forms for ≥ 10 sim-years with visible seasonal cycles. *Theo, please check:* does it feel alive?

### Phase 10: Generative audio
- Section 9. **Accept:** section 9 acceptance.

### Phase 11: Polish, performance, build
- Profile with Unity Profiler / Frame Debugger; fix spikes. Tidy the debug panel; add a clean "demo mode" (hide debug, intro camera move from desk to a close fly-over, optional slow auto-tour for recording). Make a standalone build and test it. Write a short `README.md` (how to run, controls, project structure, where each algorithm lives). Make sure every file has its header comment.
- **Accept:** standalone build runs at ≥ 60 fps on Medium on the demo machine, controls documented, no console errors, code header comments present everywhere.

---

## 11. Controls summary (defaults, adjust if awkward)
- **Scroll:** zoom (desk ↔ surface).  **RMB-drag:** orbit (desk) / look (surface).  **LMB:** pick up / apply tool; drag on empty surface to pan in surface mode.
- **WASD:** pan in surface mode.  **Esc / RMB (while holding tool):** put tool back.
- **Dials:** click-drag on desk props.  **R:** reseed world.  **F1:** debug panel.  **Space:** pause.

---

## 12. Performance & robustness notes
- Dispatch compute with thread groups of 64 (agents) or 8×8 (field texels); keep kernels branch-light.
- Avoid read-modify-write races: use ping-pong textures for fields and atomics (or scatter+resolve) for deposits. Comment what is racy and why it's acceptable.
- Never block the main thread on GPU readback. Handle readback arriving a few frames late and handle `request.hasError`.
- Clamp all fields to valid ranges each step to avoid NaN propagation. Add a debug NaN/Inf detector that can pause and report the field name.
- Floating-point on the sphere: re-normalise `pos` every step; re-orthogonalise `heading`.
- Handle focus-loss / alt-tab (audio filter must never throw; guard against null buffers).
- Make everything survive domain reload and re-entering Play mode (release `RenderTexture`s and `ComputeBuffer`s in `OnDisable`/`OnDestroy`).

---

## 13. Coding standards
- C#: small single-purpose classes, descriptive names, XML-doc summary on every public type and method, no static mutable state except tightly scoped singletons documented as such.
- HLSL: each `.compute`/`.shader`/`.hlsl` begins with: **Purpose / Idea / Equations / Parameters / Buffers & textures**.
- No dead code, no commented-out blocks. Keep experimental alternatives behind clearly named config flags.
- Tests live in `Tests/` and run from the Test Runner. Name them after the behaviour (`CubeSphere_RoundTrip_IsStable`).

---

## 14. Stretch goals (only after Phase 11, only if Theo asks)
- Quadtree LOD for the planet surface and denser close-up detail.
- Simple heritable traits (e.g. tree leaf timing, boid flock size) with mutation. This would tie into Theo's separate logic-gate-creature evolution interest.
- Clouds and weather layer (advection of moisture, visible hand-drawn cloud wisps casting shadows).
- Save / load world state.
- Second tree species (evergreen vs deciduous) and a grazing creature.
- Glass-bell-jar option around the planet.

---

## 15. Assumptions & open items (flag these back to Theo)
1. **Unity version and demo GPU: unknown.** Phase 0 detects the version/GPU and Theo supplies the demo machine specs later. Agent/boid counts are the first things to tune.
2. **Deadline:** none fixed yet. Theo is getting as far as he can tonight, so keep each phase independently demo-able.
3. **Year/day lengths, tilt, scale constants** are sensible defaults; expect tuning.
4. **Input system:** use whichever the project template provides; keep input handling isolated in `Interaction/` so it's easy to swap.
5. If any instruction here is ambiguous or conflicts with how Unity actually behaves, **ask Theo**. Don't silently substitute a third-party solution.
</pasted_content id="85ad">
