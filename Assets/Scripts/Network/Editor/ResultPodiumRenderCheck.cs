using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Marco.Core.WinCondition;
using Marco.Network.Character;
using Marco.Network.Round.ResultPodium;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.Network.Editor
{
    // Renders the result podium for fixed line-ups in an isolated preview scene and saves screenshots to
    // Logs/ResultPodium, with simple checks (every character inside the frame, no overlaps, not too dark).
    // Never opens or saves a scene. Run in the editor or in batch mode with graphics enabled.
    public static class ResultPodiumRenderCheck
    {
        private const string OutputFolder = "Logs/ResultPodium";
        private const string AssetPath = "Assets/Resources/ResultPodium/ResultPodiumAssets.asset";
        private const int Width = 1920, Height = 1080;
        // Bottom of the frame covered by the rematch vote bar (ResultPodiumView.DrawVote, 118 of 1080 px + fade).
        private const float VoteBarFraction = 150f / 1080f;

        [MenuItem("MARCO/Result/Create podium assets")]
        public static void CreateAssets()
        {
            var assets = AssetDatabase.LoadAssetAtPath<ResultPodiumAssets>(AssetPath);
            bool created = assets == null;
            if (created)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
                assets = ScriptableObject.CreateInstance<ResultPodiumAssets>();
            }
            // Only fill empty fields, so hand-picked replacements survive.
            if (assets.runner == null) assets.runner = AssetDatabase.LoadAssetAtPath<CharacterAsset>("Assets/Prefabs/Network/Characters/UnityChan.asset");
            if (assets.seeker == null) assets.seeker = AssetDatabase.LoadAssetAtPath<CharacterAsset>("Assets/Prefabs/Network/Characters/Demon.asset");
            const string unityChan = "Assets/unity-chan!/Unity-chan! Model/Art/Animations/";
            if (assets.runnerWin == null) assets.runnerWin = Clip(unityChan + "unitychan_WIN00.fbx");
            if (assets.runnerLose == null) assets.runnerLose = Clip(unityChan + "unitychan_LOSE00.fbx");
            if (assets.echoPrefab == null) assets.echoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GhostCharacter_Free/Prefabs/Ghost.prefab");
            if (assets.echoWin == null) assets.echoWin = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/GhostCharacter_Free/AnimationClips/ghost_idle.anim");
            if (assets.echoLose == null) assets.echoLose = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/GhostCharacter_Free/AnimationClips/ghost_surprised.anim");
            if (created) AssetDatabase.CreateAsset(assets, AssetPath);
            else EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MARCO][Result] Podium assets {(created ? "created" : "checked")}: {AssetPath}");
        }

        private static AnimationClip Clip(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        [MenuItem("MARCO/Result/Verify podium rendering")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Stop Play mode before checking the result podium."); return; }
            Directory.CreateDirectory(OutputFolder);
            var report = new StringBuilder();
            int exit = 0;
            try
            {
                CreateAssets();
                var assets = AssetDatabase.LoadAssetAtPath<ResultPodiumAssets>(AssetPath);
                bool ok = true;
                ok &= Capture(report, assets, "runners_win", RoundResult.RunnersWin, RunnersWinLineup(), 6.5f);
                ok &= Capture(report, assets, "runners_win_intro", RoundResult.RunnersWin, RunnersWinLineup(), 1.75f, checkLayout: false);
                ok &= Capture(report, assets, "runners_win_late", RoundResult.RunnersWin, RunnersWinLineup(), 12f);
                ok &= Capture(report, assets, "seekers_win", RoundResult.SeekersWin, SeekersWinLineup(), 6.5f);
                ok &= Capture(report, assets, "seekers_win_late", RoundResult.SeekersWin, SeekersWinLineup(), 12f);
                ok &= Capture(report, assets, "two_players", RoundResult.SeekersWin, TwoPlayerLineup(), 6.5f);
                var voidLineup = SeekersWinLineup().Select(e => { e.Won = false; return e; }).ToList();
                ok &= Capture(report, assets, "void", RoundResult.Void, voidLineup, 6.5f);
                report.Insert(0, ok ? "PASS\n" : "FAIL\n");
                if (!ok) exit = 1;
            }
            catch (Exception error)
            {
                exit = 1;
                report.Insert(0, "FAIL\n").AppendLine(error.ToString());
                Debug.LogException(error);
            }
            File.WriteAllText(OutputFolder + "/result.txt", report.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(exit);
            else Debug.Log("[MARCO][Result] " + report);
        }

        private static List<PodiumEntry> RunnersWinLineup() => new()
        {
            Entry("준제", PodiumBody.Runner, true, "탈출", new Color(.35f, .95f, 1f), local: true),
            Entry("동현", PodiumBody.Runner, true, "탈출", new Color(.35f, .95f, 1f)),
            Entry("재운", PodiumBody.Echo, true, "메아리", new Color(.78f, .58f, 1f)),
            Entry("승민", PodiumBody.Echo, true, "메아리", new Color(.78f, .58f, 1f)),
            Entry("NamKyung_Long_Nickname", PodiumBody.Runner, true, "생존", new Color(.8f, .9f, 1f)),
            Entry("술래왕", PodiumBody.Seeker, false, "술래", new Color(1f, .42f, .4f)),
        };

        private static List<PodiumEntry> SeekersWinLineup() => new()
        {
            Entry("술래왕", PodiumBody.Seeker, true, "술래", new Color(1f, .42f, .4f)),
            Entry("준제", PodiumBody.Echo, false, "메아리", new Color(.78f, .58f, 1f), local: true),
            Entry("동현", PodiumBody.Echo, false, "메아리", new Color(.78f, .58f, 1f)),
            Entry("재운", PodiumBody.Runner, false, "탈출", new Color(.35f, .95f, 1f)),
            Entry("승민", PodiumBody.Echo, false, "메아리", new Color(.78f, .58f, 1f)),
            Entry("Player6", PodiumBody.Runner, false, "생존", new Color(.8f, .9f, 1f)),
        };

        private static List<PodiumEntry> TwoPlayerLineup() => new()
        {
            Entry("술래왕", PodiumBody.Seeker, true, "술래", new Color(1f, .42f, .4f)),
            Entry("준제", PodiumBody.Echo, false, "메아리", new Color(.78f, .58f, 1f), local: true),
        };

        private static PodiumEntry Entry(string name, PodiumBody body, bool won, string outcome, Color color, bool local = false) =>
            new() { Name = name, Body = body, Won = won, Outcome = outcome, OutcomeColor = color, IsLocal = local };

        private static bool Capture(StringBuilder report, ResultPodiumAssets assets, string name, RoundResult result,
            List<PodiumEntry> lineup, float time, bool checkLayout = true)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            ResultPodiumStage stage = null;
            RenderTexture resolved = null;
            Texture2D image = null;
            bool ok = true;
            try
            {
                stage = new ResultPodiumStage(result, lineup, assets, Width, Height);
                if (!stage.IsValid) throw new Exception("Stage did not build.");
                SceneManager.MoveGameObjectToScene(stage.Root, preview);
                stage.Camera.scene = preview;
                for (float t = 0; t < time; t += 1f / 30f) stage.Tick(1f / 30f);
                stage.Render();

                resolved = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(stage.Feed, resolved);
                var previous = RenderTexture.active;
                RenderTexture.active = resolved;
                image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                RenderTexture.active = previous;
                var pixels = image.GetPixels32();

                report.AppendLine($"[{name}] {result}, {lineup.Count} players, t={time:F2}s (vote bar from {stage.VoteRevealTime:F2}s)");
                var rects = stage.ActorScreenRects();
                if (checkLayout && rects.Count != lineup.Count)
                {
                    report.AppendLine($"  MISSING: {lineup.Count - rects.Count} character(s) not rendered");
                    ok = false;
                }
                for (int i = 0; i < rects.Count; i++)
                {
                    var (label, rect) = rects[i];
                    float brightness = AverageLuma(pixels, rect);
                    bool clipped = rect.xMin < 0 || rect.xMax > 1 || rect.yMax > 1 || rect.yMin < VoteBarFraction;
                    bool dark = brightness < 28f;
                    string flags = (clipped ? " CLIPPED" : "") + (dark ? " DARK" : "");
                    report.AppendLine($"  {label,-24} x {rect.xMin:F2}-{rect.xMax:F2}  y {rect.yMin:F2}-{rect.yMax:F2}  luma {brightness:F0}{flags}");
                    if (checkLayout && (clipped || dark)) ok = false;
                    for (int j = i + 1; j < rects.Count && checkLayout; j++)
                    {
                        var other = rects[j].rect;
                        float overlap = Mathf.Max(0, Mathf.Min(rect.xMax, other.xMax) - Mathf.Max(rect.xMin, other.xMin));
                        if (overlap > .2f * Mathf.Min(rect.width, other.width) && Mathf.Min(rect.yMax, other.yMax) > Mathf.Max(rect.yMin, other.yMin))
                        {
                            report.AppendLine($"  OVERLAP {label} / {rects[j].label} ({overlap:F3})");
                            ok = false;
                        }
                    }
                }

                // Mark the area the rematch vote bar will cover, so the screenshot shows the real composition.
                int barRows = Mathf.RoundToInt(Height * 118f / 1080f);
                for (int y = 0; y < barRows; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        var p = pixels[y * Width + x];
                        pixels[y * Width + x] = new Color32((byte)(p.r * .28f), (byte)(p.g * .28f), (byte)(p.b * .28f), 255);
                    }
                image.SetPixels32(pixels);
                image.Apply();
                File.WriteAllBytes($"{OutputFolder}/{name}.png", image.EncodeToPNG());
                report.AppendLine(ok ? "  ok" : "  PROBLEM");
            }
            finally
            {
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                if (resolved != null) RenderTexture.ReleaseTemporary(resolved);
                stage?.Dispose();
                EditorSceneManager.ClosePreviewScene(preview);
            }
            return ok;
        }

        private static float AverageLuma(Color32[] pixels, Rect viewport)
        {
            int x0 = Mathf.Clamp((int)(viewport.xMin * Width), 0, Width - 1), x1 = Mathf.Clamp((int)(viewport.xMax * Width), 0, Width - 1);
            int y0 = Mathf.Clamp((int)(viewport.yMin * Height), 0, Height - 1), y1 = Mathf.Clamp((int)(viewport.yMax * Height), 0, Height - 1);
            double sum = 0;
            int count = 0;
            for (int y = y0; y <= y1; y += 2)
                for (int x = x0; x <= x1; x += 2)
                {
                    var p = pixels[y * Width + x];
                    float luma = .299f * p.r + .587f * p.g + .114f * p.b;
                    if (luma < 12) continue; // background
                    sum += luma;
                    count++;
                }
            return count == 0 ? 0 : (float)(sum / count);
        }
    }
}
