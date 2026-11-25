using System.Collections.Generic;
using Marco.Network.Round;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Network.Tests
{
    // 부록 A [사운드 차폐] 차폐 재판정 주기 0.25초 · 재판정 상향 규칙 [11-16].
    public class RecheckRulesTests
    {
        private const float T = 1e-4f;

        [TestCase(HearState.None, HearState.Muffled, true)]
        [TestCase(HearState.None, HearState.Clear, true)]
        [TestCase(HearState.Muffled, HearState.Clear, true)]
        [TestCase(HearState.Muffled, HearState.Muffled, false)]
        [TestCase(HearState.Clear, HearState.Muffled, false)]
        [TestCase(HearState.Muffled, HearState.None, false)]
        [TestCase(HearState.Clear, HearState.None, false)]
        public void UpwardOnly(HearState from, HearState to, bool send) =>
            Assert.AreEqual(send, RecheckRules.Send(from, to, 2f, .5f));

        [Test]
        public void Remaining_SeekerTalk()
        {
            float total = PulseSpec.ListenerSeconds(PulseSpec.TalkSeconds, true, false, true, 1f);
            Assert.AreEqual(1.8f, total, T, "Seeker Talk 1.2 × 1.5");
            Assert.AreEqual(1.3f, RecheckRules.Remaining(total, .5f), T);
            Assert.AreEqual(.5f / 1.8f, RecheckRules.Phase(total, .5f), T);
        }

        [Test]
        public void NothingLeft_NotSent()
        {
            Assert.IsFalse(RecheckRules.Send(HearState.None, HearState.Clear, 1.8f, 1.8f), "remaining 0");
            Assert.IsFalse(RecheckRules.Send(HearState.None, HearState.Clear, 1.8f, 2f), "remaining < 0");
        }

        [Test]
        public void State_FromHearing()
        {
            Assert.AreEqual(HearState.None, RecheckRules.State(false, false));
            Assert.AreEqual(HearState.Muffled, RecheckRules.State(true, true));
            Assert.AreEqual(HearState.Clear, RecheckRules.State(true, false));
        }

        [Test]
        public void Tracks_OnlyOthersNotYetClear()
        {
            Assert.IsTrue(RecheckRules.Tracks(false, false, false, false, HearState.None));
            Assert.IsTrue(RecheckRules.Tracks(false, false, false, false, HearState.Muffled));
            Assert.IsFalse(RecheckRules.Tracks(false, false, false, false, HearState.Clear), "already clear");
            Assert.IsFalse(RecheckRules.Tracks(true, false, false, false, HearState.None), "the source itself");
            Assert.IsFalse(RecheckRules.Tracks(false, true, false, false, HearState.None), "an Echo body (clear from the start)");
            Assert.IsFalse(RecheckRules.Tracks(false, false, true, false, HearState.None), "a looping sound (re-emitted every 0.25 s)");
            Assert.IsFalse(RecheckRules.Tracks(false, false, false, true, HearState.None), "a self-only sound");
        }

        [Test]
        public void Interval_IsTheSpecValue() => Assert.AreEqual(.25f, PulseSpec.OcclusionRecheckSeconds, T);
    }

    // The server tracker with a fake clock and fake hearing (no network): a shout behind a wall, the listener walks out.
    public class OcclusionRecheckTests
    {
        private sealed class World
        {
            public readonly Dictionary<int, HearState> Where = new();   // what each listener would hear now
            public readonly HashSet<int> Gone = new();
            public readonly List<(int listener, HearSample hearing, float elapsed)> Sent = new();

            public bool? Seeker(int id) => Gone.Contains(id) ? (bool?)null : id == 9;
            public bool Hear(int id, Vector3 origin, float radius, out HearSample sample)
            {
                var state = Where[id];
                sample = new HearSample
                {
                    Heard = state != HearState.None, Blocked = state == HearState.Muffled, Point = origin, Gain = 1f,
                    PerceivedRadius = state == HearState.Muffled ? radius / 2f : radius, DurationScale = state == HearState.Muffled ? .5f : 1f
                };
                return sample.Heard;
            }
        }

        private static (OcclusionRecheck tracker, World world) Shout(params (int id, HearState at) [] listeners)
        {
            var world = new World();
            var tracker = new OcclusionRecheck();
            var pulse = tracker.Begin(Vector3.zero, PulseSpec.Shout, PulseSpec.ShoutSeconds, 0f);
            pulse.Send = (id, hearing, elapsed) => world.Sent.Add((id, hearing, elapsed));
            foreach (var (id, at) in listeners)
            {
                world.Where[id] = at;
                if (RecheckRules.Tracks(false, false, false, false, at)) pulse.Note(id, at);
            }
            tracker.Commit(pulse);
            return (tracker, world);
        }

        private static void Run(OcclusionRecheck tracker, World world, float from, float to, System.Action<float> at = null)
        {
            for (float t = from; t <= to + 1e-4f; t += .02f)
            {
                at?.Invoke(t);
                tracker.Tick(t, world.Seeker, world.Hear);
            }
        }

        [Test]
        public void BehindWall_ThenOpen_OneClearUpgradeWithinTheNextRecheck()
        {
            var (tracker, world) = Shout((2, HearState.Muffled));
            Run(tracker, world, 0f, 3f, t => { if (t >= .5f) world.Where[2] = HearState.Clear; });
            Assert.AreEqual(1, world.Sent.Count, "one upgrade");
            Assert.IsFalse(world.Sent[0].hearing.Blocked, "clear");
            Assert.That(world.Sent[0].elapsed, Is.InRange(.5f, .75f), "sent at the first recheck after the move");
            Assert.AreEqual(0, tracker.Count, "clear listeners are not followed any more");
        }

        [Test]
        public void Open_ThenBehindWall_NothingSent()
        {
            var (tracker, world) = Shout((2, HearState.Clear));
            Assert.AreEqual(0, tracker.Count, "a clear listener is never followed");
            Run(tracker, world, 0f, 3f, t => { if (t >= .5f) world.Where[2] = HearState.Muffled; });
            Assert.AreEqual(0, world.Sent.Count);
        }

        [Test]
        public void Muffled_ThenOutOfRange_NoDowngrade()
        {
            var (tracker, world) = Shout((2, HearState.Muffled));
            Run(tracker, world, 0f, 1f, t => { if (t >= .3f) world.Where[2] = HearState.None; });
            Assert.AreEqual(0, world.Sent.Count);
        }

        [Test]
        public void NotHeard_ThenMuffled_ThenClear_TwoUpgrades()
        {
            var (tracker, world) = Shout((9, HearState.None));
            Run(tracker, world, 0f, 3f, t =>
            {
                if (t >= .4f) world.Where[9] = HearState.Muffled;
                if (t >= 1.1f) world.Where[9] = HearState.Clear;
            });
            Assert.AreEqual(2, world.Sent.Count);
            Assert.IsTrue(world.Sent[0].hearing.Blocked);
            Assert.IsFalse(world.Sent[1].hearing.Blocked);
            Assert.Less(world.Sent[0].elapsed, world.Sent[1].elapsed);
        }

        [Test]
        public void ListenerBecomesEchoOrLeaves_Dropped()
        {
            var (tracker, world) = Shout((2, HearState.Muffled));
            world.Gone.Add(2);   // Echo body / left / not a target
            Run(tracker, world, 0f, 1f, t => world.Where[2] = HearState.Clear);
            Assert.AreEqual(0, world.Sent.Count);
            Assert.AreEqual(0, tracker.Count);
        }

        [Test]
        public void AfterTheLongestPerceivedDuration_Removed()
        {
            var (tracker, world) = Shout((2, HearState.Muffled));
            Run(tracker, world, 0f, 3.7f);
            Assert.AreEqual(1, tracker.Count, "Shout 2.5 s × Seeker 1.5 = 3.75 s");
            Run(tracker, world, 3.72f, 4f);
            Assert.AreEqual(0, tracker.Count);
            Assert.AreEqual(0, world.Sent.Count);
        }

        [Test]
        public void TooLateToMatter_NotSent()
        {
            // a Runner (not Seeker): Shout 2.5 s; clear only at 2.6 s → remaining ≤ 0
            var (tracker, world) = Shout((2, HearState.None));
            Run(tracker, world, 0f, 3.7f, t => { if (t >= 2.6f) world.Where[2] = HearState.Clear; });
            Assert.AreEqual(0, world.Sent.Count);
        }
    }
}
