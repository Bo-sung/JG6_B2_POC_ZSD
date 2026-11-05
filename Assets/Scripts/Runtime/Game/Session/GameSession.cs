using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 웨이브 결과 화면에 필요한 값을 한 번에 넘긴다.
public readonly struct WaveSummary
{
    public readonly int waveNumber;
    public readonly float coreHp;
    public readonly int alive;
    public readonly int scrap;
    public readonly string[] deaths;
    public readonly int timedOutEnemies;
    public readonly float timeoutDamage;

    public WaveSummary(int _waveNumber, float _coreHp, int _alive, int _scrap, string[] _deaths, int _timedOutEnemies, float _timeoutDamage)
    {
        waveNumber = _waveNumber;
        coreHp = _coreHp;
        alive = _alive;
        scrap = _scrap;
        deaths = _deaths;
        timedOutEnemies = _timedOutEnemies;
        timeoutDamage = _timeoutDamage;
    }
}

// 단계 전환, 승패, 하위 시스템 소유. 다른 컴포넌트와 UI는 이 클래스의 공개 API로만 상태를 바꾼다.
[DisallowMultipleComponent]
public sealed class GameSession : MonoBehaviour
{
    public const string CAUSE_DEBUG = "debug";
    public const string CAUSE_EXPEDITION = "expedition";
    const string REASON_CORE = "core_destroyed";
    const string REASON_WIPED = "all_survivors_dead";
    const string REASON_CLEARED = "all_waves_cleared";
    const string REASON_RESTART = "restart";
    const string STREAM_EXPEDITION = "expedition";
    const string STREAM_WAVE = "wave";

    [SerializeField] CoreFacility core;
    [SerializeField] ProductionFacility production;
    [SerializeField] WaveDirector waveDirector;
    [SerializeField] SurvivorUnit[] units = Array.Empty<SurvivorUnit>();
    [SerializeField] Transform[] defaultDefensePoints = Array.Empty<Transform>();
    [SerializeField] Transform returnPoint;
    [SerializeField] Transform buildingRoot;
    [SerializeField] BuildingBase wallPrefab;
    [SerializeField] BuildingBase towerPrefab;

    GameplayTableData table;
    SessionFlow flow;
    ResourceEconomy economy;
    AssignmentBoard board;
    ExpeditionSystem expeditions;
    int seed;
    readonly List<SurvivorState> survivors = new List<SurvivorState>();
    readonly Dictionary<string, SurvivorUnit> unitsById = new Dictionary<string, SurvivorUnit>(StringComparer.Ordinal);
    readonly List<BuildingBase> buildings = new List<BuildingBase>();
    readonly List<string> deathsThisWave = new List<string>();

    public int Seed => seed;
    public SessionPhase Phase => flow.Phase;
    public SessionResult Result => flow.Result;
    public int WaveNumber => flow.WaveNumber;
    public int WaveCount => flow.WaveCount;
    public float PhaseRemaining => flow.Remaining;
    public bool IsLastWave => flow.IsLastWave;
    public int Scrap => economy.Scrap;
    public string EndReason { get; private set; } = string.Empty;
    public WaveSummary LastWave { get; private set; }

    public CoreFacility Core => core;
    public ProductionFacility Production => production;
    public WaveDirector Waves => waveDirector;
    public AssignmentBoard Assignments => board;
    public ExpeditionSystem Expeditions => expeditions;
    public ResourceEconomy Economy => economy;
    public IReadOnlyList<SurvivorUnit> Units => units;
    public IReadOnlyList<BuildingBase> Buildings => buildings;
    public BuildingBase WallPrefab => wallPrefab;
    public BuildingBase TowerPrefab => towerPrefab;

    public int AliveCount
    {
        get
        {
            int count = 0;
            foreach (SurvivorState survivor in survivors)
            {
                if (survivor.IsAlive)
                {
                    count++;
                }
            }
            return count;
        }
    }

    public int AwayCount
    {
        get
        {
            int count = 0;
            foreach (SurvivorState survivor in survivors)
            {
                if (survivor.IsAway)
                {
                    count++;
                }
            }
            return count;
        }
    }

    public bool CanChangeWork => SessionFlow.CanChangeWork(flow.Phase);
    public bool CanChangeDefense => SessionFlow.CanChangeDefense(flow.Phase);
    public bool CanCommand => SessionFlow.CanCommand(flow.Phase);
    public bool CanBuild => SessionFlow.CanBuild(flow.Phase);
    public bool CanDispatch => SessionFlow.CanDispatch(flow.Phase);

    void Awake()
    {
        table = TableManager.Instance.RequireValid().Gameplay;
        seed = SessionLaunch.ResolveSeed(table.session.defaultSeed);
        var rng = new SessionRng(seed);
        flow = new SessionFlow(table.session.prepSeconds, table.session.alertSeconds, table.session.combatSeconds, table.session.timeoutCoreDamagePerEnemy, table.waves.Length);
        economy = new ResourceEconomy(table.session.startScrap, table.production.scrapPerSecondPerWork);
        board = new AssignmentBoard(table.production.maxWorkers);
        expeditions = new ExpeditionSystem(table.expeditions, rng.For(STREAM_EXPEDITION));
        core.Initialize(table.core.maxHp);
        production.Initialize();
        waveDirector.Initialize(this, rng.For(STREAM_WAVE));
        BindSurvivors();
    }

    void OnEnable()
    {
        GameEvents.SurvivorDied += OnSurvivorDied;
    }

    void OnDisable()
    {
        GameEvents.SurvivorDied -= OnSurvivorDied;
    }

    // LogGameLifecycle이 OnEnable에서 구독한 뒤에 발행한다.
    void Start()
    {
        GameEvents.RaiseSessionStarted(seed);
        foreach (SurvivorState survivor in survivors)
        {
            RaiseAssignment(survivor.Id);
        }
        EnterPrep();
    }

    void Update()
    {
        buildings.RemoveAll(_building => !_building);
        if (flow.Phase == SessionPhase.Ended || CheckDefeat())
        {
            return;
        }
        float deltaTime = Time.deltaTime;
        if (SessionFlow.RunsExpeditions(flow.Phase))
        {
            TickExpeditions(deltaTime);
        }
        if (SessionFlow.ProducesScrap(flow.Phase))
        {
            economy.TickProduction(deltaTime, ActiveWork());
        }
        bool expired = flow.Tick(deltaTime);
        switch (flow.Phase)
        {
            case SessionPhase.Prep:
                if (expired)
                {
                    EnterAlert();
                }
                break;
            case SessionPhase.Alert:
                if (expired)
                {
                    EnterCombat();
                }
                break;
            case SessionPhase.Combat:
                if (waveDirector.IsCleared)
                {
                    EnterResult(0, 0);
                }
                else if (expired)
                {
                    ResolveCombatTimeout();
                }
                break;
        }
        CheckDefeat();
    }

    void BindSurvivors()
    {
        SurvivorData[] roster = table.survivors;
        if (units.Length != roster.Length || defaultDefensePoints.Length < roster.Length)
        {
            throw new InvalidOperationException("씬의 생존자 또는 기본 방어 지점 수가 테이블과 다릅니다.");
        }
        for (int index = 0; index < roster.Length; index++)
        {
            var survivor = new SurvivorState(roster[index]);
            Vector3 point = defaultDefensePoints[index].position;
            survivors.Add(survivor);
            board.Register(survivor, new DefenseAssignment(point.x, point.z));
            unitsById.Add(survivor.Id, units[index]);
            units[index].Bind(survivor, table.survivorCombat);
            units[index].ShowDefenseMarker(point);
        }
        // 첫 준비에서 바로 고민할 수 있도록 작업 능력이 높은 순으로 생산 시설을 채운다. 플레이어가 바꿀 수 있다.
        var byWork = new List<SurvivorState>(survivors);
        byWork.Sort((_left, _right) => _right.Work.CompareTo(_left.Work));
        for (int index = 0; index < byWork.Count && index < board.MaxWorkers; index++)
        {
            board.TrySetWork(byWork[index].Id, WorkAssignment.Production, out _);
        }
    }

    // ── 단계 전환 ──
    void EnterPrep()
    {
        deathsThisWave.Clear();
        GameEvents.RaisePhaseChanged(flow.Phase, flow.WaveNumber);
        foreach (SurvivorUnit unit in units)
        {
            if (unit.State.CanAct)
            {
                ApplyPeacetimeOrder(unit);
            }
        }
    }

    void EnterAlert()
    {
        flow.Advance();
        GameEvents.RaisePhaseChanged(flow.Phase, flow.WaveNumber);
        foreach (SurvivorUnit unit in units)
        {
            if (unit.State.CanAct)
            {
                OrderToDefense(unit);
            }
        }
    }

    void EnterCombat()
    {
        flow.Advance();
        GameEvents.RaisePhaseChanged(flow.Phase, flow.WaveNumber);
        GameEvents.RaiseWaveStarted(flow.WaveNumber);
        waveDirector.BeginWave(table.waves[flow.WaveNumber - 1]);
    }

    void ResolveCombatTimeout()
    {
        int remaining = waveDirector.RemainingCount;
        float damage = flow.TimeoutCoreDamage(remaining);
        core.ApplyDamage(damage);
        waveDirector.ClearRemaining();
        if (core.IsDestroyed)
        {
            StoreSummary(remaining, damage);
            GameEvents.RaiseWaveEnded(flow.WaveNumber, core.Hp, AliveCount, economy.Scrap);
            EndSession(SessionResult.Defeat, REASON_CORE);
            return;
        }
        EnterResult(remaining, damage);
    }

    void EnterResult(int _timedOutEnemies, float _timeoutDamage)
    {
        flow.Advance();
        // POC 전용 단순화: 생존 인원은 결과 단계에서 전량 회복한다(DEC-09).
        foreach (SurvivorUnit unit in units)
        {
            unit.State.RestoreHp();
            if (unit.State.CanAct)
            {
                unit.Hold();
            }
        }
        StoreSummary(_timedOutEnemies, _timeoutDamage);
        GameEvents.RaisePhaseChanged(flow.Phase, flow.WaveNumber);
        GameEvents.RaiseWaveEnded(flow.WaveNumber, core.Hp, AliveCount, economy.Scrap);
    }

    void StoreSummary(int _timedOutEnemies, float _timeoutDamage)
    {
        LastWave = new WaveSummary(flow.WaveNumber, core.Hp, AliveCount, economy.Scrap, deathsThisWave.ToArray(), _timedOutEnemies, _timeoutDamage);
    }

    // 결과 화면의 다음 버튼. 세 번째 웨이브 뒤에는 승리로 끝난다.
    public void ConfirmResult()
    {
        if (flow.Phase != SessionPhase.Result)
        {
            return;
        }
        flow.Advance();
        if (flow.Phase == SessionPhase.Ended)
        {
            EndSession(SessionResult.Victory, REASON_CLEARED);
            return;
        }
        EnterPrep();
    }

    bool CheckDefeat()
    {
        if (flow.Phase == SessionPhase.Ended)
        {
            return true;
        }
        string reason = core.IsDestroyed ? REASON_CORE : SessionFlow.IsWiped(survivors) ? REASON_WIPED : null;
        if (reason == null)
        {
            return false;
        }
        if (flow.Phase == SessionPhase.Combat)
        {
            StoreSummary(0, 0);
            GameEvents.RaiseWaveEnded(flow.WaveNumber, core.Hp, AliveCount, economy.Scrap);
        }
        EndSession(SessionResult.Defeat, reason);
        return true;
    }

    void EndSession(SessionResult _result, string _reason)
    {
        if (_result == SessionResult.Defeat)
        {
            flow.EndDefeat();
        }
        EndReason = _reason;
        waveDirector.Halt();
        foreach (SurvivorUnit unit in units)
        {
            if (unit.State.CanAct)
            {
                unit.Hold();
            }
        }
        GameEvents.RaisePhaseChanged(flow.Phase, flow.WaveNumber);
        GameEvents.RaiseSessionEnded(_result, _reason);
    }

    // 같은 시드와 초기 구성으로 Game 씬을 다시 연다.
    public void Restart()
    {
        if (flow.Phase != SessionPhase.Ended)
        {
            GameEvents.RaiseSessionEnded(SessionResult.None, REASON_RESTART);
        }
        SessionLaunch.SetSeed(seed);
        SceneManager.LoadScene(TableManager.Instance.RestartSceneOf(SceneManager.GetActiveScene().name));
    }

    // ── 배정 ──
    public SurvivorUnit FindUnit(string _id) => _id != null && unitsById.TryGetValue(_id, out SurvivorUnit unit) ? unit : null;

    public bool TrySetWork(string _id, WorkAssignment _work, out string _error)
    {
        if (!CanChangeWork)
        {
            _error = "작업 배정은 준비 단계에만 바꿀 수 있습니다.";
            return false;
        }
        if (!board.TrySetWork(_id, _work, out _error))
        {
            return false;
        }
        RaiseAssignment(_id);
        ApplyPeacetimeOrder(unitsById[_id]);
        return true;
    }

    public bool TrySetDefensePoint(string _id, Vector3 _position, out string _error)
    {
        if (!CanChangeDefense)
        {
            _error = "결과 단계에서는 방어 위치를 바꿀 수 없습니다.";
            return false;
        }
        if (!board.TrySetDefensePoint(_id, _position.x, _position.z, out _error))
        {
            return false;
        }
        RaiseAssignment(_id);
        SurvivorUnit unit = unitsById[_id];
        unit.ShowDefenseMarker(DefensePosition(_id));
        if (unit.State.Activity == SurvivorActivity.Guarding)
        {
            OrderToDefense(unit);
        }
        return true;
    }

    // 감시탑 자리 배정. TowerBuilding은 ICombatStation을 구현하고 BuildingBase로 등록되면 된다.
    public bool TrySetDefenseStation(string _id, ICombatStation _station, out string _error)
    {
        if (!CanChangeDefense)
        {
            _error = "결과 단계에서는 방어 위치를 바꿀 수 없습니다.";
            return false;
        }
        if (_station == null)
        {
            _error = "감시탑이 지정되지 않았습니다.";
            return false;
        }
        Vector3 seat = _station.StationPosition;
        if (!board.TrySetDefenseTower(_id, _station.StationId, seat.x, seat.z, out _error))
        {
            return false;
        }
        RaiseAssignment(_id);
        SurvivorUnit unit = unitsById[_id];
        unit.ShowDefenseMarker(DefensePosition(_id));
        if (unit.State.Activity == SurvivorActivity.Guarding)
        {
            OrderToDefense(unit);
        }
        return true;
    }

    public Vector3 DefensePosition(string _id)
    {
        DefenseAssignment defense = board.DefenseOf(_id);
        return new Vector3(defense.x, 0, defense.z);
    }

    // ── 직접 명령 ──
    public bool CommandMove(string _id, Vector3 _position)
    {
        SurvivorUnit unit = FindUnit(_id);
        if (!CanCommand || unit == null || !unit.State.CanAct)
        {
            return false;
        }
        unit.CommandMove(_position);
        return true;
    }

    public bool CommandAttack(string _id, EnemyUnit _target)
    {
        SurvivorUnit unit = FindUnit(_id);
        if (!CanCommand || unit == null || !unit.State.CanAct || !_target)
        {
            return false;
        }
        unit.CommandAttack(_target);
        return true;
    }

    // 직접 명령을 풀고 단계에 맞는 저장된 배정으로 돌아간다.
    public bool ReturnToAssignment(string _id)
    {
        SurvivorUnit unit = FindUnit(_id);
        if (!CanCommand || unit == null || !unit.State.CanAct)
        {
            return false;
        }
        if (flow.Phase == SessionPhase.Prep)
        {
            ApplyPeacetimeOrder(unit);
        }
        else
        {
            OrderToDefense(unit);
        }
        return true;
    }

    void ApplyPeacetimeOrder(SurvivorUnit _unit)
    {
        string id = _unit.State.Id;
        if (board.WorkOf(id) == WorkAssignment.Production && production.TryClaim(id, out Vector3 slot))
        {
            _unit.OrderWork(slot);
            return;
        }
        production.Release(id);
        OrderToDefense(_unit);
    }

    void OrderToDefense(SurvivorUnit _unit)
    {
        string id = _unit.State.Id;
        _unit.OrderGuard(DefensePosition(id), FindStation(board.DefenseOf(id).towerId));
    }

    ICombatStation FindStation(string _towerId)
    {
        if (string.IsNullOrEmpty(_towerId))
        {
            return null;
        }
        foreach (BuildingBase building in buildings)
        {
            if (building && building is ICombatStation station && station.StationId == _towerId)
            {
                return station;
            }
        }
        return null;
    }

    int ActiveWork()
    {
        int total = 0;
        foreach (SurvivorUnit unit in units)
        {
            if (unit.State.Activity == SurvivorActivity.Working && unit.AtWorkSlot)
            {
                total += unit.State.Work;
            }
        }
        return total;
    }

    void RaiseAssignment(string _id)
    {
        DefenseAssignment defense = board.DefenseOf(_id);
        string spot = defense.IsTower ? "tower:" + defense.towerId : FormattableString.Invariant($"{defense.x:0.#},{defense.z:0.#}");
        GameEvents.RaiseAssignmentChanged(_id, board.WorkOf(_id).ToString(), spot);
    }

    // ── 탐사 ──
    // 탐사 패널이 호출한다. 준비 단계에만 시작할 수 있고 전투 중에도 계속 진행한다.
    public bool TryStartExpedition(string _regionId, IReadOnlyList<string> _ids, out string _error)
    {
        if (!CanDispatch)
        {
            _error = "탐사는 준비 단계에만 시작할 수 있습니다.";
            return false;
        }
        if (_ids == null)
        {
            _error = "파견 인원이 없습니다.";
            return false;
        }
        var members = new ExpeditionMember[_ids.Count];
        for (int index = 0; index < _ids.Count; index++)
        {
            SurvivorUnit unit = FindUnit(_ids[index]);
            if (unit == null || !unit.State.CanAct)
            {
                _error = "파견할 수 없는 인원입니다.";
                return false;
            }
            members[index] = new ExpeditionMember(unit.State.Id, unit.State.Exploration);
        }
        if (!expeditions.TryStart(_regionId, members, out _error))
        {
            return false;
        }
        var party = new string[_ids.Count];
        for (int index = 0; index < _ids.Count; index++)
        {
            party[index] = _ids[index];
            production.Release(_ids[index]);
            FindUnit(_ids[index]).BeginAway();
        }
        GameEvents.RaiseExpeditionStarted(_regionId, party);
        return true;
    }

    void TickExpeditions(float _deltaTime)
    {
        foreach (ExpeditionOutcome outcome in expeditions.Tick(_deltaTime))
        {
            economy.Add(outcome.reward);
            foreach (string id in outcome.casualties)
            {
                FindUnit(id).Kill(CAUSE_EXPEDITION);
            }
            foreach (string id in outcome.returned)
            {
                ReturnFromExpedition(id);
            }
            GameEvents.RaiseExpeditionResolved(outcome.regionId, outcome.reward, outcome.casualties, outcome.returned);
        }
    }

    void ReturnFromExpedition(string _id)
    {
        SurvivorUnit unit = FindUnit(_id);
        unit.EndAway(returnPoint.position);
        if (board.ResolveReturn(_id))
        {
            RaiseAssignment(_id);
        }
        if (flow.Phase == SessionPhase.Prep)
        {
            ApplyPeacetimeOrder(unit);
        }
        else
        {
            OrderToDefense(unit);
        }
    }

    // ── 건설 ──
    // 건설 패널과 PocDebug가 호출한다. 격자에 맞춘 위치에 프리팹을 놓는다. 허용 구역 판정은 BuildController 몫이다.
    public bool TryPlaceBuilding(BuildingType _type, Vector3 _position, Quaternion _rotation, out BuildingBase _building, out string _error)
    {
        _building = null;
        BuildingBase prefab = _type == BuildingType.Wall ? wallPrefab : towerPrefab;
        BuildingStatData stat = _type == BuildingType.Wall ? table.buildings.wall : table.buildings.tower;
        if (!CanBuild)
        {
            _error = "시설은 준비 단계에만 설치할 수 있습니다.";
            return false;
        }
        if (!prefab)
        {
            _error = "설치할 수 없는 시설입니다.";
            return false;
        }
        Vector3 snapped = SnapToGrid(_position);
        foreach (BuildingBase building in buildings)
        {
            // 격자 절반보다 가까우면 같은 칸이다. 밸런스 값이 아니라 격자 정의에서 나온 비율이다.
            if (building && Vector3.Distance(building.transform.position, snapped) < table.buildings.gridSize * 0.5f)
            {
                _error = "이미 시설이 있는 위치입니다.";
                return false;
            }
        }
        if (!economy.TrySpend(stat.cost))
        {
            _error = "스크랩이 부족합니다.";
            return false;
        }
        _building = Instantiate(prefab, snapped, _rotation, buildingRoot);
        _building.Initialize(stat);
        buildings.Add(_building);
        _error = null;
        GameEvents.RaiseBuildingPlaced(_type, snapped, stat.cost);
        return true;
    }

    public Vector3 SnapToGrid(Vector3 _position)
    {
        float grid = table.buildings.gridSize;
        return new Vector3(Mathf.Round(_position.x / grid) * grid, _position.y, Mathf.Round(_position.z / grid) * grid);
    }

    void OnSurvivorDied(GameEvents.SurvivorDeathInfo _info)
    {
        SurvivorUnit unit = FindUnit(_info.survivorId);
        production.Release(_info.survivorId);
        if (unit != null)
        {
            deathsThisWave.Add(unit.State.DisplayName);
        }
    }

    // ── 디버그(PocDebug 전용) ──
    public void DebugAdvancePhase()
    {
        switch (flow.Phase)
        {
            case SessionPhase.Prep:
                EnterAlert();
                break;
            case SessionPhase.Alert:
                EnterCombat();
                break;
            case SessionPhase.Combat:
                waveDirector.ClearRemaining();
                EnterResult(0, 0);
                break;
            case SessionPhase.Result:
                ConfirmResult();
                break;
        }
    }

    public void DebugExpireTimer() => flow.ExpireTimer();

    public void DebugKillSurvivor(string _id)
    {
        SurvivorUnit unit = FindUnit(_id);
        if (unit != null)
        {
            unit.Kill(CAUSE_DEBUG);
        }
    }

    public void DebugRestartWave()
    {
        if (flow.Phase == SessionPhase.Ended)
        {
            return;
        }
        waveDirector.ClearRemaining();
        flow.RestartWave();
        foreach (SurvivorUnit unit in units)
        {
            unit.State.RestoreHp();
        }
        EnterPrep();
    }
}
