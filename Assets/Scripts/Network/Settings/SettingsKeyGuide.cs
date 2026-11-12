using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Marco.Network.Settings
{
    // 키 가이드 탭: read-only, no rebinding. Only keys checked in code are listed (PlayerMovement · PlayerInteractor ·
    // PlayerMovement.Shout · PlayerMovement.Swimming · PoolLadders · MicrophoneVisionController). Development keys
    // (F1–F11, Tab) and C are left out on purpose.
    internal sealed class SettingsKeyGuide
    {
        public enum Role { Runner, Seeker, Echo }

        private sealed class Key
        {
            public string Id;
            public Image Face;
            public Text Label;
        }

        private static readonly string[] Common = { "W", "A", "S", "D", "Esc", "Alt", "Mouse" };
        private static readonly string[] RunnerKeys = { "Shift", "E", "Q", "RMB", "1", "2", "3", "4", "G", "Ctrl", "Space", "F" };
        private static readonly string[] SeekerKeys = { "LMB", "R", "Ctrl" };
        private static readonly string[] EchoKeys = { "G", "Space", "Ctrl" };

        private const string CommonText =
            "W A S D  -  이동\n마우스  -  시점\nLeft Alt (누르는 동안)  -  마이크 음소거\nEsc  -  메뉴";
        private static readonly string[] RoleText =
        {
            // 도망자
            "Left Shift  -  질주\nE (누르고 있기)  -  밸브 · 배수구 작업 / 부상자 치료\nE  -  아이템 줍기\nQ / 마우스 우클릭  -  찰칵이\n" +
            "1 2 3 4  -  아이템 슬롯 선택\nG (1번 슬롯, 누르고 있기)  -  붕대\nLeft Ctrl  -  잠수(수영 중) / 숨 참기\n" +
            "Space  -  점프 · 수영장에서 나오기\nF  -  사다리",
            // 술래
            "마우스 좌클릭  -  태그\nR  -  외침\nLeft Ctrl (누르고 있기)  -  잠행",
            // 메아리
            "G  -  노크 (탈락 메아리만 · 탈출자 메아리는 노크 없음)\nSpace  -  상승\nLeft Ctrl  -  하강",
        };

        private readonly List<Key> keys = new();
        private readonly HashSet<string> active = new();
        private Text list;
        private readonly Image[] roleTabs = new Image[3];

        public void Build(Transform parent, float width)
        {
            string[] names = { "도망자", "술래", "메아리" };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                var button = SettingsUi.Button(parent, "Role " + names[i], 40 + i * 170, 10, 150, 44, names[i], 22, () => Select((Role)index));
                roleTabs[i] = button.GetComponent<Image>();
            }
            // A small keyboard: rows of key caps drawn with rectangles (left), two mouse buttons (right).
            float x0 = 40, y0 = 74, k = 52, gap = 6;
            Row(parent, x0, y0, k, gap, ("Esc", 1f), (null, .5f), ("1", 1f), ("2", 1f), ("3", 1f), ("4", 1f));
            Row(parent, x0, y0 + (k + gap), k, gap, (null, 1.5f), ("Q", 1f), ("W", 1f), ("E", 1f), ("R", 1f));
            Row(parent, x0, y0 + 2 * (k + gap), k, gap, (null, 1.75f), ("A", 1f), ("S", 1f), ("D", 1f), ("F", 1f), ("G", 1f));
            Row(parent, x0, y0 + 3 * (k + gap), k, gap, ("Shift", 2.25f));
            Row(parent, x0, y0 + 4 * (k + gap), k, gap, ("Ctrl", 1.5f), (null, .5f), ("Alt", 1.25f), ("Space", 4f));
            // Mouse under the keyboard; the "키 - 동작" list in the right column, inside the panel.
            float my = y0 + 5 * (k + gap) + 4;
            Cap(parent, "LMB", "좌", x0, my, 60, 52);
            Cap(parent, "RMB", "우", x0 + 66, my, 60, 52);
            Cap(parent, "Mouse", "마우스(시점)", x0 + 132, my, 150, 52);
            list = SettingsUi.Label(parent, "Key List", 490, y0, width - 530, 380, "", 18, SettingsUi.Light, TextAnchor.UpperLeft);
            list.lineSpacing = 1.05f;
            Select(Role.Runner);
        }

        public void Select(Role role)
        {
            active.Clear();
            foreach (var id in Common) active.Add(id);
            foreach (var id in role == Role.Runner ? RunnerKeys : role == Role.Seeker ? SeekerKeys : EchoKeys) active.Add(id);
            foreach (var key in keys)
            {
                bool on = active.Contains(key.Id);
                key.Face.color = on ? SettingsUi.AmberA(.9f) : SettingsUi.LightA(.08f);
                key.Label.color = on ? SettingsUi.Black : SettingsUi.LightA(.45f);
            }
            for (int i = 0; i < roleTabs.Length; i++)
                if (roleTabs[i] != null) roleTabs[i].color = i == (int)role ? SettingsUi.AmberA(.55f) : SettingsUi.LightA(.24f);
            if (list != null) list.text = CommonText + "\n\n" + RoleText[(int)role];
        }

        private void Row(Transform parent, float x, float y, float k, float gap, params (string id, float units)[] caps)
        {
            foreach (var (id, units) in caps)
            {
                float w = units * k + (units - 1f) * gap;
                if (id != null) Cap(parent, id, id, x, y, w, k);
                x += w + gap;
            }
        }

        private void Cap(Transform parent, string id, string text, float x, float y, float w, float h)
        {
            var rt = SettingsUi.At(parent, "Key " + id, x, y, w, h);
            var face = SettingsUi.Fill(rt, SettingsUi.LightA(.08f));
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = SettingsUi.LightA(.35f); outline.effectDistance = new Vector2(1, -1);
            var label = SettingsUi.Label(rt, "Text", 0, 0, w, h, text, 18, SettingsUi.Light, TextAnchor.MiddleCenter, FontStyle.Bold);
            keys.Add(new Key { Id = id, Face = face, Label = label });
        }
    }
}
