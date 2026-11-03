using System.Collections.Generic;
using UnityEngine;

namespace Fantasia.Core
{
    // 효과음 ID (AssetList 9 사운드 표). 소리 파일이 오면 둘 중 하나만 하면 된다 — 코드 수정 없음:
    //   ① Resources/Sfx/ 폴더에 "ID 이름 그대로" 파일을 넣기 (예: Resources/Sfx/UI_Click.wav)
    //   ② Resources/SfxLibrary 에셋의 같은 ID 칸에 Clip 끼우기 (소리 크기도 여기서)
    public static class SfxIds
    {
        // UI
        public const string UiClick = "UI_Click";            // 버튼 클릭
        public const string UiOpen = "UI_Open";              // 창 열기 (상점·인벤토리)
        public const string UiClose = "UI_Close";            // 창 닫기
        public const string ItemGet = "UI_ItemGet";          // 아이템 획득 알림
        public const string Coin = "UI_Coin";                // 돈 (사기·팔기·서비스·제작)
        // 보드
        public const string Dice = "Board_Dice";             // 주사위 굴림
        public const string Step = "Board_Step";             // 말 이동 (칸마다)
        public const string Encounter = "Board_Encounter";   // 조우 창
        // 전투
        public const string SlotSuccess = "Battle_SlotSuccess"; // 확률 칸 성공 (칸마다)
        public const string SlotFail = "Battle_SlotFail";       // 확률 칸 실패
        public const string Hit = "Battle_Hit";              // 명중
        public const string Crit = "Battle_Crit";            // 크리티컬
        public const string Miss = "Battle_Miss";            // 빗나감·회피
        public const string Heal = "Battle_Heal";            // 회복 (스킬·회복약, 보드 포함)
        public const string Down = "Battle_Down";            // 쓰러짐
        public const string Revive = "Battle_Revive";        // 부활
        public const string Victory = "Result_Victory";      // 전투 승리
        public const string Defeat = "Result_Defeat";        // 전투 패배·전멸

        public static readonly string[] All =
        {
            UiClick, UiOpen, UiClose, ItemGet, Coin,
            Dice, Step, Encounter, // 턴 전환은 판 연출 소리 (중복 방지)
            SlotSuccess, SlotFail, Hit, Crit, Miss, Heal, Down, Revive, Victory, Defeat,
        };
    }

    // 효과음 재생 (D28). 소리가 비어 있으면 아무 소리 없이 지나간다 (경고 1번).
    // 같은 소리는 0.05초 안에 다시 부르면 무시 (버튼 겹침·칸 이동 연타 방지). 볼륨 = 칸 볼륨 × 설정의 효과음 볼륨.
    public class SfxPlayer : MonoBehaviour
    {
        private const int Voices = 8;
        private const float RepeatGuard = 0.05f;

        private static SfxPlayer _instance;
        private static float _volumeScale = 1f;
        private static readonly HashSet<string> Warned = new();
        private static readonly Dictionary<string, AudioClip> FolderClips = new();
        private static readonly Dictionary<string, float> LastPlayed = new();
        private readonly List<AudioSource> _voices = new();
        private int _next;

        // 자동 점검용: ID별로 불린 횟수 (소리 파일 유무와 상관없이 셈)
        public static readonly Dictionary<string, int> DevCalls = new();
        public static int DevAudible;   // 실제로 소리 파일이 있어 재생된 횟수
        // 자동 점검용: 파일 대신 만든 소리를 끼워 실제 재생 경로 확인 (-autoplay-sfx-beep)
        public static void DevInject(string id, AudioClip clip) => FolderClips[id] = clip;

        public static void SetVolumeScale(float scale) => _volumeScale = Mathf.Clamp01(scale);
        public static float VolumeScale => _volumeScale;

        public static void Click() => Play(SfxIds.UiClick);

        public static void Play(string id)
        {
            if (!Application.isPlaying || string.IsNullOrEmpty(id)) return; // 셀프 테스트(에디터)에서는 무시
            float now = Time.unscaledTime;
            if (LastPlayed.TryGetValue(id, out float last) && now - last < RepeatGuard) return;
            LastPlayed[id] = now;
            DevCalls[id] = DevCalls.TryGetValue(id, out int n) ? n + 1 : 1;

            var (clip, volume) = Resolve(id);
            if (clip == null)
            {
                if (Warned.Add(id)) Debug.Log($"[Sfx] {id} 소리 없음 — Resources/Sfx/{id}.wav 를 넣거나 SfxLibrary에 연결");
                return;
            }
            var source = Instance.NextVoice();
            source.PlayOneShot(clip, volume * _volumeScale);
            DevAudible++;
        }

        // SfxLibrary 칸이 먼저, 없으면 Resources/Sfx/{id} 파일
        public static (AudioClip clip, float volume) Resolve(string id)
        {
            var entry = SfxLibrary.Current != null ? SfxLibrary.Current.Find(id) : null;
            if (entry != null && entry.Clip != null) return (entry.Clip, entry.Volume);
            if (!FolderClips.TryGetValue(id, out var clip))
            {
                clip = Resources.Load<AudioClip>("Sfx/" + id);
                FolderClips[id] = clip;
            }
            return (clip, entry != null ? entry.Volume : 0.8f);
        }

        private static SfxPlayer Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("SfxPlayer");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<SfxPlayer>();
                return _instance;
            }
        }

        private AudioSource NextVoice()
        {
            if (_voices.Count < Voices)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                _voices.Add(s);
                return s;
            }
            _next = (_next + 1) % Voices;
            return _voices[_next];
        }
    }
}
