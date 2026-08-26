# Fantasia

3D 턴제 보드게임. *For the King*(헥스 보드 이동, 주사위 판정, 파티 대형 기반 전투, 캠프 사이클)에서 시스템적 영감을 받아 제작 중.

## 문서
- 게임 디자인 문서(뼈대): [`Docs/GDD/GDD.md`](Docs/GDD/GDD.md)
- For the King 시스템 리서치 노트: [`Docs/References/ForTheKing_SystemNotes.md`](Docs/References/ForTheKing_SystemNotes.md)

## Unity 프로젝트 열기 (최초 1회)
이 폴더는 아직 `ProjectSettings/`, `Packages/`가 없는 상태입니다 (Unity 에디터가 생성하는 파일이라 미리 만들지 않았습니다).

1. Unity Hub → **New Project**
2. 템플릿: **3D (URP)** 권장 (스타일라이즈드 3D + 성능 확보에 유리)
3. Project Name: `fantasia`
4. Location: `C:\Obsidian\Game` (이 폴더의 부모 경로)
   - 이미 `fantasia` 폴더가 존재하고 `Assets/` 등이 채워져 있어도 Unity가 그대로 인식해서 `ProjectSettings/`, `Packages/`, `Library/` 등을 추가로 생성합니다.
5. 생성 후 기본 템플릿이 넣어준 `Assets/Scenes/SampleScene` 등은 정리하고, 아래 `Assets/_Project` 구조를 기준으로 작업하세요.

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
├── .gitattributes               # meta 파일 텍스트 처리 + Git LFS 템플릿(주석 처리됨)
└── README.md
```

`Docs/`는 `Assets/` 바깥(프로젝트 루트)에 있어 Unity 에디터가 임포트하지 않습니다.

## 스크립트 아키텍처 메모
- `Assets/_Project/Scripts/`에 `Fantasia.Scripts` 런타임 어셈블리 정의(asmdef)를 배치해 컴파일 시간을 단축했습니다.
- `Scripts/Editor/`는 별도 `Fantasia.Scripts.Editor` asmdef로 분리(Editor 플랫폼 전용) — 런타임 빌드에 에디터 코드가 섞이지 않도록 함.
- 기능이 늘어나면 `Board`, `Combat` 등 모듈별로 asmdef를 추가로 쪼개는 것을 권장 (현재는 단일 어셈블리로 시작, 과도한 초기 분리는 지양).
- 게임 데이터(캐릭터 스탯, 아이템, 이벤트 등)는 하드코딩 대신 `Data/` 아래 ScriptableObject로 관리 — 디자이너가 코드 수정 없이 콘텐츠 추가 가능.

## 버전 관리
- Git 저장소로 초기화되어 있습니다 (기본 브랜치: `main`).
- **최초 커밋 전 필수**: 이 머신에 git 사용자 정보가 없다면 아래를 한 번 실행하세요.
  ```
  git config --global user.name "Your Name"
  git config --global user.email "you@example.com"
  ```
- **바이너리 에셋을 추가하기 전에** 저장소 안에서 `git lfs install`을 한 번 실행하세요. `.gitattributes`에 모델/텍스처/오디오용 LFS 패턴을 이미 설정해뒀습니다 (Unity 프로젝트를 git 서버에 올릴 때 바이너리를 LFS 없이 먼저 커밋하면 나중에 히스토리 재작성 없이는 되돌리기 어렵습니다).
  - Public 저장소 + 무료 플랜 조합이라면 GitHub 기준 LFS 무료 할당량이 저장소당 월 1GB(용량/대역폭)로 꽤 빠듯합니다. 3D 에셋이 늘어나면 LFS 데이터 팩 구매나 유료 플랜 전환이 필요할 수 있다는 점 참고하세요.
- 라이선스: `LICENSE` 파일에 All Rights Reserved로 명시되어 있습니다 — 저장소는 공개(public)지만 코드/에셋의 재사용·배포는 명시적 허가 없이는 불가합니다.

## 원격 저장소(Git 서버) 연결
로컬 준비가 끝나면 GitHub/GitLab 등에서 **빈 저장소**(README/gitignore/license 자동 생성 옵션 전부 끄기 — 이미 로컬에 있음)를 만든 뒤:
```
git remote add origin <저장소 URL>
git push -u origin main
```
