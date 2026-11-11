// =============================================================================
// DialProp
// -----------------------------------------------------------------------------
// Purpose : A knob on the desk that you grab and turn with the mouse. There are
//           two: the TIME dial (pause, 0.25x, 1x, 4x, 16x) and the SPIN dial (how fast
//           the planet turns, which also speeds up the year).
// Idea    : Dragging sideways turns the knob; its angle is turned into a value.
//           The time dial has five click-stops: while dragging it follows the
//           mouse freely, and on release it springs to the nearest stop. The
//           spin dial is continuous.
//
// Equations:
//   Time dial   angle in [-75, 75] degrees; stops at -75, -37.5, 0, 37.5, 75 for 0x, 0.25x, 1x, 4x, 16x.
//               index = clamp( round( (angle + 75) / 37.5 ), 0, 4 )
//   Spin dial   angle in [-90, 90] degrees;
//               speed multiplier = range ^ (angle / 90)        (angle 0 = unchanged;
//               +90 = `range` times faster, -90 = `range` times slower: equal steps in
//               angle are equal ratios of speed, which is how a person judges speed)
//               day length = starting day length / multiplier
// Visual   : the dial's transform is turned about its own up axis; a small pointer
//            child (added by the scene builder) shows the position.
// =============================================================================

using UnityEngine;

namespace Terrarium.Interaction
{
    /// <summary>What a dial controls.</summary>
    public enum DialKind
    {
        /// <summary>Pause / 0.25x / 1x / 4x / 16x.</summary>
        Time,

        /// <summary>Planet spin speed.</summary>
        Spin,

        /// <summary>Sound volume.</summary>
        Volume,
    }

    /// <summary>A grab-and-turn knob.</summary>
    public sealed class DialProp : MonoBehaviour
    {
        private const float TimeStopSpacingDegrees = 37.5f;
        private const float TimeMinAngle = -75f;
        private const int TimeLastStop = 4;
        private const float SpinMaxAngle = 90f;
        private const float SettleOmega = 12f;

        [Tooltip("What this dial controls.")]
        [SerializeField] private DialKind kind;

        private Quaternion restRotation;
        private SpringFloat shownAngle;
        private float angle;
        private bool dragging;

        /// <summary>What this dial controls.</summary>
        public DialKind Kind => kind;

        /// <summary>The angle the dial is turned to, in degrees.</summary>
        public float Angle => angle;

        /// <summary>True while the player is turning it.</summary>
        public bool IsDragging => dragging;

        /// <summary>Time dial: the nearest stop, 0..4, for the current angle.</summary>
        public int TimeStopIndex => TimeStopForAngle(angle);

        /// <summary>The time dial's angle for a stop (0..4).</summary>
        public static float AngleForTimeStop(int stop)
        {
            return TimeMinAngle + Mathf.Clamp(stop, 0, TimeLastStop) * TimeStopSpacingDegrees;
        }

        /// <summary>The nearest time-dial stop (0..4) for an angle.</summary>
        public static int TimeStopForAngle(float angleDegrees)
        {
            return Mathf.Clamp(Mathf.RoundToInt((angleDegrees - TimeMinAngle) / TimeStopSpacingDegrees), 0, TimeLastStop);
        }

        /// <summary>Volume dial: the master volume (0..1) for an angle (-90 = silent, +90 = full).</summary>
        public static float VolumeForAngle(float angleDegrees)
        {
            return Mathf.Clamp01((Mathf.Clamp(angleDegrees, -SpinMaxAngle, SpinMaxAngle) + SpinMaxAngle) / (2f * SpinMaxAngle));
        }

        /// <summary>Volume dial: the angle that gives a volume (the inverse of <see cref="VolumeForAngle"/>).</summary>
        public static float VolumeAngle(float volume)
        {
            return Mathf.Clamp01(volume) * 2f * SpinMaxAngle - SpinMaxAngle;
        }

        /// <summary>Spin dial: speed multiplier for an angle, range^(angle/90).</summary>
        public static float SpinMultiplier(float angleDegrees, float range)
        {
            return Mathf.Pow(range, Mathf.Clamp(angleDegrees, -SpinMaxAngle, SpinMaxAngle) / SpinMaxAngle);
        }

        /// <summary>Spin dial: the angle that gives a speed multiplier (the inverse of <see cref="SpinMultiplier"/>).</summary>
        public static float SpinAngle(float multiplier, float range)
        {
            return Mathf.Clamp(Mathf.Log(Mathf.Max(multiplier, 1e-4f)) / Mathf.Log(range) * SpinMaxAngle, -SpinMaxAngle, SpinMaxAngle);
        }

        private void Awake()
        {
            restRotation = transform.rotation;
            shownAngle = new SpringFloat(0f);
        }

        /// <summary>Sets the dial's angle directly (used to make it match the simulation when it is not being turned).</summary>
        public void SetAngle(float degrees)
        {
            if (!dragging)
            {
                angle = ClampAngle(degrees);
            }
        }

        /// <summary>Starts turning the dial.</summary>
        public void BeginDrag()
        {
            dragging = true;
        }

        /// <summary>Turns the dial by a number of degrees.</summary>
        public void Turn(float degrees)
        {
            angle = ClampAngle(angle + degrees);
        }

        /// <summary>Lets go. The time dial settles on its nearest stop.</summary>
        public void EndDrag()
        {
            dragging = false;
            if (kind == DialKind.Time)
            {
                angle = AngleForTimeStop(TimeStopIndex);
            }
        }

        private float ClampAngle(float degrees)
        {
            return kind == DialKind.Time
                ? Mathf.Clamp(degrees, TimeMinAngle, -TimeMinAngle)
                : Mathf.Clamp(degrees, -SpinMaxAngle, SpinMaxAngle);
        }

        private void Update()
        {
            shownAngle.Step(angle, SettleOmega, Time.unscaledDeltaTime);
            transform.rotation = restRotation * Quaternion.Euler(0f, shownAngle.Value, 0f);
        }
    }
}
