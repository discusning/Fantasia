using System.Linq;
using UnityEngine;

namespace Fantasia.UI
{
    // 11/11 받은 아군 동작(Editor/MotionSetup이 컨트롤러에 이음): 방어·강화 스킬·아이템·회복·추스르기·부활.
    // 신호가 없는 컨트롤러(적, 아직 동작 없는 인물)는 아무 일도 안 함 → 예전처럼 서 있음.
    public partial class CharacterMotion
    {
        public const float SkillLead = 0.55f; // 강화 스킬: 관자놀이 집중 → 앞 가리키는 때 효과
        public const float ItemLead = 0.45f;  // 아이템: 병을 입에 대는 때
        public const float HealLead = 0.7f;   // 회복: 약을 아군 쪽으로 내미는 때

        public bool Has(string trigger) => Ready() && Anim.parameters.Any(p => p.name == trigger && p.type == AnimatorControllerParameterType.Trigger);

        // 동작 신호 하나 보내기. 있으면 true (부르는 쪽이 리드 시간만큼 기다림)
        public bool Play(string trigger)
        {
            if (IsDown || !Has(trigger)) return false;
            foreach (var p in Anim.parameters) if (p.type == AnimatorControllerParameterType.Trigger && p.name != trigger) Anim.ResetTrigger(p.name);
            Anim.SetTrigger(trigger);
            return true;
        }

        public bool Defend() => Play("Defend");
        public bool BuffSkill() => Play("Skill");
        public bool UseItem() => Play("Item");
        public bool Heal() => Play("Heal") || Play("Skill"); // 회복 동작이 없는 직업은 강화 스킬 동작
        public bool Recover() => Play("Recover");
    }
}
