using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Marco.Core.WinCondition;
using Marco.Network.Round;
using Marco.Network.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Network.Tests
{
    // 수치 정본(작성규칙 규칙 1 · 상세판 4.2): 기획서 부록 A의 파문 · 판정 수치를 여기 적고, 코드 상수가 다르면 실패한다.
    // 부록 A 값을 바꾸면 이 표도 같이 바꾼다 — 코드만 바뀌면 이 테스트가 잡는다.
    public class PulseCanonTests
    {
        private const float Tolerance = 0.0001f;
        private const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // 부록 A [숨 게이지] 질식 페널티 "고함급(22m) 파문 1회" — 지속은 고함 등급 정의 2.5초(11-15).
        [Test]
        public void Drowning_IsShoutGrade_22m_2point5s()
        {
            Assert.AreEqual(22f, PulseSpec.Drowning, Tolerance);
            Assert.AreEqual(2.5f, PulseSpec.DrowningSeconds, Tolerance);
        }

        // 부록 A [소리 등급] — 발생 반경 / 지속.
        [TestCase("Whisper 속삭임 4m / 0.6s", PulseSpec.Whisper, 4f, PulseSpec.WhisperSeconds, .6f)]
        [TestCase("Talk 대화 9m / 1.2s", PulseSpec.Talk, 9f, PulseSpec.TalkSeconds, 1.2f)]
        [TestCase("Shout 고함 22m / 2.5s", PulseSpec.Shout, 22f, PulseSpec.ShoutSeconds, 2.5f)]
        [TestCase("Walk 걷기 2m / 0.4s", PulseSpec.Walk, 2f, PulseSpec.WalkSeconds, .4f)]
        [TestCase("Sprint 질주 6m / 0.8s", PulseSpec.Sprint, 6f, PulseSpec.SprintSeconds, .8f)]
        [TestCase("Scream 비명 9m / 1.0s", PulseSpec.Scream, 9f, PulseSpec.ScreamSeconds, 1f)]
        [TestCase("Moan 신음 9m / 1.2s", PulseSpec.Moan, 9f, PulseSpec.MoanSeconds, 1.2f)]
        [TestCase("Knock 노크 9m / 1.2s", EchoKnockRules.Radius, 9f, EchoKnockRules.Seconds, 1.2f)]
        [TestCase("Bandage 붕대 6m / 1.2s", PulseSpec.Bandage, 6f, PulseSpec.BandageSeconds, 1.2f)]
        [TestCase("Treat 치료 9m / 1.2s", PulseSpec.Treat, 9f, PulseSpec.TreatSeconds, 1.2f)]
        [TestCase("Breath 호흡음 9m(최대) / 0.6s", PulseSpec.BreathMax, 9f, PulseSpec.BreathSeconds, .6f)]
        public void SoundGrade_MatchesAppendixA(string row, float radius, float expectedRadius, float seconds, float expectedSeconds)
        {
            Assert.AreEqual(expectedRadius, radius, Tolerance, row);
            Assert.AreEqual(expectedSeconds, seconds, Tolerance, row);
        }

        // 부록 A [소리 등급] Valve 12m · [수중 강제 파문] 2.5초 · [정지 감지] 호흡 3m 시작 · +2m · 최대 9m · [역할별 인지 배율] 술래 반경 ×1.2 · 지속 ×1.5 · [외침] 공포 반경 22m.
        [Test]
        public void OtherPulseNumbers_MatchAppendixA()
        {
            Assert.AreEqual(12f, PulseSpec.Valve, Tolerance, "Valve 밸브 12m");
            Assert.AreEqual(2.5f, PulseSpec.UnderwaterForcedSeconds, Tolerance, "수중 · 배수구 강제 파문 2.5초 주기 = 지속");
            Assert.AreEqual(PulseSpec.UnderwaterForcedSeconds, PulseSpec.DrainWorkPeriod, Tolerance, "배수구 주기 = 수중 강제 파문 주기");
            Assert.AreEqual(3f, PulseSpec.BreathStart, Tolerance, "호흡 시작 3m");
            Assert.AreEqual(2f, PulseSpec.BreathStep, Tolerance, "호흡 +2m");
            Assert.AreEqual(PulseSpec.BreathMax, AwardRules.BreathMaxRadius, Tolerance, "호흡 최대 — 어워드와 같은 값");
            Assert.AreEqual(1.2f, PulseSpec.SeekerHearing, Tolerance, "술래 청취 ×1.2");
            Assert.AreEqual(1.5f, PulseSpec.SeekerDuration, Tolerance, "술래 지속 ×1.5");
            Assert.AreEqual(22f, PulseSpec.FearRadius, Tolerance, "공포 반경 22m");
        }

        // 부록 A [아이템] 찰칵이 반경 6m — ClickerSpec.Radius 하나. 서버(섬광을 받는 거리)와 클라이언트(밝히는 반경) 두 곳이 그 값을 쓴다.
        [Test]
        public void ClickerRadius_Is6m_InBothPlaces()
        {
            Assert.AreEqual(6f, ClickerSpec.Radius, Tolerance, "ClickerSpec.Radius 찰칵이 6m");
            Assert.AreEqual(ClickerSpec.Radius, Const(typeof(NetworkRound), "ClickerVisibilityRadius"), Tolerance, "서버 ClickerVisibilityRadius");
            Assert.AreEqual(ClickerSpec.Radius, Const(typeof(MicrophoneVisionController), "ClickerCameraFlashRadius"), Tolerance, "클라이언트 ClickerCameraFlashRadius");
        }

        // 부록 A [태그 판정] 판정 반경 1.6m · 서버 재검증 상한 = 판정 반경 + 0.8 (11-14) — 코드 기본값과 게임 씬 저장값 둘 다.
        [Test]
        public void TagRadius_Is1point6_ServerAllowance0point8()
        {
            var go = new GameObject("tag radius probe");
            try
            {
                // AddComponent(Type): the test assembly does not reference FishNet (NetworkRound's base class).
                var round = go.AddComponent(typeof(NetworkRound));
                Assert.AreEqual(1.6f, (float)typeof(NetworkRound).GetProperty("TagRadius", Any).GetValue(round), Tolerance, "NetworkRound 기본값 TagRadius");
                Assert.AreEqual(.8f, (float)typeof(NetworkRound).GetField("tagServerTolerance", Any).GetValue(round), Tolerance, "기본값 tagServerTolerance");
            }
            finally { Object.DestroyImmediate(go); }
            string scene = File.ReadAllText("Assets/Scenes/Network/NetworkPool_v1_2.unity");
            Assert.AreEqual(1.6f, Saved(scene, "tagBaseRadius") * Saved(scene, "tagRangeMultiplier"), Tolerance, "NetworkPool_v1_2 저장값 판정 반경");
            Assert.AreEqual(.8f, Saved(scene, "tagServerTolerance"), Tolerance, "NetworkPool_v1_2 저장값 서버 여유");
        }

        private static float Const(System.Type type, string name)
        {
            var field = type.GetField(name, Any);
            Assert.IsNotNull(field, type.Name + "." + name);
            return (float)field.GetValue(null);
        }

        private static float Saved(string scene, string key)
        {
            var match = Regex.Match(scene, @"(?m)^\s+" + key + @": ([-\d.]+)\s*$");
            Assert.IsTrue(match.Success, key + " not saved in the scene");
            return float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
