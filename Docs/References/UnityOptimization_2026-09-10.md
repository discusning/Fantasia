# Unity 개발 / 최적화 자료조사 (2026-09-10)

> 목적: 저사양 GPU 크래시 방지를 위해 `Application.targetFrameRate = 60`을 건 것을 계기로, 지금 프로젝트 구조(런타임 코드로 uGUI 생성, 절차적 헥스보드, Built-in RP, 3D 오브젝트 다수)에 맞는 최적화 기법을 미리 조사해 둔 문서입니다. 지금 당장 전부 적용하라는 뜻이 아니라, 나중에 실제로 무거워지는 지점이 보이면 참고할 후보 목록입니다.

## 1. 드로우콜 배칭 — SRP Batcher / GPU Instancing

- 요약: SRP Batcher는 드로우콜 "개수"를 줄이는 게 아니라 같은 셰이더 변형(shader variant)을 쓰는 드로우콜 사이의 렌더 상태 전환 비용을 줄이는 방식이고, GPU Instancing보다 우선 적용됨. 반복되는 메쉬(헥스 타일 등)가 많을 때 특히 유효.
- 출처: [Unity Manual - Scriptable Render Pipeline Batcher](https://docs.unity3d.com/Manual/SRPBatcher.html), [SRP Batcher vs GPU Instancing 토론 - Unity Discussions](https://discussions.unity.com/t/srp-batcher-vs-gpu-instancing/799622)
- Fantasia 적용 지점: `HexBoard` 생성 로직(현재 구현 파일 확인 필요 — 타일마다 같은 머티리얼/셰이더를 쓰는지 먼저 점검). 다만 **SRP Batcher는 SRP(URP/HDRP) 전용**이라 지금의 Built-in RP에서는 적용되지 않음 — URP 전환 여부와 묶어서 판단해야 함(아래 5번 참고).

## 2. LOD / Occlusion Culling

- 요약: LOD는 카메라와의 거리에 따라 낮은 폴리곤 버전을 렌더링, Occlusion Culling은 카메라 시야에서 가려진 오브젝트를 아예 그리지 않음. 두 기능은 서로 영향을 주므로(LOD0 실루엣이 다른 레벨과 크게 다르면 정적 오클루더로 부적합) 함께 설정 시 주의 필요.
- 출처: [Unity Manual - Level of Detail (LOD)](https://docs.unity3d.com/2022.2/Documentation/Manual/LevelOfDetail.html), [Unity Manual - Getting started with occlusion culling](https://docs.unity3d.com/Manual/occlusion-culling-getting-started.html)
- Fantasia 적용 지점: 지금은 아트/모델이 거의 placeholder 단계라 당장은 이르지만, 실제 3D 모델(캐릭터/환경 에셋, `Docs/참고자료/UnityAssets.md`에 정리해둔 후보들)이 들어오면 보드 전체를 한 화면에 담는 오버월드 씬 특성상 Occlusion Culling보다는 LOD가 먼저 체감 효과가 클 가능성이 높음.

## 3. 텍스처 압축 / 임포트 설정

- 요약: Max Size를 실제 화면에 필요한 만큼만 낮추고, Read/Write Enabled는 꺼두기(켜두면 CPU/GPU 양쪽에 복사본이 생겨 메모리 2배), 플랫폼별로 압축 포맷을 오버라이드(안드로이드는 ETC2, 광범위 지원은 ASTC)하는 것이 기본 원칙.
- 출처: [Unity - Optimize your mobile game performance](https://unity.com/blog/games/optimize-your-mobile-game-performance-expert-tips-on-graphics-and-assets), [Unity Manual - Texture compression formats](https://docs.unity3d.com/2020.1/Documentation/Manual/class-TextureImporterOverride.html)
- Fantasia 적용 지점: 지금은 PC(에디터) 타겟으로 보이므로 모바일 압축 포맷 얘기는 아직 급하지 않음. 다만 **Read/Write Enabled 끄기**와 **Max Size 낮추기**는 플랫폼 무관하게 저사양 PC GPU 크래시 방지(오늘 작업의 원래 목적)에도 바로 도움이 되므로, 실제 텍스처 에셋을 넣기 시작할 때 기본값으로 체크할 항목.

## 4. 코드로 생성하는 uGUI의 성능 함정

- 요약: Canvas 하나에서 아무 요소 하나만 바뀌어도 "더티" 처리되어 Canvas 전체가 다시 계산됨(버텍스/인덱스/UV 재계산 + 배칭). 자주 바뀌는 요소(텍스트, 채워지는 바 등)는 별도 하위 Canvas로 분리해서 더티 범위를 좁히는 것이 핵심 최적화. Text 컴포넌트보다 TextMeshPro(SDF 렌더링)가 시각 품질 대비 성능이 낫다는 것이 일반적 권장사항.
- 출처: [Unity - Some of the best optimization tips for Unity UI](https://create.unity.com/Unity-UI-optimization-tips), [TheGamedev.Guru - Unity UI Profiling: How Dare You Break My Batches?](https://thegamedev.guru/unity-ui/profiling-canvas-rebuilds/)
- Fantasia 적용 지점 (가장 직접적으로 관련 있는 항목):
  - `StatusInventoryPanel`은 인벤토리 슬롯 12개 + 캐릭터 탭 등 여러 요소가 **한 Canvas 안**에 있고, 드래그 중(`DragSlot`)마다 고스트 위치가 갱신됨 — 드래그 고스트(`_dragGhost`)를 별도의 얕은 하위 Canvas로 분리하면, 드래그할 때마다 인벤토리 전체 슬롯이 다시 배칭되는 것을 피할 수 있음. 지금 규모(슬롯 12개)에서는 체감 차이가 없겠지만, UI가 커지면 유효한 개선 지점.
  - `ItemAcquiredToast`는 `Update()`에서 `Time.unscaledTime` 체크만 하고 텍스트 자체는 `OnItemAdded`에서만 갱신하므로 이미 "필요할 때만 갱신"에 가까운 구조 — 현재는 문제 없음.
  - 지금은 `Text`(legacy uGUI) 기반인데, TextMeshPro는 별도 패키지 설치 + 폰트 에셋(TMP Font Asset) 생성이 필요해서 코드만으로 완결되던 지금의 "런타임에 전부 생성" 방식과 약간 어긋남 — UI 텍스트 양이 늘어나거나 폰트 품질이 문제될 때 전환을 검토.

## 5. Built-in RP vs URP

- 요약: URP는 드로우콜당 CPU/GPU 오버헤드를 줄이는 데 초점을 맞춘 파이프라인이라 저사양 하드웨어에서 유리하다는 것이 일반적 평가이며, SRP Batcher 등 위 1번 최적화도 URP(SRP 계열)에서만 쓸 수 있음. 또한 Unity가 Built-in RP를 최근 버전에서 지원 종료 방향으로 가고 있다는 점도 언급됨(장기적으로는 URP 전환이 불가피할 가능성).
- 출처: [Unity: Understanding URP, HDRP, and Built-In Render Pipeline](https://www.wayline.io/blog/unity-understanding-urp-hdrp-built-in), [URP vs HDRP vs Built-in 비교](https://www.juegostudio.com/blog/urp-vs-hdrp-vs-built-in)
- Fantasia 적용 지점: README에 이미 "렌더 파이프라인 미정" 상태로 기록돼 있음. 오늘 프레임 캡을 건 이유(저사양 GPU 크래시)와 이 조사 결과를 합쳐보면, **URP 전환이 장기적으로는 프레임 안정성에도 도움이 될 가능성이 높다**는 근거가 하나 더 생긴 셈. 다만 마이그레이션(머티리얼 업그레이드 등)에는 별도 작업량이 들어가므로, 팀 결정 없이 지금 바로 전환하지는 않음 — 이 문서는 "전환할 때 참고할 근거 자료"로만 남겨둠.

## 6. 오브젝트 풀링

- 요약: `Instantiate`/`Destroy`를 반복하는 대신 미리 만들어둔 오브젝트를 재사용. Unity가 기본 제공하는 `UnityEngine.Pool.ObjectPool<T>`을 쓰면 되고, 풀에 반환하기 전 상태(속도, 이벤트 구독 등)를 반드시 초기화해야 하며, 풀 크기는 실제 동시 사용량에 맞춰 프로파일러로 검증하는 것을 권장.
- 출처: [Unity Manual - Pooling and reusing objects](https://docs.unity3d.com/6000.5/Documentation/Manual/performance-reusable-code.html), [Unity Scripting API - ObjectPool](https://docs.unity3d.com/6000.1/Documentation/ScriptReference/Pool.ObjectPool_1.html)
- Fantasia 적용 지점: `CombatTestSceneSetup`이 전투 진입마다 씬을 통째로 재구성하는 방식이라(README에 "각 Setup 메뉴는 씬을 처음부터 다시 만듦"이라 명시) 지금은 풀링 대상이 아님. 다만 나중에 "씬 재생성 없이 인카운터마다 적 유닛만 교체"하는 방식으로 바꾼다면, 적 유닛 GameObject와 데미지 텍스트 같은 반복 생성물이 풀링의 1순위 후보가 됨.

## 7. 프로파일링 워크플로우

- 요약: Window > Analysis > Profiler로 CPU/GPU/메모리 시계열을 보고, Window > Analysis > Frame Debugger로 특정 프레임의 드로우콜을 하나씩 순회하며 어떤 셰이더/지오메트리가 쓰였는지 확인. "추측하지 말고 프로파일러로 먼저 측정" 이 공통된 원칙.
- 출처: [Unity Learn - Working with the Frame Debugger](https://learn.unity.com/tutorial/working-with-the-frame-debugger), [Unity - Ultimate Guide to Profiling Unity 6 Games](https://unity.com/resources/ultimate-guide-to-profiling-unity-games-unity-6)
- Fantasia 적용 지점: 오늘 프레임 캡을 60으로 임의로 정했는데, 실제로 어느 시스템(헥스보드 렌더링? uGUI 리빌드? 전투 씬의 오브젝트 수?)이 저사양 GPU에서 부하를 유발하는지는 아직 측정하지 않은 상태. 다음에 여유가 되면 Profiler로 `BoardTest`/`CombatTest` 씬을 각각 열어 CPU vs GPU 중 어느 쪽이 병목인지부터 확인하는 것을 권장 — 그 결과에 따라 위 1~6번 중 실제로 효과 있는 항목이 갈림.

## 종합 메모
지금 프로젝트는 아직 오브젝트 수/텍스처가 적은 프로토타입 단계라, 위 항목 대부분은 "지금 당장 고쳐야 할 문제"가 아니라 "콘텐츠가 늘어났을 때 순서대로 점검할 체크리스트"에 가깝습니다. 우선순위를 매긴다면: (1) 실측을 위한 프로파일링(7번) → (2) uGUI Canvas 분리(4번, 이미 있는 코드에 바로 적용 가능) → (3) 렌더 파이프라인 결정(5번, 팀 논의 필요) → (4) 나머지(1, 2, 3, 6번)는 실제 3D 에셋/전투 재구성 방식이 정해진 뒤 순서대로.
