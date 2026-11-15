using System.Collections.Generic;
using Marco.Network.Voice;
using NUnit.Framework;

namespace Marco.Network.Tests
{
    // 부록 A [마이크 캘리브레이션] 저장 · 프롬프트 규칙.
    public class MicCalibrationStoreTests
    {
        private sealed class MemoryStore : IKeyValueStore
        {
            public readonly Dictionary<string, string> Values = new();
            public int Saves;
            public bool TryGetString(string key, out string value) => Values.TryGetValue(key, out value);
            public void SetString(string key, string value) => Values[key] = value;
            public void Save() => Saves++;
        }

        private static MicCalibration.Result Valid(float offset) =>
            new(true, MicCalibration.TargetDbfs - offset, offset, -80f, 30, MicCalibration.FailReason.None);
        private static MicCalibration.Result Invalid(MicCalibration.FailReason reason) =>
            new(false, float.NaN, 0f, -80f, 3, reason);

        [Test]
        public void Store_SaveAndLoad()
        {
            var memory = new MemoryStore();
            var store = new MicCalibrationStore(memory);
            Assert.IsNull(store.Load("USB Mic"), "never measured");
            store.Save("USB Mic", 3.25f);
            Assert.AreEqual(3.25f, store.Load("USB Mic").Value, 0.0001f);
            Assert.AreEqual(1, memory.Saves);
            StringAssert.StartsWith("marco.settings.micOffset.", MicCalibrationStore.Key("USB Mic"));
        }

        [Test]
        public void Store_ClampsToSix()
        {
            var memory = new MemoryStore();
            var store = new MicCalibrationStore(memory);
            store.Save("a", 9f);
            Assert.AreEqual(6f, store.Load("a").Value, 0.0001f, "saved clamped");
            memory.Values[MicCalibrationStore.Key("b")] = "-12";
            Assert.AreEqual(-6f, store.Load("b").Value, 0.0001f, "a hand-edited value is clamped on load");
        }

        [TestCase("")]
        [TestCase("abc")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        public void Store_BrokenValueIsNone(string text)
        {
            var memory = new MemoryStore();
            memory.Values[MicCalibrationStore.Key("m")] = text;
            Assert.IsNull(new MicCalibrationStore(memory).Load("m"));
        }

        [Test]
        public void Store_SeparatesDevices()
        {
            var store = new MicCalibrationStore(new MemoryStore());
            store.Save("마이크 (Realtek)", 2f);
            store.Save("마이크 [Realtek]", -4f);   // reads the same after the symbols become '_'
            Assert.AreEqual(2f, store.Load("마이크 (Realtek)").Value, 0.0001f);
            Assert.AreEqual(-4f, store.Load("마이크 [Realtek]").Value, 0.0001f);
            Assert.IsNull(store.Load("Other"));
            Assert.AreNotEqual(MicCalibrationStore.Key("마이크 (Realtek)"), MicCalibrationStore.Key("마이크 [Realtek]"));
            Assert.IsNull(store.Load(null), "no device name");
            Assert.IsNull(store.Load(""));
        }

        [Test]
        public void Store_KeyIsSafe()
        {
            string key = MicCalibrationStore.DeviceKey("Mic \"한글\" / 1:2 " + new string('x', 80));
            foreach (char c in key) Assert.IsTrue(c == '_' || c == '-' || (c < 128 && char.IsLetterOrDigit(c)), "char " + c);
            Assert.LessOrEqual(key.Length, 40 + 1 + 8);
        }

        [Test]
        public void Prompt_Success()
        {
            var prompt = new MicCalibrationPrompt();
            Assert.IsTrue(prompt.Start());
            Assert.AreEqual(MicCalibrationPrompt.State.Success, prompt.Report(Valid(2.5f), false));
            Assert.AreEqual(2.5f, prompt.Offset, 0.0001f);
            Assert.IsTrue(prompt.ShouldSave);
            Assert.AreEqual(1, prompt.Attempts);
            Assert.IsFalse(prompt.Start(), "finished");
        }

        [Test]
        public void Prompt_ThreeFailures_Fallback()
        {
            var prompt = new MicCalibrationPrompt();
            prompt.Start();
            Assert.AreEqual(MicCalibrationPrompt.State.Retry, prompt.Report(Invalid(MicCalibration.FailReason.TooFewActive), false));
            Assert.AreEqual(MicCalibrationPrompt.Failure.TooQuiet, prompt.LastFailure);
            prompt.Start();
            Assert.AreEqual(MicCalibrationPrompt.State.Retry, prompt.Report(Invalid(MicCalibration.FailReason.Clipped), false));
            Assert.AreEqual(MicCalibrationPrompt.Failure.Clipped, prompt.LastFailure);
            prompt.Start();
            Assert.AreEqual(MicCalibrationPrompt.State.Fallback, prompt.Report(Invalid(MicCalibration.FailReason.TooFewActive), true));
            Assert.AreEqual(MicCalibrationPrompt.Failure.Muted, prompt.LastFailure);
            Assert.AreEqual(0f, prompt.Offset);
            Assert.IsFalse(prompt.ShouldSave, "fallback is not saved");
        }

        [Test]
        public void Prompt_Skip()
        {
            var prompt = new MicCalibrationPrompt();
            prompt.Start();
            prompt.Skip();
            Assert.AreEqual(MicCalibrationPrompt.State.Skipped, prompt.Current);
            Assert.AreEqual(0f, prompt.Offset);
            Assert.IsFalse(prompt.ShouldSave);
            Assert.AreEqual(0, prompt.Attempts);
        }

        [Test]
        public void Prompt_CancelDoesNotUseAnAttempt()
        {
            var prompt = new MicCalibrationPrompt();
            for (int i = 0; i < 5; i++)
            {
                Assert.IsTrue(prompt.Start(), "can start again after a cancel");
                prompt.Cancel();   // controller disabled · scene change · round start
                Assert.AreEqual(MicCalibrationPrompt.State.Cancelled, prompt.Current);
            }
            Assert.AreEqual(0, prompt.Attempts);
            prompt.Start();
            prompt.Report(Invalid(MicCalibration.FailReason.TooFewActive), false);
            prompt.Start();
            prompt.Cancel();   // the round starts during the second attempt
            Assert.AreEqual(1, prompt.Attempts);
            prompt.Start();
            Assert.AreEqual(MicCalibrationPrompt.State.Retry, prompt.Report(Invalid(MicCalibration.FailReason.TooFewActive), false), "2 of 3");
        }

        [Test]
        public void Prompt_ReportOutsideMeasuringIsIgnored()
        {
            var prompt = new MicCalibrationPrompt();
            Assert.AreEqual(MicCalibrationPrompt.State.Idle, prompt.Report(Valid(1f), false));
            Assert.AreEqual(0, prompt.Attempts);
        }

        [Test]
        public void Prompt_RestartGivesAttemptsBack()
        {
            var prompt = new MicCalibrationPrompt();
            for (int i = 0; i < 3; i++) { prompt.Start(); prompt.Report(Invalid(MicCalibration.FailReason.TooFewActive), false); }
            Assert.AreEqual(MicCalibrationPrompt.State.Fallback, prompt.Current);
            prompt.Restart();
            Assert.AreEqual(0, prompt.Attempts);
            Assert.IsTrue(prompt.Start());
        }
    }
}
