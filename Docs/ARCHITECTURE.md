# POC 구현 설계

> 기획 기준은 `POC_CORE.md`, 결정은 `DECISIONS.md`, 규칙은 프로젝트 루트 `CLAUDE.md`를 따른다. 이 문서는 구현 구조만 정한다. 기획과 충돌하면 기획이 우선이다.

## 구현 순서

1. **수직 슬라이스:** 생존자 선택·명령 → 작업/방어 이중 배정 → 경보 자동 전환 → 실시간 전투 → 영구 사망 이월. Game 씬에서 플레이 검증한다.
2. **루프 완결:** 생산 → 방벽/감시탑 → 오프맵 탐사 → 3웨이브 → 승패·재시작 → Preload/Title → 로그 → 나머지 UI.
3. **검증:** `POC_CORE.md` 7절 수락 조건별 플레이 근거, 수치 검증 조건 3개, 코드 리뷰.

## 폴더와 책임

스크립트는 `Assets/Scripts/Runtime/Game` 아래에 둔다. 네임스페이스는 쓰지 않는다(템플릿과 동일). 순수 C# 규칙과 Unity 동작을 분리해 규칙은 EditMode로 테스트한다.

| 폴더 | 클래스 | 책임 |
| --- | --- | --- |
| `Game/Model` | `SurvivorState` | ID·이름·능력치·HP·생존·임무. Unity 비의존 |
| `Game/Model` | `AssignmentBoard` | 작업(생산/방어 대기)과 방어 위치(지점/감시탑)를 독립 저장. 정원·사망·파견 규칙 |
| `Game/Model` | `ResourceEconomy` | 스크랩, 생산 누적, 비용 차감. 음수 금지 |
| `Game/Model` | `ExpeditionSystem` | 파견·시간 경과·결과 판정(DEC-16) |
| `Game/Model` | `RecruitmentSystem` | 인력 수급: 수용 상한 판정(살아 있는 인원+예약 < capacity), 방문 후보 추첨, 신규 ID 발급, 방문 상태. G3 구조 예약 API 포함 |
| `Game/Model` | `SessionRng` | 시드 하나에서 용도별 독립 `System.Random` 생성 |
| `Game/Session` | `GameSession` | 단계 전환, 웨이브 번호, 승패, 하위 시스템 소유 |
| `Game/Session` | `SessionLaunch` | Title에서 Game으로 시드 전달. Game 직접 실행 시 기본값 |
| `Game/Units` | `SurvivorUnit` | 이동, 자동 표적·사격, 직접 명령, 사망. NavMeshAgent 사용 |
| `Game/Units` | `EnemyUnit` | 핵심 시설 추적, 사거리 내 대응, 막힌 경로의 방벽 공격 |
| `Game/World` | `CoreFacility`, `ProductionFacility` | 핵심 시설 HP, 생산 슬롯 위치 |
| `Game/World` | `BuildingBase`, `WallBuilding`, `TowerBuilding` | HP, 수리, 파괴. 방벽은 NavMeshObstacle carve, 탑은 정원 1 |
| `Game/World` | `BuildZone` | 설치 허용 구역 |
| `Game/World` | `WaveDirector` | 테이블 편성으로 두 경로에 스폰 |
| `Game/Control` | `RTSInput` | 선택·드래그·Shift·우클릭 명령 |
| `Game/Control` | `CameraRig` | 팬·줌, 고정 경사각 |
| `Game/Control` | `BuildController` | 그리드 스냅, 미리보기, 불가 표시, 설치, 수리 |
| `Game/UI` | `*View`, `*Presenter` | MVP. View는 프리팹의 MonoBehaviour, Presenter는 일반 C# 클래스 |
| `Game/Logging` | `LogSession`, `FileLogStore`, `LogGameLifecycle` | JSONL 분석 로그 |
| `Game/Debug` | `PocDebug` | 단계 강제 진행, 스크랩 지급, 웨이브 재시작. 개발 빌드 전용 |

## 단계와 시간

```text
Prep(100초) → Alert(10초) → Combat(최대 100초) → Result(확인 대기) → Prep … 3웨이브 후 승리
```

| 단계 | 생산 | 탐사 타이머 | 건설 | 파견 시작 | 배정 변경 |
| --- | --- | --- | --- | --- | --- |
| Prep | 진행 | 진행 | 가능 | 가능 | 가능 |
| Alert | 중단 | 진행 | 불가 | 불가 | 방어 위치만 |
| Combat | 중단 | 진행 | 불가 | 불가 | 방어 위치만 |
| Result | 중단 | 정지 | 불가 | 불가 | 불가 |

- Alert 시작: 파견 중이 아닌 생존 인력이 방어 위치로 실제 이동한다.
- Combat 시작: `WaveDirector`가 스폰한다. 전원 처치 또는 시간 상한(DEC-15)으로 끝난다.
- Result 진입: 생존자 HP 전량 회복(DEC-09). 확인을 누르면 다음 Prep으로 가고 인력은 작업 배정으로 복귀한다.
- 패배: 핵심 시설 HP 0 또는 파견 포함 전원 사망(DEC-14). 어느 단계에서든 즉시 판정한다.

## 생존자 행동

`GameSession`이 단계에 맞는 기본 행동을 지시하고, 직접 명령이 이를 일시적으로 덮어쓴다. 저장된 배정은 직접 명령으로 바뀌지 않는다.

| 상태 | 행동 |
| --- | --- |
| Working | 생산 슬롯으로 이동해 도착 후 생산에 기여 |
| Guarding | 방어 위치로 이동해 대기, 사거리 내 자동 사격 |
| ManualMove | 지정 지점으로 이동 후 그 자리에서 자동 사격 |
| ManualAttack | 지정 적 추격·공격. 대상이 사라지면 그 자리에서 자동 사격 |
| Away | 파견 중. 오브젝트 비활성, 어떤 명령도 불가 |
| Dead | 영구 제외 |

Prep 중 직접 이동한 인력은 생산에 기여하지 않고, 선택 패널의 `배정 복귀` 또는 다음 Result 이후 작업 배정으로 돌아간다.

## 경로와 방벽

- 맵에 NavMesh를 베이크하고, 방벽은 `NavMeshObstacle`(carve)로 길을 막는다. 관통은 버그다.
- `EnemyUnit`은 주기적으로 핵심 시설까지 `CalculatePath`를 계산한다. 경로가 완전하면 따라가고, 부분 경로면 끝까지 이동한 뒤 가장 가까운 방벽을 공격한다. 방벽이 파괴되면 재계산한다.
- 생존자가 막힌 위치를 지정해도 갈 수 있는 곳까지 간다.

## 인력 수급과 합류 경로 (v2)

- `RecruitmentSystem`이 수급 규칙의 한 곳이다. 상한 판정은 `살아 있는 보유 인원(파견·중상 포함) + 귀환 예정 예약 < capacity` 하나뿐이고, 후보는 세션 시작 때 `SessionRng`의 `recruit` 스트림으로 섞어 방문 몫(W2·W4)을 먼저 정하고 나머지는 구조 몫 큐로 둔다(V2-IMPL-09). 신규 ID는 후보와 무관한 세션 순번(`n1`, `n2`…)으로 발급하고 재사용하지 않는다.
- 방문자는 해당 웨이브 준비 시작(`EnterPrep`)에 도착하고, 경보 진입(`EnterAlert`)에 미결정이면 만료된다. 영입의 권위 경로는 `GameSession.TryRecruitVisitor(out error)` 하나다: 방문 대기 → 상한 → 스크랩 차감 → 공통 합류 순서이며, 실패하면 아무것도 바뀌지 않고 사유(`no_scrap`, `full`)를 돌려준다. '보내기'와 만료는 `RecruitmentRejected(declined/expired)`로 기록된다.
- 합류는 `GameSession.JoinSurvivor` 한 경로로만 한다. `Survivor.prefab`을 씬의 `JoinPoint`(거점 입구)에 Instantiate하고, 초기 인원과 같은 `RegisterSurvivor`로 등록(board·unitsById·units·Bind·표식·배정 로그)한다. 합류 인력은 HP 최대, 작업은 방어 대기, 방어 위치는 합류 지점이되 플레이어가 정하기 전까지는 '방어 위치 미지정'으로 표시된다(V2-IMPL-08). 씬 표식이 없으므로 `DefenseMarker.prefab`을 함께 놓는다.
- HUD는 `인원 현재/최대`를 항상 보여 주고, 대기 중 방문자가 있으면 `방문자 도착` 항목을 켠다. `VisitorPanel`(MVP: `VisitorPanelView`+`VisitorPanelPresenter`)에서 이름·능력치·특성·비용·현재 인원/상한과 영입·보내기를 처리한다. 거점 입구 표식(`VisitorMarkerView`)은 방문 대기 중에만 켜지고 콜라이더가 없어 클릭 판정에 끼지 않는다.

## 이벤트

도메인 이벤트는 템플릿 규칙대로 `GameEvents`에 struct 페이로드로 추가하고 `Raise*`로만 발행한다. 컴포넌트끼리 서로를 직접 호출해 알리지 않는다. `LogGameLifecycle`과 Presenter가 구독한다.

## 데이터

수치는 `Resources/Tables/GameplayTableData.json`에 둔다. `player` 샘플을 POC 섹션으로 교체하고, `GameTableRules`에 검증을 추가한다. `upgrades`와 `sequence`는 남아 있는 템플릿 코드가 쓰므로 유지한다. `FlowTableData.json`은 `Preload → Title → Game`으로 바꾼다.

## 씬과 프리팹

Unity CLI로 제작한다. 런타임에 UI나 레벨을 코드로 만들지 않는다. 런타임 생성은 적과 효과, 플레이어가 설치한 시설 프리팹만 허용한다.

| 씬 | 내용 |
| --- | --- |
| Preload | `TableManager` 검증 후 Title 로드. 실패 시 오류 표시 |
| Title | 시드 입력, 시작 버튼 |
| Game | 거점 맵, 카메라, 생존자 7명, 스폰 지점, HUD |

Game 재시작은 씬 재로드로 하고 같은 시드를 다시 쓴다.

### 맵 배치(초기안, 조정 가능)

- 바닥 약 80×80m. 북쪽 양 끝에 적 스폰 2곳, 거점 북쪽 벽에 폭 8m 관문 2곳(서·동).
- 핵심 시설은 중앙 남쪽, 생산 시설은 더 남쪽. 생산 시설에서 관문까지 이동 시간이 경보 시간보다 길어 끝까지 일한 인력이 첫 교전에 늦도록 거리를 잡는다.
- 복귀 지점은 남쪽 가장자리. 방벽 길이 4m, 그리드 2m라 관문 하나를 막으려면 방벽 2개(시작 스크랩 전부)가 든다.

## 테스트

- EditMode: `Assets/Scripts/Editor/Tests`에 순수 규칙 테스트. `unity command test run`으로 실행한다.
- 플레이: Unity CLI로 Play Mode에 들어가 `PocDebug`와 `runtime simulate_pointer`로 조작하고 `capture game_view`, 로그 파일로 근거를 남긴다.
- 사람 플레이테스트로만 판단할 수 있는 재미 가설은 미검증으로 보고한다.
