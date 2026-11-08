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
        [SerializeField, Min(0.1f)] private float errorLeadSeconds = 0.9f;
        [SerializeField, Min(0.1f)] private float glitchSeconds = 0.8f;
        [SerializeField, Min(0.1f)] private float letterDelay = 0.65f;
        private const float FirstBounceSeconds = 0.60f;
        private const float BounceSeconds = 1.35f;
        private float EntranceSeconds => LetterDropStart(5) + LetterFallSeconds(5) + BounceSeconds;
        private bool requestedEarly;
        [SerializeField, Min(0.1f)] private float fadeSeconds = 0.7f;
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
            requestedEarly = false;
        }

        private void Update()
        {
            if (!IsPlaying) return;
            // A startup/import hitch must not fast-forward past M's entrance into A's.
            if (started >= 0) animationTime += Mathf.Min(Time.unscaledDeltaTime, .05f);
            if (phase == Phase.Cute && started >= 0 && animationTime >= EntranceSeconds + (requestedEarly ? 0 : cuteHoldSeconds))
            {
                phase = Phase.Corroding;
                phaseStarted = animationTime;
            }
            // The exclamation mark is the single completion cue for both the logo and mood.
            if (phase == Phase.Corroding && LetterProgress(5) >= 1)
                phase = Phase.Metal;
            if (phase == Phase.Leaving && animationTime - phaseStarted >= fadeSeconds)
                finished = true;
        }

        private void OnGUI()
        {
            if (!IsPlaying) return;
            if (started < 0 && Event.current.type == EventType.Repaint) started = 0;
            float t = animationTime;
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (phase == Phase.Cute)
                {
                    // Even an early click lets all six letters finish their entrance.
                    requestedEarly = true;
                }
                else if (phase == Phase.Metal)
                {
                    phase = Phase.Leaving;
                    phaseStarted = animationTime;
                }
                // Consume clicks during transformation too, so double clicks cannot skip it.
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
                float progress = LetterProgress(letter);
                if (!ShowsMetal(letter, t)) continue;
                bool tearing = progress < 1;
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
                    float tear = GlitchOffset(rect.y + y - 235, t, letter);
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
            GUI.Label(new Rect(0, 620, 1280, 28), phase == Phase.Cute ? "CLICK TO START EARLY" : phase == Phase.Metal ? "CLICK TO ENTER LOBBY" : "", caption);
            GUI.matrix = Matrix4x4.identity;
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
                if (ShowsMetal(i, time)) continue;
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
                            p.x += GlitchOffset(p.y - 235, time, i);
                            GUI.DrawTexture(new Rect(p.x - width / 2, p.y - width / 2, width, width), dot);
                        }
                    }
                    if (i == 5) GUI.DrawTexture(new Rect(offset.x + 30 - width / 2 + GlitchOffset(offset.y + 130 - 235, time, i), offset.y + 130 - width / 2, width, width), dot);
                }
                GUI.matrix = letterMatrix;
            }
            GUI.color = Color.white;
        }

        private void OnDestroy()
        {
            if (dot != null) Destroy(dot);
            if (metalGlyphs != null) foreach (var glyph in metalGlyphs) if (glyph != null) Destroy(glyph);
        }

        private float LetterProgress(int index)
        {
            if (phase == Phase.Cute) return 0;
            if (phase != Phase.Corroding) return 1;
            return Mathf.Clamp01((animationTime - phaseStarted - index * letterDelay - errorLeadSeconds) / Mathf.Max(.1f, glitchSeconds));
        }

        private float AtmosphereProgress()
        {
            if (phase == Phase.Cute) return 0;
            float last = LetterProgress(5);
            if (last >= 1) return 1;
            float letters = 0;
            for (int i = 0; i < 6; i++)
                letters += Mathf.SmoothStep(0, 1, LetterProgress(i));
            // Most of the change follows the letters gradually. Reserve the final
            // ten percent for the exact frame when ! locks and its signal errors stop.
            return .82f * letters / 6 + .08f * Mathf.SmoothStep(0, 1, last);
        }

        private bool ShowsMetal(int index, float time)
        {
            float progress = LetterProgress(index);
            if (progress <= 0) return false;
            if (progress >= 1) return true;
            // Hard, frame-held substitutions instead of dissolving or melting the letter.
            return Hash(Mathf.FloorToInt(time * 24), index + 81) < progress;
        }

        private float GlitchOffset(float y, float time, int index)
        {
            if (phase != Phase.Corroding) return 0;
            float age = animationTime - phaseStarted - index * letterDelay;
            if (age <= 0 || LetterProgress(index) >= 1) return 0;
            int tick = Mathf.FloorToInt(time * 24);
            // Short signal failures separated by held, clean frames. No sine deformation.
            if (Hash(tick, index + 31) < .38f) return 0;
            int band = Mathf.FloorToInt(y / 13);
            if (Hash(tick + index, band + 47) < .45f) return 0;
            float strength = Mathf.Clamp01(age / .25f);
            return Mathf.Round((Hash(tick + index * 7, band) - .5f) * 62 * strength);
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

