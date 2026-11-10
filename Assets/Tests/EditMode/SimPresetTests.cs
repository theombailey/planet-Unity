using NUnit.Framework;
using UnityEngine;

namespace Terrarium.Tests
{
    public sealed class SimPresetTests
    {
        [Test]
        public void LushIsWarmerAndHarshIsColderThanDefault()
        {
            var config = ScriptableObject.CreateInstance<SimConfig>();
            float baseline = config.baseTemperature;

            SimPresets.Apply(config, SimPresetKind.Lush);
            Assert.Greater(config.baseTemperature, baseline);

            SimPresets.Apply(config, SimPresetKind.Harsh);
            Assert.Less(config.baseTemperature, baseline);

            Object.DestroyImmediate(config);
        }

        [Test]
        public void FreshStartRestoresDefaultsAndKeepsSeed()
        {
            var config = ScriptableObject.CreateInstance<SimConfig>();
            float baseline = config.baseTemperature;
            config.seed = 777;

            SimPresets.Apply(config, SimPresetKind.Harsh);
            SimPresets.Apply(config, SimPresetKind.FreshStart);

            Assert.AreEqual(baseline, config.baseTemperature);
            Assert.AreEqual(777, config.seed);
            Object.DestroyImmediate(config);
        }
    }
}
