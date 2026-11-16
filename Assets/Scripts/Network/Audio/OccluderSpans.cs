using System.Collections.Generic;

namespace Marco.Network.Audio
{
    // One occluder part along a sound line: where the line enters and leaves it (metres from the source), and
    // whether it is a storey slab (Floor / Ceiling — two walls' worth, 부록 A [사운드 차폐] 층간 바닥 ×0.25).
    public readonly struct OccluderSpan
    {
        public readonly float Enter, Exit;
        public readonly bool Slab;
        public OccluderSpan(float enter, float exit, bool slab) { Enter = enter; Exit = exit; Slab = slab; }
    }

    // 부록 A [사운드 차폐] 차폐 셈 단위 [11-16]: parts that touch or overlap along the line count as one, but only within
    // one family — horizontal (Floor · Ceiling) or vertical (every other occluder: Wall · Door · 차폐 파트 · 락커 줄).
    // Spans of one family merge when the gap between one and the next (along the line) is at most mergeGap; a slab block
    // counts 2, a vertical block 1. A slab touching a wall stays two blocks (3). Pure, no scene.
    public static class OccluderSpans
    {
        private static readonly List<OccluderSpan> Family = new();

        public static int Count(List<OccluderSpan> spans, float mergeGap)
        {
            if (spans == null || spans.Count == 0) return 0;
            return Blocks(spans, true, mergeGap) * 2 + Blocks(spans, false, mergeGap);
        }

        private static int Blocks(List<OccluderSpan> spans, bool slab, float mergeGap)
        {
            Family.Clear();
            foreach (var span in spans) if (span.Slab == slab) Family.Add(span);
            if (Family.Count == 0) return 0;
            Family.Sort((a, b) => a.Enter.CompareTo(b.Enter));
            int blocks = 1;
            float blockEnd = Family[0].Exit;
            for (int i = 1; i < Family.Count; i++)
            {
                if (Family[i].Enter - blockEnd <= mergeGap) { if (Family[i].Exit > blockEnd) blockEnd = Family[i].Exit; continue; }
                blocks++;
                blockEnd = Family[i].Exit;
            }
            return blocks;
        }
    }
}
