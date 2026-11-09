using System;
using System.Collections.Generic;

namespace Marco.Core.WinCondition
{
    // One award as the result screen shows it (14.3 GameEnd awards[]): winners (empty = 수상자 없음) and
    // the number behind it — screams, the silence score, or lures.
    public sealed class AwardOutcome
    {
        public AwardKind Kind { get; }
        public IReadOnlyList<int> Winners { get; }
        public float Value { get; }
        public bool HasWinner => Winners.Count > 0;

        public AwardOutcome(AwardKind kind, IReadOnlyList<int> winners, float value)
        {
            Kind = kind;
            Winners = winners;
            Value = value;
        }
    }

    // 기획서 8 · 8.1 · 8.2 · 8.3: judged once when the round is decided. Ties share the award; winners are
    // listed by player id so every machine shows the same order.
    public static class AwardJudge
    {
        public static AwardOutcome[] JudgeAll(AwardTally tally) => new[]
        {
            MostScreams(tally),
            SilentSurvivor(tally),
            BestLure(tally),
        };

        // 8.1: Scream events only (Shout · Moan never reach this count). 0 screams = no winner.
        public static AwardOutcome MostScreams(AwardTally tally) =>
            MostOf(AwardKind.MostScreams, tally, tally.ScreamsOf, _ => true);

        // 8.3: lures judged by LureJudge, eliminated Echoes only. 0 lures = no winner.
        public static AwardOutcome BestLure(AwardTally tally) =>
            MostOf(AwardKind.BestLure, tally, tally.LuresOf, id => tally.StateOf(id) == AwardPlayerState.Tagged);

        // 8.2: noise ÷ distance, lowest wins. Survivors only (escaped or not escaped — never tagged), and
        // under 1 m of movement is not ranked. Nobody eligible = no winner.
        public static AwardOutcome SilentSurvivor(AwardTally tally)
        {
            var winners = new List<int>();
            double best = double.MaxValue;
            foreach (int id in Sorted(tally.RunnerIds))
            {
                if (tally.StateOf(id) == AwardPlayerState.Tagged) continue;
                double distance = tally.DistanceOf(id);
                if (distance < AwardRules.MinimumDistanceMeters) continue;
                double score = tally.NoiseOf(id) / distance;
                if (winners.Count > 0 && SameScore(score, best)) { winners.Add(id); continue; }
                if (score >= best) continue;
                best = score;
                winners.Clear();
                winners.Add(id);
            }
            return new AwardOutcome(AwardKind.SilentSurvivor, winners, winners.Count > 0 ? (float)best : 0f);
        }

        // Scores are float sums; treat values a hair apart as the same score (a tie is shared).
        public static bool SameScore(double a, double b) => Math.Abs(a - b) <= 1e-6 * Math.Max(1.0, Math.Abs(b));

        private static AwardOutcome MostOf(AwardKind kind, AwardTally tally, Func<int, int> count, Func<int, bool> eligible)
        {
            var winners = new List<int>();
            int best = 0;
            foreach (int id in Sorted(tally.RunnerIds))
            {
                if (!eligible(id)) continue;
                int value = count(id);
                if (value <= 0 || value < best) continue;
                if (value > best) { best = value; winners.Clear(); }
                winners.Add(id);
            }
            return new AwardOutcome(kind, winners, best);
        }

        private static List<int> Sorted(IEnumerable<int> ids)
        {
            var list = new List<int>(ids);
            list.Sort();
            return list;
        }
    }
}
