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
    public partial class BattleHud
    {
        private GameObject _descCard;
        private RectTransform _descRoot;
        private Text _descText;
        public static string DevLastDesc = ""; // 점검용

        private static readonly Color DescTag = new Color(0.62f, 0.8f, 0.95f);

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

        private void BuildDescCard(Transform canvas)
        {
            var panel = UGUIKit.CreateThemedPanel(canvas, "DescCard", new Vector2(0.26f, 0.505f), new Vector2(0.74f, 0.635f), Gold, PanelFill, 2f);
            _descCard = panel.parent.gameObject;
            _descRoot = _descCard.GetComponent<RectTransform>();
            _descText = Label(panel, "Text", new Vector2(0.025f, 0.06f), new Vector2(0.975f, 0.94f), 16, TextAnchor.UpperLeft, TextLight, bold: false);
            _descText.supportRichText = true;
            _descText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _descText.verticalOverflow = VerticalWrapMode.Truncate;
            _descText.lineSpacing = 1.1f;
            var group = _descCard.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false; group.interactable = false; // 뒤의 적 클릭을 막지 않음
            _descCard.SetActive(false);
        }

        // 설명 카드는 고르는 칸(스킬 / 소모품) 바로 위. 칸이 닫히거나, 고른 뒤 대상을 고르는 동안은 숨김 (적이 가려지지 않게)
        private void ShowDesc(string text, bool overItems)
        {
            if (_descCard == null || _chosen != ChosenKind.None) return;
            float y0 = overItems ? 0.59f : 0.505f;
            _descRoot.anchorMin = new Vector2(0.26f, y0);
            _descRoot.anchorMax = new Vector2(0.74f, y0 + 0.18f); // 이름 줄 + 설명 2줄 + 규칙 원문 1~2줄
            _descCard.transform.SetAsLastSibling(); // 보호막 표시·떠오르는 숫자보다 위에
            _descText.text = text;
            DevLastDesc = text;
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

        private string SkillDescText(SkillDefinition skill, string reason)
        {
            string id = string.IsNullOrEmpty(skill.Id) ? "" : skill.Id.Substring(skill.Id.LastIndexOf('.') + 1);
            SkillPlain.TryGetValue(id, out var plain);
            string kind = plain.kind ?? KindOf(skill);
            string target = TargetText(skill.Target);
            string tag = string.Join(" · ", new[] { kind, target }.Where(s => !string.IsNullOrEmpty(s)));
            var sb = new System.Text.StringBuilder();
            sb.Append($"<size=19><b><color=#{Hex(new Color(0.95f, 0.8f, 0.5f))}>{skill.DisplayName}</color></b></size>");
            if (tag.Length > 0) sb.Append($"   <color=#{Hex(DescTag)}>[{tag}]</color>");
            sb.Append($"   <color=#{Hex(TextDim)}>{SkillCostText(skill)}</color>\n");
            sb.Append(plain.text ?? skill.EffectDescription);
            string now = NowNumbers(skill);
            if (now != null) sb.Append($"  <color=#{Hex(new Color(1f, 0.78f, 0.45f))}><b>{now}</b></color>");
            if (reason != null) sb.Append($"\n<color=#{Hex(new Color(0.95f, 0.45f, 0.4f))}><b>지금 못 씀: {reason}</b></color>");
            else if (plain.text != null) sb.Append($"\n<size=12><color=#{Hex(new Color(0.66f, 0.63f, 0.58f))}>규칙: {skill.EffectDescription}</color></size>");
            return sb.ToString();
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

        private string ItemDescText(ConsumableDefinition item)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"<size=19><b><color=#{Hex(new Color(0.95f, 0.8f, 0.5f))}>{item.DisplayName}</color></b></size>   <color=#{Hex(DescTag)}>[소모품 · 개인 행동 1회]</color>\n");
            sb.Append($"<size=18>{item.EffectDescription}</size>");
            return sb.ToString();
        }
    }
}
