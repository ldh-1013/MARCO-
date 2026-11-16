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

    // 부록 A [사운드 차폐] 차폐 셈 단위 [11-16]: parts that touch or overlap along the line count as one. Spans are
    // merged when the gap between one and the next (along the line) is at most mergeGap; a merged block holding a
    // slab counts 2, any other block 1. Pure, no scene — SoundWorld builds the spans from raycasts.
    public static class OccluderSpans
    {
        public static int Count(List<OccluderSpan> spans, float mergeGap)
        {
            if (spans == null || spans.Count == 0) return 0;
            spans.Sort((a, b) => a.Enter.CompareTo(b.Enter));
            int walls = 0;
            float blockEnd = spans[0].Exit;
            bool blockSlab = spans[0].Slab;
            for (int i = 1; i < spans.Count; i++)
            {
                var span = spans[i];
                if (span.Enter - blockEnd <= mergeGap)
                {
                    if (span.Exit > blockEnd) blockEnd = span.Exit;
                    blockSlab |= span.Slab;
                    continue;
                }
                walls += blockSlab ? 2 : 1;
                blockEnd = span.Exit;
                blockSlab = span.Slab;
            }
            return walls + (blockSlab ? 2 : 1);
        }
    }
}
