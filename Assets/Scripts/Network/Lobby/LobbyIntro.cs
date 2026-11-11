using UnityEngine;

namespace Marco.Network.Lobby
{
    /// <summary>표시 전용. Local title sequence; never changes the network session or time scale.</summary>
    [DisallowMultipleComponent]
    public sealed class LobbyIntro : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float cuteHoldSeconds = 1.2f;
        [Tooltip("Entrance rhythm: successive gaps tighten from two beats to one beat.")]
        [SerializeField, Range(80, 160)] private float entranceTempoBpm = 112;
        [SerializeField, Min(0.1f)] private float glitchSeconds = 0.48f;
        private const float FirstBounceSeconds = 0.60f;
        private const float BounceSeconds = 1.35f;
        private float EntranceSeconds => LetterDropStart(5) + LetterFallSeconds(5) + BounceSeconds;
        [SerializeField, Min(0.1f)] private float fadeSeconds = 0.15f;
        private enum Phase { Cute, Corroding, Metal, Leaving }
        private Phase phase;
        private float phaseStarted;
        private static bool played;
        private float started = -1f;
        private float animationTime;
        private bool finished;
        private Texture2D[] metalGlyphs;
        private GUIStyle caption;
        private Texture2D dot;
        private Texture2D glitchNoise;
        public bool IsPlaying => enabled && !finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => played = false;

        private void Awake()
        {
            finished = played || Application.isBatchMode;
            played = true;
            started = -1f;
            if (!finished) EnsureMetalGlyphs();
        }

        [ContextMenu("Replay intro")]
        public void Replay()
        {
            finished = false;
            started = -1f;
            animationTime = 0;
            EnsureMetalGlyphs();
            phase = Phase.Cute;
        }

        private void Update()
        {
            if (!IsPlaying) return;
            // A startup/import hitch must not fast-forward past M's entrance into A's.
            if (started >= 0) animationTime += Mathf.Min(Time.unscaledDeltaTime, .05f);
            // Edge-triggered once per frame: held keys and IMGUI key-repeat must not
            // advance multiple stages. anyKeyDown includes keyboard and mouse buttons.
            if (started >= 0 && phase != Phase.Leaving && Input.anyKeyDown)
            {
                Advance();
                return;
            }
            if (phase == Phase.Cute && started >= 0 && animationTime >= EntranceSeconds + cuteHoldSeconds)
            {
                phase = Phase.Corroding;
                phaseStarted = animationTime;
            }
            // A short full-screen signal failure, then the entire metal title holds.
            if (phase == Phase.Corroding && BurstProgress >= 1)
                phase = Phase.Metal;
            if (phase == Phase.Leaving && animationTime - phaseStarted >= fadeSeconds)
                finished = true;
        }

        private void Advance()
        {
            if (phase == Phase.Cute)
            {
                // Finish all drops and rebounds before starting the next visual stage.
                animationTime = Mathf.Max(animationTime, EntranceSeconds);
                phase = Phase.Corroding;
                phaseStarted = animationTime;
            }
            else if (phase == Phase.Corroding || phase == Phase.Metal)
            {
                // Leaving draws the fully converted logo, including !, during the short fade.
                phase = Phase.Leaving;
                phaseStarted = animationTime;
            }
        }

        private void OnGUI()
        {
            if (!IsPlaying) return;
            if (started < 0 && Event.current.type == EventType.Repaint) started = 0;
            float t = animationTime;
            var e = Event.current;
            if (e.type == EventType.MouseDown || e.type == EventType.KeyDown)
            {
                // The transition is handled in Update; keep its input out of the lobby UI.
                e.Use();
            }
            var matrix = GUI.matrix;
            var color = GUI.color;
            int depth = GUI.depth;
            GUI.depth = -10000;
            GUI.matrix = Matrix4x4.identity;
            float decay = AtmosphereProgress();
            float fade = phase == Phase.Leaving ? Mathf.Clamp01((animationTime - phaseStarted) / fadeSeconds) : 0;
            Fill(new Rect(0, 0, Screen.width, Screen.height), Color.Lerp(new Color(.83f, .96f, .94f), new Color(.018f, .026f, .028f), decay));
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280 * scale) / 2, (Screen.height - 720 * scale) / 2), Quaternion.identity, Vector3.one * scale);
            caption ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            EnsureMetalGlyphs();
            var logoMatrix = GUI.matrix;
            DrawCute(t);
            for (int letter = 0; letter < 6; letter++)
            {
                if (!MetalVisible) continue;
                bool tearing = phase == Phase.Corroding;
                Texture2D glyph = metalGlyphs[letter];
                // Match the candy stroke's actual ink bounds, not a font's line box.
                // Candy: 86 x 130 path + 27px stroke; exclamation: 27 x 157.
                var rect = new Rect(233.5f + letter * 140, 271.5f, 113, 157);
                if (letter == 5) { rect.x = 263.5f + letter * 140; rect.width = 27; }
                float opacity = 1;
                DrawGlyph(glyph, Offset(rect, 2, 5), 0, 1, new Color(.09f, .04f, .02f, opacity));
                for (int y = 0; y < 157; y += 2)
                {
                    Color steel = Color.white;
                    float tear = GlitchOffset(rect.y + y - 235, t);
                    float bandHeight = Mathf.Min(2, rect.height - y);
                    var strip = new Rect(rect.x + tear, rect.y + y, rect.width, bandHeight);
                    float top = y / rect.height;
                    float bottom = (y + bandHeight) / rect.height;
                    if (tearing)
                    {
                        DrawGlyph(glyph, Offset(strip, -5, 0), top, bottom, new Color(.15f, .75f, .75f, .6f * opacity));
                        DrawGlyph(glyph, Offset(strip, 5, 0), top, bottom, new Color(.7f, .18f, .12f, .6f * opacity));
                    }
                    steel.a = opacity;
                    DrawGlyph(glyph, strip, top, bottom, steel);
                }
            }
            GUI.matrix = logoMatrix;
            if (decay > 0)
                for (int y = 0; y < 720; y += 4) Fill(new Rect(0, y, 1280, 1), new Color(0, 0, 0, .15f * decay));
            caption.normal.textColor = Color.Lerp(new Color(.22f, .48f, .49f), new Color(.43f, .48f, .46f), decay);
            GUI.Label(new Rect(0, 620, 1280, 28), phase == Phase.Cute ? "CLICK OR ANY KEY TO CONTINUE"
                : phase == Phase.Leaving ? "" : "CLICK OR ANY KEY TO ENTER LOBBY", caption);
            GUI.matrix = Matrix4x4.identity;
            DrawFullScreenGlitch();
            if (fade > 0) Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, fade));
            GUI.matrix = matrix;
            GUI.color = color;
            GUI.depth = depth;
        }

        // Rounded, hand-drawn letterforms keep the opening distinct even without an assigned font.
        private void DrawCute(float time)
        {
            if (dot == null)
            {
                dot = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                dot.hideFlags = HideFlags.HideAndDontSave;
                var pixels = new Color[4096];
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                        pixels[y * 64 + x] = new Color(1, 1, 1, Mathf.Clamp01(31.5f - Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f))));
                dot.SetPixels(pixels);
                dot.Apply();
            }
            Vector2[][] paths = {
                new[] { new Vector2(0, 130), new Vector2(0, 0), new Vector2(43, 65), new Vector2(86, 0), new Vector2(86, 130) },
                new[] { new Vector2(0, 130), new Vector2(43, 0), new Vector2(86, 130), new Vector2(66, 76), new Vector2(20, 76) },
                new[] { new Vector2(0, 130), new Vector2(0, 0), new Vector2(62, 0), new Vector2(86, 20), new Vector2(86, 49), new Vector2(62, 66), new Vector2(0, 66), new Vector2(38, 66), new Vector2(86, 130) },
                new[] { new Vector2(86, 10), new Vector2(62, 0), new Vector2(23, 0), new Vector2(0, 25), new Vector2(0, 105), new Vector2(23, 130), new Vector2(62, 130), new Vector2(86, 120) },
                new[] { new Vector2(23, 0), new Vector2(63, 0), new Vector2(86, 25), new Vector2(86, 105), new Vector2(63, 130), new Vector2(23, 130), new Vector2(0, 105), new Vector2(0, 25), new Vector2(23, 0) },
                new[] { new Vector2(30, 0), new Vector2(30, 83) }
            };
            for (int i = 0; i < paths.Length; i++)
            {
                if (MetalVisible) continue;
                float life = 1;
                float age = time - LetterDropStart(i);
                if (age < 0) continue;
                DropPose(age, LetterFallSeconds(i), out float height, out float squash, out float stretch);
                height *= life;
                squash *= life;
                stretch *= life;
                Vector2 origin = new Vector2(247 + i * 140, 285 - height);
                // The ground shadow stays in place while the letter falls and rebounds.
                float shadowSize = Mathf.Lerp(95, 28, Mathf.Clamp01(height / 400));
                GUI.color = new Color(.16f, .36f, .37f, .14f * life * (1 - Mathf.Clamp01(height / 650)));
                GUI.DrawTexture(new Rect(origin.x + 43 - shadowSize / 2, 436, shadowSize, 10), dot);
                var letterMatrix = GUI.matrix;
                GUIUtility.ScaleAroundPivot(new Vector2(1 + squash * .22f - stretch * .4f, 1 - squash * .24f + stretch), origin + new Vector2(43, 130));
                float tilt = Mathf.Sin(age * 9 + i) * Mathf.Clamp01(height / 100) * 5 * life;
                GUIUtility.RotateAroundPivot(tilt, origin + new Vector2(43, 130));
                Color candy = Color.HSVToRGB(Mathf.Repeat(.94f + i * .13f, 1), .36f, .98f);
                for (int layer = 0; layer < 3; layer++)
                {
                    float width = layer == 2 ? 27 : 39;
                    GUI.color = layer == 0 ? new Color(.28f, .62f, .62f) : layer == 1 ? Color.white : candy;
                    var tint = GUI.color;
                    tint.a = life;
                    GUI.color = tint;
                    Vector2 offset = origin + (layer == 0 ? new Vector2(0, 8) : Vector2.zero);
                    for (int j = 1; j < paths[i].Length; j++)
                    {
                        Vector2 a = offset + paths[i][j - 1], b = offset + paths[i][j];
                        int steps = Mathf.CeilToInt(Vector2.Distance(a, b) / 3);
                        for (int k = 0; k <= steps; k++)
                        {
                            Vector2 p = Vector2.Lerp(a, b, k / (float)steps);
                            p.x += GlitchOffset(p.y - 235, time);
                            GUI.DrawTexture(new Rect(p.x - width / 2, p.y - width / 2, width, width), dot);
                        }
                    }
                    if (i == 5) GUI.DrawTexture(new Rect(offset.x + 30 - width / 2 + GlitchOffset(offset.y + 130 - 235, time), offset.y + 130 - width / 2, width, width), dot);
                }
                GUI.matrix = letterMatrix;
            }
            GUI.color = Color.white;
        }

        private void OnDestroy()
        {
            if (dot != null) Destroy(dot);
            if (glitchNoise != null) Destroy(glitchNoise);
            if (metalGlyphs != null) foreach (var glyph in metalGlyphs) if (glyph != null) Destroy(glyph);
        }

        private float BurstProgress => Mathf.Clamp01((animationTime - phaseStarted) / Mathf.Max(.1f, glitchSeconds));

        // All six letters and the background hard-cut on the SAME frame. Never dissolve.
        private bool MetalVisible => phase != Phase.Cute && (phase != Phase.Corroding || BurstProgress >= .42f);
        private float AtmosphereProgress() => MetalVisible ? 1 : 0;

        private float GlitchOffset(float y, float time)
        {
            if (phase != Phase.Corroding) return 0;
            int tick = Mathf.FloorToInt(time * 30);
            int band = Mathf.FloorToInt(y / 16);
            // Shared screen-space bands tear the whole title, rather than individual letters.
            return Mathf.Round((Hash(tick, band + 47) - .5f) * 125);
        }

        private void DrawFullScreenGlitch()
        {
            if (phase != Phase.Corroding) return;
            if (glitchNoise == null)
            {
                glitchNoise = new Texture2D(96, 64, TextureFormat.RGBA32, false) {
                    name = "Intro signal noise", hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point
                };
                var pixels = new Color[96 * 64];
                var random = new System.Random(191);
                for (int i = 0; i < pixels.Length; i++)
                {
                    float value = (float)random.NextDouble();
                    pixels[i] = new Color(value, value, value, 1);
                }
                glitchNoise.SetPixels(pixels);
                glitchNoise.Apply(false, true);
            }
            int tick = Mathf.FloorToInt(animationTime * 30);
            var full = new Rect(0, 0, Screen.width, Screen.height);
            Color previous = GUI.color;
            GUI.color = new Color(1, 1, 1, .32f + Hash(tick, 3) * .2f);
            GUI.DrawTextureWithTexCoords(full, glitchNoise, new Rect(Hash(tick, 5), Hash(tick, 9), 5, 3));
            for (int band = 0; band < 12; band++)
            {
                float y = Hash(tick, band + 12) * Screen.height;
                float height = (band % 3 == 0 ? .065f : .008f) * Screen.height;
                Fill(new Rect(0, y, Screen.width, height), new Color(.01f, .025f, .027f, .85f));
                Fill(new Rect(0, y + height, Screen.width * (.3f + Hash(tick, band) * .7f), 2),
                    band % 2 == 0 ? new Color(.12f, .8f, .8f, .7f) : new Color(.8f, .12f, .16f, .65f));
            }
            // One impact at the shared cut point; no repeated whole-screen flashing.
            if (BurstProgress >= .42f && BurstProgress < .55f)
                Fill(full, new Color(.55f, .65f, .62f, .42f));
            GUI.color = previous;
        }

        private float LetterDropStart(int index)
        {
            float beat = 60f / Mathf.Clamp(entranceTempoBpm, 80, 160);
            // Gaps: 2, 1.75, 1.5, 1.25, 1 beats. Accumulate the gaps so
            // each letter keeps its own turn while the phrase gathers pace.
            return beat * (2 * index - .125f * index * (index - 1));
        }

        private static float LetterFallSeconds(int index) => Mathf.Lerp(.56f, .46f, index / 5f);

        private static void DropPose(float age, float fallSeconds, out float height, out float squash, out float stretch)
        {
            squash = 0;
            stretch = 0;
            if (age < fallSeconds)
            {
                float fall = Mathf.Clamp01(age / fallSeconds);
                height = 650 * (1 - fall * fall);
                stretch = .16f * fall;
                return;
            }
            float elapsed = age - fallSeconds;
            // Three successively smaller parabolic rebounds, with a compressed landing
            // before each takeoff. Positions remain continuous at every impact.
            for (int bounce = 0; bounce < 3; bounce++)
            {
                float duration = bounce == 0 ? FirstBounceSeconds : bounce == 1 ? .43f : .32f;
                float peak = bounce == 0 ? 110 : bounce == 1 ? 42 : 12;
                const float contact = .09f;
                if (elapsed < duration)
                {
                    if (elapsed < contact)
                    {
                        height = 0;
                        squash = Mathf.Sin(elapsed / contact * Mathf.PI) / (1 + bounce * .6f);
                    }
                    else
                    {
                        float flight = (elapsed - contact) / (duration - contact);
                        height = 4 * flight * (1 - flight) * peak;
                        stretch = Mathf.Sin(flight * Mathf.PI) * .12f / (1 + bounce);
                    }
                    return;
                }
                elapsed -= duration;
            }
            height = 0;
        }

        private static void DrawGlyph(Texture2D glyph, Rect rect, float top, float bottom, Color tint)
        {
            Color previous = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(rect, glyph, new Rect(0, 1 - bottom, 1, bottom - top), true);
            GUI.color = previous;
        }

        private void EnsureMetalGlyphs()
        {
            if (metalGlyphs != null) return;
            metalGlyphs = new Texture2D[6];
            // Angular plate-cut silhouettes. Explicit contours keep their ink bounds
            // identical to the candy lettering without depending on an installed font.
            float[][] outer = {
                new float[] {0,1, 0,0, .24f,0, .5f,.36f, .76f,0, 1,0, 1,1, .72f,1, .72f,.42f, .5f,.7f, .28f,.42f, .28f,1},
                new float[] {0,1, .27f,0, .73f,0, 1,1, .72f,1, .65f,.73f, .35f,.73f, .28f,1},
                new float[] {0,1, 0,0, .78f,0, 1,.14f, 1,.48f, .76f,.62f, 1,1, .67f,1, .42f,.64f, .28f,.64f, .28f,1},
                new float[] {.16f,0, .84f,0, 1,.13f, 1,.28f, .7f,.28f, .7f,.21f, .3f,.21f, .27f,.25f, .27f,.75f, .3f,.79f, .7f,.79f, .7f,.72f, 1,.72f, 1,.87f, .84f,1, .16f,1, 0,.87f, 0,.13f},
                new float[] {.16f,0, .84f,0, 1,.13f, 1,.87f, .84f,1, .16f,1, 0,.87f, 0,.13f},
                new float[] {0,0, 1,0, .84f,.72f, .16f,.72f}
            };
            float[][] holes = {
                null,
                new float[] {.4f,.51f, .5f,.19f, .6f,.51f},
                new float[] {.28f,.2f, .67f,.2f, .72f,.25f, .72f,.4f, .67f,.44f, .28f,.44f},
                null,
                new float[] {.3f,.21f, .7f,.21f, .73f,.25f, .73f,.75f, .7f,.79f, .3f,.79f, .27f,.75f, .27f,.25f},
                null
            };
            for (int letter = 0; letter < 6; letter++)
            {
                const int height = 192;
                int width = letter == 5 ? 36 : 144;
                bool Solid(float x, float y) => x >= 0 && x <= 1 && y >= 0 && y <= 1 &&
                    ((Inside(outer[letter], x, y) && !Inside(holes[letter], x, y)) || (letter == 5 && y >= .84f));
                var pixels = new Color[width * height];
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float u = (x + .5f) / width, v = (y + .5f) / height;
                    if (!Solid(u, v)) continue;
                    float noise = Mathf.PerlinNoise(x * .047f + letter * 13, y * .041f);
                    float fine = Hash(x + letter * 179, y);
                    float rust = Mathf.SmoothStep(0, 1, Mathf.Clamp01((noise - .43f) * 5));
                    Color iron = Color.Lerp(new Color(.24f,.28f,.29f), new Color(.58f,.61f,.58f), .45f + fine * .28f + .22f * Mathf.Cos(v * 5));
                    Color oxide = Color.Lerp(new Color(.16f,.065f,.025f), new Color(.57f,.25f,.075f), fine * .6f + noise * .4f);
                    Color surface = Color.Lerp(iron, oxide, rust);
                    if (!Solid(u - 2f / width, v - 2f / height)) surface = Color.Lerp(surface, new Color(.79f,.77f,.66f), .65f);
                    if (!Solid(u + 3f / width, v + 3f / height)) surface *= .35f;
                    if (fine > .965f) surface *= .3f; // Corrosion pits.
                    if ((y + letter * 17) % 43 == 0 && fine > .35f) surface *= .52f; // Machining scars.
                    surface.a = 1;
                    pixels[(height - 1 - y) * width + x] = surface;
                }
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) {
                    name = "MARCO corroded plate " + letter, hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
                };
                texture.SetPixels(pixels);
                texture.Apply(false, true);
                metalGlyphs[letter] = texture;
            }
        }

        private static bool Inside(float[] polygon, float x, float y)
        {
            if (polygon == null) return false;
            bool inside = false;
            for (int i = 0, j = polygon.Length - 2; i < polygon.Length; j = i, i += 2)
                if ((polygon[i + 1] > y) != (polygon[j + 1] > y) &&
                    x < (polygon[j] - polygon[i]) * (y - polygon[i + 1]) / (polygon[j + 1] - polygon[i + 1]) + polygon[i])
                    inside = !inside;
            return inside;
        }

        private static Rect Offset(Rect rect, float x, float y) => new Rect(rect.x + x, rect.y + y, rect.width, rect.height);
        private static float Hash(int a, int b) => Mathf.Repeat(Mathf.Sin(a * 127.1f + b * 311.7f) * 43758.5453f, 1);
        private static void Fill(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}

