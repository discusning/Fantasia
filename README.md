# Fantasia (판타지아, 가제)

유한대학교 게임콘텐츠 · 게임엔진 팀 프로젝트 — Unity로 만드는 3D 턴제 보드 RPG

> **1774년 런던에서 시작하는 18세기 오컬트 판타지.** 탐정 사무소의 명탐정이 연쇄 살인사건을 쫓다
> 영생을 위해 "잭 더 리퍼"를 불러내려는 교단 **붉은 밤**과 마주한다.
> 런던 → 베르사유 → 로마 제국 3막. (모티브: *For the King* — 육각형 칸 보드 + 확률 칸 판정 전투)

## 게임 한눈에 보기

| 메인 메뉴 | 파티 구성 |
|---|---|
| ![메인 메뉴](Docs/Images/README/main_menu.png) | ![파티 구성](Docs/Images/README/party_select.png) |

| 보드 (지도 이동) | 전투 |
|---|---|
| ![보드](Docs/Images/README/board.png) | ![전투](Docs/Images/README/battle.png) |

| 항목 | 내용 |
|---|---|
| 플레이 | 혼자서 3명 파티를 움직임 — 1차 직업: **명탐정 · 역병 의사 · 저격수** |
| 보드 | 주사위(1~6)를 굴려 육각형 칸을 이동. 아군 턴 → 적 턴이 번갈아 진행 |
| 칸 종류 | 적(주황) · 보스 · 거점(상점, 파랑) · 이벤트(보라) · 회복(초록) · 보물(노랑) |
| 조우 | 적 칸에 가면 싸우기 / 기습 / 몰래 지나가기 / 후퇴 중 선택 |
| 전투 | 속도가 빠른 순서로 1번씩 행동. 공격은 무기의 확률 칸(2~4칸)을 굴리고, 전부 성공하면 크리티컬 |
| 자원 | HP(체력) · MP(마력) · 집중력(확률 칸 1개 성공 고정) · **LP(부활 횟수)** — LP가 없을 때 쓰러지면 영구 사망 |
| 1막 목표 | 런던 보스 **검시관 아벨 모로**를 쓰러뜨리고 다음 지역 단서를 찾기 |

| 상점 (거점) | 회복 칸 | 십자가 석상 (기도) |
|---|---|---|
| ![상점](Docs/Images/README/shop.png) | ![회복 칸](Docs/Images/README/recovery_tile.png) | ![석상](Docs/Images/README/statue.png) |

## 지금 상태 (2026-10-26)

- **1막(런던)은 처음부터 끝까지 플레이 가능** — 새 게임 → 런던 탐험 → 보스 → 1막 클리어
- 들어간 것: 메인 메뉴·설정, 자동 저장·이어하기, 상점, 적 칸 10곳, 회복·보물 칸, 점술마녀·분수대, 십자가 석상 기도, 피의 사냥, Esc 메뉴
- 아직 임시: 캐릭터·적 모양(단색 인형), 음악·효과음 일부, 대사 일부 — 아트·스토리 자료가 오면 바꿈
- 날짜별 진행: [`Docs/진행일지/`](Docs/진행일지/) · 남은 일: [작업 계획](Docs/DevPlan/WorkPlan_M0-M1.md)

## 문서

| 문서 | 내용 |
|---|---|
| [`Docs/Planning/System.md`](Docs/Planning/System.md) | **시스템 기획 원문** — 수치는 이 문서가 기준 |
| [`Docs/Planning/Story.md`](Docs/Planning/Story.md) | 스토리 기획 원문 (1~3막) |
| [`Docs/Planning/Decisions.md`](Docs/Planning/Decisions.md) | **원문 이후 정해진 것** — 원문과 다르면 이 문서를 따름 |
| `Docs/Planning/` 의 다른 문서 | 주제별 확정안 (보스·저장·속도·지도 칸·이벤트 칸 등) — 기획 원문 + 개발 반영 내용 |
| [`Docs/DevPlan/WorkPlan_M0-M1.md`](Docs/DevPlan/WorkPlan_M0-M1.md) | 개발 작업 계획 · 기획 확인 필요 항목 |
| [`Docs/진행일지/`](Docs/진행일지/) | 날짜별 작업 요약 |

## 게임 실행

1. Unity **6000.5.9f1** 설치 → Unity Hub → Add → `fantasia` 폴더
2. **글꼴 넣기 (필수 아님, 권장)**: 팀 글꼴 `ZEN-SERIF-TTF-Regular.ttf`를 `Assets/_Project/Resources/Fonts/`에 넣는다
   - 글꼴 라이선스가 재배포 금지라 저장소에는 없음 (팀 공유 폴더에서 받기). 없으면 기본 글꼴로 실행됨
3. `Scenes/MainMenu/MainMenu` 씬에서 Play, 또는 메뉴 `Fantasia > Build > Windows Test Build`로 만든 `Builds/Windows/Fantasia.exe` 실행

**조작**: Space 주사위 → 밝은 칸 클릭으로 이동 · I 인벤토리 · Q 사건 기록 · B 상점(거점에서) · Esc 메뉴 · WASD·마우스 휠 카메라

## 아트 넣는 곳 (코드 수정 없이 교체)

| 넣을 것 | 위치 |
|---|---|
| 창 테두리·버튼·게이지 등 UI 그림 | `Assets/_Project/Resources/UITheme.asset` |
| 캐릭터·적 3D 모델 | 직업·적 데이터의 **Model** 칸 |
| 2D 초상화 (넣으면 3D보다 우선) | 직업·적 데이터의 **Portrait** 칸 |
| 상태이상·스킬 아이콘 | 상태·스킬 데이터의 **Icon** 칸 |

비어 있으면 단색 도형과 임시 인형으로 보인다.

## 협업 규칙

- **문서는 누구나 이해할 수 있는 쉬운 말로 쓴다.** 어떤 기능인지 말로 헷갈리면 게임 스크린샷을 붙인다. 커밋 메시지·이슈·PR도 같다.
- 받은 기획서에서 어느 화면·기능인지 애매하면 짐작하지 말고 스크린샷을 붙여 확인한다. 해석해서 넣은 부분은 "해석"으로 표시한다.
- 수치는 `System.md` → `Decisions.md` 순서로 따른다.
- 코드는 게임에서 동작을 확인한 뒤 커밋한다. 기본 브랜치 `main` · 라이선스 [GPLv3](LICENSE)
- 그림·소리 파일은 Git LFS로 올라간다 (처음 한 번 `git lfs install`).
- 씬 파일이 내부 번호만 바뀌었으면 커밋하지 않는다 (`git checkout -- <파일>`).
