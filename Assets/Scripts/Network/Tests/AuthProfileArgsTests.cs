using Marco.Network.Lobby;
using NUnit.Framework;

namespace Marco.Network.Tests
{
    // [11-25 V61] "-profile <name>" launch argument for same-PC testing (AuthProfileArgs).
    public sealed class AuthProfileArgsTests
    {
        [Test]
        public void NoArgument_IsNone_SoTheDefaultProfileIsUsedAsBefore()
        {
            Assert.AreEqual(AuthProfileArgs.Result.None, AuthProfileArgs.Parse(new[] { "MARCO.exe", "-popupwindow" }, out var name));
            Assert.IsNull(name);
            Assert.AreEqual(AuthProfileArgs.Result.None, AuthProfileArgs.Parse(null, out _));
            Assert.AreEqual(AuthProfileArgs.Result.None, AuthProfileArgs.Parse(new[] { "MARCO.exe", "-profile" }, out _), "no value after the flag");
        }

        [TestCase("A")]
        [TestCase("player-2")]
        [TestCase("Test_B")]
        [TestCase("123456789012345678901234567890")]   // 30 characters
        public void ValidName_IsTaken(string value)
        {
            Assert.AreEqual(AuthProfileArgs.Result.Valid, AuthProfileArgs.Parse(new[] { "MARCO.exe", "-profile", value }, out var name));
            Assert.AreEqual(value, name);
        }

        [TestCase("")]
        [TestCase("a b")]
        [TestCase("한글")]
        [TestCase("a.b")]
        [TestCase("1234567890123456789012345678901")]   // 31 characters
        public void InvalidName_IsReportedAndNotTaken(string value)
        {
            Assert.AreEqual(AuthProfileArgs.Result.Invalid, AuthProfileArgs.Parse(new[] { "MARCO.exe", "-profile", value }, out _));
            Assert.IsFalse(AuthProfileArgs.IsValidName(value));
        }

        [Test]
        public void Flag_IsCaseInsensitive_AndMayComeAfterOtherArguments()
        {
            Assert.AreEqual(AuthProfileArgs.Result.Valid, AuthProfileArgs.Parse(new[] { "MARCO.exe", "-screen-width", "960", "-PROFILE", "B" }, out var name));
            Assert.AreEqual("B", name);
        }
    }
}
