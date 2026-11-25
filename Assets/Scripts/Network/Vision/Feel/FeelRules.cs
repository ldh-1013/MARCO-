using UnityEngine;

namespace Marco.Network.Vision
{
    // V29 A-1 · A-2 카메라 · 타격감 (연출.md 2.2 · 4.2 · 4.5 · 15.3 · 16). Pure curves, caps and priorities; every value is a
    // function of the time since its event, so 30 / 60 / 144 fps give the same value at the same time.
    // [GAP 연출 고유 시작값 — 연출.md 16.2] unless a value is quoted from 연출.md.
    public static class FeelRules
    {
        // One strength multiplier for every shake and flash. [GAP] 설정 메뉴 연결은 UI 팀 몫 — 상수만 둔다.
        public const float FeelIntensity = 1f;

        // Caps after summing all channels (rotation as the length of the euler offset vector, position as a length).
        public const float MaxRotationDegrees = .8f, MaxPositionMeters = .02f;
        // 연출.md 2.2 질주 +5° · 잠행 −3°, 4.2 punch-in −5°.
        public const float SprintFov = 5f, SneakFov = -3f, PunchFov = -5f, MaxFovOffset = 5f;
        public const float FovInSeconds = .2f, SprintFovOutSeconds = .25f;
        // Lower priority effects are scaled to 40 % while a higher one runs (punch-in > hit > miss > bob).
        public const float LowerPriorityScale = .4f;

        // 술래 적중 (15.3): back 1.2 cm and pitch 0.5° (head up), 40 ms in, critically damped return, gone by 0.25 s.
        public const float HitKickBackMeters = .012f, HitKickPitchDegrees = .5f, HitKickRiseSeconds = .04f, HitKickSeconds = .25f;
        // 헛스윙 (15.3): a pitch lean (forward, with the swing) of 0.3°, back by 0.3 s. [V30] No yaw / roll — the client does
        // not know which way the arm swings (V29 assumed a right-hand swing).
        public const float MissPitchDegrees = .3f, MissRiseSeconds = .06f, MissSeconds = .3f;
        // 맞은 도망자 (15.3): away from the Seeker — yaw 0.6°, pitch 0.4° — 40 ms in, 0.3 s decay, then 150 ms of low-frequency
        // residual (0.15°).
        public const float HurtYawDegrees = .6f, HurtPitchDegrees = .4f, HurtRiseSeconds = .04f, HurtDecaySeconds = .3f,
            HurtResidualSeconds = .15f, HurtResidualDegrees = .15f;
        // 4.2 메아리 전환 (술래 화면): #EF6A4C full flash 0.00 → 0.15 max → 0.50 gone; FOV −5° max at 0.15, back over 1.0 s.
        public const float FlashMaxAlpha = .35f, FlashRiseSeconds = .15f, FlashSeconds = .5f;
        public const float PunchRiseSeconds = .15f, PunchReturnSeconds = 1f;
        public static readonly Color SeekerFlashColor = new Color32(0xEF, 0x6A, 0x4C, 0xFF);   // 연출 4.2 · §16.1 술래 림

        public static float EaseOut(float x) { x = Mathf.Clamp01(x); float y = 1f - x; return 1f - y * y * y; }
        public static float EaseInOut(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        // A one-shot impulse: ease-out rise to 1 by `rise`, then a critically damped return that reaches exactly 0 at
        // `total` (no overshoot). 0 outside [0, total].
        public static float Impulse(float t, float rise, float total)
        {
            if (t < 0f || t >= total) return 0f;
            if (t < rise) return EaseOut(t / rise);
            const float Tail = .02f, X = 5.8335f;   // (1 + X) e^-X = Tail
            float s = (t - rise) / Mathf.Max(1e-4f, total - rise) * X;
            float v = (1f + s) * Mathf.Exp(-s);
            return Mathf.Max(0f, (v - Tail) / (1f - Tail));
        }

        // Seeker's hit kick: (pitch, yaw, roll) degrees and the back offset in metres.
        public static Vector3 HitKickRotation(float t) => new Vector3(-HitKickPitchDegrees, 0f, 0f) * Impulse(t, HitKickRiseSeconds, HitKickSeconds) * FeelIntensity;
        public static float HitKickBack(float t) => HitKickBackMeters * Impulse(t, HitKickRiseSeconds, HitKickSeconds) * FeelIntensity;

        // Miss lean: pitch only (direction-less).
        public static Vector3 MissRotation(float t) =>
            new Vector3(MissPitchDegrees, 0f, 0f) * Impulse(t, MissRiseSeconds, MissSeconds) * FeelIntensity;

        // 15.3 술래 손끝 림: the Seeker's own rim colour (#EF6A4C) flashed for 0.15 s, 40 ms in, then a critically damped fall.
        public const float RimSeconds = .15f, RimRiseSeconds = .04f, RimIntensity = 1.2f;
        public static Color RimColor(float t) => SeekerFlashColor * (RimIntensity * Impulse(t, RimRiseSeconds, RimSeconds) * FeelIntensity);

        // [V30] The animated head bob (FirstPersonBody) is outside this stack. The stack's own position effects get only the
        // room left under the 2 cm cap after the bob offset of this frame — the bob itself is never cut or changed.
        public const float BobBaselineSeconds = .3f;
        public static float PositionBudget(Vector3 bobOffset) => Mathf.Max(0f, MaxPositionMeters - bobOffset.magnitude);
        public static Vector3 CapPositionWithBob(Vector3 offset, Vector3 bobOffset) =>
            Vector3.ClampMagnitude(CapPosition(offset), PositionBudget(bobOffset));
        // Low-pass of the camera's base position (time constant BobBaselineSeconds) — the bob is what is left over.
        public static Vector3 Baseline(Vector3 previous, Vector3 current, float deltaTime) =>
            Vector3.Lerp(previous, current, 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / BobBaselineSeconds));

        // Hurt shake: away from the Seeker (awaySide +1 = turn right, −1 = left, 0 = no direction known → pitch only).
        public static float HurtSeconds => HurtRiseSeconds + HurtDecaySeconds + HurtResidualSeconds;
        public static Vector3 HurtRotation(float t, float awaySide, float seed)
        {
            if (t < 0f || t >= HurtSeconds) return Vector3.zero;
            float main = Impulse(t, HurtRiseSeconds, HurtRiseSeconds + HurtDecaySeconds);
            var rot = new Vector3(-HurtPitchDegrees, HurtYawDegrees * awaySide, 0f) * main;
            float r = t - (HurtRiseSeconds + HurtDecaySeconds);
            if (r >= 0f)
            {
                float fade = 1f - EaseInOut(r / HurtResidualSeconds);
                rot += new Vector3((Mathf.PerlinNoise(seed, r * 9f) - .5f) * 2f, (Mathf.PerlinNoise(r * 9f, seed) - .5f) * 2f, 0f)
                    * HurtResidualDegrees * fade;
            }
            return rot * FeelIntensity;
        }

        // 4.2 flash alpha on the Seeker's screen.
        public static float FlashAlpha(float t)
        {
            if (t < 0f || t >= FlashSeconds) return 0f;
            float a = t < FlashRiseSeconds ? EaseOut(t / FlashRiseSeconds)
                : 1f - EaseInOut((t - FlashRiseSeconds) / (FlashSeconds - FlashRiseSeconds));
            return FlashMaxAlpha * a * FeelIntensity;
        }

        // 4.2 punch-in FOV offset (degrees, negative).
        public static float PunchFovOffset(float t)
        {
            if (t < 0f || t >= PunchRiseSeconds + PunchReturnSeconds) return 0f;
            return t < PunchRiseSeconds ? PunchFov * EaseOut(t / PunchRiseSeconds)
                : PunchFov * (1f - EaseOut((t - PunchRiseSeconds) / PunchReturnSeconds));
        }

        // A state-driven FOV offset easing (ease-out) from `from` to `to` over `seconds` since the change.
        public static float Follow(float from, float to, float since, float seconds) =>
            Mathf.Lerp(from, to, EaseOut(since / Mathf.Max(1e-4f, seconds)));

        public static Vector3 CapRotation(Vector3 euler) => Vector3.ClampMagnitude(euler, MaxRotationDegrees);
        public static Vector3 CapPosition(Vector3 offset) => Vector3.ClampMagnitude(offset, MaxPositionMeters);
        public static float CapFov(float offset) => Mathf.Clamp(offset, -MaxFovOffset, MaxFovOffset);

        // Priority (punch-in > hit > miss > bob): scale for an effect while a higher one is active.
        public static float PriorityScale(bool higherActive) => higherActive ? LowerPriorityScale : 1f;

        // 15.3 / 4.2 overlap: the Seeker's red flash and the Runner's edge swell are on different players' screens.
        public static bool ShowsSeekerFlash(bool localIsSeeker) => localIsSeeker;
        public static bool ShowsRunnerSwell(bool localIsSeeker) => !localIsSeeker;
    }
}
