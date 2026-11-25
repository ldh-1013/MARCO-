using Marco.Network.Escape;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Network.Tests
{
    // 탈출 연출 시간표 (연출.md 5 · 부록 A [탈출자] 전환 페이드 1.0초 청록).
    public class EscapeFadeTests
    {
        [Test]
        public void Colour_IsTheEscapeeCyan()
        {
            Assert.AreEqual("7FE7E0", ColorUtility.ToHtmlStringRGB(EscapeFade.Cyan));
        }

        [Test]
        public void FadeOut_TakesOneSecond_ThenScreenIsFullyCyan()
        {
            Assert.AreEqual(1f, EscapeFade.FadeOutSeconds);
            Assert.AreEqual(0f, EscapeFade.Alpha(0f));
            Assert.AreEqual(1f, EscapeFade.Alpha(1f), 1e-5f);
            Assert.AreEqual(.5f, EscapeFade.Alpha(.5f), 1e-5f); // symmetric ease
        }

        [Test]
        public void FadeOut_IsMonotonicAndEased()
        {
            float previous = 0f;
            for (float t = .02f; t <= 1f; t += .02f)
            {
                float a = EscapeFade.Alpha(t);
                Assert.GreaterOrEqual(a, previous);
                previous = a;
            }
            // Soft start and soft landing: slower than linear near both ends.
            Assert.Less(EscapeFade.Alpha(.1f), .1f);
            Assert.Greater(EscapeFade.Alpha(.9f), .9f);
        }

        [Test]
        public void BodySwitches_OnlyUnderFullCover()
        {
            Assert.IsFalse(EscapeFade.HasSwitchedBody(.999f));
            Assert.IsTrue(EscapeFade.HasSwitchedBody(1f));
            Assert.AreEqual(1f, EscapeFade.Alpha(1f), 1e-5f);
        }

        [Test]
        public void Reveal_ClearsTheCyanAndFinishes()
        {
            float previous = 1f;
            for (float t = 1.02f; t <= EscapeFade.TotalSeconds; t += .02f)
            {
                float a = EscapeFade.Alpha(t);
                Assert.LessOrEqual(a, previous);
                previous = a;
            }
            Assert.AreEqual(0f, EscapeFade.Alpha(EscapeFade.TotalSeconds));
            Assert.IsTrue(EscapeFade.IsFinished(EscapeFade.TotalSeconds));
            Assert.IsFalse(EscapeFade.IsFinished(EscapeFade.TotalSeconds - .01f));
            Assert.AreEqual(0f, EscapeFade.Alpha(10f));
        }

        [Test]
        public void Reveal_FromPartialCover_WhenTheResultScreenCutsIn()
        {
            Assert.AreEqual(.4f, EscapeFade.Reveal(.4f, 0f), 1e-5f);
            Assert.Less(EscapeFade.Reveal(.4f, .25f), .4f);
            Assert.AreEqual(0f, EscapeFade.Reveal(.4f, EscapeFade.RevealSeconds));
        }
    }
}
