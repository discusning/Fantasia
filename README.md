# Fantasia

유한대학교 게임콘텐츠, 게임엔진, 팀플

3D 턴제 보드게임. *For the King*(헥스 보드 이동, 주사위 판정, 파티 대형 기반 전투, 캠프 사이클)에서 시스템적 영감을 받아 제작 중.

## 문서
- 게임 디자인 문서(뼈대): [`Docs/GDD/GDD.md`](Docs/GDD/GDD.md)
- For the King 시스템 리서치 노트: [`Docs/References/ForTheKing_SystemNotes.md`](Docs/References/ForTheKing_SystemNotes.md)

## Unity 프로젝트 열기
프로젝트는 이미 생성되어 저장소에 커밋되어 있습니다 (Unity **6000.5.9f1**, 명령줄 배치 모드로 생성). 팀원은 새로 만들 필요 없이 그대로 열면 됩니다.

1. Unity Hub 설치 후, Editor 버전 **6000.5.9f1** 설치 (다른 버전으로 열면 `Library/`가 재생성되며 임포트 시간이 오래 걸릴 수 있습니다).
2. Unity Hub → **Add** → 클론한 `fantasia` 폴더 선택 → 프로젝트 열기.
3. 최초 오픈 시 에셋 임포트가 진행됩니다 (`Library/`는 gitignore 대상이라 로컬에서 새로 생성됨).
4. 기본값 확인: **Asset Serialization = Force Text**가 기본으로 적용되어 있습니다 (`ProjectSettings/EditorSettings.asset`). 이 Unity 버전부터는 meta 파일이 항상 보이는 방식(Visible Meta Files)만 지원하므로 별도 설정이 필요 없습니다.
5. **렌더 파이프라인 미정 상태**: 현재는 Built-in Render Pipeline으로 생성되어 있고, URP는 아직 패키지로 추가하지 않았습니다. URP로 전환하기로 확정되면 Package Manager에서 `Universal RP` 설치 후 `Edit > Render Pipeline > Universal Render Pipeline > Upgrade Project Materials`로 마이그레이션하세요.
6. 기본 씬(`Assets/Scenes/SampleScene` 등)이 남아있다면 정리하고, `Assets/_Project` 구조를 기준으로 작업하세요.

## 폴더 구조

```
fantasia/
├── Assets/
│   ├── _Project/              # 우리가 만드는 모든 콘텐츠 (언더스코어로 최상단 정렬)
│   │   ├── Scripts/
│   │   │   ├── Core/          # 게임/씬 매니저, 상태 머신, 부트스트랩
│   │   │   ├── Board/         # 헥스 보드, 이동, POI, 챕터 진행
│   │   │   ├── Combat/        # 턴 순서, 액션, 포커스 리소스
│   │   │   ├── Dice/          # 범용 확률 판정 모듈 (전투/이벤트/캠프 공용)
│   │   │   ├── Characters/    # 파티원 스탯/성장
│   │   │   ├── Items/         # 인벤토리, 장비, 상점
│   │   │   ├── Events/        # 텍스트 이벤트/인카운터
│   │   │   ├── Camp/          # 야영, 자원 소비
│   │   │   ├── Quests/        # 퀘스트/챕터 목표
│   │   │   ├── SaveSystem/    # 세이브/로드
│   │   │   ├── UI/            # UI 로직
│   │   │   ├── Utils/         # 공용 유틸리티
│   │   │   └── Editor/        # 커스텀 에디터 툴 (Editor 전용 asmdef)
│   │   ├── Prefabs/           # Characters / Enemies / Environment / UI / VFX
│   │   ├── Art/                # Models / Materials / Textures / Animations
│   │   ├── Audio/              # Music / SFX / Voice
│   │   ├── Scenes/             # MainMenu / Overworld / Combat / Town
│   │   ├── Data/                # ScriptableObject 데이터 (Characters/Items/Enemies/Events/Boards)
│   │   └── UI/                  # Fonts / Sprites
│   ├── Plugins/                # 네이티브/서드파티 플러그인
│   └── ThirdParty/             # 에셋스토어 등 외부 에셋 (직접 수정 X)
├── Docs/                        # 기획 문서 (Unity에는 임포트되지 않음, 루트에 위치)
│   ├── GDD/
│   ├── References/
│   └── Concept/                 # 컨셉/참고 이미지
│       ├── Concept/             # 자체 제작 컨셉 아트
│       └── Images/              # 외부 참고 자료 이미지
├── .gitignore                   # Unity 표준 gitignore
├── .gitattributes               # meta 파일 텍스트 처리 + Git LFS 패턴
└── README.md
```

`Docs/`는 `Assets/` 바깥(프로젝트 루트)에 있어 Unity 에디터가 임포트하지 않습니다.

## 스크립트 아키텍처 메모
- `Assets/_Project/Scripts/`에 `Fantasia.Scripts` 런타임 어셈블리 정의(asmdef)를 배치해 컴파일 시간을 단축했습니다.
- `Scripts/Editor/`는 별도 `Fantasia.Scripts.Editor` asmdef로 분리(Editor 플랫폼 전용) — 런타임 빌드에 에디터 코드가 섞이지 않도록 함.
- 기능이 늘어나면 `Board`, `Combat` 등 모듈별로 asmdef를 추가로 쪼개는 것을 권장 (현재는 단일 어셈블리로 시작, 과도한 초기 분리는 지양).
- 게임 데이터(캐릭터 스탯, 아이템, 이벤트 등)는 하드코딩 대신 `Data/` 아래 ScriptableObject로 관리 — 디자이너가 코드 수정 없이 콘텐츠 추가 가능.

## 구현 현황 / 테스트 방법
아직 아트/애니메이션 없이 로직만 먼저 만드는 단계입니다. Unity Editor 상단 메뉴 **Fantasia**에서 실행:

- **Setup Board Test Scene** — 헥스 보드(`Scripts/Board`) 생성. Play 후 Space로 주사위 굴려 이동 범위(초록) 확인, 클릭하면 굴린 만큼의 타일을 한 칸씩 밟으며 이동(순간이동 아님). 어두운 타일은 진입 불가(장애물, `HexBoard.obstacleChance`). **I 키**로 스테이터스/인벤토리 창(`Scripts/UI/StatusInventoryPanel.cs`, 참고: `Docs/Concept/Images/Fantasia_Status_Inventory.png`) 토글 — 캐릭1/2/3 탭으로 캐릭터 전환.
- **Setup Combat Test Scene** — 파티(파랑) vs 적(빨강) 3:3 전투 프로토타입(`Scripts/Combat`). Speed 기반 턴 큐 + 무기 슬롯 롤 판정을 화면 버튼(공격/포커스 소모/턴 넘기기)으로 직접 테스트 가능. 오버월드와 다른 로우앵글 대치 구도(참고: `Docs/Concept/Concept/판타지아 전투 화면.jpg`).
- **Run Combat Slot-Roll Self-Test** / **Run Turn Queue Self-Test** / **Run Pathfinding Self-Test** — 각각 슬롯 판정 확률, 자동 전투 진행, 이동 경로 계산을 화면 없이 시뮬레이션해 콘솔에 결과 출력.

두 테스트 씬 모두 화면 우상단에 씬 전환 버튼(`Scripts/Core/DevSceneNav.cs`)이 있어 Play 중에 오버월드 ↔ 전투 화면을 자유롭게 오갈 수 있습니다. 시작 씬은 `Overworld/BoardTest`(Build Settings 0번)입니다.

각 `Setup ...` 메뉴는 해당 테스트 씬을 처음부터 다시 만듭니다(기존 씬 내용 덮어씀) — 씬 안에서 수동으로 손댄 게 있다면 재실행 전 백업하세요.

캐릭터/아이템 데이터는 아직 실제 기획값이 아니라 UI 작업용 placeholder입니다 (`Data/Characters`, `Data/Items`, `Scripts/Editor/PlaceholderDataSetup.cs`에서 생성). Canvas UI를 쓰기 위해 `com.unity.ugui` 패키지를 추가했습니다(기본 명령줄 프로젝트 생성 시 빠져 있었음).

## 협업 / 버전 관리
- 기본 브랜치: `main`.
- 팀원 각자 로컬에서 `git config --global user.name/user.email` 설정 필요.
- **바이너리 에셋을 추가하기 전에** 저장소 안에서 `git lfs install`을 한 번 실행하세요. `.gitattributes`에 모델/텍스처/오디오용 LFS 패턴이 이미 설정되어 있습니다 (Unity 프로젝트에서 바이너리를 LFS 없이 먼저 커밋하면 나중에 히스토리 재작성 없이는 되돌리기 어렵습니다).
  - Public 저장소 + 무료 플랜 조합이라면 GitHub 기준 LFS 무료 할당량이 저장소당 월 1GB(용량/대역폭)입니다. 팀 작업으로 3D 에셋이 늘어나면 데이터 팩 구매/플랜 전환이 필요할 수 있습니다.
- 라이선스: [`LICENSE`](LICENSE) — GPLv3.
- Force Text / meta 파일 설정은 위 "Unity 프로젝트 열기" 4번 참고.
