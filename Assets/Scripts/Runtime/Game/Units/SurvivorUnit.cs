using UnityEngine;
using UnityEngine.AI;

// 생존자의 월드 동작. 저장된 배정은 AssignmentBoard에 있고, 여기는 지시받은 행동만 수행한다.
// 직접 명령은 행동만 덮어쓰며 배정을 바꾸지 않는다(POC_CORE 3.1).
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class SurvivorUnit : MonoBehaviour
{
    // 동시에 탐지할 적 수의 상한. 웨이브 편성보다 넉넉하면 되는 버퍼 크기이며 밸런스 수치가 아니다.
    const int TARGET_BUFFER_SIZE = 32;
    // 사선 한 줄에 겹치는 차폐물 수의 상한. 소형 맵이면 충분한 버퍼 크기이며 밸런스 수치가 아니다.
    const int SIGHT_BUFFER_SIZE = 8;

    // 밸런스가 아닌 이동·표시 보정값이다. 프리팹에서 조정한다.
    [SerializeField] float arrivalTolerance = 0.6f;
    [SerializeField] float retargetInterval = 0.2f;
    [SerializeField] float chaseRepathInterval = 0.25f;
    [SerializeField] float tracerSeconds = 0.08f;
    [SerializeField] float sightHeight = 1.5f;
    [SerializeField] LineRenderer tracer;
    [SerializeField] GameObject selectionRing;
    [SerializeField] WorldLabelView nameLabel;
    [SerializeField] DefenseMarkerView defenseMarker;

    [SerializeField] LineRenderer currentRange;
    [SerializeField] LineRenderer defenseConnection;
    [SerializeField] Transform weapon;
    [SerializeField] Renderer bodyRenderer;
    [SerializeField] Color combatColor = new Color(1f, 0.35f, 0.12f);
    [SerializeField] Color workColor = new Color(0.2f, 0.7f, 1f);
    [SerializeField] Color explorationColor = new Color(0.3f, 1f, 0.4f);
    [SerializeField] Color generalColor = new Color(0.85f, 0.65f, 1f);
    [SerializeField] float weaponSideOffset = 0.6f;
    [SerializeField] float weaponLengthPerCombat = 0.55f;
    [SerializeField] float weaponWidthPerCombat = 0.08f;
    [SerializeField] float tracerWidthPerDps = 0.009f;
    [SerializeField] float rangeHeight = 0.12f;
    [SerializeField] float stationRetrySeconds = 1f;
    [SerializeField] float stationBlockedSeconds = 3f;
    [SerializeField] float stationProgressTolerance = 0.05f;
    // 중상으로 쓰러진 몸체의 표시 기울기(도). 표시용이며 판정에 쓰지 않는다.
    [SerializeField] float fallenTilt = 80f;
    bool selected;
    bool isDown;
    Vector3 lastProgressPosition;
    float lastProgressTime;
    float nextStationRetry;
    public string MovementIssue { get; private set; } = string.Empty;
    public bool IsHeadingToTower => station is TowerBuilding tower && tower && State.Activity == SurvivorActivity.Guarding;

    readonly Collider[] targetBuffer = new Collider[TARGET_BUFFER_SIZE];
    readonly RaycastHit[] sightBuffer = new RaycastHit[SIGHT_BUFFER_SIZE];
    NavMeshAgent agent;
    SurvivorCombatData combat;
    ICombatStation station;
    Transform visualBody;
    Vector3 bodyRest;
    Vector3 labelRest;
    EnemyUnit autoTarget;
    EnemyUnit manualTarget;
    bool arrived;
    float nextRetargetTime;
    float nextRepathTime;
    float nextFireTime;
    float tracerEndTime;
    bool halted;

    public void Halt()
    {
        Hold();
        halted = true;
        tracer.enabled = false;
    }

    public SurvivorState State { get; private set; }
    public bool AtWorkSlot => State.CanAct && State.Activity == SurvivorActivity.Working && arrived;
    public bool AtTreatmentSlot => State.CanAct && State.Activity == SurvivorActivity.Treating && arrived;
    public bool HasArrived => arrived;
    public bool IsFiring => autoTarget || manualTarget;
    public EnemyUnit ManualTarget => manualTarget;

    // 경보 이동 비교용. 방어 위치 지시 시각과 도착 시각(Time.time). 아직 도착 전이면 도착 시각은 음수다.
    public float LastGuardOrderTime { get; private set; } = -1;
    public float LastGuardArrivalTime { get; private set; } = -1;

    ICombatStation ActiveStation
    {
        get
        {
            bool alive = station is Object stationObject && stationObject;
            return alive && State.CanAct && State.Activity == SurvivorActivity.Guarding && arrived
                && Vector3.ProjectOnPlane(transform.position - station.StationPosition, Vector3.up).magnitude <= arrivalTolerance ? station : null;
        }
    }

    public TowerBuilding OccupiedTower => ActiveStation as TowerBuilding;
    public float DamagePerSecond => ShotDamage / combat.attackInterval;
    // 사격 한 발의 피해. 초당 피해 표시와 실제 사격이 같은 값을 쓴다. 부상 효율은 피해에만 곱한다(V2-IMPL-07).
    float ShotDamage => (combat.baseDamage + combat.damagePerCombat * State.Combat) * State.CombatEfficiency
        * (ActiveStation != null ? ActiveStation.DamageMultiplier : 1);
    // 적이 공격할 수 있는 상태. 쓰러진 인력도 포함하며 우선순위는 EnemyUnit이 정한다(V2-IMPL-02).
    public bool IsTargetable => State.IsAlive && !State.IsAway && !IsProtected;

    public void LeaveTower(TowerBuilding _tower)
    {
        if (ReferenceEquals(station, _tower))
        {
            station = null;
            RefreshTowerVisual();
        }
    }

    // 합류 인력은 씬 표식이 없으므로 Bind 전에 새 표식을 연결한다.
    public void SetDefenseMarker(DefenseMarkerView _marker)
    {
        defenseMarker = _marker;
    }

    void LateUpdate()
    {
        if (State != null)
        {
            RefreshTowerVisual();
            if (selected && State.CanAct)
            {
                DefenseMarkerView.DrawCircle(currentRange, transform.position, AttackRange, rangeHeight);
                defenseConnection.SetPosition(0, new Vector3(transform.position.x, rangeHeight, transform.position.z));
                Vector3 target = defenseMarker.transform.position;
                defenseConnection.SetPosition(1, new Vector3(target.x, rangeHeight, target.z));
            }
            // 쓰러진 인력도 선택해 볼 수 있지만 사거리와 이동 연결선은 그리지 않는다.
            currentRange.enabled = selected && State.CanAct;
            defenseConnection.enabled = selected && State.CanAct;
        }
    }

    void RefreshTowerVisual()
    {
        if (!visualBody)
        {
            return;
        }
        Vector3 offset = OccupiedTower ? OccupiedTower.SeatPosition - transform.position : Vector3.zero;
        visualBody.position = transform.position + bodyRest + offset;
        nameLabel.transform.position = transform.position + labelRest + offset;
    }

    public bool IsProtected => ActiveStation != null && ActiveStation.ProtectsOccupant;
    Vector3 FiringOrigin => OccupiedTower ? OccupiedTower.SeatPosition : transform.position;
    public float AttackRange => GameTableRules.SurvivorRange(combat, State.Combat, ActiveStation != null ? ActiveStation.RangeBonus : 0);

    // GameSession.Awake에서 호출되므로 이 컴포넌트의 Awake 순서에 기대지 않는다.
    public void Bind(SurvivorState _state, SurvivorCombatData _combat)
    {
        agent = GetComponent<NavMeshAgent>();
        visualBody = transform.Find("Body");
        bodyRest = visualBody.localPosition;
        labelRest = nameLabel.transform.localPosition;
        State = _state;
        combat = _combat;
        agent.speed = _combat.moveSpeed;
        var tint = new MaterialPropertyBlock();
        Color color = State.Specialty switch
        {
            SurvivorSpecialty.Combat => combatColor,
            SurvivorSpecialty.Work => workColor,
            SurvivorSpecialty.Exploration => explorationColor,
            _ => generalColor,
        };
        tint.SetColor("_BaseColor", color);
        bodyRenderer.SetPropertyBlock(tint);
        weapon.localScale = new Vector3(weaponWidthPerCombat * State.Combat, weaponWidthPerCombat * State.Combat, weaponLengthPerCombat * State.Combat);
        weapon.localPosition = new Vector3(weaponSideOffset, 0, weapon.localScale.z * 0.5f);
        nameLabel.SetText(_state.DisplayName);
        defenseMarker.SetLabel(_state.DisplayName);
        SetSelected(false);
    }

    void Update()
    {
        if (tracer.enabled && Time.time >= tracerEndTime)
        {
            tracer.enabled = false;
        }
        if (State != null)
        {
            SyncDown();
        }
        if (State == null || !State.CanAct || halted)
        {
            return;
        }
        switch (State.Activity)
        {
            case SurvivorActivity.Working:
                UpdateArrival();
                break;
            case SurvivorActivity.Treating:
                UpdateArrival();
                break;
            case SurvivorActivity.Guarding:
            case SurvivorActivity.ManualMove:
                UpdateArrival();
                UpdateAutoFire();
                break;
            case SurvivorActivity.ManualAttack:
                UpdateManualAttack();
                break;
        }
    }

    // ── 지시 ──
    public void OrderWork(Vector3 _slot)
    {
        if (State.SetActivity(SurvivorActivity.Working))
        {
            ClearTargets();
            station = null;
            MoveTo(_slot);
        }
    }

    // 치료소의 담당자 자리 또는 환자 대기 자리로 간다.
    public void OrderTreat(Vector3 _slot)
    {
        if (State.SetActivity(SurvivorActivity.Treating))
        {
            ClearTargets();
            station = null;
            MoveTo(_slot);
        }
    }

    public void OrderGuard(Vector3 _position, ICombatStation _station)
    {
        if (State.SetActivity(SurvivorActivity.Guarding))
        {
            ClearTargets();
            station = _station;
            LastGuardOrderTime = Time.time;
            LastGuardArrivalTime = -1;
            MoveTo(_position);
        }
    }

    public void CommandMove(Vector3 _position)
    {
        if (State.SetActivity(SurvivorActivity.ManualMove))
        {
            ClearTargets();
            station = null;
            MoveTo(_position);
        }
    }

    public void CommandAttack(EnemyUnit _target)
    {
        if (State.SetActivity(SurvivorActivity.ManualAttack))
        {
            ClearTargets();
            station = null;
            manualTarget = _target;
            arrived = false;
            nextRepathTime = 0;
        }
    }

    // 결과·종료 단계의 정지. 다음 단계에서 다시 지시받는다.
    public void Hold()
    {
        if (State.SetActivity(SurvivorActivity.Guarding))
        {
            ClearTargets();
            StopAgent();
            arrived = true;
        }
    }

    public void BeginAway()
    {
        if (State.BeginAway())
        {
            ClearTargets();
            station = null;
            SetSelected(false);
            defenseMarker.gameObject.SetActive(false);
            gameObject.SetActive(false);
        }
    }

    // 복귀 지점에 등장한다. 다음 행동은 GameSession이 단계에 맞게 지시한다.
    public void EndAway(Vector3 _position)
    {
        if (State.EndAway())
        {
            gameObject.SetActive(true);
            defenseMarker.gameObject.SetActive(true);
            agent.Warp(_position);
        }
    }

    // 중상자를 치료소 대기 자리로 옮긴다. 동료가 옮겼다고 보고 이동 시간은 없다(V2-IMPL-03).
    public void PlaceAt(Vector3 _position)
    {
        if (!agent.isActiveAndEnabled || !agent.Warp(_position))
        {
            transform.position = _position;
        }
        arrived = true;
    }

    public void SetSelected(bool _selected)
    {
        selected = _selected;
        selectionRing.SetActive(_selected);
        currentRange.enabled = _selected;
        defenseConnection.enabled = _selected;
        defenseMarker.SetHighlighted(_selected);
    }

    public void ShowDefenseMarker(Vector3 _position, float _towerBonus = 0, Vector3? _rangeCenter = null)
    {
        defenseMarker.Place(_position);
        defenseMarker.SetRange(GameTableRules.SurvivorRange(combat, State.Combat, _towerBonus), _rangeCenter ?? _position);
    }

    // ── 피해·사망 ──
    public void ApplyDamage(float _amount, string _cause)
    {
        if (IsProtected)
        {
            return;
        }
        InjuryStatus before = State.Injury;
        if (State.ApplyDamage(_amount))
        {
            OnDied(_cause);
            return;
        }
        if (InjuryRules.IsWorse(before, State.Injury))
        {
            GameEvents.RaiseSurvivorInjured(State.Id, State.Hp, State.Injury.ToString());
        }
        SyncDown();
    }

    public void Kill(string _cause)
    {
        if (State.Kill())
        {
            OnDied(_cause);
        }
    }

    void OnDied(string _cause)
    {
        ClearTargets();
        SetSelected(false);
        tracer.enabled = false;
        defenseMarker.gameObject.SetActive(false);
        gameObject.SetActive(false);
        GameEvents.RaiseSurvivorDied(State.Id, _cause);
    }

    // 쓰러짐은 HP에서 파생한다. 그 자리에서 이동·사격을 멈추고 탑에서 내려온다. 탑 자리 배정은 지우지 않는다(V2-IMPL-04).
    void SyncDown()
    {
        bool down = State.IsCritical;
        if (down == isDown)
        {
            return;
        }
        isDown = down;
        if (down)
        {
            ClearTargets();
            station = null;
            tracer.enabled = false;
            StopAgent();
            arrived = true;
            RefreshTowerVisual();
        }
        visualBody.localRotation = down ? Quaternion.Euler(fallenTilt, 0, 0) : Quaternion.identity;
    }

    // ── 내부 동작 ──
    void MoveTo(Vector3 _position)
    {
        arrived = false;
        MovementIssue = string.Empty;
        lastProgressPosition = transform.position;
        lastProgressTime = Time.time;
        nextStationRetry = Time.time + stationRetrySeconds;
        if (agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.SetDestination(_position);
        }
    }

    void StopAgent()
    {
        if (agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    void ClearTargets()
    {
        MovementIssue = string.Empty;
        autoTarget = null;
        manualTarget = null;
    }

    // 막힌 위치를 지정해도 갈 수 있는 곳까지 가면 도착으로 본다(부분 경로의 끝).
    void UpdateArrival()
    {
        if (IsHeadingToTower && !arrived)
        {
            UpdateStationArrival();
            return;
        }
        if (arrived || !agent.isOnNavMesh || agent.pathPending)
        {
            return;
        }
        if (agent.remainingDistance <= Mathf.Max(agent.stoppingDistance, arrivalTolerance))
        {
            arrived = true;
            if (State.Activity == SurvivorActivity.Guarding)
            {
                LastGuardArrivalTime = Time.time;
            }
        }
    }

    void UpdateStationArrival()
    {
        float distance = Vector3.ProjectOnPlane(transform.position - station.StationPosition, Vector3.up).magnitude;
        if (distance <= arrivalTolerance)
        {
            arrived = true;
            MovementIssue = string.Empty;
            LastGuardArrivalTime = Time.time;
            StopAgent();
            return;
        }
        if ((transform.position - lastProgressPosition).sqrMagnitude > stationProgressTolerance * stationProgressTolerance)
        {
            lastProgressPosition = transform.position;
            lastProgressTime = Time.time;
            MovementIssue = string.Empty;
        }
        if (Time.time - lastProgressTime >= stationBlockedSeconds)
        {
            MovementIssue = "탑 접근 경로가 막혔습니다 · 인력·시설을 비워 주세요(재시도 중)";
        }
        if (!agent.isOnNavMesh)
        {
            MovementIssue = "탑 접근 불가 · 걸을 수 있는 지형을 확인하세요.";
            return;
        }
        if (!agent.pathPending && Time.time >= nextStationRetry)
        {
            nextStationRetry = Time.time + stationRetrySeconds;
            agent.isStopped = false;
            agent.SetDestination(station.StationPosition);
            if (agent.pathStatus == NavMeshPathStatus.PathPartial || agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                MovementIssue = "탑 접근 경로가 막혔습니다 · 인력·시설을 비워 주세요(재시도 중)";
            }
        }
    }

    void UpdateAutoFire()
    {
        if (Time.time >= nextRetargetTime)
        {
            nextRetargetTime = Time.time + retargetInterval;
            autoTarget = FindNearestEnemy();
        }
        if (autoTarget && !autoTarget.IsDead && CanHit(autoTarget))
        {
            FireAt(autoTarget);
        }
    }

    void UpdateManualAttack()
    {
        // 대상이 사라지면 그 자리에서 자동 사격으로 돌아간다. 저장된 방어 배정은 그대로다.
        if (!manualTarget || manualTarget.IsDead)
        {
            manualTarget = null;
            if (State.SetActivity(SurvivorActivity.ManualMove))
            {
                StopAgent();
                arrived = true;
            }
            return;
        }
        if (CanHit(manualTarget))
        {
            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
            }
            FireAt(manualTarget);
            return;
        }
        if (agent.isOnNavMesh && Time.time >= nextRepathTime)
        {
            nextRepathTime = Time.time + chaseRepathInterval;
            agent.isStopped = false;
            agent.SetDestination(manualTarget.transform.position);
        }
    }

    EnemyUnit FindNearestEnemy()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, AttackRange, targetBuffer, GameLayers.ENEMY_MASK, QueryTriggerInteraction.Ignore);
        EnemyUnit nearest = null;
        float nearestDistance = float.MaxValue;
        for (int index = 0; index < count; index++)
        {
            EnemyUnit enemy = targetBuffer[index].GetComponentInParent<EnemyUnit>();
            if (!enemy || enemy.IsDead || !HasLineOfFire(enemy))
            {
                continue;
            }
            float distance = (enemy.transform.position - transform.position).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearest = enemy;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    bool CanHit(EnemyUnit _enemy)
    {
        float range = AttackRange;
        return (_enemy.transform.position - transform.position).sqrMagnitude <= range * range && HasLineOfFire(_enemy);
    }

    // 사선 높이는 설치 방벽보다 높고 지형 벽과 시설보다 낮다. 방벽 너머로는 쏘고 지형 벽은 뚫지 못한다.
    bool HasLineOfFire(EnemyUnit _enemy)
    {
        Vector3 from = FiringOrigin + Vector3.up * sightHeight;
        Vector3 to = _enemy.transform.position + Vector3.up * sightHeight;
        TowerBuilding tower = OccupiedTower;
        if (!tower)
        {
            return !Physics.Linecast(from, to, GameLayers.OBSTACLE_MASK, QueryTriggerInteraction.Ignore);
        }
        // 탑승자의 사선은 자기 탑 몸체에 막히지 않는다. 그래야 탑에 붙은 적도 쏠 수 있다.
        Vector3 line = to - from;
        int count = Physics.RaycastNonAlloc(from, line.normalized, sightBuffer, line.magnitude, GameLayers.OBSTACLE_MASK, QueryTriggerInteraction.Ignore);
        for (int index = 0; index < count; index++)
        {
            if (sightBuffer[index].collider.GetComponentInParent<TowerBuilding>() != tower)
            {
                return false;
            }
        }
        return true;
    }

    void FireAt(EnemyUnit _enemy)
    {
        if (Time.time < nextFireTime)
        {
            return;
        }
        nextFireTime = Time.time + combat.attackInterval;
        float damage = ShotDamage;
        Vector3 direction = Vector3.ProjectOnPlane(_enemy.transform.position - transform.position, Vector3.up);
        if (direction.sqrMagnitude > 0)
        {
            visualBody.rotation = Quaternion.LookRotation(direction);
        }
        tracer.widthMultiplier = DamagePerSecond * tracerWidthPerDps;
        tracer.SetPosition(0, FiringOrigin + Vector3.up * sightHeight);
        tracer.SetPosition(1, _enemy.transform.position + Vector3.up * sightHeight);
        tracer.enabled = true;
        tracerEndTime = Time.time + tracerSeconds;
        _enemy.ApplyDamage(damage);
    }
}
