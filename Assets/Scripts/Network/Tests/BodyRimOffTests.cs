using System.IO;
using Marco.Network.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Network.Tests
{
    // V41: no standing role colour on the other characters' bodies (the V39 BodyRim path is off).
    public class BodyRimOffTests
    {
        [Test]
        public void SurvivorBodies_CarryNoRoleRim()
        {
            Assert.IsFalse(RippleClientFeatures.BodyRim, "the body rim switch is off");
            Assert.IsFalse(SurvivorBodyVisibility.RimActive(), "no rim in any view");
            Assert.IsFalse(RippleClientFeatures.SourceSnapshot, "no frozen silhouette either (it would draw the role colour)");
            Assert.IsFalse(SurvivorBodyVisibility.Active());
        }

        [Test]
        public void CharacterShader_RimDefaultsToNothing()
        {
            string src = File.ReadAllText(Path.Combine(Application.dataPath, "Prototype/Shaders/MarcoMinusCharacter.shader"));
            Assert.IsTrue(src.Contains("_FeelRim (\"Feel rim (rgb × intensity)\", Color) = (0,0,0,0)"), "a body that nobody writes to has no rim");
        }
    }
}
