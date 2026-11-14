using System.Collections.Generic;
using System.Linq;
using Fantasia.Data;
using UnityEngine;
using UnityEngine.UI;

namespace Fantasia.UI
{
    // 11/14 팀장: 스킬·소모품 설명이 행동 창 오른쪽 위 10pt 한 줄이라 읽기 어려움 → 고르는 칸 바로 위에 설명 카드.
    // 다른 게임 조사 (Docs/진행일지 11/14): 툴팁은 고르는 버튼 가까이, 이름·종류·대상·비용을 첫 줄에 모아 한눈에,
    // 본문은 쉬운 말 한두 문장 + 지금 수치(계산식 대신), 긴 규칙 원문은 작게 아래로 (Slay the Spire 카드·BG3 툴팁처럼 요약 → 자세히).
    // 11/14 팀장 플레이 QA "카드가 너무 크고 안쪽 글이 정렬 안 됨": 글 한 덩어리(크기 섞인 서식 글) → 줄마다 따로 칸.
    //  윗줄 이름(왼쪽)·종류·비용(오른쪽) / 가는 선 / 본문 / 규칙 원문. 모든 줄 왼쪽 끝 같은 여백, 높이는 고정(작게), 글이 넘치면 글씨만 줄임.
    public partial class BattleHud
    {
        private GameObject _descCard;
        private RectTransform _descRoot;
        private Text _descName, _descTag, _descBody, _descFoot;
        public static string DevLastDesc = ""; // 점검용

        private static readonly Color DescTag = new Color(0.62f, 0.8f, 0.95f);
        private static readonly Color DescName = new Color(0.95f, 0.8f, 0.5f);
        private static readonly Color DescRule = new Color(0.66f, 0.63f, 0.58f);
        private static readonly Color DescWarn = new Color(0.95f, 0.45f, 0.4f);
        private static readonly Color DescNow = new Color(1f, 0.78f, 0.45f);

        private const float DescHeight = 0.1f;  // 화면 높이 비율 (1080에서 약 108px)
        private const float DescGap = 0.006f;   // 고르는 칸과 띄움
        private const float PadX = 0.022f;      // 카드 안 왼쪽·오른쪽 여백 (모든 줄 같음)

        private readonly struct DescParts
        {
            public readonly string Name, Tag, Body, Foot; public readonly bool Warn;
            public DescParts(string name, string tag, string body, string foot, bool warn) { Name = name; Tag = tag; Body = body; Foot = foot; Warn = warn; }
            public override string ToString() => $"{Name} [{Tag}]\n{Body}\n{Foot}";
        }

        // 스킬 쉬운 말 요약 (기획 원문 System.md는 맨 아래 작게 그대로). id 끝 → (종류, 요약)
        private static readonly Dictionary<string, (string kind, string text)> SkillPlain = new()
        {
            ["expose_weakness"] = ("약화", "적의 물리·마법 방어를 <b>4씩</b> 낮춥니다. 적이 <b>2번</b> 행동할 동안. 반드시 걸리고 겹치지 않습니다."),
            ["pinpoint_breach"] = ("공격", "무기로 강하게 찌릅니다. 크리티컬이면 적에게 <b>방해</b>를 겁니다."),
            ["lift_the_veil"] = ("약화", "적의 <b>회피 상태</b>를 적이 <b>2번</b> 행동할 동안 없앱니다. 그림자 적·인공 사신에게 쓰세요."),
            ["black_miasma"] = ("공격", "<b>모든 적</b>에게 독기 마법 피해. 판정 3칸이 모두 성공하면 각 적에게 <b>중독 1</b>."),
            ["purge_plague"] = ("정화", "<b>중독·저주·혼란</b>을 모두 없앱니다. 죽음의 표식·영혼 수확은 못 없앱니다."),
            ["emergency_suture"] = ("회복", "HP를 회복합니다 (반드시). 판정 2칸이 모두 성공하면 <b>출혈</b>도 없앱니다."),
            ["piercing_round"] = ("공격", "적 방어를 <b>절반만</b> 받는 관통탄."),
            ["zero_in"] = ("강화", "다음 무기 공격의 판정 칸 성공률 <b>+15%p</b>. 내 다음 2턴 안에 공격하지 않으면 사라집니다."),
            ["suppressing_fire"] = ("공격", "견제 사격. 크리티컬이면 적 <b>속도 −15</b>(2턴)."),
        };

        private static string TargetText(SkillTarget t) => t switch
        {
            SkillTarget.Self => "자신",
            SkillTarget.SingleAlly => "아군 1명",
            SkillTarget.SingleEnemy => "적 1명",
            SkillTarget.AllAllies => "모든 아군",
            SkillTarget.AllEnemies => "모든 적",
            _ => "",
        };

        private static string KindOf(SkillDefinition s) => s.Effect switch
        {
            SkillEffect.Attack => "공격",
            SkillEffect.Heal => "회복",
            SkillEffect.Cleanse => "정화",
            SkillEffect.ApplyStatus => s.Target is SkillTarget.SingleEnemy or SkillTarget.AllEnemies ? "약화" : "강화",
            _ => "",
        };

        private Text DescLine(Transform panel, string name, float yMin, float yMax, int size, TextAnchor align, Color color, bool bold, float xMin = PadX, float xMax = 1f - PadX)
        {
            var t = Label(panel, name, new Vector2(xMin, yMin), new Vector2(xMax, yMax), size, align, color, bold);
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true; // 넘치면 잘리지 않고 글씨만 조금 작게
            t.resizeTextMinSize = Mathf.Max(9, size - 3);
            t.resizeTextMaxSize = size;
            t.lineSpacing = 1.05f;
            return t;
        }

        private void BuildDescCard(Transform canvas)
        {
            var panel = UGUIKit.CreateThemedPanel(canvas, "DescCard", new Vector2(0.26f, 0.505f), new Vector2(0.74f, 0.505f + DescHeight), Gold, PanelFill, 2f);
            _descCard = panel.parent.gameObject;
            _descRoot = _descCard.GetComponent<RectTransform>();
            // 윗줄: 이름(왼쪽) · 종류·대상·비용(오른쪽) — 같은 줄, 같은 가운데 높이
            _descName = DescLine(panel, "Name", 0.68f, 0.94f, 15, TextAnchor.MiddleLeft, DescName, bold: true, xMax: 0.5f);
            _descTag = DescLine(panel, "Tag", 0.68f, 0.94f, 12, TextAnchor.MiddleRight, DescTag, bold: false, xMin: 0.4f);
            var line = UGUIKit.CreateImage(panel, "Line", new Vector2(PadX, 0.655f), new Vector2(1f - PadX, 0.665f), new Color(Gold.r, Gold.g, Gold.b, 0.35f));
            line.raycastTarget = false;
            _descBody = DescLine(panel, "Body", 0.27f, 0.64f, 13, TextAnchor.UpperLeft, TextLight, bold: false);
            _descFoot = DescLine(panel, "Foot", 0.05f, 0.27f, 11, TextAnchor.LowerLeft, DescRule, bold: false);
            var group = _descCard.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false; group.interactable = false; // 뒤의 적 클릭을 막지 않음
            _descCard.SetActive(false);
        }

        // 설명 카드는 고르는 칸(스킬 / 소모품) 바로 위, 칸과 같은 너비. 칸이 닫히거나, 고른 뒤 대상을 고르는 동안은 숨김 (적이 가려지지 않게)
        private void ShowDesc(DescParts d, bool overItems)
        {
            if (_descCard == null || _chosen != ChosenKind.None) return;
            float y0 = (overItems ? 0.585f : 0.5f) + DescGap;
            _descRoot.anchorMin = new Vector2(0.26f, y0);
            _descRoot.anchorMax = new Vector2(0.74f, y0 + DescHeight);
            _descCard.transform.SetAsLastSibling(); // 보호막 표시·떠오르는 숫자보다 위에
            _descName.text = d.Name;
            _descTag.text = d.Tag;
            _descBody.text = d.Body;
            _descFoot.text = d.Foot;
            _descFoot.color = d.Warn ? DescWarn : DescRule;
            _descFoot.fontStyle = d.Warn ? FontStyle.Bold : FontStyle.Normal;
            DevLastDesc = d.ToString();
            _descCard.SetActive(true);
        }

        private void HideDescIfClosed()
        {
            if (_descCard == null || !_descCard.activeSelf) return;
            if ((!_skillPanel.activeSelf && !_itemPanel.activeSelf) || _chosen != ChosenKind.None) { _descCard.SetActive(false); return; }
            var t = _descCard.transform;
            if (t.GetSiblingIndex() != t.parent.childCount - 1) t.SetAsLastSibling(); // 나중에 생긴 보호막 표시보다 위
        }

        private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        private DescParts SkillDescText(SkillDefinition skill, string reason)
        {
            string id = string.IsNullOrEmpty(skill.Id) ? "" : skill.Id.Substring(skill.Id.LastIndexOf('.') + 1);
            SkillPlain.TryGetValue(id, out var plain);
            string kind = plain.kind ?? KindOf(skill);
            string target = TargetText(skill.Target);
            string tag = string.Join(" · ", new[] { kind, target, SkillCostText(skill) }.Where(s => !string.IsNullOrEmpty(s)));
            string body = plain.text ?? skill.EffectDescription;
            string now = NowNumbers(skill);
            if (now != null) body += $"  <color=#{Hex(DescNow)}><b>{now}</b></color>";
            string foot = reason != null ? $"지금 못 씀: {reason}" : plain.text != null ? $"규칙: {skill.EffectDescription}" : "";
            return new DescParts(skill.DisplayName, tag, body, foot, reason != null);
        }

        // 계산식(1.3A·floor(0.5M)) 대신 지금 이 캐릭터의 수치
        private string NowNumbers(SkillDefinition skill)
        {
            if (_actionActor == null || _actionActor.Member == null) return null;
            if (skill.Effect == SkillEffect.Attack)
            {
                var req = _actionActor.SkillAttack(skill, 0);
                int raw = Mathf.RoundToInt(req.Coefficient * req.AttackValue);
                return $"(지금 피해 약 {raw}, 적 방어 전)";
            }
            if (skill.Effect == SkillEffect.Heal && (skill.HealFlat > 0 || skill.HealMagicRatio > 0f))
                return $"(지금 회복 {skill.HealFlat + Mathf.FloorToInt(skill.HealMagicRatio * _actionActor.Member.FinalMagicAttack + 0.0001f)})";
            return null;
        }

        private DescParts ItemDescText(ConsumableDefinition item) =>
            new DescParts(item.DisplayName, "소모품 · 개인 행동 1회", item.EffectDescription, "", false);
    }
}
