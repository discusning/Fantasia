# Fantasia (판타지아, 가제)

유한대학교 게임콘텐츠 · 게임엔진 팀 프로젝트 — Unity 3D 턴제 보드 RPG (로그라이크)

> **1774년 런던에서 시작하는 18세기 오컬트 판타지.** 탐정 사무소의 명탐정이 연쇄 살인사건을 쫓다
> 영생을 위해 "잭 더 리퍼"의 강림을 준비하는 교단 **붉은 밤**의 영혼 수확망과 마주한다.
> 런던 → 베르사유 → 로마 제국 3막, 열린 결말. (모티브: *For the King* — 헥스 보드 + 슬롯 판정 전투)

## 게임 한눈에 보기

| 항목 | 내용 |
|---|---|
| 플레이 | 솔로 1명이 3인 파티 조작 — 1차 직업: **명탐정 · 역병 의사 · 저격수** (추가 5직업은 후속) |
| 보드 | 주사위 1~6 이동, 인카운터·이벤트 칸, 도시 거점 |
| 조우 | 싸우기 / 기습(인지) / 몰래 지나가기(속도) / 후퇴 |
| 전투 | 아군·적 통합 **속도 큐**, 무기 **슬롯 판정**(2·3·4슬롯, 전부 성공 = 크리티컬 → 방어 무시 2배), 집중력으로 슬롯 1개 확정 |
| 자원 | HP · MP · 집중력 · **LP(부활 횟수)** — LP 0에서 HP 0이면 영구 사망 |
| 성장 | 레벨 1~10, 무기 10계열 × 등급 6단계, 상태이상·직업 스킬 |
| 1막 목표 | 새 게임부터 런던 보스 **검시관 아벨 모로** 처치 + 다음 지역 단서 확보까지 |

## 문서

| 문서 | 내용 |
|---|---|
| [`Docs/GDD/GDD.md`](Docs/GDD/GDD.md) | 게임 디자인 요약 (기획서 기준) |
| [`Docs/Planning/System.md`](Docs/Planning/System.md) | **시스템 기획 원문** (2026-10-21 확정 — 수치는 이 문서가 기준) |
| [`Docs/Planning/Story.md`](Docs/Planning/Story.md) | 스토리 기획 원문 (1~3막) |
| [`Docs/DevPlan/WorkPlan_M0-M1.md`](Docs/DevPlan/WorkPlan_M0-M1.md) | 개발 작업 계획·진행 기록·기획 확인 필요 항목 |
| [`Docs/진행일지/`](Docs/진행일지/) | 날짜별 작업 요약 |
| [`Docs/References/`](Docs/References/) | 리서치 자료 (모티브 참고용 — 그대로 복제 금지) |

## 개발 진행 현황 (개발 업무 D01~D30 기준)

| 상태 | 업무 |
|---|---|
| ✅ 구현·검증 | D03 데이터 구조·검증 · D04 파티·자원·레벨 · D08 조우 판정 · D09 전투 순서 · D10 슬롯·피해·회피 · D11 3직업 스킬 12개 · D12 상태이상 1차(9종) · D13 1막 소형 적 4종 |
| 🟡 일부 | D01 (테스트 빌드 ✅, URP 미정) · D05/D06 (규칙만 — 보드 맵·파티 이동 방식 기획 확인 중) · D26 (전투·조우·파티 UI) · D29 (전투 로그·자동 플레이·빌드) |
| ⬜ 다음 | D21 쓰러짐·부활 → D19 전리품·경험치 → D17 인벤토리·소모품 → D14 1막 보스 → D20 상점 · D22 퀘스트 · D27 저장 |

자세한 내용과 기획 확인 대기 항목은 [작업 계획서](Docs/DevPlan/WorkPlan_M0-M1.md) 참고.

## 실행·테스트

**프로젝트 열기**: Unity **6000.5.9f1** 설치 → Unity Hub → Add → `fantasia` 폴더. (Built-in 렌더 파이프라인, URP 전환은 미정)

**플레이**: `Overworld/BoardTest` 씬 Play → **Space** 주사위 → 초록 칸 클릭 이동 → 주황(인카운터) 칸 도착 시 조우 창 → 전투 씬 → 승리 후 보드 복귀.
씬 내용이 이상하면 메뉴 `Fantasia > Setup Board Test Scene` / `Setup Combat Test Scene`으로 다시 만든다 (기존 씬 덮어씀).

**Fantasia 메뉴**

| 메뉴 | 용도 |
|---|---|
| `Data > Seed Sample Game Data` | 기획서 수치로 직업·무기·적·스킬·상태 데이터 생성/갱신 |
| `Data > Validate Game Data` | 필수값·중복 ID·잘못된 참조 검사 |
| `Data > Run ... Self-Test` | 전투 계산·순서·상태·스킬·조우 등을 System.md 수치와 대조 (콘솔에 "전부 통과") |
| `Build > Windows Test Build` | `Builds/Windows/Fantasia.exe` 생성 |

**자동 플레이 점검**: `Builds/Windows/Fantasia.exe -autoplay -autoplay-out <폴더> [-autoplay-enemy <적 Id>]`
→ 보드 이동부터 전투·복귀까지 자동 진행하고 단계별 스크린샷 + 전투 로그(`autoplay_log.txt`)를 남긴다.

## 아트 넣는 곳 (코드 수정 없이 교체)

| 넣을 것 | 위치 |
|---|---|
| 패널·배너·버튼·게이지·슬롯·집중 칸·행동 아이콘·대상 표시·한글 글꼴 | `Assets/_Project/Resources/UITheme.asset` |
| 캐릭터·적 3D 모델 (전투 필드·초상 공용, 정면 +Z) | 직업·적 데이터의 **Model** |
| 2D 초상 (넣으면 3D보다 우선) | 직업·적 데이터의 **Portrait** |
| 상태이상·스킬 아이콘 | 상태·스킬 데이터의 **Icon** |

비어 있으면 단색 도형과 임시 인형으로 그린다.

## 폴더 구조

```
Assets/_Project/
├── Scripts/
│   ├── Data/      # 데이터 정의(직업·적·무기·스킬·상태) + 파티·상태 런타임
│   ├── Combat/    # 전투 진행·순서·판정·적 AI
│   ├── World/     # 이동·월드 시계·조우 규칙
│   ├── Board/     # 헥스 보드·보드 테스트
│   ├── UI/        # 조우 창·전투 HUD·파티 HUD·초상·테마
│   ├── Core/      # 세션(씬 간 유지)·자동 플레이 점검
│   └── Editor/    # 시드·검증·셀프테스트·씬 생성·빌드
├── Data/          # ScriptableObject 데이터 자산 (Jobs/Weapons/Enemies/Skills/StatusEffects)
├── Resources/     # UITheme
└── Scenes/        # Overworld/BoardTest · Combat/CombatTest · Dialogue/DialogueTest
Docs/              # 기획·계획·리서치 (Unity 임포트 대상 아님)
```

## 협업 규칙

- 기본 브랜치 `main` · 라이선스 [GPLv3](LICENSE)
- 바이너리 에셋 추가 전 `git lfs install` (`.gitattributes`에 LFS 패턴 설정됨)
- **fileID만 바뀐 씬 diff는 커밋하지 않는다** — `git diff`로 오브젝트·값이 같으면 `git checkout -- <파일>`
- 코드는 에디터·게임에서 동작 확인 후 커밋
- 수치는 `Docs/Planning/System.md`가 기준 — 다른 문서와 다르면 System.md를 따른다
