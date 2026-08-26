# Fantasia

유한대학교 게임콘텐츠, 게임엔진, 팀플

3D 턴제 보드게임. *For the King*(헥스 보드 이동, 주사위 판정, 파티 대형 기반 전투, 캠프 사이클)에서 시스템적 영감을 받아 제작 중.

## 문서
- 게임 디자인 문서(뼈대): [`Docs/GDD/GDD.md`](Docs/GDD/GDD.md)
- For the King 시스템 리서치 노트: [`Docs/References/ForTheKing_SystemNotes.md`](Docs/References/ForTheKing_SystemNotes.md)

## Unity 프로젝트 열기 (최초 1회)
이 폴더는 아직 Unity 에디터로 실제 생성된 적이 없습니다. `Packages/`는 없고, `ProjectSettings/EditorSettings.asset` 한 파일만 미리 세팅해둔 상태입니다 (아래 참고).

1. Unity Hub → **New Project**
2. 템플릿: **3D (URP)** 권장 (스타일라이즈드 3D + 성능 확보에 유리)
3. Project Name: `fantasia`
4. Location: `C:\Obsidian\Game` (이 폴더의 부모 경로)
   - 이미 `fantasia` 폴더가 존재하고 `Assets/`, `ProjectSettings/` 등이 일부 채워져 있어도 Unity가 그대로 인식해서 `Packages/`, `Library/` 등 나머지를 추가로 생성합니다.
   - 단, Unity 버전에 따라 템플릿이 기존 `ProjectSettings/` 내용을 덮어쓸 수도 있습니다 — 아래 4번 확인 필수.
5. 생성 후 기본 템플릿이 넣어준 `Assets/Scenes/SampleScene` 등은 정리하고, 아래 `Assets/_Project` 구조를 기준으로 작업하세요.
6. **필수 확인**: `Edit > Project Settings > Editor`에서 **Asset Serialization = Force Text**, **Version Control Mode = Visible Meta Files**로 되어 있는지 확인하세요. 미리 세팅해뒀지만 프로젝트 생성 과정에서 덮어써졌을 수 있습니다. 값이 바뀌어 있었다면 다시 맞추고 `git add ProjectSettings/EditorSettings.asset && git commit`으로 커밋해주세요.

## 폴더 구조

```
fantasia/
├── Assets/
│   ├── _Project/              # 우리가 만드는 모든 콘텐츠 (언더스코어로 최상단 정렬)
│   │   ├── Scripts/
│   │   │   ├── Core/          # 게임/씬 매니저, 상태 머신, 부트스트랩
│   │   │   ├── Board/         # 헥스 보드, 이동, POI, 챕터 진행
│   │   │   ├── Combat/        # 턴 순서, 액션, 포커스 리소스
│   │   │   ├── Dice/          # 범용 d100 판정 모듈 (전투/이벤트/캠프 공용)
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
│   └── Concept/
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

## 협업 / 버전 관리
- 기본 브랜치: `main`.
- 팀원 각자 로컬에서 `git config --global user.name/user.email` 설정 필요.
- **바이너리 에셋을 추가하기 전에** 저장소 안에서 `git lfs install`을 한 번 실행하세요. `.gitattributes`에 모델/텍스처/오디오용 LFS 패턴이 이미 설정되어 있습니다 (Unity 프로젝트에서 바이너리를 LFS 없이 먼저 커밋하면 나중에 히스토리 재작성 없이는 되돌리기 어렵습니다).
  - Public 저장소 + 무료 플랜 조합이라면 GitHub 기준 LFS 무료 할당량이 저장소당 월 1GB(용량/대역폭)입니다. 팀 작업으로 3D 에셋이 늘어나면 데이터 팩 구매/플랜 전환이 필요할 수 있습니다.
- 라이선스: [`LICENSE`](LICENSE) — GPLv3.
- Force Text / Visible Meta Files 설정은 위 "Unity 프로젝트 열기" 6번 참고.
