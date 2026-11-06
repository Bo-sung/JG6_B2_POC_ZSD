using UnityEngine;
using UnityEngine.InputSystem;

// 개발 전용 테스트 기능. 단계 강제 진행, 생존자 사망, 스크랩 지급, 방벽 배치, 웨이브 재시작.
// 에디터와 개발 빌드에서만 동작하며 정규 UI를 대체하지 않는다.
// 단축키: F5 단계 진행, F6 선택 인원 사망, F7 스크랩 지급, F8 커서 위치에 방벽, F9 웨이브 재시작.
[DisallowMultipleComponent]
public sealed class PocDebug : MonoBehaviour
{
    [SerializeField] GameSession session;
    [SerializeField] RTSInput input;
    [SerializeField] Camera worldCamera;
    // 테스트 편의값이며 밸런스에 쓰이지 않는다.
    [SerializeField] int scrapGrant = 20;
    [SerializeField] float rayDistance = 300f;

    void Awake()
    {
        if (!Debug.isDebugBuild)
        {
            Destroy(this);
        }
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }
        if (keyboard.f5Key.wasPressedThisFrame)
        {
            ForceNextPhase();
        }
        if (keyboard.f6Key.wasPressedThisFrame)
        {
            KillSelected();
        }
        if (keyboard.f7Key.wasPressedThisFrame)
        {
            GrantScrap();
        }
        if (keyboard.f8Key.wasPressedThisFrame)
        {
            PlaceWallAtCursor();
        }
        if (keyboard.f9Key.wasPressedThisFrame)
        {
            RestartWave();
        }
    }

    [ContextMenu("단계 강제 진행")]
    public void ForceNextPhase() => session.DebugAdvancePhase();

    // 전투 시간 상한을 바로 넘겨 DEC-15를 시험한다.
    [ContextMenu("전투 시간 상한 만료")]
    public void ExpireCombatTimer() => session.DebugExpireTimer();

    public void KillSurvivor(string _id) => session.DebugKillSurvivor(_id);

    [ContextMenu("선택 인원 사망")]
    public void KillSelected()
    {
        foreach (string id in new System.Collections.Generic.List<string>(input.Selection.Ids))
        {
            session.DebugKillSurvivor(id);
        }
    }

    [ContextMenu("스크랩 지급")]
    public void GrantScrap() => session.Economy.Add(scrapGrant);

    // 비용이 모자라면 먼저 채워서 정규 설치 경로로 놓는다.
    public BuildingBase PlaceWall(Vector3 _position, float _yaw = 0)
    {
        if (!session.WallPrefab)
        {
            return null;
        }
        int cost = TableManager.Instance.Gameplay.buildings.wall.cost;
        if (!session.Economy.CanAfford(cost))
        {
            session.Economy.Add(cost);
        }
        if (!session.TryPlaceBuilding(BuildingType.Wall, _position, Quaternion.Euler(0, _yaw, 0), out BuildingBase building, out string error))
        {
            Debug.LogWarning($"방벽 배치 실패: {error}");
            return null;
        }
        return building;
    }

    void PlaceWallAtCursor()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }
        Ray ray = worldCamera.ScreenPointToRay(mouse.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit, rayDistance, GameLayers.GROUND_MASK, QueryTriggerInteraction.Ignore))
        {
            PlaceWall(hit.point);
        }
    }

    public void LowerHp(string _id, float _hp) => session.DebugLowerHp(_id, _hp);

    [ContextMenu("디버그 합류(구조 몫)")]
    public void DebugJoinSurvivor()
    {
        if (!session.DebugJoinSurvivor(out string error))
        {
            Debug.LogWarning($"디버그 합류 실패: {error}");
        }
    }

    // 진행 중인 탐사에 사건을 일으킨다(G6 검증용). _outcomeRoll은 0~1이고 음수면 파견 때 뽑은 값을 쓴다.
    public void RaiseExpeditionEvent(ExpeditionEventType _type, double _outcomeRoll = -1)
    {
        if (!session.DebugRaiseExpeditionEvent(_type, out string error, _outcomeRoll))
        {
            Debug.LogWarning($"디버그 사건 실패: {error}");
        }
    }

    [ContextMenu("탐사 사건: 생존자 발견")]
    public void RaiseRescueEvent() => RaiseExpeditionEvent(ExpeditionEventType.Rescue);

    [ContextMenu("탐사 사건: 추가 보급품")]
    public void RaiseSupplyEvent() => RaiseExpeditionEvent(ExpeditionEventType.Supply);

    [ContextMenu("탐사 사건: 적대 생물 조우")]
    public void RaiseEncounterEvent() => RaiseExpeditionEvent(ExpeditionEventType.Encounter);

    [ContextMenu("웨이브 재시작")]
    public void RestartWave() => session.DebugRestartWave();

    // 디버그 전용: XP 지급 진입점(G6 검증용). 정규 경로와 같은 ExperienceTracker를 거친다.
    public void AddExperience(string _id, StatType _stat, int _amount) => session.DebugAddExperience(_id, _stat, _amount);
    public void AddExperience(string _id, string _stat, int _amount) => session.DebugAddExperience(_id, TraitRules.ParseStat(_stat), _amount);
}
