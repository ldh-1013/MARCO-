using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Marco.Network.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Network.Tests
{
    // [11-23 V44] 기획서 §16.1 술래 고함 실루엣 하이라이트 (ShoutRimRules + the one read-only hook) and 연출 4.2 · 15.4 메아리 전환 페이드
    // (EchoFadeRules / EchoFadeRun + the local hold in NetworkPlayer). Set MARCO_VD_CSV_DIR to also write the curves as CSV.
    public class PresentationHooksTests
    {
        static readonly int FeelRim = Shader.PropertyToID("_FeelRim"), BaseColor = Shader.PropertyToID("_BaseColor");

        static void Csv(string name, IEnumerable<string> rows)
        {
            string dir = Environment.GetEnvironmentVariable("MARCO_VD_CSV_DIR");
            if (string.IsNullOrEmpty(dir)) return;
            try { Directory.CreateDirectory(dir); File.WriteAllLines(Path.Combine(dir, name), rows); } catch (Exception) { }
        }
        static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
        static string Src(string relative) => File.ReadAllText(Path.Combine(Application.dataPath, relative));

        // ---- shout rim ----

        [Test]
        public void ShoutRim_Curve_HalfASecond_RisesFast_FallsWithoutBounce_AndIsZeroAtTheEnd()
        {
            var rows = new List<string> { "t_s,weight" };
            Assert.AreEqual(.5f, ShoutRimRules.Seconds, "V44c: 0.5 s");
            Assert.AreEqual(0f, ShoutRimRules.Weight(-.1f)); Assert.AreEqual(0f, ShoutRimRules.Weight(0f), 1e-6f);
            Assert.AreEqual(1f, ShoutRimRules.Weight(ShoutRimRules.RiseSeconds), 1e-4f);
            float previous = 1f;
            for (float t = ShoutRimRules.RiseSeconds; t < ShoutRimRules.Seconds; t += 1f / 240f)
            {
                float w = ShoutRimRules.Weight(t); rows.Add($"{F(t)},{F(w)}");
                Assert.LessOrEqual(w, previous + 1e-6f, $"no bounce at {t}"); Assert.GreaterOrEqual(w, 0f); previous = w;
            }
            Assert.AreEqual(0f, ShoutRimRules.Weight(ShoutRimRules.Seconds)); Assert.AreEqual(0f, ShoutRimRules.Weight(2f));
            Assert.AreEqual(new Color32(0xEF, 0x6A, 0x4C, 0xFF), (Color32)ShoutRimRules.Colour, "§16.1 술래 림 — no new colour");
            Csv("curve_shoutrim.csv", rows);
        }

        [Test]
        public void ShoutRim_OutsideTheLight_IsNotLit_InsideItIs()
        {
            const float radius = 8f, falloff = 2f;
            float near = ShoutRimRules.LightVisibility(1f, radius, 1f, falloff, true), edgeIn = ShoutRimRules.LightVisibility(7.5f, radius, 1f, falloff, true);
            float edge = ShoutRimRules.LightVisibility(8f, radius, 1f, falloff, true), outside = ShoutRimRules.LightVisibility(10f, radius, 1f, falloff, true);
            Assert.Greater(near, .7f); Assert.Less(edgeIn, ShoutRimRules.MinVisibility, "just inside the border the body is already dark");
            Assert.AreEqual(0f, edge); Assert.AreEqual(0f, outside);
            Assert.AreEqual(0f, ShoutRimRules.LightVisibility(0f, 0f, 1f, falloff, true), "no light radius (silent): dark");
            Assert.AreEqual(1f, ShoutRimRules.LightVisibility(50f, 0f, 0f, falloff, false), "no mic-light world (lobby): drawn plainly");
            float peak = ShoutRimRules.RiseSeconds;
            Assert.Greater(ShoutRimRules.RimColor(peak, near).maxColorComponent, .6f, "lit body: the rim shows");
            Assert.AreEqual(0f, ShoutRimRules.RimColor(peak, outside).maxColorComponent, "outside the light: rim 0");
            Assert.AreEqual(0f, ShoutRimRules.RimColor(peak, edgeIn).maxColorComponent, "dark body: rim 0");
            Assert.AreEqual(0f, ShoutRimRules.RimColor(.6f, 1f).maxColorComponent, "off again after 0.5 s even in the light");
            Assert.Less(ShoutRimRules.RimColor(peak, .5f).maxColorComponent, ShoutRimRules.RimColor(peak, 1f).maxColorComponent, "dimmer light, dimmer rim");
        }

        // The hook is one event and one call, raised only for a Seeker's first delivery (never the scream, never the Seeker itself, never a
        // recheck upgrade), so one shout lights the silhouette exactly once per listener. Pinned as source text like V42 C's.
        [Test]
        public void ShoutHook_IsOneEventAndOneCall_FirstDeliveryOnly()
        {
            string src = Src("Scripts/Network/Player/PlayerMovement.Shout.cs");
            Assert.AreEqual(1, Regex.Matches(src, @"public event System\.Action<float> RemoteShoutHeard;").Count, "one event declaration");
            Assert.AreEqual(1, Regex.Matches(src, @"if \(!self && !scream && elapsed <= 0f\) RemoteShoutHeard\?\.Invoke\(duration\);").Count, "one call, this condition");
            Assert.AreEqual(1, Regex.Matches(src, @"RemoteShoutHeard\?\.Invoke").Count, "invoked nowhere else");
            Assert.AreEqual(1, Regex.Matches(src, @"LocalScreamFlash\?\.Invoke").Count, "the scream flash hook is untouched");
            int target = src.IndexOf("private void AbilitySoundTarget", StringComparison.Ordinal);
            Assert.Greater(src.IndexOf("RemoteShoutHeard?.Invoke", StringComparison.Ordinal), target, "inside the existing pulse message handler");
            Assert.AreEqual(3, Regex.Matches(src, @"^\s*\[Target" + @"Rpc]", RegexOptions.Multiline).Count);
            Assert.AreEqual(3, Regex.Matches(src, @"^\s*\[Server" + @"Rpc]", RegexOptions.Multiline).Count);
        }

        // ---- echo transition fade ----

        [Test]
        public void EchoFade_Timing_LandAndUnderwater()
        {
            Assert.AreEqual(1f, EchoFadeRules.HoldSeconds(false)); Assert.AreEqual(2f, EchoFadeRules.HoldSeconds(true));
            Assert.AreEqual(0f, EchoFadeRules.Progress(.5f, false), 1e-6f); Assert.AreEqual(1f, EchoFadeRules.Progress(1f, false), 1e-6f);
            Assert.AreEqual(0f, EchoFadeRules.Progress(1.5f, true), 1e-6f); Assert.AreEqual(1f, EchoFadeRules.Progress(2f, true), 1e-6f);
            Assert.AreEqual(0f, EchoFadeRules.Progress(1f, true), "underwater: nothing happens in the first 1.0 s (the forced rise)");
            Assert.AreEqual(1f, EchoFadeRules.Alpha(.5f, false)); Assert.AreEqual(0f, EchoFadeRules.Alpha(1f, false), 1e-6f);
            Assert.AreEqual(1f, EchoFadeRules.Alpha(1.5f, true)); Assert.AreEqual(0f, EchoFadeRules.Alpha(2f, true), 1e-6f);
            Assert.AreEqual(0f, EchoFadeRules.Rise(.5f, false)); Assert.AreEqual(.25f, EchoFadeRules.Rise(1f, false), 1e-6f);
            Assert.AreEqual(.25f, EchoFadeRules.RiseMeters, "the approved float-up");
            Assert.AreEqual(new Color32(0xEF, 0x6A, 0x4C, 0xFF), (Color32)EchoFadeRules.Tint, "4.2 적색 #EF6A4C — no new colour");
        }

        [Test]
        public void EchoFade_Curves_AreMonotone_AndNeverOvershoot()
        {
            var rows = new List<string> { "t_s,alpha,tint,rise_m" }; float a = 1f, tint = 0f, rise = 0f;
            for (float t = 0f; t <= 1.2f; t += 1f / 120f)
            {
                float al = EchoFadeRules.Alpha(t, false), ti = EchoFadeRules.TintAmount(t, false), ri = EchoFadeRules.Rise(t, false);
                rows.Add($"{F(t)},{F(al)},{F(ti)},{F(ri)}");
                Assert.LessOrEqual(al, a + 1e-6f); Assert.GreaterOrEqual(ti, tint - 1e-6f); Assert.GreaterOrEqual(ri, rise - 1e-6f);
                Assert.LessOrEqual(ri, EchoFadeRules.RiseMeters + 1e-6f); Assert.That(al, Is.InRange(0f, 1f)); Assert.That(ti, Is.InRange(0f, 1f));
                a = al; tint = ti; rise = ri;
            }
            Csv("curve_echofade.csv", rows);
            // ease-in fade (slow first, quick last), ease-out tint (quick first)
            Assert.Greater(EchoFadeRules.Alpha(.75f, false), .6f); Assert.Greater(EchoFadeRules.TintAmount(.625f, false), .5f);
        }

        static Material NewMaterial()
        {
            var shader = Shader.Find("MARCO/WorldSpaceCharacter");
            Assert.IsNotNull(shader, "the character shader is in the project");
            var material = new Material(shader) { name = "fade test" };
            material.SetColor(BaseColor, new Color(.9f, .8f, .7f, 1f));
            return material;
        }

        static GameObject Body(out Material material, out Renderer renderer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.transform.localPosition = new Vector3(0f, 1f, 0f);
            material = NewMaterial(); renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            return go;
        }

        [Test]
        public void EchoFadeRun_FadesThenRestoresEverythingItTouched()
        {
            var go = Body(out var material, out _);
            try
            {
                float surface = material.GetFloat("_Surface"), src = material.GetFloat("_SrcBlend"), z = material.GetFloat("_ZWrite"); int queue = material.renderQueue;
                var run = new EchoFadeRun(); var rows = new List<string> { "t_s,alpha,r,g,b,y" };
                Assert.IsTrue(run.Begin(go.transform, false, 10f)); Assert.IsTrue(run.Active); Assert.IsFalse(run.Underwater);
                bool transparentSeen = false;
                for (float t = 0f; t < .99f; t += 1f / 60f)
                {
                    Assert.IsTrue(run.Tick(10f + t), $"still running at {t}");
                    var c = material.GetColor(BaseColor); rows.Add($"{F(t)},{F(c.a)},{F(c.r)},{F(c.g)},{F(c.b)},{F(go.transform.localPosition.y)}");
                    if (t < .49f) { Assert.AreEqual(1f, c.a, 1e-5f, "fully visible for the first 0.5 s"); Assert.AreEqual(.9f, c.r, 1e-5f, "untinted"); Assert.AreEqual(1f, go.transform.localPosition.y, 1e-5f); }
                    if (t > .75f) { transparentSeen = true; Assert.Less(c.a, 1f); Assert.AreEqual(1f, material.GetFloat("_Surface"), "switched to transparent"); }
                }
                Assert.IsTrue(transparentSeen);
                Assert.IsFalse(run.Tick(11f), "over at 1.0 s — the caller swaps to the hidden body");
                Assert.IsFalse(run.Active);
                var back = material.GetColor(BaseColor); Assert.AreEqual(.9f, back.r, 1e-5f); Assert.AreEqual(1f, back.a, 1e-5f);
                Assert.AreEqual(surface, material.GetFloat("_Surface")); Assert.AreEqual(src, material.GetFloat("_SrcBlend")); Assert.AreEqual(z, material.GetFloat("_ZWrite"));
                Assert.AreEqual(queue, material.renderQueue); Assert.AreEqual(1f, go.transform.localPosition.y, 1e-5f, "float-up put back");
                Csv("echofade_run.csv", rows);
            }
            finally { UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void EchoFadeRun_Underwater_StartsAtOnePointFive()
        {
            var go = Body(out var material, out _);
            try
            {
                var run = new EchoFadeRun(); Assert.IsTrue(run.Begin(go.transform, true, 0f)); Assert.AreEqual(2f, run.HoldSeconds);
                Assert.IsTrue(run.Tick(1.4f)); Assert.AreEqual(1f, material.GetColor(BaseColor).a, 1e-5f, "nothing before 1.5 s");
                Assert.IsTrue(run.Tick(1.75f)); Assert.Less(material.GetColor(BaseColor).a, 1f);
                Assert.IsTrue(run.Tick(1.99f)); Assert.IsFalse(run.Tick(2f));
            }
            finally { UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void EchoFadeRun_StateChangeBodyDestroyedAndRestart_AreSafe()
        {
            var go = Body(out var material, out _);
            var run = new EchoFadeRun();
            try
            {
                // a state change mid-fade (the player is Alive again): Cancel puts the living body back
                run.Begin(go.transform, false, 0f); run.Tick(.8f); Assert.Less(material.GetColor(BaseColor).a, 1f);
                run.Cancel(); Assert.IsFalse(run.Active); Assert.AreEqual(1f, material.GetColor(BaseColor).a, 1e-5f); Assert.AreEqual(1f, go.transform.localPosition.y, 1e-5f);
                run.Cancel();   // twice is fine
                // the same target hit again: a second Begin starts over from the original values, not from the faded ones
                run.Begin(go.transform, false, 0f); run.Tick(.9f); run.Begin(go.transform, false, 5f);
                Assert.AreEqual(1f, material.GetColor(BaseColor).a, 1e-5f); Assert.IsTrue(run.Tick(5.1f)); Assert.AreEqual(.9f, material.GetColor(BaseColor).r, 1e-5f);
                // the body destroyed mid-fade: no exception, the fade just ends
                run.Tick(5.8f); UnityEngine.Object.DestroyImmediate(go); go = null;
                Assert.DoesNotThrow(() => run.Tick(5.9f)); Assert.IsFalse(run.Active);
                Assert.DoesNotThrow(() => run.Cancel());
                // a material destroyed under the run
                var go2 = Body(out var material2, out _);
                run.Begin(go2.transform, false, 0f); run.Tick(.8f); UnityEngine.Object.DestroyImmediate(material2);
                Assert.DoesNotThrow(() => run.Tick(.9f)); Assert.DoesNotThrow(() => run.Cancel());
                UnityEngine.Object.DestroyImmediate(go2);
                Assert.IsFalse(run.Begin(null, false, 0f), "no body = nothing to fade");
            }
            finally { UnityEngine.Object.DestroyImmediate(material); if (go != null) UnityEngine.Object.DestroyImmediate(go); }
        }

        // The hold is local and self-expiring, and only the three approved places in NetworkPlayer know about it.
        [Test]
        public void EchoHold_IsLocalOnly_AndTouchesOnlyTheApprovedLooks()
        {
            string src = Src("Scripts/Network/Player/NetworkPlayer.cs");
            Assert.AreEqual(1, Regex.Matches(src, @"echoFade\?\.OnStatus\(previous, next, Local\)").Count);
            Assert.AreEqual(1, Regex.Matches(src, @"echoFade != null && echoFade\.Holds").Count);
            Assert.AreEqual(10, Regex.Matches(src, @"private readonly SyncVar<").Count, "no SyncVar was added");
            string fade = Src("Scripts/Network/Vision/Feel/EchoTransitionFade.cs");
            Assert.IsFalse(Regex.IsMatch(fade, @"\[(Server|Target|Observers)Rpc"), "no RPC");
            Assert.IsFalse(fade.Contains("SyncVar"), "no SyncVar");
        }
    }
}
