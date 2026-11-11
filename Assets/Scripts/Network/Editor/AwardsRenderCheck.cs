using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Marco.Core.WinCondition;
using Marco.Network.Round.ResultPodium;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.Network.Editor
{
    // MARCO > Result > Verify award cards (기획서 8 · 12.5 · 연출.md 6.2 · 7.2). Renders the result podium with
    // award cards in an isolated preview scene and saves screenshots to Logs/Awards:
    //   three_winners / shared / no_winners / two_players   all three cards shown
    //   three_winners_card1                                   0.8 s sequence: only the first card is up
    //   lure_notice                                            the "낚았다!" HUD (composited, as in batch)
    // Checks: three cards always, revealed 0.8 s apart, inside the frame and clear of the vote bar and of
    // the characters, readable (not too dark). Never opens or saves a scene. Batch mode with graphics:
    //   Unity.exe -batchmode -projectPath <repo> -executeMethod Marco.Network.Editor.AwardsRenderCheck.RunAndExit -logFile <log>
    public static class AwardsRenderCheck
    {
        private const string OutputFolder = "Logs/Awards";
        private const string AssetPath = "Assets/Resources/ResultPodium/ResultPodiumAssets.asset";
        private const int Width = 1920, Height = 1080;
        private const float VoteBarFraction = 150f / 1080f;
        private static readonly Color Escaped = new(.35f, .95f, 1f), Echo = new(.78f, .58f, 1f), Alive = new(.8f, .9f, 1f), Seeker = new(1f, .42f, .4f);

        [MenuItem("MARCO/Result/Verify award cards")]
        public static void Run() => Execute(exit: false);

        public static void RunAndExit() => Execute(exit: true);

        private static void Execute(bool exit)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Stop Play mode before checking the award cards."); return; }
            Directory.CreateDirectory(OutputFolder);
            foreach (var old in Directory.GetFiles(OutputFolder, "*.png")) File.Delete(old);
            var report = new StringBuilder();
            bool ok = true;
            try
            {
                // Read-only: never re-saves the asset (the podium check's CreateAssets would touch it).
                var assets = AssetDatabase.LoadAssetAtPath<ResultPodiumAssets>(AssetPath);
                if (assets == null) throw new Exception("Missing " + AssetPath + " (MARCO > Result > Create podium assets).");
                ok &= Capture(report, assets, "three_winners", RoundResult.RunnersWin, SixPlayers(RoundResult.RunnersWin), ThreeWinners(), CardsDone);
                ok &= Capture(report, assets, "three_winners_card1", RoundResult.RunnersWin, SixPlayers(RoundResult.RunnersWin), ThreeWinners(), s => s.CardRevealTime(0) + .5f, expectCards: 1);
                ok &= Capture(report, assets, "shared", RoundResult.SeekersWin, SixPlayers(RoundResult.SeekersWin), Shared(), CardsDone);
                ok &= Capture(report, assets, "no_winners", RoundResult.SeekersWin, SixPlayers(RoundResult.SeekersWin), NoWinners(), CardsDone);
                ok &= Capture(report, assets, "two_players", RoundResult.SeekersWin, TwoPlayers(), TwoPlayerAwards(), CardsDone);
                ok &= LureNotice(report);
            }
            catch (Exception error)
            {
                ok = false;
                report.AppendLine(error.ToString());
                Debug.LogException(error);
            }
            report.Insert(0, ok ? "PASS\n" : "FAIL\n");
            File.WriteAllText(OutputFolder + "/result.txt", report.ToString());
            Debug.Log("[MARCO][Awards] " + report);
            if (exit) EditorApplication.Exit(ok ? 0 : 1);
        }

        private static float CardsDone(ResultPodiumStage stage) => stage.CardRevealTime(2) + 1.2f;

        // ---------------------------------------------------------------- line-ups (ids match the awards)

        private static List<PodiumEntry> SixPlayers(RoundResult result)
        {
            bool runners = result == RoundResult.RunnersWin;
            return new()
            {
                Entry(1, "준제", PodiumBody.Runner, runners, "탈출", Escaped, local: true),
                Entry(2, "동현", PodiumBody.Runner, runners, "생존", Alive),
                Entry(3, "재운", PodiumBody.Echo, runners, "메아리", Echo),
                Entry(4, "승민", PodiumBody.Echo, runners, "메아리", Echo),
                Entry(5, "NamKyung_Long_Nickname", PodiumBody.Runner, runners, "탈출", Escaped),
                Entry(6, "술래왕", PodiumBody.Seeker, !runners, "술래", Seeker),
            };
        }

        private static List<PodiumEntry> TwoPlayers() => new()
        {
            Entry(6, "술래왕", PodiumBody.Seeker, true, "술래", Seeker),
            Entry(1, "준제", PodiumBody.Echo, false, "메아리", Echo, local: true),
        };

        private static PodiumEntry Entry(int id, string name, PodiumBody body, bool won, string outcome, Color color, bool local = false) =>
            new() { PlayerId = id, Name = name, Body = body, Won = won, Outcome = outcome, OutcomeColor = color, IsLocal = local };

        private static readonly Dictionary<int, string> Names = new()
        { [1] = "준제", [2] = "동현", [3] = "재운", [4] = "승민", [5] = "NamKyung_Long_Nickname", [6] = "술래왕" };

        private static PodiumAward Award(AwardKind kind, float value, params int[] ids)
        {
            var names = new string[ids.Length];
            for (int i = 0; i < ids.Length; i++) names[i] = Names[ids[i]];
            return new PodiumAward { Kind = kind, WinnerIds = ids, WinnerNames = names, Value = value };
        }

        // 재운 screamed most and also lured twice (two badges under one name); 준제 was the quietest.
        private static List<PodiumAward> ThreeWinners() => new()
        {
            Award(AwardKind.MostScreams, 3, 3),
            Award(AwardKind.SilentSurvivor, .42f, 1),
            Award(AwardKind.BestLure, 2, 3),
        };

        private static List<PodiumAward> Shared() => new()
        {
            Award(AwardKind.MostScreams, 2, 1, 2),
            Award(AwardKind.SilentSurvivor, .6f, 2, 5),
            Award(AwardKind.BestLure, 1, 3, 4),
        };

        private static List<PodiumAward> NoWinners() => new()
        {
            Award(AwardKind.MostScreams, 0),
            Award(AwardKind.SilentSurvivor, 0),
            Award(AwardKind.BestLure, 0),
        };

        // 1v1: the only Runner was tagged — not a survivor, so no 무성 생존상; screamed once, lured once.
        private static List<PodiumAward> TwoPlayerAwards() => new()
        {
            Award(AwardKind.MostScreams, 1, 1),
            Award(AwardKind.SilentSurvivor, 0),
            Award(AwardKind.BestLure, 1, 1),
        };

        // ---------------------------------------------------------------- capture

        private static bool Capture(StringBuilder report, ResultPodiumAssets assets, string name, RoundResult result,
            List<PodiumEntry> lineup, List<PodiumAward> awards, Func<ResultPodiumStage, float> when, int expectCards = 3)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            ResultPodiumStage stage = null;
            RenderTexture resolved = null;
            Texture2D image = null;
            bool ok = true;
            try
            {
                stage = new ResultPodiumStage(result, lineup, assets, Width, Height, awards);
                if (!stage.IsValid) throw new Exception("Stage did not build.");
                SceneManager.MoveGameObjectToScene(stage.Root, preview);
                stage.Camera.scene = preview;
                float time = when(stage);
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

                report.AppendLine($"[{name}] {result}, {lineup.Count} players, t={time:F2}s, cards at " +
                    $"{stage.CardRevealTime(0):F2}/{stage.CardRevealTime(1):F2}/{stage.CardRevealTime(2):F2}s, vote bar {stage.VoteRevealTime:F2}s");
                ok &= Expect(report, Mathf.Approximately(stage.CardRevealTime(1) - stage.CardRevealTime(0), .8f) &&
                    Mathf.Approximately(stage.CardRevealTime(2) - stage.CardRevealTime(1), .8f), "cards 0.8 s apart");

                var cards = stage.CardScreenRects();
                ok &= Expect(report, cards.Count == 3, $"three cards built ({cards.Count})");
                var actors = stage.ActorScreenRects();
                int shown = 0;
                for (int i = 0; i < cards.Count; i++)
                {
                    var rect = cards[i];
                    float luma = AverageLuma(pixels, rect);
                    if (stage.CardRevealTime(i) <= time) shown++;
                    bool clipped = rect.xMin < 0 || rect.xMax > 1 || rect.yMax > 1 || rect.yMin < VoteBarFraction;
                    report.AppendLine($"  card {i + 1}  x {rect.xMin:F2}-{rect.xMax:F2}  y {rect.yMin:F2}-{rect.yMax:F2}  luma {luma:F0}{(clipped ? " CLIPPED" : "")}");
                    if (i < expectCards) ok &= !clipped;
                    foreach (var (label, actor) in actors)
                        if (actor.Overlaps(rect)) { report.AppendLine($"  OVERLAP card {i + 1} / {label}"); ok = false; }
                }
                ok &= Expect(report, shown == expectCards, $"{expectCards} card(s) visible at this moment ({shown})");

                // Vote bar area, as the podium check marks it.
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

        // The knocking Echo's HUD (8.3 · 연출.md 7.2) on a dark frame, at the same rect as the in-game IMGUI.
        private static bool LureNotice(StringBuilder report)
        {
            var image = new Texture2D(HudComposite.Width, HudComposite.Height, TextureFormat.RGB24, false);
            var pixels = new Color[HudComposite.Width * HudComposite.Height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(.01f, .012f, .02f);
            var panel = new Color(0f, 0f, 0f, .6f);
            var status = new Rect((HudComposite.Width - 640) / 2f, 24, 640, NetworkTestHud.RoundPanelHeight);
            HudComposite.Fill(pixels, status, panel);
            HudComposite.Text(pixels, new Rect(status.x + 16, status.y + 16, status.width - 32, 52), "남은 시간  6:42", 40, true, Color.white);
            HudComposite.Text(pixels, new Rect(status.x + 16, status.y + 72, status.width - 32, 36), "게이트 닫힘", 28, true, new Color(.95f, .3f, .3f));
            var rect = NetworkTestHud.EchoBodyBannerRect(HudComposite.Width, status.yMax);
            HudComposite.Fill(pixels, rect, panel);
            HudComposite.Text(pixels, rect, NetworkTestHud.LureNoticeText, 44, true, Color.white);
            image.SetPixels(pixels);
            image.Apply();
            File.WriteAllBytes($"{OutputFolder}/lure_notice.png", image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            report.AppendLine($"[lure_notice] \"{NetworkTestHud.LureNoticeText}\" {NetworkTestHud.LureNoticeSeconds}s at {rect}");
            return true;
        }

        private static bool Expect(StringBuilder report, bool condition, string what)
        {
            report.AppendLine((condition ? "  ok    " : "  FAIL  ") + what);
            return condition;
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
                    sum += .299f * p.r + .587f * p.g + .114f * p.b;
                    count++;
                }
            return count == 0 ? 0 : (float)(sum / count);
        }
    }
}
