using Marco.Network.Round;
using NUnit.Framework;

namespace Marco.Network.Tests
{
    // 수치 정본(작성규칙 규칙 1 · 상세판 4.2): 기획서 부록 A의 파문 수치를 여기 적고, 코드 상수가 다르면 실패한다.
    public class PulseCanonTests
    {
        private const float Tolerance = 0.0001f;

        // 부록 A [숨 게이지] 질식 페널티 "고함급(22m) 파문 1회" — 지속은 고함 등급 정의 2.5초(11-15).
        [Test]
        public void Drowning_IsShoutGrade_22m_2point5s()
        {
            Assert.AreEqual(22f, PulseSpec.Drowning, Tolerance);
            Assert.AreEqual(2.5f, PulseSpec.DrowningSeconds, Tolerance);
        }
    }
}
