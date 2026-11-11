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

    // 기획서 8 · 12.5 · 연출.md 6.2: the three award cards under the row, revealed one by one 0.8 s apart
    // (the card count never changes — an award without a winner says "수상자 없음"), and a badge under each
    // winner's name that pops with its card. Colours stay in the 16.2 palette: amber #FFB84D for awards,
    // #E8E8E8 text, black panels.
    public sealed partial class ResultPodiumStage
    {
        public const float CardRevealInterval = .8f;
        private const float CardWidth = 2.55f, CardHeight = .95f, CardGap = .25f, CardY = -2.05f;
        private const float BadgeSize = .34f, BadgeGap = .06f, BadgeY = LabelTop - .74f;
        private static readonly Color Amber = new(1f, .722f, .302f);        // #FFB84D
        private static readonly Color Pale = new(.91f, .91f, .91f);         // #E8E8E8

        private static float CardsWidth => 3 * CardWidth + 2 * CardGap;
        private static float CardsBottom => CardY - CardHeight / 2 - .12f;

        private sealed class Card
        {
            public Transform Root;
            public float RevealTime;
        }

        private sealed class Badge
        {
            public Transform Root;
            public float RevealTime;
        }

        private readonly List<Card> cards = new();
        private readonly List<Badge> badges = new();

        // The first card shows together with the rematch bar; the others follow every 0.8 s.
        public float CardsStart => VoteRevealTime;
        public float CardRevealTime(int index) => CardsStart + index * CardRevealInterval;
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
                cards.Add(new Card { Root = BuildCard(awards[i], cardRow, x), RevealTime = CardRevealTime(i) });
            }
            BuildBadges(awards);
            SortLayers(cardRow);
            SetLayer(cardRow);
            foreach (var actor in actors) SetLayer(actor.Info);
            TickAwards(Time);
        }

        private Transform BuildCard(PodiumAward award, Transform parent, float x)
        {
            var card = new GameObject("Award card " + award.Kind).transform;
            card.SetParent(parent, false);
            card.localPosition = new Vector3(x, 0, 0);
            bool won = award.WinnerIds != null && award.WinnerIds.Length > 0;

            var panel = Quad("Panel", new Vector3(0, 0, .03f), new Vector3(CardWidth, CardHeight, 1),
                Shape(6, new Color(0, 0, 0, .78f), won ? new Color(Amber.r, Amber.g, Amber.b, .95f) : new Color(Pale.r, Pale.g, Pale.b, .28f)), card);
            panel.GetComponent<MeshRenderer>().sharedMaterial.SetFloat("_Aspect", CardWidth / CardHeight);

            // Badge on the left, text block on the right.
            float badgeX = -CardWidth / 2 + .5f;
            BuildBadgeGraphic(award.Kind, card, new Vector3(badgeX, 0, 0), .62f, won ? 1f : .35f);
            float textX = badgeX + .46f + (CardWidth / 2 - .18f - (badgeX + .46f)) / 2;
            float textWidth = CardWidth / 2 - .18f - (badgeX + .46f);
            Text(AwardRules.Title(award.Kind), new Vector3(textX, .27f, 0), .15f, won ? Amber : new Color(Pale.r, Pale.g, Pale.b, .6f), card, true);
            if (won)
            {
                string names = string.Join(" · ", award.WinnerNames ?? new string[0]);
                float nameHeight = Mathf.Min(.24f, .24f * textWidth / Mathf.Max(.01f, EstimateWidth(names, .24f)));
                Text(names, new Vector3(textX, -.01f, 0), nameHeight, Pale, card, true);
                Text(AwardRules.Evidence(award.Kind, award.Value), new Vector3(textX, -.29f, 0), .13f, new Color(Pale.r, Pale.g, Pale.b, .78f), card, false);
            }
            else Text(AwardRules.NoWinner, new Vector3(textX, -.1f, 0), .19f, new Color(Pale.r, Pale.g, Pale.b, .55f), card, true);
            card.localScale = Vector3.zero;
            return card;
        }

        // Amber disc with the award's initial in black (비명 · 무성 · 낚시).
        private void BuildBadgeGraphic(AwardKind kind, Transform parent, Vector3 position, float size, float strength)
        {
            Quad("Badge", position + new Vector3(0, 0, .02f), new Vector3(size, size, 1),
                Shape(5, new Color(Amber.r, Amber.g, Amber.b, strength), Color.clear), parent);
            Text(Glyph(kind), position + new Vector3(0, -size * .02f, 0), size * .5f, new Color(0, 0, 0, strength < 1 ? .55f : .92f), parent, true);
        }

        private static string Glyph(AwardKind kind) => kind switch
        {
            AwardKind.MostScreams => "비",
            AwardKind.SilentSurvivor => "무",
            _ => "낚",
        };

        // Under each winner's name, in card order, centred; each pops with its own card.
        private void BuildBadges(IReadOnlyList<PodiumAward> awards)
        {
            foreach (var actor in actors)
            {
                var won = new List<int>();
                for (int i = 0; i < awards.Count; i++)
                    if (awards[i].WinnerIds != null && System.Array.IndexOf(awards[i].WinnerIds, actor.Entry.PlayerId) >= 0) won.Add(i);
                for (int k = 0; k < won.Count; k++)
                {
                    float x = (k - (won.Count - 1) / 2f) * (BadgeSize + BadgeGap);
                    var holder = new GameObject("Award badge").transform;
                    holder.SetParent(actor.Info, false);
                    holder.localPosition = new Vector3(x, BadgeY, 0);
                    BuildBadgeGraphic(awards[won[k]].Kind, holder, Vector3.zero, BadgeSize, 1f);
                    holder.localScale = Vector3.zero;
                    SortLayers(holder);
                    badges.Add(new Badge { Root = holder, RevealTime = CardRevealTime(won[k]) + .12f });
                }
            }
        }

        private void TickAwards(float t)
        {
            foreach (var card in cards) card.Root.localScale = Vector3.one * Pop((t - card.RevealTime) / InfoPop);
            foreach (var badge in badges) badge.Root.localScale = Vector3.one * Pop((t - badge.RevealTime) / InfoPop);
        }

        // Everything here is transparent and would be sorted by distance to the camera, which puts a card's
        // panel over its own badge and text on the outer cards. Fix the order: panel, badge, text.
        private static void SortLayers(Transform root)
        {
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                renderer.sortingOrder = renderer.name switch { "Panel" => 1, "Badge" => 2, "Text" => 3, _ => 0 };
        }

        // Same back-out pop as the name labels.
        private static float Pop(float u) => u <= 0 ? 0 : u >= 1 ? 1 : 1 + 2.2f * Mathf.Pow(u - 1, 3) + 1.2f * Mathf.Pow(u - 1, 2);

        // Viewport rectangles (0..1, y up) of the cards, for the editor render check.
        public List<Rect> CardScreenRects()
        {
            var rects = new List<Rect>();
            foreach (var card in cards)
            {
                Vector3 c = card.Root.position;
                Vector2 a = Camera.WorldToViewportPoint(c + new Vector3(-CardWidth / 2, -CardHeight / 2, 0));
                Vector2 b = Camera.WorldToViewportPoint(c + new Vector3(CardWidth / 2, CardHeight / 2, 0));
                rects.Add(Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)));
            }
            return rects;
        }
    }
}
