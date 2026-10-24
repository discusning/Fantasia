# 보드 이동 화면 상단 UI 조사 (2026-10-24)

> 목적: 보드 1인 이동 + [아군 턴] → [적 턴] 구조(Docs/Planning/Decisions.md)에 맞는 **보드 화면 상단 UI**를 새로 설계하기 위한 참고.
> For the King식 "파티원 이동 순서 + 낮·밤 바"는 1인 이동과 맞지 않아 다른 보드게임을 조사했다. 참고용 — 그대로 복제하지 않는다.
> 성과 수치는 출처별 측정 방식이 달라(출하·판매·추정·다운로드) **규모 감**으로만 본다.

## 1. 조사 대상 16종 (+ For the King 기준)

| # | 게임 | 플랫폼 | 성과 (출처) | 보드 화면 턴·시간 표시 방식 |
|---|---|---|---|---|
| 1 | 슈퍼 마리오 파티 잼버리 | Switch | 출시 약 10주 만에 617만 장 출하, 시리즈 최고 속도 — 전작 슈퍼 마리오 파티 누계 2,110만 ([Nintendo Life](https://nintendolife.com/news/2025/02/super-mario-party-jamboree-is-the-fastest-selling-entry-in-franchise-history), [VGC](https://videogameschronicle.com/news/super-mario-party-jamboree-marked-the-series-biggest-launch-with-6m-shipped)) | 남은 턴 수 + 막바지 **"Homestretch(마지막 5턴)" 이벤트**·"남은 턴 n" 연출 ([Super Mario Wiki](https://www.mariowiki.com/Last_5_Turns)), 차례 바뀔 때 큰 전환 배너, 플레이어 정보는 화면 모서리 |
| 2 | 모노폴리 GO! | iOS·Android | 2024년 매출 약 13~16억 달러(AppMagic·Appfigures), 누적 다운로드 1.5억+, 누적 인앱 매출 60억 달러(Sensor Tower, 2025) ([Business of Apps](https://businessofapps.com/data/monopoly-go-statistics), [Outlook Respawn](https://respawn.outlookindia.com/gaming/gaming-news/monopoly-go-becomes-fastest-mobile-game-to-reach-6-billion)) | 턴 대신 **자원(주사위 롤 수)·순자산**을 상단에, 이벤트 **진행도 바**를 화면 가장자리에 상시 표시 |
| 3 | 모두의마블 | iOS·Android (넷마블) | 누적 다운로드 2억 건(2015), 누적 매출 5천억 원 보도 ([OSEN](https://osen.co.kr/article/G1110324580)) | 화면 모서리 플레이어 패널(자산·순위), **턴 수(n/총턴)** 표시, 내 차례 전환 연출 |
| 4 | Ludo King | iOS·Android | 인도 게임 앱 최초 10억 다운로드 ([Wikipedia](https://en.wikipedia.org/wiki/Ludo_King)) | 각 플레이어 진영 모서리에 아바타, **차례인 플레이어 아바타에 원형 타이머 링** |
| 5 | 모모타로 전철 (쇼와·헤이세이·레이와도 정번! / 월드 / 2) | Switch | 정번 400만+, 월드 150만+, 2편 100만+ (출하+DL) ([Gematsu](https://www.gematsu.com/2026/03/momotaro-dentetsu-2-anata-no-machi-mo-kitto-aru-higashi-nihon-hen-nishi-nihon-hen-shipments-and-digital-sales-top-one-million), [Perfectly Nintendo](https://www.perfectly-nintendo.com/?p=600456)) | **"○년째 ○월" 달력 = 턴**(전원 1턴 = 1개월), 매년 3월 **결산** 이벤트 ([전격온라인](https://dengekionline.com/articles/74376/), [J-CAST](https://www.j-cast.com/2021/04/02408724.html?p=all)) |
| 6 | Pummel Party | Steam | Steam 약 210만 장 추정, SteamSpy 소유자 200~500만, 동접 최고 1.6~2.1만 ([Raijin](https://raijin.gg/app/880940/Pummel_Party), [SteamSpy](https://steamspy.com/dev/Rebuilt+Games)) | 상단 **턴 카운터(n/총턴)**, 플레이어 목록(열쇠·트로피) |
| 7 | Armello | PC·PS·Xbox·Switch·모바일 | Steam 27만~106만 장 추정(출처 간 차이 큼) ([Raijin](https://raijin.gg/app/290340/Armello), [Vaporlens](https://vaporlens.app/app/290340/armello/stats/details)) | **낮·밤 2단계 턴**(플레이어는 낮·밤 행동, NPC는 새벽·황혼에 행동, 새벽마다 왕이 피해) — 해·달 시계 표시 ([Wikipedia](https://en.wikipedia.org/wiki/Armello), [Indie Game Reviewer](https://indiegamereviewer.com/?p=23458)) |
| 8 | For The King II | Steam·콘솔 | Steam 37만~105만 장 추정 ([Raijin](https://raijin.gg/app/1676840/For_The_King_II), [Vaporlens](https://vaporlens.app/app/1676840/for_the_king_ii/stats/details)) | 파티원 이동 순서 초상 + 낮·밤 진행 바 + 일차 + 지역명 배너 (기준 이미지) |
| 9 | 100% Orange Juice | Steam·Switch | Steam 36만~180만 장 추정 ([Raijin](https://raijin.gg/app/282800/100_Orange_Juice), [Vaporlens](https://vaporlens.app/app/282800/100_orange_juice/stats/details)) | **챕터(라운드) 수** 상단, 플레이어 패널에 별·승점·노르마 레벨 |
| 10 | Dokapon Kingdom: Connect | Switch·Steam | Steam 약 2.4만 장 추정 (Switch 미포함) ([Raijin](https://raijin.gg/app/2338140/Dokapon_Kingdom_Connect)) | **일·주 달력**(전원 1턴 = 1일, 게임 길이를 주 단위로 선택) ([Nintendo World Report](https://www.nintendoworldreport.com/review/17274/dokapon-kingdom-wii)) |
| 11 | Talisman: Digital Edition | PC·모바일 | Steam 약 13.7만 장 추정, 현재 무료 전환 / 보드+디지털 평생 100만+ ([Raijin](https://raijin.gg/app/247000/Talisman_Digital_Classic_Edition), [GamesBeat](https://gamesbeat.com/talisman-digital-edition-adds-the-martial-artist-and-saracen/)) | 플레이어 초상 줄 + 현재 차례 강조, 턴 단계(이동·조우) 패널 |
| 12 | Gloomhaven (디지털) | PC·콘솔 | Steam 33.5만~47.8만 장 추정 ([Raijin](https://raijin.gg/app/780290/Gloomhaven), [SteamPulse](https://steampulse.org/game/780290)) | 이니셔티브 순서 트래커(전투), **라운드 수**·원소 게이지 |
| 13 | Wingspan (디지털) | PC·Switch·Xbox·PS·iOS | 디지털 67.4만 장(2023.2) ([ICv2](https://icv2.com/articles/news/view/53373/stonemaier-games-shares-tabletop-games-in-print-vs-digital-sales-totals)) | **라운드 트랙**(4라운드, 라운드마다 행동 수 감소)·라운드 목표 상시 |
| 14 | Root (디지털) | PC·Switch·iOS·Android | SteamSpy 소유자 20만~50만, iOS 평점 4.9(4.5천) ([SteamSpy](https://steamspy.com/dev/Dire+Wolf), [App Store](https://apps.apple.com/app/id1439262206)) | **승점 트랙**·진영 턴 순서·현재 페이즈(새벽·낮·저녁) 표시 |
| 15 | CATAN Universe | PC·iOS·Android (F2P) | 수치 비공개 — 이전 디지털판 "수백만 장" ([USM 보도자료](https://www.usm.de/_files_media/mediathek/downloads/132/catan_universe_mobile_release_en.pdf)) | 플레이어 패널(승점·자원), **턴 타이머**, 주사위 결과 |
| 16 | Ticket to Ride (디지털) | PC·콘솔·iOS | 수치 비공개 ([App Store](https://apps.apple.com/app/id6463616555)) | 플레이어 패널 세로 열(남은 기차·점수), 현재 차례 강조 |

## 2. 상단 UI 패턴 정리

| 패턴 | 대표 | 핵심 | 우리 게임 적합도 |
|---|---|---|---|
| **A. 낮·밤 2단계 시계** | Armello | 한 사이클이 두 페이즈(낮·밤), 페이즈마다 행동 주체가 다르고 **사이클 경계에서 월드 효과**(왕 피해) | ★★★ — [아군 턴]→[적 턴]과 구조가 같고, 적 턴의 월드 처리(피의 흔적·영혼 수확)와 그대로 대응 |
| **B. 달력 = 턴** | 모모타로 전철, Dokapon | 턴을 "○년 ○월 / ○일"로 표현, **정해진 시점 이벤트**(3월 결산) | ★★ — 1774년 배경과 어울림(예: "1774년 11월 3일 밤"), 상점 재고(3라운드)·결산 같은 주기 이벤트에 적합. 단 기획이 "세부 타임라인 제거" — 날짜 표시만 쓰고 미래 칸은 그리지 않는 방식이면 충돌 없음 |
| **C. 위협 트랙** | Root(승점), Wingspan(라운드), Pandemic류 | 진행 게이지를 **칸(노치)**으로 쪼개 남은 여유를 한눈에 | ★★★ — 피의 흔적 G(라운드당 +10 → 10칸), 영혼 수확 S(100) 에 그대로 |
| **D. 막바지 경고 연출** | 마리오 파티 Homestretch | 남은 턴 n, 마지막 구간 진입 시 큰 연출 | ★★ — G 80+ "사신이 가까워진다", S 25·50·75 경고(System.md)와 연결 |
| **E. 차례 전환 배너** | 마리오 파티, 모두의마블, 모모타로 | 차례가 바뀔 때 화면 가운데 큰 배너 | ★★★ — 지금의 작은 배너 대신 페이즈 전환을 확실히 |
| F. 모서리 플레이어 패널 | 모두의마블, Ludo King, 카탄 | 경쟁 플레이어 여럿 | ✗ — 솔로·1파티라 불필요 (하단 파티 카드가 이미 담당) |
| G. 자원·이벤트 진행 바 | 모노폴리 GO | 턴보다 자원 중심 | △ — Cent 표시 정도만 |

## 3. 결론 — Fantasia에 맞는 조합

1. **페이즈 시계 (A)** — 상단 가운데 해·달(아군·적) 원형 시계, 가운데 월드 라운드.
2. **위협 트랙 2줄 (C)** — 시계 양옆에 **피의 흔적 G**(10칸)·**영혼 수확 S**(경고 25·50·75 표시) 칸 게이지. 수치 증가는 D24·D25에서 연결, UI는 먼저.
3. **날짜 표기 (B 일부)** — 라운드를 "1774년 ○월 ○일 낮/밤"으로 보조 표기(기획 확인 필요 — 1라운드 = 하루 가정).
4. **페이즈 전환 배너 (E)** + **경고 연출 (D)** — 아군/적 턴이 바뀔 때 가운데 큰 배너, G·S 임계치 접근 시 강조.
5. 왼쪽: 파티장 초상·이름 + 지역명 배너(For the King의 지역명은 유지), 오른쪽: Cent.

모두 `UITheme` 스프라이트로 교체 가능하게 — 기획 UI 시안이 오면 그림만 바꾼다.
