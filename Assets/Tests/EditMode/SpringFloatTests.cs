// =============================================================================
// SpringFloatTests (EditMode)
// -----------------------------------------------------------------------------
// Purpose : Checks the critically damped spring used for camera smoothing:
//           it reaches its target, never overshoots, and gives the same answer
//           whether time is fed in as one big step or many small ones (which
//           is what "integrated exactly" means, and why it is frame-rate
//           independent).
// =============================================================================

using NUnit.Framework;

namespace Terrarium.Tests
{
    public sealed class SpringFloatTests
    {
        private const float Omega = 9f;

        [Test]
        public void SpringFloat_Step_ConvergesToTarget()
        {
            var spring = new SpringFloat(0f);

            for (int i = 0; i < 300; i++)
            {
                spring.Step(10f, Omega, 1f / 60f);
            }

            Assert.AreEqual(10f, spring.Value, 1e-3f);
            Assert.AreEqual(0f, spring.Velocity, 1e-3f);
        }

        [Test]
        public void SpringFloat_Step_NeverOvershootsFromRest()
        {
            var spring = new SpringFloat(0f);

            for (int i = 0; i < 600; i++)
            {
                spring.Step(1f, Omega, 1f / 60f);
                Assert.LessOrEqual(spring.Value, 1f + 1e-5f, $"Overshot the target at step {i}.");
            }
        }

        [Test]
        public void SpringFloat_Step_IsIndependentOfStepSize()
        {
            var oneBigStep = new SpringFloat(0f);
            var manySmallSteps = new SpringFloat(0f);

            oneBigStep.Step(5f, Omega, 0.5f);
            for (int i = 0; i < 100; i++)
            {
                manySmallSteps.Step(5f, Omega, 0.005f);
            }

            Assert.AreEqual(oneBigStep.Value, manySmallSteps.Value, 1e-3f);
            Assert.AreEqual(oneBigStep.Velocity, manySmallSteps.Velocity, 1e-3f);
        }

        [Test]
        public void SpringFloat_ZeroDelta_ChangesNothing()
        {
            var spring = new SpringFloat(3f) { Velocity = 2f };

            spring.Step(10f, Omega, 0f);

            Assert.AreEqual(3f, spring.Value);
            Assert.AreEqual(2f, spring.Velocity);
        }
    }
}
