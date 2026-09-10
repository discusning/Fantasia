# 게임 디자인 레퍼런스 조사 (2026-09-10)

> **목적 및 주의사항**: 이 문서는 기획서(GDD)가 아직 확정되지 않은 상태에서, 나중에 실제 기획/코드 작업을 할 때 참고할 "아이디어 재료"를 미리 모아두기 위한 자료조사입니다. 아래 내용은 각 게임의 공개된 시스템을 **일반화된 설계 패턴 수준으로 요약**한 것이며, 특정 게임의 수치·텍스트·에셋·코드를 그대로 가져다 쓰라는 뜻이 아닙니다. 실제로 채택할 아이디어가 있다면 Fantasia의 맥락에 맞게 재해석해서 우리만의 값/문구/구현으로 새로 작성해야 합니다(모티브 참고용, 그대로 복제 금지).

## 1. 파티 진영 / 턴 순서(이니셔티브)

### Darkest Dungeon — 턴 순서를 일부러 안 보여주는 설계
- 출처: [Darkest Dungeon Wiki - Turn-based Combat](https://darkestdungeon-archive.fandom.com/wiki/Turn-based_Combat)
- 핵심 아이디어: 전투 대형에서 앞/뒤 위치가 스킬 사용 가능 여부에 직결되고(예: 전열 전용기, 후열 전용기), 턴 순서를 화면에 정확히 숫자로 보여주지 않아 "다음에 누가 움직일지 완전히는 모르는" 긴장감을 의도적으로 유지함.
- Fantasia에 적용한다면: 현재 `CombatManager`가 Speed 기반 턴 큐를 쓰는데, 큐 전체를 다 보여주는 대신 "다음 1~2턴만 미리보기"로 제한하는 옵션을 나중에 검토해볼 만함(긴장감 vs 예측 가능성 트레이드오프는 팀 논의 필요).

### Wildermyth — 대형 자체보다 "관계/서사" 중심의 파티 설계
- 출처: [TV Tropes - Wildermyth](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/Wildermyth), [Legacy - Wildermyth Wiki](https://wildermyth.com/wiki/Legacy)
- 핵심 아이디어: 전투 대형보다도 캐릭터 간 관계(우정/연애/라이벌) 이벤트가 스탯/스킬에 영향을 주고, 영구적인 신체 변형(팔 잃음 등)이 이후 플레이에 계속 반영됨.
- Fantasia에 적용한다면: 현재는 캐릭터-전투 스탯 연결이 미정(GDD 6.3)이지만, 나중에 "부상/트라우마가 남는 캐릭터 상태" 같은 장치를 캠프/휴식 시스템(GDD 6.4~)과 엮을 여지가 있음.

## 2. 인벤토리 / 장비 슬롯 / 루팅

### Battle Brothers — 장비 부위별로 다른 드롭 확률 규칙
- 출처: [Battle Brothers Wiki - Game Guide](https://battlebrothers.fandom.com/wiki/Game_Guide), 커뮤니티 논의([Steam 토론](https://steamcommunity.com/app/365360/discussions/0/2971776551517767395/))
- 핵심 아이디어: 아머/투구/무기/방패가 전부 같은 확률로 드랍되지 않고, 부위별로 별도 판정을 두어(예: 방어구는 조건 충족 시 항상 드랍, 무기/방패/투구는 별도의 낮은 확률로 드랍) 초반에 지나치게 좋은 장비가 쏟아지는 것을 막는 구조. "직접 처치했는지" 같은 조건도 드랍에 영향을 줌.
- Fantasia에 적용한다면: 현재 `CombatTestController.GrantLoot()`는 `PossibleLoot` 배열에서 완전 균등 랜덤으로 1개만 뽑는 placeholder 구조. 나중에 아이템에 `ItemCategory`(이미 존재: Consumable/Equipment/Material)별 가중치나 "카테고리당 드랍 확률표"를 얹는 확장을 고려할 수 있음 — 지금 배열 구조를 (아이템, 가중치) 쌍의 테이블로만 바꾸면 됨.

### For the King — 보드 위 파밍과 상점을 통한 장비 순환
- 출처: [For The King Wiki](https://fortheking.wiki.gg/wiki/For_The_King_(game))
- 핵심 아이디어: 전투 루팅뿐 아니라 보드 위 상자/상점/이벤트가 장비 획득 경로로 함께 작동 — 전투가 유일한 아이템 소스가 아님.
- Fantasia에 적용한다면: 이미 `BoardSession.AddItem`이 소스에 무관한 범용 API로 설계돼 있어(코드 주석에도 명시) 이 방향과 잘 맞음 — 나중에 보드 이벤트/상점 시스템을 추가할 때 그대로 재사용 가능.

## 3. 캠프 / 휴식 / 자원 소비 루프

### Pathfinder: Kingmaker의 캠프 시스템 (For the King류 캠프의 참고 사례)
- 출처: [Camping - Pathfinder: Kingmaker Wiki](https://pathfinder-kingmaker.fandom.com/wiki/Camping)
- 핵심 아이디어: 캠프마다 "식량(레이션) 소비" + 파티원에게 사냥/요리/보초/위장 같은 역할을 배정하는 방식. 한 명이 역할을 2개 이상 맡으면 피로가 남아 다음 날 페널티(예: 주문 회복 실패)로 이어짐 — 자원 관리와 캐릭터별 트레이드오프를 동시에 강제.
- Fantasia에 적용한다면: GDD 6.4(캠프)가 아직 TBD인데, "레이션 소비 + 역할 배정 + 배정 부족 시 페널티" 골격은 파티원 수가 고정된(3인 파티) 구조와 잘 맞을 수 있음. 다만 그대로 가져오지 말고 우리 파티 규모/전투 루프에 맞게 단순화 필요.

## 4. 랜덤 이벤트 / 인카운터 구조

### XCOM 2 — 절차적 조합 + 수작업 파츠의 혼합
- 출처: [GDC talk - Plot and Parcel: Procedural Level Design in XCOM 2](https://gdcvault.com/play/1025387/Plot-and-Parcel-Procedural-Level), [Gamedeveloper.com 기사](https://www.gamedeveloper.com/design/video-procedural-level-design-in-i-xcom-2-i-)
- 핵심 아이디어: 완전 수작업도 완전 절차생성도 아니고, 미리 만든 "조각(parcel)"들을 규칙 기반으로 조합해 다양성과 제작 효율을 동시에 확보. 랜덤 요소(Dark Events 등)를 겹쳐서 난이도 예측을 일부러 어렵게 만들되, 너무 여러 개가 동시에 겹치면 밸런스가 깨지는 문제도 있었다고 언급됨(과도한 랜덤 중첩의 위험 사례로 참고).
- Fantasia에 적용한다면: 현재 `HexBoard`의 인카운터/장애물은 타일별 독립 확률 롤(`encounterChance`, `obstacleChance`) 방식. 나중에 텍스트 이벤트를 추가할 때 "완전 랜덤 텍스트 뽑기"보다 "상황별 조각(전투 후/자원 부족/특정 지형)" 태그를 걸어 조합하는 방식을 검토해볼 만함. 랜덤 요소를 겹칠 때는 XCOM 사례처럼 동시 중첩 상한을 두는 것도 고려.

## 5. 절차적 보드 / 맵 생성

### Red Blob Games — 헥사곤 그리드 알고리즘 정리 (게임이 아닌 개발 레퍼런스)
- 출처: [Red Blob Games: Hexagonal Grids](https://www.redblobgames.com/grids/hexagons/)
- 핵심 아이디어: 헥스 좌표계(axial/cube), 이웃 탐색, 거리 계산, 범위(range) 탐색, 경로탐색을 한 곳에 정리한 사실상 업계 표준 참고자료. 여러 언어(C# 포함) 예제 코드 제공.
- Fantasia에 적용한다면: 현재 `HexCoord`/`HexBoard`/이동 범위·경로탐색 로직이 이미 구현돼 있으므로 새로 만들 필요는 없지만, 향후 "범위 스킬"(예: 광역 공격 사거리)이나 "시야(FOV)" 시스템을 추가할 때 이 문서의 range/field-of-view 섹션이 바로 참고가 됨. 좌표계 버그가 의심될 때 대조용 레퍼런스로도 유용.

## 6. 난이도 곡선 / 영속적 손실(퍼머데스) / 리스크-리워드

### Wildermyth — 죽음이 곧 끝이 아닌 퍼머데스 (레거시 시스템)
- 출처: [Legacy - Wildermyth Wiki](https://wildermyth.com/wiki/Legacy), [Steam 토론 - Perma Death of Legacy Heroes](https://steamcommunity.com/app/763890/discussions/0/3768985982141090452/)
- 핵심 아이디어: 캐릭터가 죽어도 그 캠페인에서만 죽는 것이고, "레거시"에 남아 이후 다른 캠페인에서 다시 쓸 수 있음 — 죽음의 심리적 무게(스토리 완결감)와 콘텐츠 재사용(플레이어의 노력이 완전히 사라지지 않음)을 동시에 잡는 절충안.
- Fantasia에 적용한다면: For the King류 게임 특유의 "죽으면 끝" 긴장감과, Wildermyth류 "죽어도 뭔가는 남는다"는 방식은 상반된 톤이라 팀이 어느 쪽을 원하는지 먼저 정해야 함(GDD pillar 미정 항목과 직결). 리서치 단계에서는 두 극단이 있다는 것만 기록해두고, 실제 채택은 기획 확정 이후로 미룸.

### Divinity: Original Sin — 환경 상호작용을 리스크/보상으로 쓰는 예
- 출처: [Original Sin Environmental Effects - Divinity Wiki](https://divinity.fandom.com/wiki/Original_Sin_Environmental_Effects)
- 핵심 아이디어: 기름 바닥 + 화염 스킬 = 불바다, 물 바닥 + 번개 스킬 = 광역 감전처럼, 지형 상태끼리 조합해 예상보다 큰(때로는 아군에게도 위험한) 결과를 만드는 시스템 — 플레이어의 전술적 창의성을 보상하면서 동시에 실수 시 자멸 리스크도 부여.
- Fantasia에 적용한다면: 현재 전투는 슬롯 롤 판정 기반의 비교적 단순한 구조. 지형/상태이상 조합까지 가는 건 스코프가 크지만, "이 타일에 서면 다음 턴 효과가 강화/약화된다" 정도의 단순화된 버전은 헥스 보드 특성과 잘 맞을 수 있음 — 전투 시스템 설계가 확정된 이후 검토.

## 참고: Mario + Rabbids Kingdom Battle
검색으로는 신뢰할 만한 설계 자료(공식 인터뷰/GDC 자료)를 찾지 못해 이번 문서에는 포함하지 않았습니다. 필요하면 추후 별도로 조사해서 추가하는 것을 권장합니다.
