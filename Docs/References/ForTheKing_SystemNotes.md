# For the King — 시스템 리서치 노트

`fantasia`가 차용할 예정인 핵심 게임플레이 루프를 이해하기 위해 정리한 참고 노트.
프로덕션 코드 설계(`Assets/_Project/Scripts/...`)와 GDD(`Docs/GDD/GDD.md`) 작성의 기반 자료로 사용.

## 1. 오버월드 / 보드
- 헥사곤 타일로 나뉜 보드를 파티가 함께 이동.
- 이동력은 고정값이 아니라 **매 턴 이동 주사위를 굴려서 결정** — 계획을 세우기 어렵게 만드는 랜덤 요소.
- 보드 위에는 마을, 던전, 숲, 야영지, 퀘스트 마커 등 POI(Point of Interest)가 배치됨.
- 챕터(Chapter) 단위로 목표가 갱신되고 다음 지역으로 이동.

→ `Scripts/Board`: 타일 그래프, 이동 처리, POI 데이터, 챕터 진행 상태를 분리해서 설계할 것.

## 2. 주사위 / 판정 시스템
- 모든 행동은 **d100 기반 퍼센트 판정**으로 해결됨. 스탯 값 자체가 성공 확률(대략 40~90 범위).
- 전투 안팎의 스킬 체크(어웨어니스, 힐링, 채집 등)에 동일한 규칙 적용 → 하나의 범용 판정 모듈로 재사용 가능.

→ `Scripts/Dice`: `RollResult`, `SkillCheck(statValue, modifiers)` 같은 순수 로직으로 UI/전투/이벤트 어디서든 재사용.

## 3. 전투
- **턴제, JRPG 스타일** — 속도(Speed) 스탯 기반으로 캐릭터/적이 번갈아 행동.
- 데미지는 랜덤 범위 값으로 생성되어 목표의 HP를 깎음.
- 전투 진입 시 Fight / Ambush 등 선택지 존재. Ambush는 어웨어니스 판정 3회 성공 시 선제 턴 확보.
- **파티 대형(Formation)이 턴 순서를 결정** — 픽 화면에서 좌→우로 배치한 순서가 그대로 턴 순서(왼쪽이 라운드 시작, 오른쪽이 라운드 종료).
- **포커스 포인트(Focus)**: 소모 시 해당 주사위 판정을 확정 성공(쇼크 상태 무시) 처리, 이후 판정에도 성공 확률 보너스(체감 감소형). 퍼펙트 롤 시 크리티컬 확률 +5%.
- 후속작(FTK2)은 배틀 그리드와 4번째 파티원을 추가해 포지셔닝 전략을 강화.

→ `Scripts/Combat`: 턴 순서(대형 기반), 액션 큐, 포커스 리소스, 랜덤 데미지 롤러를 분리된 컴포넌트로.

## 4. 캐릭터 / 파티
- 소수 인원(2~4인) 파티, 각기 다른 클래스/스탯 조합.
- 스탯이 곧 판정 확률이므로 성장(레벨업)이 "능력 해금"보다 "확률 상승"에 가까움.

→ `Scripts/Characters`: 스탯 컨테이너, 성장 곡선을 ScriptableObject(`Data/Characters`)로 데이터화.

## 5. 캠프 / 생존
- 하루 주기가 끝나면 야영이 강제되며 식량 등 자원을 소모.
- 캠핑 중 이벤트가 발생할 수 있음(습격, 휴식 보너스 등).

→ `Scripts/Camp`: 자원 소비 타이머, 캠프 이벤트 훅.

## 6. 이벤트 / 인카운터
- 텍스트 기반 선택형 이벤트(방문 지점, 조우)가 잦음 — 선택지마다 판정이 걸리기도 함.

→ `Scripts/Events`: 이벤트 정의를 데이터(`Data/Events`)로 두고, 실행기는 `Dice` 모듈을 재사용.

## 7. 퀘스트 / 챕터 진행
- 메인 퀘스트(챕터 목표) + 사이드 퀘스트 병행.
- 챕터가 넘어갈 때 난이도/위협이 상승하는 구조(다크니스류 메커닉).

→ `Scripts/Quests`: 챕터 상태 머신, 퀘스트 목표 추적.

## 8. 아이템 / 경제
- 마을에서 장비 구매, 인벤토리 관리.

→ `Scripts/Items`: 인벤토리, 장비 슬롯, 상점 데이터.

## Fantasia에서 그대로 가져갈 것
- 헥스 보드 + 주사위 이동
- d100 스탯=확률 판정 시스템
- 대형(Formation) 기반 턴 순서 + 포커스 리소스
- 캠프/일일 사이클
- 텍스트 이벤트 + 판정 선택지

## Fantasia에서 다르게 가져갈 수 있는 지점 (기획 진행 중 — 채워 넣을 것)
- [ ] 3D 비주얼 표현 방식 (디오라마 카메라 vs 자유 카메라)
- [ ] 파티 인원수 / 영구사망 여부
- [ ] 협동(co-op) 지원 여부
- [ ] 차별화 시스템(추가 기획 중인 것)

## 참고 자료
- [Combat - Official For The King Wiki (FTK2)](https://fortheking.wiki.gg/wiki/Combat_(FTK2))
- [Gameplay - Official For The King Wiki](https://fortheking.fandom.com/wiki/Gameplay)
- [Combat - Official For The King Wiki](https://fortheking.fandom.com/wiki/Combat)
- [Aston's Guide to Party Layout - Official For The King Wiki](https://fortheking.fandom.com/wiki/Aston's_Guide_to_Party_Layout)
- [Roll the Dice for Vengeance and Glory in 'For The King' | Fandom](https://www.fandom.com/articles/for-the-king)
- [For The King II Character Stats Guide](https://www.forthekingii.wiki/combat/for-the-king-ii-character-stats)
