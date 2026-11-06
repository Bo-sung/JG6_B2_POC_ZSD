using UnityEngine;
using UnityEngine.AI;

// 적 한 기. 핵심 시설로 향하고, 사거리 내 생존자에게 대응하며, 경로가 막히면 가장 가까운 시설을 부순다.
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class EnemyUnit : MonoBehaviour
{
    // 동시에 탐지할 생존자 수의 상한. 수용 상한 9명이면 충분한 버퍼 크기이며 밸런스 수치가 아니다.
    const int TARGET_BUFFER_SIZE = 16;

    public enum Mode
    {
        Advance,
        Survivor,
        Breach,
        Core,
    }

    [SerializeField] string enemyId;
    // 밸런스가 아닌 판단 주기와 경로 보정값이다. 프리팹에서 조정한다.
    [SerializeField] float thinkInterval = 0.25f;
    [SerializeField] float navSampleRadius = 6f;
    [SerializeField] float meleeHeight = 0.5f;
    [SerializeField] float sightHeight = 1.5f;

    readonly Collider[] targetBuffer = new Collider[TARGET_BUFFER_SIZE];
    NavMeshAgent agent;
    NavMeshPath path;
    EnemyData data;
    GameSession session;
    SurvivorUnit survivorTarget;
    BuildingBase buildingTarget;
    bool targetingCore;
    float hp;
    float nextThinkTime;
    float nextAttackTime;
    bool halted;

    public string EnemyId => enemyId;
    public float Hp => hp;
    public bool IsDead => hp <= 0;
    public Mode CurrentMode { get; private set; }
    public BuildingBase BuildingTarget => buildingTarget;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        path = new NavMeshPath();
    }

    public void Initialize(EnemyData _data, GameSession _session, float _thinkOffset)
    {
        data = _data;
        session = _session;
        hp = _data.maxHp;
        agent.speed = _data.moveSpeed;
        nextThinkTime = Time.time + _thinkOffset * thinkInterval;
    }

    void Update()
    {
        if (data == null || IsDead || halted || !agent.isOnNavMesh)
        {
            return;
        }
        if (Time.time >= nextThinkTime)
        {
            nextThinkTime = Time.time + thinkInterval;
            Think();
        }
        UpdateAttack();
    }

    public void Halt()
    {
        halted = true;
        if (agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    public void ApplyDamage(float _amount)
    {
        if (IsDead)
        {
            return;
        }
        hp -= _amount;
        if (IsDead)
        {
            Destroy(gameObject);
        }
    }

    // 경로를 다시 계산한다. 방벽이 부서지거나 새로 막혀도 이 주기 안에 반영된다.
    void Think()
    {
        TowerBuilding tower = FindOccupiedTower(out Vector3 towerApproach);
        if (tower)
        {
            survivorTarget = null;
            buildingTarget = tower;
            targetingCore = false;
            CurrentMode = Mode.Breach;
            agent.isStopped = false;
            agent.SetDestination(towerApproach);
            return;
        }
        SurvivorUnit survivor = FindReachableSurvivor();
        if (survivor)
        {
            survivorTarget = survivor;
            buildingTarget = null;
            targetingCore = false;
            CurrentMode = Mode.Survivor;
            agent.SetDestination(survivor.transform.position);
            return;
        }
        survivorTarget = null;

        Vector3 coreGoal = ApproachPoint(session.Core.ClosestPoint(transform.position));
        if (agent.CalculatePath(coreGoal, path) && path.status == NavMeshPathStatus.PathComplete)
        {
            buildingTarget = null;
            targetingCore = true;
            CurrentMode = Mode.Core;
            agent.SetPath(path);
            return;
        }

        targetingCore = false;
        BuildingBase blocker = FindBlockingBuilding(out Vector3 approach);
        if (blocker)
        {
            buildingTarget = blocker;
            CurrentMode = Mode.Breach;
            agent.SetDestination(approach);
            return;
        }

        // 부술 시설도 없으면 갈 수 있는 곳까지 간다. 멈춘 전투는 시간 상한(DEC-15)이 끝낸다.
        buildingTarget = null;
        CurrentMode = Mode.Advance;
        agent.SetDestination(coreGoal);
    }

    void UpdateAttack()
    {
        if (survivorTarget)
        {
            bool inRange = survivorTarget.IsTargetable
                && HorizontalDistance(survivorTarget.transform.position) <= data.attackRange
                && !IsMeleeBlocked(survivorTarget.transform.position);
            if (inRange)
            {
                Strike(() => survivorTarget.ApplyDamage(data.damage, enemyId));
                return;
            }
        }
        else if (buildingTarget && HorizontalDistance(buildingTarget.ClosestPoint(transform.position)) <= data.attackRange
            && (!(buildingTarget is TowerBuilding) || HasMeleeAccess(buildingTarget)))
        {
            BuildingBase building = buildingTarget;
            Strike(() => building.ApplyDamage(data.damage * data.buildingDamageMultiplier));
            return;
        }
        else if (targetingCore && HorizontalDistance(session.Core.ClosestPoint(transform.position)) <= data.attackRange)
        {
            Strike(() => session.Core.ApplyDamage(data.damage));
            return;
        }
        agent.isStopped = false;
    }

    void Strike(System.Action _hit)
    {
        agent.isStopped = true;
        if (Time.time >= nextAttackTime)
        {
            nextAttackTime = Time.time + data.attackInterval;
            _hit();
        }
    }

    float HorizontalDistance(Vector3 _point)
    {
        Vector3 offset = _point - transform.position;
        offset.y = 0;
        return offset.magnitude;
    }

    // 낮은 방벽도 근접 공격은 막는다. 방벽 너머 생존자를 치려면 먼저 방벽을 부숴야 한다.
    bool IsMeleeBlocked(Vector3 _target)
    {
        Vector3 from = transform.position + Vector3.up * meleeHeight;
        Vector3 to = _target + Vector3.up * meleeHeight;
        return Physics.Linecast(from, to, GameLayers.OBSTACLE_MASK, QueryTriggerInteraction.Ignore);
    }

    // 지형 벽 너머는 보이지 않고, 보이더라도 걸어서 닿지 못하면(방벽 너머) 대상으로 삼지 않는다.
    // 어그로 범위에 행동 가능한 인력이 있으면 그쪽을 먼저 고르고, 없을 때만 쓰러진 인력을 친다(V2-IMPL-02).
    SurvivorUnit FindReachableSurvivor()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, data.aggroRange, targetBuffer, GameLayers.PLAYER_MASK, QueryTriggerInteraction.Ignore);
        SurvivorUnit nearest = null;
        float nearestDistance = float.MaxValue;
        bool nearestCanAct = false;
        for (int index = 0; index < count; index++)
        {
            SurvivorUnit survivor = targetBuffer[index].GetComponentInParent<SurvivorUnit>();
            if (!survivor || survivor.State == null || !survivor.IsTargetable)
            {
                continue;
            }
            bool canAct = survivor.State.CanAct;
            float distance = (survivor.transform.position - transform.position).sqrMagnitude;
            if (!InjuryRules.IsBetterTarget(canAct, distance, nearest, nearestCanAct, nearestDistance))
            {
                continue;
            }
            Vector3 from = transform.position + Vector3.up * sightHeight;
            Vector3 to = survivor.transform.position + Vector3.up * sightHeight;
            if (Physics.Linecast(from, to, GameLayers.OBSTACLE_MASK, QueryTriggerInteraction.Ignore))
            {
                continue;
            }
            if (agent.CalculatePath(survivor.transform.position, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                nearest = survivor;
                nearestDistance = distance;
                nearestCanAct = canAct;
            }
        }
        return nearest;
    }

    bool HasMeleeAccess(BuildingBase _building)
    {
        Vector3 from = transform.position + Vector3.up * meleeHeight;
        Vector3 to = _building.ClosestPoint(from);
        return !Physics.Linecast(from, to, out RaycastHit hit, GameLayers.OBSTACLE_MASK, QueryTriggerInteraction.Ignore)
            || hit.collider.GetComponentInParent<BuildingBase>() == _building;
    }

    TowerBuilding FindOccupiedTower(out Vector3 _approach)
    {
        TowerBuilding nearest = null;
        float best = data.aggroRange;
        _approach = transform.position;
        foreach (BuildingBase building in session.Buildings)
        {
            if (!(building is TowerBuilding tower) || !tower || !tower.IsOccupied)
            {
                continue;
            }
            float distance = HorizontalDistance(tower.ClosestPoint(transform.position));
            // 같은 지형 벽의 반대편 NavMesh를 잘못 골라 제자리에서 멈추지 않게 실제 탑승 접근 자리를 쓴다.
            Vector3 approach = tower.StationPosition;
            if (distance <= best && agent.CalculatePath(approach, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                best = distance;
                nearest = tower;
                _approach = approach;
            }
        }
        return nearest;
    }

    // 걸어서 닿을 수 있는 시설 중 경로가 가장 짧은 것을 고른다.
    BuildingBase FindBlockingBuilding(out Vector3 _approach)
    {
        BuildingBase best = null;
        float bestLength = float.MaxValue;
        _approach = transform.position;
        foreach (BuildingBase building in session.Buildings)
        {
            if (!building)
            {
                continue;
            }
            Vector3 approach = ApproachPoint(building.ClosestPoint(transform.position));
            if (!agent.CalculatePath(approach, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                continue;
            }
            float length = PathLength(path);
            if (length < bestLength)
            {
                best = building;
                bestLength = length;
                _approach = approach;
            }
        }
        return best;
    }

    Vector3 ApproachPoint(Vector3 _point)
    {
        return NavMesh.SamplePosition(_point, out NavMeshHit hit, navSampleRadius, agent.areaMask) ? hit.position : _point;
    }

    static float PathLength(NavMeshPath _path)
    {
        float length = 0;
        Vector3[] corners = _path.corners;
        for (int index = 1; index < corners.Length; index++)
        {
            length += Vector3.Distance(corners[index - 1], corners[index]);
        }
        return length;
    }
}
