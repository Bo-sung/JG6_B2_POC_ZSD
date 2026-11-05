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

    readonly Collider[] targetBuffer = new Collider[TARGET_BUFFER_SIZE];
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

    public SurvivorState State { get; private set; }
    public bool AtWorkSlot => State.Activity == SurvivorActivity.Working && arrived;
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
                && Vector3.Distance(transform.position, station.StationPosition) <= arrivalTolerance ? station : null;
        }
    }

    public TowerBuilding OccupiedTower => ActiveStation as TowerBuilding;
    public float DamagePerSecond => (combat.baseDamage + combat.damagePerCombat * State.Combat)
        * (ActiveStation != null ? ActiveStation.DamageMultiplier : 1) / combat.attackInterval;

    public void LeaveTower(TowerBuilding _tower)
    {
        if (station == _tower)
        {
            station = null;
            RefreshTowerVisual();
        }
    }

    void LateUpdate()
    {
        if (State != null)
        {
            RefreshTowerVisual();
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
    public float AttackRange => combat.attackRange + (ActiveStation != null ? ActiveStation.RangeBonus : 0);

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
        if (State == null || !State.CanAct)
        {
            return;
        }
        switch (State.Activity)
        {
            case SurvivorActivity.Working:
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

    public void SetSelected(bool _selected)
    {
        selectionRing.SetActive(_selected);
        defenseMarker.SetHighlighted(_selected);
    }

    public void ShowDefenseMarker(Vector3 _position)
    {
        defenseMarker.Place(_position);
    }

    // ── 피해·사망 ──
    public void ApplyDamage(float _amount, string _cause)
    {
        if (IsProtected)
        {
            return;
        }
        if (State.ApplyDamage(_amount))
        {
            OnDied(_cause);
        }
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

    // ── 내부 동작 ──
    void MoveTo(Vector3 _position)
    {
        arrived = false;
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
        autoTarget = null;
        manualTarget = null;
    }

    // 막힌 위치를 지정해도 갈 수 있는 곳까지 가면 도착으로 본다(부분 경로의 끝).
    void UpdateArrival()
    {
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
        return !Physics.Linecast(from, to, GameLayers.OBSTACLE_MASK, QueryTriggerInteraction.Ignore);
    }

    void FireAt(EnemyUnit _enemy)
    {
        if (Time.time < nextFireTime)
        {
            return;
        }
        nextFireTime = Time.time + combat.attackInterval;
        float damage = combat.baseDamage + combat.damagePerCombat * State.Combat;
        if (ActiveStation != null)
        {
            damage *= ActiveStation.DamageMultiplier;
        }
        tracer.SetPosition(0, FiringOrigin + Vector3.up * sightHeight);
        tracer.SetPosition(1, _enemy.transform.position + Vector3.up * sightHeight);
        tracer.enabled = true;
        tracerEndTime = Time.time + tracerSeconds;
        _enemy.ApplyDamage(damage);
    }
}
