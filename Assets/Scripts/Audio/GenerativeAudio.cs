// =============================================================================
// GenerativeAudio
// -----------------------------------------------------------------------------
// Purpose : Makes all the sound in the game, live, from maths. There are no audio
//           files. What you hear is the ecosystem: more birds means busier song,
//           more fungus means a fuller drone, leaves rustle, wind follows the
//           weather, and the tools make their own small sounds.
// Idea    : The audio thread (OnAudioFilterRead) calls us with a block of samples
//           to fill, thousands of times a second. We add up a few simple layers:
//             wind     white noise through two one-pole low-pass filters, with a
//                      slow "gust" wobble. Colder planets whistle (brighter noise).
//             drone    three slightly detuned sine waves (A1, A1+, E2) whose
//                      loudness follows the fungus count and whose slow tremolo
//                      follows the network's trail density.
//             flock    short bell-like notes (a sine plus a quiet octave) picked
//                      at random from a pentatonic scale, which cannot sound
//                      wrong. Notes arrive at random (Poisson) times; the rate
//                      follows the bird count. Day plays an octave higher.
//             leaves   tiny noise ticks whose rate follows trees alive x leaf
//                      cover (bare winter trees are silent).
//             tools    held tools hum or hiss while used; one-shot tools play a
//                      short event (spore pop, meteor thud, seed plink, bird
//                      flutter).
//           Everything is mixed, scaled by the master volume, and squashed with
//           tanh so it can never clip.
// Equations:
//   one-pole low-pass   y += k * (x - y)                    (k small = darker)
//   sine voice          out = sin(phase), phase += 2*pi*f/sampleRate
//   note envelope       env *= exp(-1 / (sampleRate * decaySeconds))
//   Poisson arrivals    wait = -ln(u) / rate, u uniform in (0,1)
//   pentatonic pitch    f = 220 Hz * 2^(semitone/12), semitone in {0,2,4,7,9}
//   soft limit          out = tanh(mix * masterVolume)
// Threads : The main thread (Update) turns EcosystemStats into a few target
//           numbers; the audio thread only reads them. They are plain floats
//           and ints, so a half-updated value just means a slightly stale
//           number for a few milliseconds. No locks and no allocation, ever.
// Parameters: masterVolume (0..1), plus the layer gains below.
// =============================================================================

using UnityEngine;
using Terrarium.Interaction;

namespace Terrarium.Audio
{
    /// <summary>Generative soundscape driven by the ecosystem. Put it on the object with the AudioListener.</summary>
    public sealed class GenerativeAudio : MonoBehaviour
    {
        private const int VoiceCount = 8;
        private const int EventNone = 0;
        private const int EventSpore = 1;
        private const int EventMeteor = 2;
        private const int EventSeed = 3;
        private const int EventFlock = 4;
        private const float TwoPi = 2f * Mathf.PI;

        private static readonly float[] PentatonicSemitones = { 0f, 2f, 4f, 7f, 9f };

        [Tooltip("Where the ecosystem numbers come from.")]
        [SerializeField] private StatsSystem stats;

        [Tooltip("The simulation clock (for day and night).")]
        [SerializeField] private SimRoot simRoot;

        [Tooltip("Overall loudness, 0 to 1.")]
        [Range(0f, 1f)] [SerializeField] private float masterVolume = 0.6f;

        [Tooltip("How loud the wind layer can get.")]
        [Range(0f, 0.3f)] [SerializeField] private float windLevel = 0.12f;

        [Tooltip("How loud the fungus drone can get.")]
        [Range(0f, 0.2f)] [SerializeField] private float droneLevel = 0.06f;

        [Tooltip("How loud the bird notes are.")]
        [Range(0f, 0.5f)] [SerializeField] private float flockLevel = 0.12f;

        [Tooltip("How loud the leaf rustle is.")]
        [Range(0f, 0.3f)] [SerializeField] private float leafLevel = 0.05f;

        // ---- Written by the main thread, read by the audio thread --------------------------------------------
        private float targetWind;
        private float targetBright;
        private float targetDrone;
        private float targetTremolo;
        private float targetFlockRate;
        private float targetLeafRate;
        private float targetNight;
        private int heldTool;       // 0 none, 1 water, 2 compost, 3 heat, 4 frost
        private int eventCounter;
        private int eventKind;
        private float heldTimer;

        // ---- Owned by the audio thread ------------------------------------------------------------------------
        private float sampleRate = 48000f;
        private uint rngState = 2463534242u;
        private float wind1;
        private float wind2;
        private float gustPhase;
        private float windGain;
        private float brightness;
        private float droneGain;
        private float tremolo;
        private float tremPhase;
        private float dronePhaseA;
        private float dronePhaseB;
        private float dronePhaseC;
        private float night;
        private float flockRate;
        private float leafRate;
        private float noteWait = 1f;
        private float leafWait = 1f;
        private float leafEnv;
        private float leafPrev;
        private float toolLevel;
        private int toolShown;
        private float toolPhase;
        private float toolLow;
        private int seenEvents;
        private int eventPlaying;
        private float eventTime;
        private float eventPhase;
        private float eventLow;

        private readonly float[] voiceFreq = new float[VoiceCount];
        private readonly float[] voicePhase = new float[VoiceCount];
        private readonly float[] voiceEnv = new float[VoiceCount];
        private readonly float[] voicePan = new float[VoiceCount];
        private int nextVoice;

        /// <summary>Overall loudness, 0 to 1.</summary>
        public float MasterVolume
        {
            get => masterVolume;
            set => masterVolume = Mathf.Clamp01(value);
        }

        private void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate;
            targetNight = 0f;
            targetWind = 0.5f;
        }

        /// <summary>Called by the tool controller whenever a tool is used.</summary>
        /// <param name="kind">Which tool.</param>
        public void NotifyTool(ToolKind kind)
        {
            switch (kind)
            {
                case ToolKind.WateringCan: heldTool = 1; heldTimer = 0.15f; break;
                case ToolKind.CompostScoop: heldTool = 2; heldTimer = 0.15f; break;
                case ToolKind.HeatLamp: heldTool = 3; heldTimer = 0.15f; break;
                case ToolKind.FrostSpray: heldTool = 4; heldTimer = 0.15f; break;
                case ToolKind.SporeDish: Trigger(EventSpore); break;
                case ToolKind.Meteor: Trigger(EventMeteor); break;
                case ToolKind.SeedJar: Trigger(EventSeed); break;
                case ToolKind.BoidCage: Trigger(EventFlock); break;
            }
        }

        private void Trigger(int kind)
        {
            eventKind = kind;
            eventCounter++;
        }

        // Main thread: turn the ecosystem snapshot into target loudnesses and rates.
        private void Update()
        {
            heldTimer -= Time.unscaledDeltaTime;
            if (heldTimer <= 0f)
            {
                heldTool = 0;
            }

            if (stats == null)
            {
                return;
            }

            EcosystemStats s = stats.Latest;
            if (!s.Valid)
            {
                return;
            }

            float day = simRoot != null ? simRoot.Clock.DayPhase : 0f;
            targetNight = 0.5f - 0.5f * Mathf.Cos(TwoPi * day);

            float bare = 1f - Mathf.Clamp01(s.MeanLeafCover);
            targetWind = Mathf.Lerp(0.4f, 1f, bare) * (1f - 0.4f * targetNight);
            targetBright = 1f - Mathf.Clamp01((s.MeanTemperature + 10f) / 40f);
            targetDrone = Mathf.Clamp01(Mathf.Log10(1f + s.PhysarumAlive) / 5.2f);
            targetTremolo = Mathf.Clamp01(s.MeanTrail);
            targetFlockRate = (0.15f + 1.85f * Mathf.Clamp01(Mathf.Log10(1f + s.BoidsAlive) / 4f)) * (1f - 0.7f * targetNight);
            targetLeafRate = 12f * Mathf.Clamp01(s.TreesAlive / 300f) * Mathf.Clamp01(s.MeanLeafCover);
        }

        // Audio thread. data holds interleaved samples (channels per frame).
        private void OnAudioFilterRead(float[] data, int channels)
        {
            float invRate = 1f / sampleRate;
            float noteDecay = Mathf.Exp(-invRate / 0.7f);
            float leafDecay = Mathf.Exp(-invRate / 0.012f);
            float slew = 0.0004f;
            int frames = data.Length / channels;

            for (int i = 0; i < frames; i++)
            {
                // Glide the controls toward their targets so nothing ever jumps.
                windGain += (targetWind - windGain) * slew;
                brightness += (targetBright - brightness) * slew;
                droneGain += (targetDrone - droneGain) * slew;
                tremolo += (targetTremolo - tremolo) * slew;
                night += (targetNight - night) * slew;
                flockRate += (targetFlockRate - flockRate) * slew;
                leafRate += (targetLeafRate - leafRate) * slew;

                float left = 0f;
                float right = 0f;

                // ---- Wind: noise through two low-pass filters, with gusts ----
                float noise = Noise();
                float k = 0.006f + 0.05f * brightness;
                wind1 += k * (noise - wind1);
                wind2 += k * (wind1 - wind2);
                gustPhase += TwoPi * 0.09f * invRate;
                if (gustPhase > TwoPi) gustPhase -= TwoPi;
                float gust = 0.65f + 0.35f * Mathf.Sin(gustPhase);
                float windSample = wind2 * windGain * windLevel * gust * 14f;
                left += windSample;
                right += windSample;

                // ---- Fungus drone: three detuned sines with a slow tremolo ----
                tremPhase += TwoPi * 0.17f * invRate;
                if (tremPhase > TwoPi) tremPhase -= TwoPi;
                float tremGain = 1f - 0.4f * tremolo * (0.5f + 0.5f * Mathf.Sin(tremPhase));
                dronePhaseA = Advance(dronePhaseA, 55f, invRate);
                dronePhaseB = Advance(dronePhaseB, 55.4f, invRate);
                dronePhaseC = Advance(dronePhaseC, 82.5f, invRate);
                float drone = (Mathf.Sin(dronePhaseA) + Mathf.Sin(dronePhaseB) + 0.6f * Mathf.Sin(dronePhaseC)) * 0.33f;
                float droneSample = drone * droneGain * droneLevel * tremGain * (1f + 0.5f * night);
                left += droneSample;
                right += droneSample;

                // ---- Flock: random pentatonic notes at a Poisson rate ----
                noteWait -= invRate;
                if (noteWait <= 0f)
                {
                    StartNote();
                    noteWait = -Mathf.Log(Mathf.Max(Uniform(), 1e-4f)) / Mathf.Max(flockRate, 0.05f);
                }

                for (int v = 0; v < VoiceCount; v++)
                {
                    float env = voiceEnv[v];
                    if (env < 0.0005f)
                    {
                        continue;
                    }

                    voicePhase[v] = Advance(voicePhase[v], voiceFreq[v], invRate);
                    float tone = (Mathf.Sin(voicePhase[v]) + 0.25f * Mathf.Sin(2f * voicePhase[v])) * env * flockLevel;
                    left += tone * (1f - voicePan[v]);
                    right += tone * voicePan[v];
                    voiceEnv[v] = env * noteDecay;
                }

                // ---- Leaves: tiny noise ticks ----
                leafWait -= invRate;
                if (leafWait <= 0f)
                {
                    leafEnv = 0.4f + 0.6f * Uniform();
                    leafWait = -Mathf.Log(Mathf.Max(Uniform(), 1e-4f)) / Mathf.Max(leafRate, 0.05f);
                }

                float hiss = Noise();
                float leaf = (hiss - leafPrev) * leafEnv * leafLevel;
                leafPrev = hiss;
                leafEnv *= leafDecay;
                left += leaf;
                right += leaf;

                // ---- Held tools ----
                float toolTarget = heldTool != 0 ? 1f : 0f;
                if (heldTool != 0)
                {
                    toolShown = heldTool;
                }

                toolLevel += (toolTarget - toolLevel) * 0.0008f;
                if (toolLevel > 0.001f)
                {
                    float toolSample = ToolSample(toolShown, invRate) * toolLevel * 0.12f;
                    left += toolSample;
                    right += toolSample;
                }

                // ---- One-shot events ----
                if (seenEvents != eventCounter)
                {
                    seenEvents = eventCounter;
                    eventPlaying = eventKind;
                    eventTime = 0f;
                    eventPhase = 0f;
                }

                if (eventPlaying != EventNone)
                {
                    float e = EventSample(invRate);
                    left += e;
                    right += e;
                }

                // ---- Mix down: master volume, then a soft limit ----
                float outL = (float)System.Math.Tanh(left * masterVolume * 2f);
                float outR = (float)System.Math.Tanh(right * masterVolume * 2f);
                int index = i * channels;
                if (channels == 1)
                {
                    data[index] = 0.5f * (outL + outR);
                }
                else
                {
                    data[index] = outL;
                    data[index + 1] = outR;
                    for (int c = 2; c < channels; c++)
                    {
                        data[index + c] = 0f;
                    }
                }
            }
        }

        private void StartNote()
        {
            // Quiet at night: far fewer notes and an octave lower.
            float octave = night > 0.5f ? 1f : 2f;
            float semitone = PentatonicSemitones[(int)(Uniform() * PentatonicSemitones.Length) % PentatonicSemitones.Length];
            int v = nextVoice;
            nextVoice = (nextVoice + 1) % VoiceCount;
            voiceFreq[v] = 220f * octave * Mathf.Pow(2f, semitone / 12f);
            voicePhase[v] = 0f;
            voiceEnv[v] = 0.6f + 0.4f * Uniform();
            voicePan[v] = Uniform();
        }

        private float ToolSample(int tool, float invRate)
        {
            float noise = Noise();
            switch (tool)
            {
                case 1: // watering can: hissing rain (noise with the low end removed)
                    toolLow += 0.08f * (noise - toolLow);
                    return noise - toolLow;
                case 2: // compost scoop: dull rustling
                    toolLow += 0.02f * (noise - toolLow);
                    return toolLow * 4f;
                case 3: // heat lamp: warm rising hum
                    toolPhase = Advance(toolPhase, 180f + 40f * Mathf.Sin(gustPhase * 3f), invRate);
                    return Mathf.Sin(toolPhase) + 0.4f * Mathf.Sin(2f * toolPhase);
                case 4: // frost spray: thin, glassy shimmer
                    toolPhase = Advance(toolPhase, 2400f + 300f * Mathf.Sin(gustPhase * 5f), invRate);
                    return Mathf.Sin(toolPhase) * (0.6f + 0.4f * noise);
                default:
                    return 0f;
            }
        }

        private float EventSample(float invRate)
        {
            eventTime += invRate;
            float t = eventTime;
            float sample = 0f;
            bool done = false;

            switch (eventPlaying)
            {
                case EventSpore: // a soft downward pop
                    eventPhase = Advance(eventPhase, Mathf.Lerp(700f, 220f, Mathf.Clamp01(t / 0.15f)), invRate);
                    sample = Mathf.Sin(eventPhase) * Mathf.Exp(-t * 22f) * 0.5f;
                    done = t > 0.4f;
                    break;

                case EventMeteor: // low thud plus a rumble of filtered noise
                    eventPhase = Advance(eventPhase, 55f * Mathf.Exp(-t * 1.2f) + 25f, invRate);
                    eventLow += 0.03f * (Noise() - eventLow);
                    sample = (Mathf.Sin(eventPhase) * 0.9f + eventLow * 6f) * Mathf.Exp(-t * 2.2f);
                    done = t > 2.5f;
                    break;

                case EventSeed: // a small bright plink
                    eventPhase = Advance(eventPhase, 1318f, invRate);
                    sample = Mathf.Sin(eventPhase) * Mathf.Exp(-t * 9f) * 0.4f;
                    done = t > 0.8f;
                    break;

                case EventFlock: // a quick flutter of rising chirps
                    float chirp = Mathf.Repeat(t * 14f, 1f);
                    eventPhase = Advance(eventPhase, 1500f + 900f * chirp + 120f * Mathf.Floor(t * 14f), invRate);
                    sample = Mathf.Sin(eventPhase) * (1f - chirp) * Mathf.Exp(-t * 3f) * 0.3f;
                    done = t > 1.2f;
                    break;

                default:
                    done = true;
                    break;
            }

            if (done)
            {
                eventPlaying = EventNone;
            }

            return sample;
        }

        private static float Advance(float phase, float frequency, float invRate)
        {
            phase += TwoPi * frequency * invRate;
            return phase > TwoPi ? phase - TwoPi : phase;
        }

        // xorshift32 random numbers: fast and allocation-free, fine for noise.
        private float Uniform()
        {
            rngState ^= rngState << 13;
            rngState ^= rngState >> 17;
            rngState ^= rngState << 5;
            return (rngState & 0xFFFFFF) / 16777216f;
        }

        private float Noise()
        {
            return Uniform() * 2f - 1f;
        }

        /// <summary>Panel readout and volume slider.</summary>
        public void OnDebugGui()
        {
            GUILayout.Label($"Sound volume: {masterVolume:0.00}");
            masterVolume = GUILayout.HorizontalSlider(masterVolume, 0f, 1f);
        }
    }
}
