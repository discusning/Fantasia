# 문서 안내 (Docs)

> 2026-11-06 정리. 무엇이 어디 있는지, **지금 기준이 되는 문서**가 무엇인지 한 곳에 모음.
> 새 문서를 만들면 여기에 한 줄 추가.

## 1. 먼저 볼 것 (지금 기준)

| 문서 | 내용 |
|---|---|
| [`Planning/현행기획서_1막.md`](Planning/현행기획서_1막.md) | **1막 현행 기획서 — 가장 먼저 따름.** 원문·결정·실제 게임 값을 한 문서에 |
| [`Planning/Decisions.md`](Planning/Decisions.md) | 날짜별로 정해진 것 (바뀌면 여기 한 줄 + 현행 기획서 수정) |
| [`진행일지/`](진행일지/) | 날짜별 개발 작업 · 다음 할 일 |
| [`DevPlan/WorkPlan_M0-M1.md`](DevPlan/WorkPlan_M0-M1.md) | 개발 작업 계획 |

규칙 우선순위: **현행 기획서 → Decisions → 주제별 문서 → System.md 원문**

## 2. 기획 (`Planning/`)

| 종류 | 문서 |
|---|---|
| 원문 | `System.md` (시스템) · `Story.md` (스토리) · `GDD/GDD.md` (개요) |
| 주제별 | `Boss_Abel_v1.0.md` 보스 · `City.md`·`City_Shop.md` 거점·상점 · `EventTiles.md` 이벤트 칸 · `MapTiles.md` 지도 칸 · `WorldMap.md` 지도 · `Save.md` 저장 · `Speed.md` 속도 · `MainMenu_Proposal.md` 메뉴 |
| 기획 → 개발 (받은 것) | `UI_Solutions_Q1-Q9_2026-10-27.pdf` · `Fantasia_1막_기획_결정서_2026-11-01.pdf` · `판타지아_1막_게임기획_보고서_2026-11-03.pdf` · `Boss/…수정요청서.pdf` |
| 개발 → 기획 (보낸 것) | `PlannerRequests_2026-10-24.md` · `Fantasia_기획_리소스_대기목록_2026-10-25` · `Request_1030/` · `Request_1103/` · `Report_1103/` |

## 3. 아트·사운드 요청 (`Art/`, `Audio/`)

| 날짜 | 문서 | 상태 |
|---|---|---|
| 10/25 | `Art/Fantasia_에셋_UI_요청목록_2026-10-25` | 지난 요청 |
| 10/27 | `Art/Fantasia_UI_요청서_2026-10-27` | 지난 요청 |
| 10/28 | `Art/Design_Request_1028/` UI 그림 요청 | 일부 대기 (창·버튼 N08·N14~N17) |
| 11/3 | `Art/PanelDrop_1028/` 턴·승패 판 애니메이션 | 적용 완료 |
| 11/4 | `Audio/Fantasia_사운드_요청서_2026-11-04` | 대기 (11/7 목표) |
| 11/5 | `Art/Model_Request_1105/` 3D 에셋 요청 | 받음 · 적용 완료 (거미 대기) |
| 11/6 | `Art/Animation_Request_1106/` 움직임 요청 | 대기 (11/7 Idle) |
| 11/6 | `Art/InGame_Report_1106/` 에셋 적용 화면 보고서 | 공유함 |

- `Art/AssetList.md` — 필요한 에셋 전체 목록
- `Art/UI_Reference/` — 받은 UI 참고 그림 · `Art/Fantasia_Icon_Usage_Guide.pdf` — 아이콘 사용법

## 4. 그림 자료

| 폴더 | 내용 |
|---|---|
| `Images/README/` | 저장소 첫 화면(README) 스크린샷 |
| `Images/UI/` | 화면별 UI 스크린샷 모음 (메뉴·보드·전투·상점 등 10개 묶음) |
| `Concept_Image/` | 초기 콘셉트 그림 · 참고 게임 스크린샷 |

## 5. 참고 조사 (`References/`)

For the King 시스템 메모, 보드 턴 UI·지형 UI 조사, Unity 최적화·에셋 메모.

## 6. 게임 쪽 파일 위치 (참고)

| 무엇 | 어디 |
|---|---|
| 받은 원본 묶음 그대로 (unitypackage 등, git 제외·로컬만) | `ArtSource/` (날짜별 폴더, 안내 `ArtSource/README.md`) |
| 게임에 쓰는 3D 모델 | `Assets/_Project/Art/Models/` (인물·건물·칸·소품) |
| 게임용으로 맞춘 인물 | `Assets/_Project/Art/Prefabs/` |
| 게임용 보드 칸·구조물 | `Assets/_Project/Resources/BoardArt/` |
| UI 그림 | `Assets/_Project/UI/Sprites/` → `Resources/UITheme.asset`에 연결 |
| 3D 에셋 다시 맞추기 | Unity 메뉴 `Fantasia > Art > Set Up Received 3D Assets` |
