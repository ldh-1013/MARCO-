using System.Collections.Generic;
using Marco.Core.WinCondition;
using UnityEngine;

namespace Marco.Network.Round.ResultPodium
{
    // One award card (기획서 12.5): winners (none = 수상자 없음) and the number behind it.
    public struct PodiumAward
    {
        public AwardKind Kind;
        public int[] WinnerIds;
        public string[] WinnerNames;
        public float Value;
    }

    // 기획서 8 · 12.5 · 연출.md 6.2: the three award cards under the row. The three backs come up together,
    // then each card flips over 0.8 s after the one before (the count never changes — an award without a
    // winner flips to a calm grey "수상자 없음"). A won card lands with a little hop, a short glow and a glint
    // running along its outline; at that moment every winner of it on the podium gets a spotlight and the
    // award's icon pops over the head, and the badge under the name follows. Icons are drawn in code
    // (AwardIconArt). Colours stay in the 16.2 palette: amber #FFB84D for awards, #E8E8E8 for text, grey
    // and light, black panels.
    //
    // Timing against the 15 s rematch clock (both start at the result): the backs come up with the labels,
    // the first card flips with the vote bar (at most 3.4 s), the last at +1.6 s — every card is face up by
    // about 5.4 s, leaving ~9.5 s of vote with everything on screen. The vote button works from the bar.
    public sealed partial class ResultPodiumStage
    {
        public const float CardRevealInterval = .8f;
        private const float CardWidth = 2.55f, CardHeight = .95f, CardGap = .25f, CardY = -2.05f;
        private const float BadgeSize = .36f, BadgeGap = .06f, BadgeY = LabelTop - .74f;
        private const float BacksLead = .45f, BackStagger = .06f;      // backs come up this long before the first flip
        private const float FlipSeconds = .46f, FlipLead = .1f;         // the face turns to the camera at ~reveal time
        private const float ShineSeconds = .65f, ShinePeriod = 3.4f;
        private const float SpotRise = .14f, SpotHold = .55f, SpotFall = .6f;
        // Gone before the next card turns (0.8 s later + its flip), so only that card's winners pop.
        private const float PopupLife = 1.1f, PopupSize = .5f;
        private const float HaloInset = .16f;
        private static readonly Color Amber = new(1f, .722f, .302f);        // #FFB84D
        private static readonly Color Pale = new(.91f, .91f, .91f);         // #E8E8E8

        private static float CardsWidth => 3 * CardWidth + 2 * CardGap;
        private static float CardsBottom => CardY - CardHeight / 2 - .12f;

        private sealed class Card
        {
            public Transform Root, Front, Back;
            public Material Panel, Halo;
            public float RevealTime, BackTime;
            public bool Won;
            public int Index;
        }

        private sealed class Badge
        {
            public Transform Root;
            public float RevealTime;
        }

        // A winner's moment: spotlight on the podium and the icon popping over the head.
        private sealed class Highlight
        {
            public Actor Actor;
            public Transform Beam, Pool, Popup;
            public Material BeamMaterial, PoolMaterial, IconMaterial, BurstMaterial;
            public float Start, PopupEnd;
        }

        private readonly List<Card> cards = new();
        private readonly List<Badge> badges = new();
        private readonly List<Highlight> highlights = new();
        private readonly Dictionary<AwardKind, Material> iconMaterials = new();

        // The first card flips with the rematch bar; the others follow every 0.8 s.
        public float CardsStart => VoteRevealTime;
        public float CardRevealTime(int index) => CardsStart + index * CardRevealInterval;
        public float CardsSettled => CardRevealTime(2) + FlipSeconds - FlipLead;
        public bool HasAwards => cards.Count > 0;

        // Once per stage. Awards arriving after their reveal time simply show at once.
        public void SetAwards(IReadOnlyList<PodiumAward> awards)
        {
            if (root == null || HasAwards || awards == null || awards.Count == 0) return;
            var cardRow = new GameObject("Award cards").transform;
            cardRow.SetParent(root.transform, false);
            cardRow.localPosition = new Vector3(0, CardY, LabelZ);
            for (int i = 0; i < awards.Count; i++)
            {
                float x = (i - (awards.Count - 1) / 2f) * (CardWidth + CardGap);
                cards.Add(BuildCard(awards[i], cardRow, x, i));
            }
            BuildBadges(awards);
            BuildHighlights(awards);
            SortLayers(cardRow);
            SetLayer(cardRow);
            foreach (var actor in actors) SetLayer(actor.Info);
            foreach (var highlight in highlights) { SetLayer(highlight.Beam); SetLayer(highlight.Pool); SetLayer(highlight.Popup); }
            TickAwards(Time);
        }

        // ---------------------------------------------------------------- cards

        private Card BuildCard(PodiumAward award, Transform parent, float x, int index)
        {
            var root = new GameObject("Award card " + award.Kind).transform;
            root.SetParent(parent, false);
            root.localPosition = new Vector3(x, 0, 0);
            bool won = award.WinnerIds != null && award.WinnerIds.Length > 0;
            var card = new Card
            {
                Root = root, Won = won, Index = index,
                RevealTime = CardRevealTime(index), BackTime = CardsStart - BacksLead + index * BackStagger,
            };

            // Glow behind a won card (drawn first, sits just behind the panel).
            if (won)
            {
                float haloHeight = CardHeight / (1 - 2 * HaloInset);
                float haloWidth = CardWidth + (haloHeight - CardHeight);
                card.Halo = Shape(8, new Color(Amber.r, Amber.g, Amber.b, 0), Color.clear);
                card.Halo.SetFloat("_Aspect", haloWidth / haloHeight);
                card.Halo.SetFloat("_Inset", HaloInset);
                Quad("Halo", new Vector3(0, 0, .05f), new Vector3(haloWidth, haloHeight, 1), card.Halo, root);
            }

            card.Front = new GameObject("Front").transform;
            card.Front.SetParent(root, false);
            card.Panel = Shape(6, new Color(0, 0, 0, won ? .8f : .62f),
                won ? new Color(Amber.r, Amber.g, Amber.b, .95f) : new Color(Pale.r, Pale.g, Pale.b, .2f));
            card.Panel.SetFloat("_Aspect", CardWidth / CardHeight);
            Quad("Panel", new Vector3(0, 0, .03f), new Vector3(CardWidth, CardHeight, 1), card.Panel, card.Front);

            // Icon on the left, words on the right.
            float iconX = -CardWidth / 2 + .5f;
            float textLeft = iconX + .44f, textRight = CardWidth / 2 - .16f;
            float textX = (textLeft + textRight) / 2, textWidth = textRight - textLeft;
            Icon(award.Kind, card.Front, new Vector3(iconX, 0, 0), .7f,
                won ? Amber : new Color(Pale.r, Pale.g, Pale.b, .28f));
            var muted = new Color(Pale.r, Pale.g, Pale.b, .5f);
            if (won)
            {
                // Title / name(s) / words, and for 무성 a small score line: the block moves up to make room.
                string detail = AwardRules.EvidenceDetail(award.Kind, award.Value);
                bool four = detail != null;
                Text(AwardRules.Title(award.Kind), new Vector3(textX, four ? .315f : .28f, 0), .15f, Amber, card.Front, true);
                var (names, nameHeight) = WinnerNames(award.WinnerNames, textWidth, four ? .22f : .25f);
                Text(names, new Vector3(textX, four ? .095f : .02f, 0), nameHeight, Color.white, card.Front, true);
                string evidence = AwardRules.Evidence(award.Kind, award.Value);
                Text(evidence, new Vector3(textX, four ? -.13f : -.26f, 0), FitHeight(evidence, .14f, textWidth), new Color(Pale.r, Pale.g, Pale.b, .9f), card.Front, false);
                if (four)
                    Text(detail, new Vector3(textX, -.295f, 0), .095f, new Color(Pale.r, Pale.g, Pale.b, .6f), card.Front, false);
            }
            else
            {
                Text(AwardRules.Title(award.Kind), new Vector3(textX, .2f, 0), .15f, new Color(Pale.r, Pale.g, Pale.b, .55f), card.Front, true);
                Text(AwardRules.NoWinner, new Vector3(textX, -.1f, 0), .19f, muted, card.Front, true);
            }

            // Back: the same for every card — grey outline, dim icon and the award's name, nothing about who.
            card.Back = new GameObject("Back").transform;
            card.Back.SetParent(root, false);
            card.Back.localRotation = Quaternion.Euler(0, 180, 0);
            var back = Shape(6, new Color(0, 0, 0, .8f), new Color(Pale.r, Pale.g, Pale.b, .3f));
            back.SetFloat("_Aspect", CardWidth / CardHeight);
            Quad("Panel", new Vector3(0, 0, .03f), new Vector3(CardWidth, CardHeight, 1), back, card.Back);
            Icon(award.Kind, card.Back, new Vector3(0, .1f, 0), .5f, new Color(Pale.r, Pale.g, Pale.b, .32f));
            Text(AwardRules.Title(award.Kind), new Vector3(0, -.3f, 0), .13f, new Color(Pale.r, Pale.g, Pale.b, .6f), card.Back, true);

            root.localScale = Vector3.zero;
            return card;
        }

        // Winner name(s) on one line. When co-winners with long nicknames would shrink below a readable size,
        // the long ones are cut with "…" (here only — the podium label keeps the whole name), shorter each try.
        private const float NameMinimum = .17f;
        private static readonly int[] NameCuts = { int.MaxValue, 12, 9, 7 };

        private static (string text, float height) WinnerNames(string[] winners, float width, float maxHeight)
        {
            string text = "";
            float height = 0;
            foreach (int cut in NameCuts)
            {
                var names = new List<string>();
                foreach (var name in winners ?? new string[0])
                    names.Add(name.Length > cut ? name.Substring(0, cut - 1) + "…" : name);
                text = string.Join(" · ", names);
                height = FitHeight(text, maxHeight, width);
                if (height >= NameMinimum) break;
            }
            return (text, height);
        }

        private static float FitHeight(string text, float height, float width) =>
            Mathf.Min(height, height * width / Mathf.Max(.01f, EstimateWidth(text, height)));

        // A code-drawn award icon on a quad, tinted (amber when won, grey otherwise).
        private Transform Icon(AwardKind kind, Transform parent, Vector3 position, float size, Color tint)
        {
            var material = Shape(7, tint, Color.clear);
            material.SetTexture("_MainTex", AwardIconArt.Get(kind));
            return Quad("Badge", position + new Vector3(0, 0, .02f), new Vector3(size, size, 1), material, parent);
        }

        // ---------------------------------------------------------------- badges under the names

        // Under each winner's name, in card order, centred; each pops right after its own card turns.
        private void BuildBadges(IReadOnlyList<PodiumAward> awards)
        {
            foreach (var actor in actors)
            {
                var won = AwardsOf(actor, awards);
                for (int k = 0; k < won.Count; k++)
                {
                    float x = (k - (won.Count - 1) / 2f) * (BadgeSize + BadgeGap);
                    var holder = new GameObject("Award badge").transform;
                    holder.SetParent(actor.Info, false);
                    holder.localPosition = new Vector3(x, BadgeY, 0);
                    Icon(awards[won[k]].Kind, holder, Vector3.zero, BadgeSize, Amber);
                    holder.localScale = Vector3.zero;
                    SortLayers(holder);
                    badges.Add(new Badge { Root = holder, RevealTime = CardRevealTime(won[k]) + .25f });
                }
            }
        }

        private static List<int> AwardsOf(Actor actor, IReadOnlyList<PodiumAward> awards)
        {
            var won = new List<int>();
            for (int i = 0; i < awards.Count; i++)
                if (awards[i].WinnerIds != null && System.Array.IndexOf(awards[i].WinnerIds, actor.Entry.PlayerId) >= 0) won.Add(i);
            return won;
        }

        // ---------------------------------------------------------------- spotlight + pop-up per winner

        private void BuildHighlights(IReadOnlyList<PodiumAward> awards)
        {
            foreach (var actor in actors)
            {
                var won = AwardsOf(actor, awards);
                for (int k = 0; k < won.Count; k++)
                {
                    float start = CardRevealTime(won[k]);
                    // A second award for the same player replaces the first pop-up.
                    float end = k + 1 < won.Count ? Mathf.Min(start + PopupLife, CardRevealTime(won[k + 1])) : start + PopupLife;
                    highlights.Add(BuildHighlight(actor, awards[won[k]].Kind, start, end));
                }
            }
        }

        private Highlight BuildHighlight(Actor actor, AwardKind kind, float start, float popupEnd)
        {
            var h = new Highlight { Actor = actor, Start = start, PopupEnd = popupEnd };
            float floor = actor.Home.y;
            const float beamHeight = 5.2f;
            h.BeamMaterial = Shape(9, new Color(Pale.r, Pale.g, Pale.b, 0), Color.clear);
            h.Beam = Quad("Award spotlight", new Vector3(actor.X, floor + beamHeight / 2, .35f), new Vector3(1.7f, beamHeight, 1), h.BeamMaterial);
            h.PoolMaterial = Shape(0, new Color(Pale.r, Pale.g, Pale.b, 0), Color.clear);
            h.Pool = Quad("Award light pool", new Vector3(actor.X, floor + .02f, 0), new Vector3(1.9f, 1.9f, 1), h.PoolMaterial);
            h.Pool.rotation = Quaternion.Euler(90, 0, 0);

            h.Popup = new GameObject("Award pop-up").transform;
            h.Popup.SetParent(root.transform, false);
            h.Popup.localPosition = new Vector3(actor.X, floor + actor.Top + .36f, -.45f);
            h.BurstMaterial = Shape(0, new Color(Amber.r, Amber.g, Amber.b, 0), Color.clear);
            Quad("Burst", new Vector3(0, 0, .02f), new Vector3(PopupSize * 2.4f, PopupSize * 2.4f, 1), h.BurstMaterial, h.Popup);
            h.IconMaterial = Shape(7, Amber, Color.clear);
            h.IconMaterial.SetTexture("_MainTex", AwardIconArt.Get(kind));
            Quad("Icon", Vector3.zero, new Vector3(PopupSize, PopupSize, 1), h.IconMaterial, h.Popup);
            foreach (var renderer in h.Popup.GetComponentsInChildren<MeshRenderer>(true))
                renderer.sortingOrder = renderer.name == "Icon" ? 5 : 4;
            h.Popup.localScale = Vector3.zero;
            return h;
        }

        // ---------------------------------------------------------------- per frame

        private void TickAwards(float t)
        {
            foreach (var card in cards) TickCard(card, t);
            foreach (var badge in badges) badge.Root.localScale = Vector3.one * Pop((t - badge.RevealTime) / InfoPop);
            foreach (var h in highlights) TickHighlight(h, t);
        }

        private void TickCard(Card card, float t)
        {
            float appear = Pop((t - card.BackTime) / InfoPop);
            float u = Mathf.Clamp01((t - card.RevealTime + FlipLead) / FlipSeconds);
            // Won cards overshoot and hop; empty ones turn over and settle quietly.
            float angle = 180f * (1 - EaseOutBack(u, card.Won ? 1.7f : .35f));
            float hop = Mathf.Sin(Mathf.PI * u) * (card.Won ? .2f : .05f);
            float swell = 1 + Mathf.Sin(Mathf.PI * u) * (card.Won ? .07f : .02f);
            card.Root.localScale = Vector3.one * appear * swell;
            card.Root.localRotation = Quaternion.Euler(0, angle, 0);
            card.Root.localPosition = new Vector3(card.Root.localPosition.x, hop, 0);
            bool faceUp = angle < 90f;
            card.Front.gameObject.SetActive(faceUp);
            card.Back.gameObject.SetActive(!faceUp);
            if (!card.Won) return;

            float since = t - card.RevealTime;
            if (card.Halo != null)
            {
                float flash = since < 0 ? 0 : .7f * Mathf.Exp(-3.2f * since);
                float rest = since < 0 ? 0 : Mathf.Clamp01(since / .2f) * (.2f + .05f * Mathf.Sin(2.2f * t + card.Index));
                card.Halo.SetColor("_Color", new Color(Amber.r, Amber.g, Amber.b, flash + rest));
            }
            // Glint: once as it lands, then again every few seconds, the cards out of step.
            float k = since - .12f, sweep = -1;
            if (k >= 0 && k < ShineSeconds) sweep = k / ShineSeconds;
            else
            {
                float again = k - ShinePeriod * .6f - card.Index * .55f;
                if (again >= 0 && again % ShinePeriod < ShineSeconds) sweep = again % ShinePeriod / ShineSeconds;
            }
            card.Panel.SetFloat("_Shine", sweep >= 0 ? Mathf.Lerp(-1.6f, 1.6f, sweep) : -10f);
        }

        private void TickHighlight(Highlight h, float t)
        {
            float since = t - h.Start;
            float light = since < 0 ? 0
                : since < SpotRise ? since / SpotRise
                : since < SpotRise + SpotHold ? 1
                : 1 - Mathf.Clamp01((since - SpotRise - SpotHold) / SpotFall);
            light = Mathf.SmoothStep(0, 1, light);
            h.BeamMaterial.SetColor("_Color", new Color(Pale.r, Pale.g, Pale.b, .26f * light));
            h.PoolMaterial.SetColor("_Color", new Color(Pale.r, Pale.g, Pale.b, .42f * light));
            h.Beam.gameObject.SetActive(light > 0);
            h.Pool.gameObject.SetActive(light > 0);

            float life = h.PopupEnd - h.Start;
            bool shown = since >= 0 && since < life;
            h.Popup.gameObject.SetActive(shown);
            if (!shown) return;
            float pop = Pop(since / .3f);
            float fade = 1 - Mathf.Clamp01((since - (life - .3f)) / .3f);
            h.Popup.localScale = Vector3.one * pop * Mathf.Lerp(.85f, 1f, fade);
            var position = h.Popup.localPosition;
            h.Popup.localPosition = new Vector3(position.x, h.Actor.Home.y + h.Actor.Top + .36f + .12f * Mathf.Clamp01(since / life), position.z);
            h.IconMaterial.SetColor("_Color", new Color(Amber.r, Amber.g, Amber.b, fade));
            h.BurstMaterial.SetColor("_Color", new Color(Amber.r, Amber.g, Amber.b, fade * (.18f + .4f * Mathf.Exp(-5f * since))));
        }

        // Names of the players whose award spotlight is at least half up at the current time (render check).
        public List<string> LitWinners()
        {
            var names = new List<string>();
            foreach (var h in highlights)
            {
                float since = Time - h.Start;
                bool lit = since >= SpotRise * .5f && since <= SpotRise + SpotHold + SpotFall * .5f;
                if (lit && !names.Contains(h.Actor.Entry.Name)) names.Add(h.Actor.Entry.Name);
            }
            return names;
        }

        // Everything here is transparent and would be sorted by distance to the camera, which puts a card's
        // panel over its own icon and text on the outer cards. Fix the order: glow, panel, icon, text.
        private static void SortLayers(Transform root)
        {
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                renderer.sortingOrder = renderer.name switch { "Halo" => 0, "Panel" => 1, "Badge" => 2, "Text" => 3, _ => 0 };
        }

        // Same back-out pop as the name labels.
        private static float Pop(float u) => u <= 0 ? 0 : u >= 1 ? 1 : 1 + 2.2f * Mathf.Pow(u - 1, 3) + 1.2f * Mathf.Pow(u - 1, 2);

        private static float EaseOutBack(float u, float overshoot)
        {
            float v = u - 1;
            return 1 + (overshoot + 1) * v * v * v + overshoot * v * v;
        }

        // Viewport rectangles (0..1, y up) of the cards at rest, for the editor render check.
        public List<Rect> CardScreenRects()
        {
            var rects = new List<Rect>();
            foreach (var card in cards)
            {
                Vector3 c = card.Root.parent.TransformPoint(new Vector3(card.Root.localPosition.x, 0, 0));
                Vector2 a = Camera.WorldToViewportPoint(c + new Vector3(-CardWidth / 2, -CardHeight / 2, 0));
                Vector2 b = Camera.WorldToViewportPoint(c + new Vector3(CardWidth / 2, CardHeight / 2, 0));
                rects.Add(Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)));
            }
            return rects;
        }
    }
}
