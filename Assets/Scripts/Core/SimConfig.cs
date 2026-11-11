// =============================================================================
// SimConfig
// -----------------------------------------------------------------------------
// Purpose : The single home for every tunable number in the simulation.
// Idea    : No magic numbers in code. Each system reads its parameters from
//           this asset, so a value can be changed in the Inspector (or from the
//           F1 debug panel) without touching code, and so a marker reading the
//           code can find every constant in one place.
// Layout  : Fields are grouped by system with [Header]. Each has a [Tooltip]
//           that states its meaning and its units. Later phases add their own
//           groups (climate, physarum, trees, boids, audio) as they are built.
// Note    : This asset is treated as READ-ONLY at runtime. Values that change
//           while playing (the active seed, the time scale) live on SimRoot and
//           SimClock, so pressing Play never dirties the asset on disk.
// =============================================================================

using UnityEngine;

namespace Terrarium
{
    /// <summary>All tunable simulation parameters, grouped by system.</summary>
    [CreateAssetMenu(fileName = "SimConfig", menuName = "Terrarium/Sim Config")]
    public sealed class SimConfig : ScriptableObject
    {
        [Header("General")]
        [Tooltip("Seed for every random choice in the sim. Same seed + same config = same world.")]
        public int seed = 12345;

        [Header("Scale (world units; the planet radius is the yardstick)")]
        [Tooltip("Radius of the planet at sea level, in world units.")]
        [Range(0.25f, 4f)] public float planetRadius = 1.0f;

        [Tooltip("Gap between the desk surface and the lowest point of the planet, in world units.")]
        [Range(0.1f, 2f)] public float planetHoverHeight = 0.4f;

        [Tooltip("Height of the planet's gentle up-and-down bob, in world units (peak deviation).")]
        [Range(0f, 0.2f)] public float bobAmplitude = 0.02f;

        [Tooltip("Time for one full bob cycle, in real seconds.")]
        [Range(1f, 30f)] public float bobPeriodSeconds = 6f;

        [Tooltip("Height of the smallest mature tree, in world units.")]
        [Range(0.005f, 0.1f)] public float treeHeightMin = 0.02f;

        [Tooltip("Height of the tallest mature tree, in world units.")]
        [Range(0.01f, 0.2f)] public float treeHeightMax = 0.06f;

        [Tooltip("Body length of one boid, in world units.")]
        [Range(0.001f, 0.02f)] public float boidLength = 0.004f;

        [Tooltip("Camera near clip plane, in world units. Small so the camera can get right down to the surface.")]
        [Range(0.0005f, 0.05f)] public float cameraNearClip = 0.001f;

        [Header("Desk scene (world units; the desk top is the plane y = 0)")]
        [Tooltip("Desk width (x), thickness (y) and depth (z), in world units.")]
        public Vector3 deskSize = new Vector3(6.4f, 0.12f, 3.8f);

        [Tooltip("World position of the lamp's bulb. The lamp is the only strong light; it shines in all directions from here.")]
        public Vector3 lampLightPosition = new Vector3(-1.75f, 2.55f, -0.45f);

        [Tooltip("Colour of the lamp light (warm).")]
        public Color lampColor = new Color(1.0f, 0.82f, 0.58f);

        [Tooltip("Lamp brightness. Light falls off with the square of distance, so this is the brightness at 1 world unit.")]
        [Range(0f, 100f)] public float lampIntensity = 9f;

        [Tooltip("Distance at which the lamp light is cut off, in world units.")]
        [Range(2f, 30f)] public float lampRange = 12f;

        [Tooltip("Dim, cool fill light that keeps the night side of the planet readable.")]
        public Color ambientColor = new Color(0.20f, 0.25f, 0.36f);

        [Tooltip("Colour of the dark lab behind the desk.")]
        public Color backgroundColor = new Color(0.045f, 0.040f, 0.036f);

        [Tooltip("Tools are laid out on an ellipse on the desk around the planet. x = half-width, y = half-depth, in world units.")]
        public Vector2 toolArcRadii = new Vector2(2.2f, 1.35f);

        [Tooltip("Radius of the decorative levitation ring drawn on the desk under the planet, in world units.")]
        [Range(0.1f, 2f)] public float levitationRingRadius = 0.8f;

        [Header("Terrain (see FieldInit.compute)")]
        [Tooltip("Share of the planet's surface that is land (above sea level), 0..1. Adjustable on the F1 panel.")]
        [Range(0.1f, 0.8f)] public float landFraction = 0.38f;

        [Tooltip("Size of the continents: noise blobs per planet radius. Higher = more, smaller landmasses.")]
        [Range(0.3f, 4f)] public float continentFrequency = 1.1f;

        [Tooltip("Layers of fractal noise in the continents. More = finer coastline detail.")]
        [Range(1, 8)] public int terrainOctaves = 5;

        [Tooltip("Size of mountain-range features: noise blobs per planet radius.")]
        [Range(0.5f, 8f)] public float ridgeFrequency = 2.6f;

        [Tooltip("How much ridged noise is added to the high ground to make mountain chains (0 = none).")]
        [Range(0f, 1.5f)] public float ridgeAmount = 0.45f;

        [Tooltip("Raw-noise distance above sea level that counts as height 1 (the highest peaks). Smaller = steeper, more extreme relief.")]
        [Range(0.05f, 1f)] public float reliefSpan = 0.6f;

        [Tooltip("How far the highest mountain stands above sea level, as a fraction of the planet radius (visual only).")]
        [Range(0f, 0.1f)] public float terrainHeightScale = 0.035f;

        [Header("Climate (see Climate.compute)")]
        [Tooltip("Temperature of ground that gets no lamp light, before altitude and latitude cooling, in deg C.")]
        [Range(-60f, 30f)] public float baseTemperature = 12f;

        [Tooltip("Extra temperature at full lamp light (lamp straight overhead), in deg C.")]
        [Range(0f, 200f)] public float sunGain = 150f;

        [Tooltip("Cooling per unit of terrain height (1 = highest peak), in deg C.")]
        [Range(0f, 80f)] public float altitudeLapse = 25f;

        [Tooltip("Extra cooling at the poles, in deg C. Keeps the polar ice caps even in polar summer.")]
        [Range(0f, 60f)] public float latitudeCooling = 28f;

        [Tooltip("How fast land follows its target temperature, per sim second. Small = more thermal inertia (nights stay warm). Time constant = 1 / this.")]
        [Range(0.005f, 1f)] public float landResponse = 0.08f;

        [Tooltip("How fast ocean follows its target temperature, per sim second. Much smaller than land: water holds heat.")]
        [Range(0.002f, 0.5f)] public float oceanResponse = 0.02f;

        [Tooltip("How fast heat spreads sideways, in radians squared per sim second. ")]
        [Range(0f, 0.0005f)] public float heatDiffusion = 0.0002f;

        [Tooltip("How fast moisture spreads inland, in radians squared per sim second.")]
        [Range(0f, 0.0005f)] public float moistureDiffusion = 0.0002f;

        [Tooltip("How fast land dries at 30 deg C or warmer, per sim second. Together with the diffusion this sets how far rain-bearing air reaches inland.")]
        [Range(0.0001f, 0.05f)] public float evaporation = 0.0022f;

        [Tooltip("Rate at which warm, wet rock weathers into nutrients, per sim second. This is the planet's base energy input to life.")]
        [Range(0f, 0.05f)] public float weatherRate = 0.004f;

        [Tooltip("The most nutrients a texel's soil can hold from weathering alone.")]
        [Range(0.1f, 5f)] public float nutrientCap = 1f;

        [Tooltip("Sim seconds of climate run before the world is shown, so it does not begin at a uniform temperature. Run in 0.1 s steps.")]
        [Range(0f, 1000f)] public float climateSpinUpSeconds = 600f;

        [Tooltip("Texels per face edge of the coarse grid the long spin-up runs on. The result is then copied up to the full grid. Smaller = faster start, rougher coastlines in the starting climate.")]
        [Range(32, 256)] public int spinUpResolution = 64;

        [Tooltip("Sim seconds of the spin-up that are re-run on the full-size grid after the copy, to smooth out the coarse grid's rough coastlines.")]
        [Range(0f, 100f)] public float climateSettleSeconds = 20f;

        [Header("Physarum: fungal / root network (see Physarum.compute, Nutrients.compute)")]
        [Tooltip("Agents scattered on land when a world starts. The population then rises or falls with the nutrients; the pool size comes from the quality preset.")]
        [Range(1000, 500_000)] public int physarumStartAgents = 30_000;

        [Tooltip("Angle between the forward sensor and each side sensor, in degrees.")]
        [Range(5f, 90f)] public float physarumSensorAngleDegrees = 35f;

        [Tooltip("How far ahead the sensors look, in radians of arc on the planet (0.01 rad = 1% of the planet radius).")]
        [Range(0.005f, 0.1f)] public float physarumSensorDistance = 0.03f;

        [Tooltip("How fast an agent can turn toward a stronger signal, in radians per sim second.")]
        [Range(1f, 80f)] public float physarumTurnSpeed = 20f;

        [Tooltip("Agent speed along the ground, in radians of arc per sim second.")]
        [Range(0.02f, 1.5f)] public float physarumSpeed = 0.25f;

        [Tooltip("Trail laid down by one agent per step.")]
        [Range(0.01f, 5f)] public float physarumDepositAmount = 0.5f;

        [Tooltip("How much of each step the trail is blurred toward its 3x3 neighbourhood mean, 0..1. More = thicker, smoother tubes.")]
        [Range(0f, 1f)] public float physarumTrailDiffusion = 0.3f;

        [Tooltip("Fraction of the trail that fades per sim second.")]
        [Range(0f, 5f)] public float physarumTrailDecay = 0.9f;

        [Tooltip("Highest trail value a texel can hold (stops runaway build-up under a crowd).")]
        [Range(1f, 50f)] public float physarumTrailMax = 10f;

        [Tooltip("How strongly agents are drawn to nutrients, compared with trail (trail counts 1).")]
        [Range(0f, 20f)] public float physarumNutrientWeight = 3f;

        [Tooltip("Most nutrients one agent can eat per sim second.")]
        [Range(0.0005f, 0.1f)] public float physarumEatRate = 0.005f;

        [Tooltip("Energy gained per unit of nutrient eaten.")]
        [Range(1f, 500f)] public float physarumEnergyPerNutrient = 40f;

        [Tooltip("Energy an agent burns per sim second just to stay alive.")]
        [Range(0f, 2f)] public float physarumUpkeep = 0.13f;

        [Tooltip("Energy a new agent starts with, and that scattered start agents are given.")]
        [Range(0.1f, 5f)] public float physarumStartEnergy = 1f;

        [Tooltip("An agent with more energy than this splits in two.")]
        [Range(0.5f, 10f)] public float physarumReproduceEnergy = 2f;

        [Tooltip("Body mass invested when an agent splits. It returns to the soil as organic matter when an agent dies, so mass is conserved.")]
        [Range(0.05f, 2f)] public float physarumBirthCost = 0.5f;

        [Tooltip("Energy lost per sim second by an agent that ends up in the ocean.")]
        [Range(0f, 20f)] public float physarumOceanPenalty = 2f;

        [Tooltip("Ground colder than this, in deg C, starts to drain agent energy.")]
        [Range(-40f, 10f)] public float physarumColdLimit = -10f;

        [Tooltip("Energy lost per sim second in ground 10 deg C below the cold limit (scales up with the cold).")]
        [Range(0f, 5f)] public float physarumColdPenalty = 0.4f;

        [Tooltip("Spores that drift in from elsewhere: new agents landing at random places, per sim second. A faint background supply, so the network can recover after a crash instead of dying out for good. Zero turns it off.")]
        [Range(0f, 100f)] public float physarumSporeRain = 6f;

        [Header("Nutrient transport (see Nutrients.compute)")]
        [Tooltip("Plain spreading of nutrients through the soil, in radians squared per sim second.")]
        [Range(0f, 0.001f)] public float nutrientDiffusion = 0.00002f;

        [Tooltip("Extra spreading per unit of trail, in radians squared per sim second. This is how the network moves nutrients from rich spots to poor ones.")]
        [Range(0f, 0.01f)] public float nutrientNetworkDiffusion = 0.0002f;

        [Tooltip("Trail above this adds no further transport speed.")]
        [Range(0.5f, 10f)] public float nutrientTrailSaturation = 2f;

        [Tooltip("How fast organic matter turns into nutrients in warm, moist soil, per sim second.")]
        [Range(0f, 0.2f)] public float decompositionRate = 0.02f;

        [Header("Trees: shape (see SpaceColonization, TreeMesher)")]
        [Tooltip("Pioneer seeds scattered on land when a world starts. They only grow where conditions allow.")]
        [Range(0, 500)] public int treeStartSeeds = 400;

        [Tooltip("Length of one branch segment, as a fraction of the tree's mature height.")]
        [Range(0.01f, 0.15f)] public float treeSegmentFraction = 0.045f;

        [Tooltip("How far an attractor reaches for a branch, as a fraction of mature height. Larger = longer, sweeping branches; smaller = dense, twiggy ones.")]
        [Range(0.05f, 1f)] public float treeInfluenceFraction = 0.3f;

        [Tooltip("An attractor this close to a branch tip is used up, as a fraction of mature height.")]
        [Range(0.01f, 0.4f)] public float treeKillFraction = 0.08f;

        [Tooltip("Height of the bare trunk, as a fraction of mature height.")]
        [Range(0.05f, 0.7f)] public float treeTrunkFraction = 0.3f;

        [Tooltip("Half-width of the crown, as a fraction of mature height (at full richness).")]
        [Range(0.1f, 1.2f)] public float treeCrownWidthFraction = 0.45f;

        [Tooltip("Half-height of the crown, as a fraction of mature height (at full richness).")]
        [Range(0.1f, 1f)] public float treeCrownHeightFraction = 0.4f;

        [Tooltip("Most attractors a tree can hold, at full richness. A tree on poor ground gets a fraction of them, so it is sparse.")]
        [Range(50, 1500)] public int treeAttractorsMax = 450;

        [Tooltip("Attractors added each growth step, at full richness.")]
        [Range(5, 200)] public int treeAttractorsPerStep = 40;

        [Tooltip("Pull toward local up in each new segment (1 = as strong as the pull toward attractors).")]
        [Range(0f, 1f)] public float treeGravitropism = 0.12f;

        [Tooltip("Pull toward the lamp in each new segment. Trees lean toward the light.")]
        [Range(0f, 1.5f)] public float treePhototropism = 0.45f;

        [Tooltip("Random wobble in each new segment.")]
        [Range(0f, 0.6f)] public float treeWobble = 0.15f;

        [Tooltip("Radius of a twig tip, in world units.")]
        [Range(0.0001f, 0.003f)] public float treeTipRadius = 0.0005f;

        [Tooltip("Pipe-model exponent: r_parent^e = sum of r_child^e. About 2.5 for real trees.")]
        [Range(2f, 3f)] public float treePipeExponent = 2.5f;

        [Tooltip("Size of one leaf, in world units.")]
        [Range(0.001f, 0.02f)] public float treeLeafSize = 0.0036f;

        [Tooltip("Most leaves one tree can show. The shader draws this many per tree slot.")]
        [Range(50, 2000)] public int treeLeavesPerTree = 700;

        [Tooltip("Branches no thicker than this many tip radii carry leaves.")]
        [Range(1f, 8f)] public float treeTwigRadiusMultiple = 2.5f;

        [Header("Trees: life (see TreeLife, TreeManager)")]
        [Tooltip("Sim seconds of full-speed growth per growth step (a growth step adds one layer of branches).")]
        [Range(0.2f, 20f)] public float treeGrowSecondsPerStep = 2.5f;

        [Tooltip("Coldest temperature a tree can grow in, deg C.")]
        [Range(-20f, 20f)] public float treeTempMin = 2f;

        [Tooltip("Temperature trees grow fastest at, deg C.")]
        [Range(5f, 35f)] public float treeTempOptimum = 22f;

        [Tooltip("Hottest temperature a tree can grow in, deg C.")]
        [Range(25f, 60f)] public float treeTempMax = 42f;

        [Tooltip("Moisture below which a tree does not grow.")]
        [Range(0f, 0.5f)] public float treeMoistureMin = 0.12f;

        [Tooltip("Moisture range over which growth rises from none to full.")]
        [Range(0.05f, 1f)] public float treeMoistureSpan = 0.35f;

        [Tooltip("Soil nutrients at which the nutrient factor is one half.")]
        [Range(0.01f, 1f)] public float treeNutrientHalf = 0.12f;

        [Tooltip("How much the fungal network (trail) improves a tree's nutrient uptake: effective nutrients = n x (1 + this x trail).")]
        [Range(0f, 10f)] public float treeMycorrhizaBoost = 2f;

        [Tooltip("How strongly neighbours' canopy shades a tree: light factor = 1 / (1 + this x neighbours' canopy).")]
        [Range(0f, 5f)] public float treeShadeStrength = 1.2f;

        [Tooltip("Growth rate below which a tree is starving and loses health.")]
        [Range(0f, 0.3f)] public float treeStarveBelow = 0.04f;

        [Tooltip("Health lost per sim second when starving.")]
        [Range(0f, 0.05f)] public float treeStarveRate = 0.004f;

        [Tooltip("Moisture below which a tree suffers drought.")]
        [Range(0f, 0.3f)] public float treeDroughtMoisture = 0.05f;

        [Tooltip("Health lost per sim second in drought.")]
        [Range(0f, 0.05f)] public float treeDroughtRate = 0.006f;

        [Tooltip("Temperature below which a tree freezes, deg C.")]
        [Range(-60f, 0f)] public float treeFreezeTemp = -18f;

        [Tooltip("Health lost per sim second when frozen.")]
        [Range(0f, 0.1f)] public float treeFreezeRate = 0.01f;

        [Tooltip("Temperature above which a tree is scorched, deg C.")]
        [Range(35f, 90f)] public float treeHeatTemp = 50f;

        [Tooltip("Health lost per sim second when scorched.")]
        [Range(0f, 0.1f)] public float treeHeatRate = 0.01f;

        [Tooltip("Health regained per sim second while growing well.")]
        [Range(0f, 0.05f)] public float treeRecoverRate = 0.004f;

        [Tooltip("Below this temperature a deciduous tree has dropped all its leaves, deg C.")]
        [Range(-10f, 20f)] public float treeLeafDropTemp = 9f;

        [Tooltip("Above this temperature a tree is in full leaf, deg C.")]
        [Range(0f, 25f)] public float treeBudTemp = 15f;

        [Tooltip("Below this temperature, leaves start to turn their autumn colours, deg C.")]
        [Range(5f, 30f)] public float treeAutumnTemp = 20f;

        [Tooltip("How fast leaves bud or fall, as a fraction of a full canopy per sim second.")]
        [Range(0.005f, 0.5f)] public float treeLeafRate = 0.04f;

        [Tooltip("Sim seconds between seeds from a mature, healthy tree.")]
        [Range(2f, 200f)] public float treeSeedInterval = 22f;

        [Tooltip("Chance a seed germinates in ideal conditions (it is multiplied by how good the spot is).")]
        [Range(0f, 1f)] public float treeGerminationChance = 0.25f;

        [Tooltip("Nearest a seed falls from its tree, in radians of arc.")]
        [Range(0.002f, 0.1f)] public float treeSeedDispersalMin = 0.012f;

        [Tooltip("Furthest a seed falls from its tree, in radians of arc.")]
        [Range(0.01f, 0.4f)] public float treeSeedDispersalMax = 0.09f;

        [Tooltip("Shortest tree lifespan, in sim seconds.")]
        [Range(100f, 20000f)] public float treeLifespanMin = 1600f;

        [Tooltip("Longest tree lifespan, in sim seconds.")]
        [Range(200f, 40000f)] public float treeLifespanMax = 3600f;

        [Tooltip("How long a dead tree stands before it collapses, in sim seconds.")]
        [Range(1f, 600f)] public float treeDeadSeconds = 45f;

        [Tooltip("Organic matter a full-grown tree returns to the soil when it collapses.")]
        [Range(0f, 20f)] public float treeDeathOrganic = 2.5f;

        [Tooltip("Nutrients a tree takes from the soil per sim second at full growth rate and full size.")]
        [Range(0f, 0.1f)] public float treeUptakePerSecond = 0.01f;

        [Tooltip("How much canopy density a full-grown tree in full leaf adds to the field (used for shading and food).")]
        [Range(0.1f, 3f)] public float treeCanopyStrength = 1f;

        [Tooltip("Real seconds between the tree system's GPU passes (sampling the world, taking nutrients, painting canopy).")]
        [Range(0.03f, 1f)] public float treeTickRealSeconds = 0.15f;

        [Tooltip("Most branch meshes rebuilt in one frame. Spreads growth over frames so many trees never spike the frame time.")]
        [Range(1, 20)] public int treeMeshesPerFrame = 2;

        [Tooltip("Most growth steps run in one frame, across all trees.")]
        [Range(1, 50)] public int treeStepsPerFrame = 8;

        [Header("Boids: flocking (see Boids.compute). Lengths are in planet radii, speeds per sim second")]
        [Tooltip("Boids alive when a world starts, scattered in small clusters on land. The population then finds its own level.")]
        [Range(0, 20000)] public int boidStartCount = 800;

        [Tooltip("How many separate clusters the starting boids are split into.")]
        [Range(1, 200)] public int boidStartClusters = 24;

        [Tooltip("How far a boid sees its neighbours. Also the cell size of the spatial hash grid.")]
        [Range(0.01f, 0.2f)] public float boidPerception = 0.04f;

        [Tooltip("Neighbours closer than this are pushed away from.")]
        [Range(0.002f, 0.1f)] public float boidSeparationRadius = 0.012f;

        [Tooltip("Strength of the push away from crowding neighbours.")]
        [Range(0f, 10f)] public float boidSeparationWeight = 3f;

        [Tooltip("Strength of the pull toward the neighbours' average heading.")]
        [Range(0f, 10f)] public float boidAlignmentWeight = 1.2f;

        [Tooltip("Strength of the pull toward the neighbours' centre.")]
        [Range(0f, 10f)] public float boidCohesionWeight = 0.8f;

        [Tooltip("Fastest a boid flies, in planet radii per sim second.")]
        [Range(0.005f, 0.3f)] public float boidMaxSpeed = 0.06f;

        [Tooltip("Slowest a boid flies (birds do not hover).")]
        [Range(0f, 0.2f)] public float boidMinSpeed = 0.025f;

        [Tooltip("Strongest sideways steering acceleration.")]
        [Range(0.01f, 2f)] public float boidMaxForce = 0.25f;

        [Tooltip("Height boids fly above the ground, in planet radii.")]
        [Range(0.002f, 0.1f)] public float boidHoverHeight = 0.025f;

        [Tooltip("Spring pulling a boid back to its hover height (acceleration per planet radius of error).")]
        [Range(1f, 100f)] public float boidAltitudeSpring = 20f;

        [Tooltip("Damping of up and down motion.")]
        [Range(0.5f, 30f)] public float boidAltitudeDamping = 6f;

        [Tooltip("Strength of the pull up the canopy (food) gradient.")]
        [Range(0f, 10f)] public float boidFoodWeight = 1.5f;

        [Tooltip("How far ahead a boid samples the canopy and temperature to find their gradients, in planet radii.")]
        [Range(0.005f, 0.2f)] public float boidSenseReach = 0.04f;

        [Tooltip("Temperature boids are happiest at, deg C. They drift toward ground at this temperature; nothing tells them where that is.")]
        [Range(0f, 40f)] public float boidPreferredTemp = 20f;

        [Tooltip("Strength of the drift toward comfortable temperature.")]
        [Range(0f, 10f)] public float boidWarmWeight = 0.8f;

        [Tooltip("Random steering that keeps flocks from freezing into a pattern.")]
        [Range(0f, 1f)] public float boidWander = 0.15f;

        [Tooltip("Strength of the pull up the forest-scent slope. The scent is a broad glow around forests that lets boids feel a forest from far away.")]
        [Range(0f, 10f)] public float boidScentWeight = 1.2f;

        [Tooltip("How far apart the scent samples are when finding its slope, in planet radii.")]
        [Range(0.02f, 0.5f)] public float boidScentReach = 0.12f;

        [Tooltip("How fast the scent spreads sideways: the share of each scent cell mixed with its neighbours per sim second. More = a wider glow.")]
        [Range(0f, 20f)] public float boidScentSpread = 3f;

        [Tooltip("How fast the scent fades, per sim second. Less = a longer-lasting, wider glow.")]
        [Range(0.005f, 1f)] public float boidScentDecay = 0.05f;

        [Header("Boids: energy and seeds")]
        [Tooltip("Energy a boid gains per sim second over full canopy (less over thinner canopy).")]
        [Range(0f, 0.5f)] public float boidEatRate = 0.035f;

        [Tooltip("Energy a boid pays per sim second just to stay alive.")]
        [Range(0f, 0.1f)] public float boidUpkeep = 0.004f;

        [Tooltip("Extra energy per sim second for flying: this x speed squared.")]
        [Range(0f, 20f)] public float boidFlightCost = 0.5f;

        [Tooltip("Energy per sim second paid for each neighbour within sight. This is what stops the population growing without limit.")]
        [Range(0f, 0.02f)] public float boidCrowdCost = 0.0002f;

        [Tooltip("Ground colder than this costs energy, deg C.")]
        [Range(-20f, 20f)] public float boidColdLimit = -10f;

        [Tooltip("Energy per sim second lost in ground 15 degrees below the cold limit.")]
        [Range(0f, 0.2f)] public float boidColdCost = 0.005f;

        [Tooltip("Energy a newborn boid starts with, and what the starting flock begins with.")]
        [Range(0.1f, 30f)] public float boidStartEnergy = 14f;

        [Tooltip("A boid with more energy than this has a chick, if it is not crowded.")]
        [Range(0.5f, 40f)] public float boidReproduceEnergy = 18f;

        [Tooltip("Energy the parent spends on a chick (the chick and parent split the rest).")]
        [Range(0f, 10f)] public float boidBirthCost = 2.5f;

        [Tooltip("A boid with more neighbours than this does not breed.")]
        [Range(1, 64)] public int boidBreedCrowdLimit = 20;

        [Tooltip("Chance per sim second, over full canopy, that a boid picks up a seed.")]
        [Range(0f, 1f)] public float boidSeedPickupRate = 0.03f;

        [Tooltip("Canopy density below which a boid cannot find a seed.")]
        [Range(0f, 2f)] public float boidSeedMinCanopy = 0.25f;

        [Tooltip("Sim seconds a boid carries a seed before dropping it.")]
        [Range(1f, 200f)] public float boidSeedCarrySeconds = 25f;

        [Tooltip("Sim seconds after a drop before a boid picks up another seed.")]
        [Range(0f, 200f)] public float boidSeedCooldown = 15f;

        [Tooltip("Organic matter a dead boid returns to the soil.")]
        [Range(0f, 1f)] public float boidDeathOrganic = 0.05f;

        [Tooltip("Boids released by one click of the bird cage.")]
        [Range(10, 2000)] public int boidCageCount = 120;

        [Header("Boids: drawing")]
        [Tooltip("Wing span of a boid, in world units.")]
        [Range(0.001f, 0.02f)] public float boidSize = 0.004f;

        [Tooltip("Smallest size a boid is drawn at on screen, in pixels, so distant flocks read as drifting ink smudges.")]
        [Range(0f, 8f)] public float boidMinPixels = 2.5f;

        [Tooltip("Wing beats per real second.")]
        [Range(1f, 30f)] public float boidFlapRate = 9f;

        [Tooltip("Real seconds between reads of the boid counters and the seed-drop events.")]
        [Range(0.05f, 1f)] public float boidReadbackRealSeconds = 0.25f;

        [Header("Tools (see ToolController)")]
        [Tooltip("Brush radius as a fraction of the camera's distance to the surface, so a tool covers more from far away and less up close.")]
        [Range(0.01f, 0.3f)] public float toolRadiusPerDistance = 0.07f;

        [Tooltip("Smallest brush radius, in world units (reached close to the surface).")]
        [Range(0.002f, 0.1f)] public float toolRadiusMin = 0.01f;

        [Tooltip("Largest brush radius, in world units (reached in desk view).")]
        [Range(0.05f, 1f)] public float toolRadiusMax = 0.3f;

        [Tooltip("How high above the ground a held tool floats, in world units.")]
        [Range(0f, 0.5f)] public float toolHoverHeight = 0.12f;

        [Tooltip("Moisture the watering can adds per second at the brush centre (moisture runs 0..1).")]
        [Range(0.05f, 5f)] public float wateringRate = 0.8f;

        [Tooltip("Nutrients the compost scoop adds per second at the brush centre.")]
        [Range(0.05f, 5f)] public float compostRate = 1.2f;

        [Tooltip("Organic matter the compost scoop adds per second at the brush centre (it decomposes into nutrients).")]
        [Range(0f, 5f)] public float compostOrganicRate = 0.6f;

        [Tooltip("Temperature the heat lamp adds per second at the brush centre, in deg C. The climate pulls it back toward its target, so heating a spot has to be sustained.")]
        [Range(1f, 300f)] public float heatRate = 60f;

        [Tooltip("Temperature the frost spray removes per second at the brush centre, in deg C.")]
        [Range(1f, 300f)] public float frostRate = 60f;

        [Tooltip("Agents the spore dish tries to start per click.")]
        [Range(10, 5000)] public int sporeCount = 400;

        [Tooltip("Radius of a meteor crater, in world units.")]
        [Range(0.03f, 0.6f)] public float meteorRadius = 0.16f;

        [Tooltip("How far a meteor digs the centre of its crater down, in height units (the terrain height runs -1..1). Deep enough on lowland to leave a crater lake.")]
        [Range(0.05f, 1f)] public float meteorDepth = 0.4f;

        [Tooltip("How far the crater rim is raised, in height units.")]
        [Range(0f, 0.5f)] public float meteorRimHeight = 0.12f;

        [Tooltip("Organic matter thrown onto the ring just outside a crater.")]
        [Range(0f, 10f)] public float meteorOrganic = 2f;

        [Tooltip("Seconds before another meteor can be fired.")]
        [Range(0f, 10f)] public float meteorCooldownSeconds = 1.5f;

        [Header("Camera: surface mode (see CameraRig, SurfaceNavigation)")]
        [Tooltip("Lowest camera height above the planet radius in surface mode, in world units. Must clear the highest mountain (terrainHeightScale).")]
        [Range(0.04f, 0.3f)] public float surfaceHeightMin = 0.05f;

        [Tooltip("Highest camera height in surface mode; zooming out past it returns to desk view.")]
        [Range(0.2f, 2f)] public float surfaceHeightMax = 0.7f;

        [Tooltip("Camera height when surface mode is entered.")]
        [Range(0.05f, 1f)] public float surfaceHeightDefault = 0.35f;

        [Tooltip("How far the camera looks down from the horizon in surface mode when entered, in degrees.")]
        [Range(5f, 89f)] public float surfacePitchDefaultDegrees = 62f;

        [Tooltip("How fast WASD moves the camera over the ground, in radians of arc per second per unit of height (so it is slower near the ground).")]
        [Range(0.1f, 5f)] public float surfaceMoveSpeed = 1.5f;

        [Tooltip("Pan speed for left-drag, in radians of arc per pixel per unit of height.")]
        [Range(0.0001f, 0.01f)] public float surfaceDragSpeed = 0.0012f;

        [Tooltip("Look speed in surface mode: degrees of turn per pixel of right-drag.")]
        [Range(0.02f, 1f)] public float surfaceLookDegreesPerPixel = 0.2f;

        [Tooltip("How fast the camera flies between desk view and surface mode (spring stiffness, rad/s; settles in about 4.7 / omega seconds).")]
        [Range(1f, 20f)] public float surfaceBlendOmega = 3.2f;

        [Header("Dials (see DialProp)")]
        [Tooltip("Fastest the spin dial can make the planet turn, as a multiple of the starting spin. The slowest is its reciprocal.")]
        [Range(1.5f, 8f)] public float spinDialRange = 4f;

        [Header("Planet surface and debug brush (see PlanetMeshBuilder, DebugBrush)")]
        [Tooltip("Quads along one edge of each of the 6 cube faces of the planet mesh. 128 gives ~99k vertices.")]
        [Range(16, 256)] public int planetMeshSubdivisions = 128;

        [Tooltip("Radius of the debug brush on the planet, in world units. Adjustable on the F1 panel.")]
        [Range(0.02f, 1f)] public float debugBrushRadius = 0.18f;

        [Tooltip("How fast the debug brush adds to a field, in field units per second at the brush centre.")]
        [Range(0.05f, 10f)] public float debugBrushStrength = 1.5f;

        [Header("Rendering: painterly look (see Painterly.hlsl, InkPaper.shader)")]
        [Tooltip("Colour of ink outlines and hatch strokes.")]
        public Color inkColor = new Color(0.10f, 0.07f, 0.08f);

        [Tooltip("Scales raw lamp light into the 0..1 tone range before banding. Higher = more of the scene reaches the brightest band.")]
        [Range(0.05f, 3f)] public float toneExposure = 1.0f;

        [Tooltip("Number of flat tone steps the lighting is snapped to.")]
        [Range(2, 8)] public int bandCount = 4;

        [Tooltip("Softness of the edge between two tone bands, as a fraction of one band (0 = hard edge).")]
        [Range(0.005f, 0.5f)] public float bandSoftness = 0.07f;

        [Tooltip("How far noise pushes a band edge back and forth, in bands. This is what makes the edges look brushed.")]
        [Range(0f, 1.5f)] public float bandWobble = 0.45f;

        [Tooltip("Size of the band-edge wobble: noise blobs per world unit.")]
        [Range(0.2f, 20f)] public float bandWobbleFrequency = 3.5f;

        [Tooltip("Gap between hatch lines, in pixels at 720p.")]
        [Range(3f, 30f)] public float hatchSpacingPixels = 8f;

        [Tooltip("Direction of the first set of hatch lines, in degrees from horizontal.")]
        [Range(-90f, 90f)] public float hatchAngleDegrees = 35f;

        [Tooltip("How dark a hatch stroke is (0 = invisible, 1 = solid ink).")]
        [Range(0f, 1f)] public float hatchStrength = 0.6f;

        [Tooltip("Width of the inverted-hull outline on props, in pixels at 720p.")]
        [Range(0f, 8f)] public float hullOutlinePixels = 1.8f;

        [Tooltip("Width of the fullscreen edge-detection outline, in pixels at 720p.")]
        [Range(0.5f, 6f)] public float edgeOutlinePixels = 1.2f;

        [Tooltip("Depth jump (relative to distance) that starts an ink line. Lower = more lines.")]
        [Range(0.001f, 0.5f)] public float edgeDepthThreshold = 0.03f;

        [Tooltip("Change in surface direction that starts an ink line. Lower = more lines.")]
        [Range(0.05f, 2f)] public float edgeNormalThreshold = 0.6f;

        [Tooltip("How much outline width varies along the line (0 = constant, 1 = strongly uneven).")]
        [Range(0f, 1f)] public float outlineWobble = 0.6f;

        [Tooltip("Strength of the paper fibre grain (0 = none).")]
        [Range(0f, 0.6f)] public float paperGrainStrength = 0.16f;

        [Tooltip("How much the screen corners are darkened (0 = none).")]
        [Range(0f, 1f)] public float vignetteStrength = 0.5f;

        [Tooltip("How many times a second the line noise re-rolls ('boil'), in Hz. Hand-drawn animation is typically 8 to 12.")]
        [Range(1f, 24f)] public float boilRateHz = 9f;

        [Header("Camera: desk view (see CameraRig)")]
        [Tooltip("Vertical field of view, in degrees.")]
        [Range(20f, 80f)] public float cameraFieldOfView = 40f;

        [Tooltip("How far below the planet's centre the camera aims, in world units. Keeps the desk in frame.")]
        [Range(0f, 1f)] public float deskFocusDrop = 0.45f;

        [Tooltip("How far the camera may orbit left or right of straight-on, in degrees.")]
        [Range(0f, 180f)] public float deskYawLimitDegrees = 55f;

        [Tooltip("Lowest camera elevation above the horizontal, in degrees.")]
        [Range(0f, 45f)] public float deskPitchMinDegrees = 6f;

        [Tooltip("Highest camera elevation above the horizontal, in degrees.")]
        [Range(10f, 89f)] public float deskPitchMaxDegrees = 50f;

        [Tooltip("Camera elevation when Play starts, in degrees.")]
        [Range(0f, 89f)] public float deskPitchDefaultDegrees = 20f;

        [Tooltip("Closest desk-view distance from the focus point, in world units.")]
        [Range(1.2f, 5f)] public float deskDistanceMin = 1.8f;

        [Tooltip("Farthest desk-view distance from the focus point, in world units.")]
        [Range(3f, 20f)] public float deskDistanceMax = 8.5f;

        [Tooltip("Desk-view distance when Play starts, in world units.")]
        [Range(1.2f, 20f)] public float deskDistanceDefault = 6.4f;

        [Tooltip("Orbit speed: degrees of rotation per pixel of mouse drag.")]
        [Range(0.02f, 1f)] public float orbitDegreesPerPixel = 0.2f;

        [Tooltip("Fraction of the current distance removed by one scroll notch (0.12 = 12% closer).")]
        [Range(0.02f, 0.5f)] public float zoomFractionPerNotch = 0.12f;

        [Tooltip("Stiffness of the camera's smoothing spring, in rad/s. Higher = snappier. Settles in about 4.7 / omega seconds.")]
        [Range(1f, 40f)] public float cameraSpringOmega = 9f;

        [Header("Atmosphere and sky (see AtmosphereController)")]
        [Tooltip("Thickness of the planet's air, in world units above the surface. Sets how wide the glow around the planet is.")]
        [Range(0.02f, 0.5f)] public float atmosphereHeight = 0.20f;

        [Tooltip("Brightness of the glow around the planet seen from the desk (0 = none).")]
        [Range(0f, 4f)] public float atmosphereStrength = 2.5f;

        [Tooltip("Colour of the air in daylight (blue, because short wavelengths scatter most).")]
        public Color atmosphereColor = new Color(0.30f, 0.55f, 1.0f);

        [Tooltip("Colour of the air and sky when the lamp is near the horizon (sunset).")]
        public Color sunsetColor = new Color(1.0f, 0.42f, 0.16f);

        [Tooltip("Sky colour straight overhead on a clear day, seen from the surface.")]
        public Color skyZenithColor = new Color(0.09f, 0.29f, 0.88f);

        [Tooltip("Sky colour at the horizon on a clear day, seen from the surface.")]
        public Color skyHorizonColor = new Color(0.50f, 0.74f, 0.97f);

        [Tooltip("Sky colour at night.")]
        public Color nightSkyColor = new Color(0.012f, 0.022f, 0.070f);

        [Tooltip("How many stars show at night and, faintly, behind the desk (0 = none).")]
        [Range(0f, 1f)] public float starDensity = 0.35f;

        [Tooltip("Strength of the faint coloured haze behind the planet in desk view (0 = plain dark room).")]
        [Range(0f, 3f)] public float deskHazeStrength = 1f;

        [Header("Clouds (see AtmosphereController and Clouds.hlsl)")]
        [Tooltip("Height of the cloud layer above the surface, in world units.")]
        [Range(0.02f, 0.5f)] public float cloudHeight = 0.12f;

        [Tooltip("How cloudy it is on the whole (0 = clear, 1 = overcast). Where it is wet, clouds form more easily whatever this says.")]
        [Range(0f, 1f)] public float cloudCoverage = 0.06f;

        [Tooltip("How much the ground's moisture decides where clouds form (0 = clouds ignore the ground, 1 = clouds sit over wet places).")]
        [Range(0f, 1f)] public float cloudMoistureInfluence = 0.7f;

        [Tooltip("Size of the cloud pattern: bigger numbers give more, smaller clouds.")]
        [Range(0.5f, 8f)] public float cloudScale = 3.0f;

        [Tooltip("How high the clouds you see overhead from the surface are, in world units above the ground. Must be higher than the camera can climb (surfaceHeightMax).")]
        [Range(0.3f, 1.5f)] public float skyCloudHeight = 0.9f;

        [Tooltip("Size of the clouds seen overhead from the surface compared with the planet's cloud layer (2 = clouds half as wide, so there are more of them).")]
        [Range(0.5f, 6f)] public float skyCloudScaleMultiplier = 2.0f;

        [Tooltip("How fast the wind carries the clouds round the planet, in radians per sim second.")]
        [Range(0f, 0.2f)] public float cloudDriftRadiansPerSecond = 0.05f;

        [Tooltip("How fast clouds change shape as they travel (they grow, merge and fade), in noise cells per sim second. 0 = clouds keep their shape.")]
        [Range(0f, 0.3f)] public float cloudMorphRate = 0.04f;

        [Tooltip("How much warm air makes more cloud and cold air less (0 = temperature does not matter).")]
        [Range(0f, 1f)] public float cloudTemperatureInfluence = 0.4f;

        [Tooltip("How dark the heart of a raining cloud is (1 = same as other cloud, lower = darker storm cloud).")]
        [Range(0.2f, 1f)] public float stormCloudBrightness = 0.55f;

        [Tooltip("How fast rain adds water to the ground under a raining cloud, in moisture per sim second at full rain. 0 = it never rains water.")]
        [Range(0f, 0.05f)] public float rainRate = 0.015f;

        [Tooltip("How many rain streaks you see on the surface in heavy rain (0 = none).")]
        [Range(0f, 1f)] public float rainStreakStrength = 0.8f;

        [Tooltip("How solid the clouds look (1 = opaque white).")]
        [Range(0f, 1f)] public float cloudOpacity = 0.85f;

        [Tooltip("How dark the shadow of a cloud is on the ground below it (0 = no shadows).")]
        [Range(0f, 0.8f)] public float cloudShadowStrength = 0.35f;

        [Header("Time (see SimClock)")]
        [Tooltip("Length of one planet day (one full spin) at 1x speed, in real seconds.")]
        [Range(4f, 240f)] public float dayLengthSeconds = 24f;

        [Tooltip("Length of one year (one full summer-winter cycle), in planet days.")]
        [Range(2f, 60f)] public float yearLengthDays = 10f;

        [Tooltip("Axial tilt epsilon, in degrees: the latitude the lamp is overhead at midsummer. Larger tilt = stronger seasons. Earth is about 23, but the lamp is only ~2 planet radii away so it lights a cap of just ~62 degrees; the tilt must exceed ~28 for the pole to see it, giving polar day and night.")]
        [Range(0f, 45f)] public float axialTiltDegrees = 32f;

        [Tooltip("Size of one simulation step, in sim seconds. Every system advances by exactly this much per step.")]
        [Range(1f / 240f, 1f / 15f)] public float fixedStepSeconds = 1f / 60f;

        [Tooltip("Most sim steps allowed in one rendered frame. If more are owed the sim runs slow instead of freezing the frame. 16 lets the 16x dial keep up at 60 fps.")]
        [Range(1, 64)] public int maxStepsPerFrame = 16;

        /// <summary>World position of the planet's centre when it is not bobbing: above the desk by hover height + radius.</summary>
        public Vector3 PlanetRestCentre => new Vector3(0f, planetHoverHeight + planetRadius, 0f);
    }
}
