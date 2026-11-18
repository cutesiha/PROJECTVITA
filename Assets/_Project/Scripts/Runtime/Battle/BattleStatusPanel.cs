using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProjectVita.Core;
using ProjectVita.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectVita.Battle
{
    /// <summary>
    /// 능력치 창 (UNIT STATUS). 유닛 카드를 누르거나 STATUS 버튼, I 키로 연다.
    /// 왼쪽: 그림, 이름, 진영, 타입, HP. 오른쪽: 능력치 (Battle_Stat.csv의 줄마다, 기본값 → 지금 값과 단계), 상태이상, 상성, 스킬.
    /// 능력치 줄은 CSV에서 만들어서, 비디님 기획으로 스탯이 바뀌어도 화면을 고치지 않아도 된다.
    /// 막대 길이는 전체 유닛 중 그 스탯의 최고 기본값 기준이다.
    /// 오른쪽 클릭, I, Esc, X로 닫는다. 열려 있는 동안은 일시정지 상태다.
    /// </summary>
    public sealed class BattleStatusPanel : MonoBehaviour, IClosablePanel
    {
        [SerializeField] UIPanelAnim panel;
        [SerializeField] Button closeButton;
        [SerializeField] Image portrait;
        [SerializeField] TMP_Text portraitLabel;
        [SerializeField] Image accentBar;
        [SerializeField] TMP_Text nameText;
        [SerializeField] TMP_Text subText;
        [SerializeField] TMP_Text sideText;
        [SerializeField] TMP_Text typeText;
        [SerializeField] RectTransform hpFill;
        [SerializeField] TMP_Text hpText;
        [SerializeField] RectTransform statRoot;
        [SerializeField] BattleStatRow statTemplate;
        [SerializeField] TMP_Text statusText;
        [SerializeField] TMP_Text matchupText;
        [SerializeField] RectTransform skillRoot;
        [SerializeField] BattleSkillButton skillTemplate;

        public bool IsOpen => panel != null && panel.IsShown;

        readonly List<BattleStatRow> rows = new List<BattleStatRow>();
        readonly List<BattleSkillButton> skills = new List<BattleSkillButton>();
        bool pausedBefore;
        float openedAt;

        void Awake()
        {
            statTemplate.gameObject.SetActive(false);
            skillTemplate.gameObject.SetActive(false);
            closeButton.onClick.AddListener(Close);
            panel.HideInstant();
        }

        public void Open(Unit unit, BattleDatabase db)
        {
            if (unit == null || db == null) return;
            Fill(unit, db);
            if (!IsOpen)
            {
                pausedBefore = GamePause.IsPaused;
                GamePause.Set(true);
                panel.Show();
            }
            openedAt = Time.unscaledTime;
        }

        public void Close()
        {
            if (!IsOpen) return;
            panel.Hide();
            GamePause.Set(pausedBefore);
            EventSystem.current?.SetSelectedGameObject(null);
        }

        void Update()
        {
            if (!IsOpen || Time.unscaledTime - openedAt < 0.2f) return;
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if ((mouse != null && mouse.rightButton.wasPressedThisFrame) || (kb != null && kb.iKey.wasPressedThisFrame)) Close();
        }

        void Fill(Unit u, BattleDatabase db)
        {
            bool ally = u.Side == Side.Ally;
            var accent = ally ? UITheme.Cyan : UITheme.Amber;

            var sprite = AssetCatalog.Instance.FindBattlePortrait(u.Def.portraitId);
            portrait.sprite = sprite;
            portrait.preserveAspect = true;
            portrait.color = sprite != null ? (u.IsDown ? new Color(0.35f, 0.35f, 0.38f) : Color.white) : new Color(0.12f, 0.13f, 0.17f);
            portraitLabel.text = sprite != null ? "" : u.Def.id;
            accentBar.color = accent;
            nameText.text = u.Name;
            subText.text = u.Def.id.ToUpperInvariant();
            sideText.text = ally ? "ALLY" : "ENEMY";
            sideText.color = accent;
            typeText.text = string.IsNullOrEmpty(u.Def.type) ? "TYPE  —" : $"TYPE  {u.Def.type.ToUpperInvariant()}";

            hpFill.anchorMax = new Vector2(u.HpRatio, 1f);
            hpFill.offsetMax = Vector2.zero;
            hpFill.GetComponent<Image>().color = BattleUnitView.HpColor(u.HpRatio);
            hpText.text = $"{Mathf.Max(0, u.Hp)} / {u.MaxHp}";

            // 능력치 (Battle_Stat.csv 순서)
            while (rows.Count < db.stats.Count)
            {
                var row = Instantiate(statTemplate, statRoot);
                row.gameObject.SetActive(true);
                rows.Add(row);
            }
            for (int i = 0; i < rows.Count; i++)
            {
                bool used = i < db.stats.Count;
                rows[i].gameObject.SetActive(used);
                if (!used) continue;
                var stat = db.stats[i];
                int baseValue = u.Def.Stat(stat.id, 0);
                int current = u.Gauges.TryGetValue(stat.id, out int gauge) ? gauge : u.Stat(stat.id);
                if (string.Equals(stat.id, u.Rules.HpStat, System.StringComparison.OrdinalIgnoreCase)) current = Mathf.Max(0, u.Hp);
                u.Stages.TryGetValue(stat.id, out int stage);
                int best = Mathf.Max(1, db.units.Max(d => d.Stat(stat.id, 0)));
                rows[i].Set(stat.displayName, stat.id, baseValue, current, stage, current / (best * 1.1f));
            }

            // 상태이상
            var sb = new StringBuilder();
            foreach (var s in u.Statuses)
                sb.Append($"<color={UITheme.ToHex(UITheme.Amber)}>{s.Def.displayName}</color> <color={UITheme.ToHex(UITheme.TextDim)}>{s.TurnsLeft}턴</color>    ");
            statusText.text = sb.Length > 0 ? sb.ToString() : $"<color={UITheme.ToHex(UITheme.TextDim)}>없음</color>";

            // 상성: 이 유닛이 맞을 때 (공격 타입 → 배율)
            matchupText.text = Matchups(u, db);

            // 스킬
            while (skills.Count < u.Skills.Count)
            {
                var s = Instantiate(skillTemplate, skillRoot);
                s.gameObject.SetActive(true);
                skills.Add(s);
            }
            for (int i = 0; i < skills.Count; i++)
            {
                bool used = i < u.Skills.Count;
                skills[i].gameObject.SetActive(used);
                if (used) skills[i].Set(u.Skills[i], -1, true);
            }
        }

        static string Matchups(Unit u, BattleDatabase db)
        {
            if (string.IsNullOrEmpty(u.Def.type) || db.typeChart.Count == 0)
                return $"<color={UITheme.ToHex(UITheme.TextDim)}>상성 없음</color>";
            var weak = new List<string>();
            var resist = new List<string>();
            foreach (var row in db.typeChart)
            {
                float m = db.TypeMultiplier(row.attackType, u.Def.type);
                if (m > 1.01f) weak.Add($"{row.attackType.ToUpperInvariant()} ×{m:0.#}");
                else if (m < 0.99f) resist.Add($"{row.attackType.ToUpperInvariant()} ×{m:0.##}");
            }
            string dim = UITheme.ToHex(UITheme.TextDim);
            return $"약점  <color={UITheme.ToHex(UITheme.Danger)}>{(weak.Count > 0 ? string.Join("   ", weak) : "—")}</color>\n" +
                   $"내성  <color={UITheme.ToHex(UITheme.Cyan)}>{(resist.Count > 0 ? string.Join("   ", resist) : "—")}</color>" +
                   $"   <color={dim}><size=80%>(맞을 때 받는 배율)</size></color>";
        }
    }
}
