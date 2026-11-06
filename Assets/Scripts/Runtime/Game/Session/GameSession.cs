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
    public readonly ExpeditionOutcome[] expeditions;
    public readonly int destroyedBuildings;
    public readonly int damagedBuildings;
    public readonly string[] growths;

    public WaveSummary(int _waveNumber, float _coreHp, int _alive, int _scrap, string[] _deaths, int _timedOutEnemies, float _timeoutDamage, ExpeditionOutcome[] _expeditions = null, int _destroyedBuildings = 0, int _damagedBuildings = 0, string[] _growths = null)
    {
        waveNumber = _waveNumber;
        coreHp = _coreHp;
        alive = _alive;
        scrap = _scrap;
        deaths = _deaths;
        timedOutEnemies = _timedOutEnemies;
        timeoutDamage = _timeoutDamage;
        expeditions = _expeditions ?? Array.Empty<ExpeditionOutcome>();
        destroyedBuildings = _destroyedBuildings;
        damagedBuildings = _damagedBuildings;
        growths = _growths ?? Array.Empty<string>();
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
    const string STREAM_RECRUIT = "recruit";
    const string STREAM_EXPEDITION_EVENT = "expedition_event";
    // ExpeditionEventDecision의 값. 기준서 7.4 로그의 decision이다.
    public const string DECISION_RESCUE = "rescue";
    public const string DECISION_SEARCH = "search";
    public const string DECISION_CONTINUE = "continue";
    public const string DECISION_WITHDRAW = "withdraw";
    public const string DECISION_TIMEOUT = "timeout";
    public const string DECISION_FORCED = "forced";

    [SerializeField] CoreFacility core;
    [SerializeField] ProductionFacility production;
    [SerializeField] InfirmaryFacility infirmary;
    [SerializeField] WaveDirector waveDirector;
    // 초기 인원은 씬에 배치하고, 합류 인력은 survivorPrefab을 joinPoint에 Instantiate한다.
    [SerializeField] List<SurvivorUnit> units = new List<SurvivorUnit>();
    [SerializeField] SurvivorUnit survivorPrefab;
    // 합류 인력은 씬에 표식이 없으므로 프리팹 표식을 새로 놓는다.
    [SerializeField] DefenseMarkerView defenseMarkerPrefab;
    [SerializeField] Transform survivorRoot;
    [SerializeField] Transform joinPoint;
    [SerializeField] Transform[] defaultDefensePoints = Array.Empty<Transform>();
    [SerializeField] Transform returnPoint;
    [SerializeField] Transform buildingRoot;
    [SerializeField] BuildingBase wallPrefab;
    [SerializeField] BuildingBase towerPrefab;

    // 설치 접촉면의 부동소수점 오차 보정. 밸런스 값이 아니다.
    [SerializeField] float placementSkin = 0.01f;
    // 지상 접근 자리의 충돌 조사 높이. 보행자 반경은 생존자 프리팹의 Agent에서 읽는다.
    [SerializeField] float stationAccessHeight = 0.5f;
    GameplayTableData table;
    SessionFlow flow;
    ResourceEconomy economy;
    AssignmentBoard board;
    ExpeditionSystem expeditions;
    RecruitmentSystem recruitment;
    ExperienceTracker experienceTracker;
    // Awake가 테이블 값으로 교체한다. 그 전에도 '치료 없음'으로 읽을 수 있게 비어 있는 시스템을 둔다.
    TreatmentSystem treatment = new TreatmentSystem(null);
    int seed;
    int buildingSequence;
    readonly Dictionary<string, (WorkAssignment work, DefenseAssignment defense)> loggedAssignments = new Dictionary<string, (WorkAssignment, DefenseAssignment)>();
    readonly List<SurvivorState> survivors = new List<SurvivorState>();
    readonly Dictionary<string, SurvivorUnit> unitsById = new Dictionary<string, SurvivorUnit>(StringComparer.Ordinal);
    readonly List<BuildingBase> buildings = new List<BuildingBase>();
    readonly List<ExpeditionOutcome> expeditionHistory = new List<ExpeditionOutcome>();
    readonly List<ExpeditionOutcome> expeditionsThisWave = new List<ExpeditionOutcome>();
    int destroyedThisWave;
    public IReadOnlyList<ExpeditionOutcome> ExpeditionHistory => expeditionHistory;
    public IReadOnlyList<ExpeditionOutcome> ExpeditionsThisWave => expeditionsThisWave;
    public int DestroyedThisWave
    {
        get
        {
            int count = destroyedThisWave;
            foreach (BuildingBase building in buildings)
            {
                // 파괴와 결과 전환이 같은 프레임이어도 아직 정리되지 않은 시설을 센다.
                if (!building || building.Hp <= 0)
                {
                    count++;
                }
            }
            return count;
        }
    }
    public int DamagedBuildings
    {
        get
        {
            int count = 0;
            foreach (BuildingBase building in buildings)
            {
                if (building && building.Hp > 0 && building.IsDamaged)
                {
                    count++;
                }
            }
            return count;
        }
    }
    readonly List<string> deathsThisWave = new List<string>();
    readonly List<string> growthsThisWave = new List<string>();

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
    public RecruitmentSystem Recruitment => recruitment;
    public TreatmentSystem Treatment => treatment;
    public ExperienceTracker Experience => experienceTracker;
    // 지금 치료가 진행 중인 인원. 일시정지 중에는 방어 등 다른 행동을 하므로 false다.
    public bool IsTreating(string _id) => treatment.Involves(_id) && !treatment.Current.IsPaused;
    public ResourceEconomy Economy => economy;
    public IReadOnlyList<SurvivorUnit> Units => units;
    public IReadOnlyList<BuildingBase> Buildings => buildings;
    public BuildingBase WallPrefab => wallPrefab;
    public BuildingBase TowerPrefab => towerPrefab;
    // 수용 상한 9명. HUD와 패널이 이 값을 표시한다.
    public int Capacity => recruitment.Capacity;

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

    public int CountInjury(InjuryStatus _status)
    {
        int count = 0;
        foreach (SurvivorState survivor in survivors)
        {
            if (survivor.Injury == _status)
            {
                count++;
            }
        }
        return count;
    }

    public float Speed { get; private set; } = 1;
    public IReadOnlyList<float> SpeedOptions => table.session.speedOptions;

    public bool SetSpeed(float _speed)
    {
        if (Array.IndexOf(table.session.speedOptions, _speed) < 0)
        {
            return false;
        }
        Speed = _speed;
        Time.timeScale = _speed;
        return true;
    }

    void OnDestroy()
    {
        Time.timeScale = 1;
    }

    public bool CanChangeWork => SessionFlow.CanChangeWork(flow.Phase);
    public bool CanChangeDefense => SessionFlow.CanChangeDefense(flow.Phase);
    public bool CanCommand => SessionFlow.CanCommand(flow.Phase);
    public bool CanBuild => SessionFlow.CanBuild(flow.Phase);
    public bool CanDispatch => SessionFlow.CanDispatch(flow.Phase);

    void Awake()
    {
        Time.timeScale = 1;
        table = TableManager.Instance.RequireValid().Gameplay;
        seed = SessionLaunch.ResolveSeed(table.session.defaultSeed);
        var rng = new SessionRng(seed);
        flow = new SessionFlow(table.session.prepSeconds, table.session.alertSeconds, table.session.combatSeconds, table.session.timeoutCoreDamagePerEnemy, table.waves.Length);
        economy = new ResourceEconomy(table.session.startScrap, table.production.scrapPerSecondPerWork);
        treatment = new TreatmentSystem(table.treatment);
        board = new AssignmentBoard(table.production.maxWorkers, treatment.Involves);
        expeditions = new ExpeditionSystem(table.expeditions, rng.For(STREAM_EXPEDITION), table.expeditionEvents, rng.For(STREAM_EXPEDITION_EVENT));
        recruitment = new RecruitmentSystem(table.recruitment, rng.For(STREAM_RECRUIT));
        experienceTracker = new ExperienceTracker(table.experience, _id => FindUnit(_id)?.State);
        core.Initialize(table.core.maxHp);
        BindProduction();
        BindInfirmary();
        waveDirector.Initialize(this, rng.For(STREAM_WAVE));
        BindSurvivors();
    }

    void BindProduction()
    {
        if (production.SlotCount < table.production.maxWorkers)
        {
            throw new InvalidOperationException("씬의 생산 작업 슬롯 수가 테이블의 maxWorkers보다 적습니다.");
        }
        production.Initialize();
    }

    // 수용 상한 전원이 중상이어도 대기 자리가 겹치지 않아야 한다(V2-IMPL-03).
    void BindInfirmary()
    {
        if (infirmary.WaitSlotCount < table.recruitment.capacity)
        {
            throw new InvalidOperationException("씬의 치료소 대기 자리 수가 테이블의 capacity보다 적습니다.");
        }
        infirmary.Initialize();
    }

    void OnEnable()
    {
        GameEvents.SurvivorDied += OnSurvivorDied;
        GameEvents.StatImproved += OnStatImproved;
    }

    void OnDisable()
    {
        GameEvents.SurvivorDied -= OnSurvivorDied;
        GameEvents.StatImproved -= OnStatImproved;
    }

    void OnStatImproved(GameEvents.StatImprovedInfo _info)
    {
        SurvivorUnit unit = FindUnit(_info.id);
        string name = unit != null && unit.State != null ? unit.State.DisplayName : _info.id;
        string statName = ExperienceTracker.StatDisplayName(TraitRules.ParseStat(_info.stat));
        growthsThisWave.Add($"{name} {statName} {_info.old}→{_info.@new}");
        // 사거리가 바뀌었다. 감시탑 보너스와 범위 중심은 배정에서 계산해야 맞는다.
        if (unit != null)
        {
            RefreshDefenseMarker(unit);
        }
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
        TickSession(Time.deltaTime);
    }

    void TickSession(float _deltaTime)
    {
        destroyedThisWave += buildings.RemoveAll(_building => !_building || _building.Hp <= 0);
        if (flow.Phase == SessionPhase.Ended || CheckDefeat())
        {
            return;
        }
        if (SessionFlow.RunsExpeditions(flow.Phase))
        {
            TickExpeditions(_deltaTime);
        }
        if (SessionFlow.ProducesScrap(flow.Phase))
        {
            economy.TickProduction(_deltaTime, ActiveWork());
            foreach (SurvivorUnit unit in units)
            {
                if (unit.AtWorkSlot)
                {
                    experienceTracker?.TickWorkTime(unit.State.Id, _deltaTime);
                }
            }
        }
        if (flow.Phase == SessionPhase.Prep)
        {
            TickTreatment(_deltaTime);
        }
        bool expired = flow.Tick(_deltaTime);
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
        // 직렬화된 units에는 씬에 배치한 초기 인원이 이미 들어 있다. 등록 경로가 다시 추가하지 않게 별도로 받아 둔다.
        var placed = new List<SurvivorUnit>(units);
        units.Clear();
        if (unitsById.Count != 0 || placed.Count != roster.Length || defaultDefensePoints.Length < roster.Length)
        {
            throw new InvalidOperationException("씬의 생존자 또는 기본 방어 지점 수가 테이블과 다릅니다.");
        }
        for (int index = 0; index < roster.Length; index++)
        {
            RegisterSurvivor(new SurvivorState(roster[index], _injury: table.injury, _traitMultiplier: table.experience.traitMultiplier), placed[index], defaultDefensePoints[index].position);
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
        growthsThisWave.Clear();
        expeditionsThisWave.Clear();
        destroyedThisWave = 0;
        experienceTracker?.ResetWave();
        GameEvents.RaisePhaseChanged(flow.Phase, flow.WaveNumber);
        // 방문 웨이브의 준비가 시작하면 방문자가 도착한다(기준서 4.1A).
        VisitorRecord arrived = recruitment.ArriveVisitor(flow.WaveNumber);
        if (arrived != null)
        {
            GameEvents.RaiseVisitorArrived(arrived.Candidate.id, arrived.Wave);
        }
        // 다른 인원이 생산 자리를 잡기 전에 중상자의 자리를 비우고 치료소로 옮긴다(V2-IMPL-03).
        foreach (SurvivorUnit unit in units)
        {
            if (unit.State.IsCritical && !unit.State.IsAway)
            {
                MoveToWard(unit);
            }
        }
        if (treatment.ResumeAtPrep())
        {
            GameEvents.RaiseTreatmentResumed(treatment.Current.Patient.Id, treatment.Current.Worker.Id);
        }
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
        // 준비 단계가 끝나면 미결정 방문자는 만료된다.
        VisitorRecord expired = recruitment.ExpireVisitor(flow.WaveNumber);
        if (expired != null)
        {
            GameEvents.RaiseRecruitmentRejected(RecruitmentSystem.REASON_EXPIRED);
        }
        // 치료는 진행률을 보존하고 멈춘다. 담당자와 경상 환자는 아래에서 방어로 가고, 중상 환자는 움직이지 않는다.
        if (treatment.Pause())
        {
            GameEvents.RaiseTreatmentPaused(treatment.Current.Patient.Id);
        }
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
        // HP와 부상은 웨이브를 넘어 이어진다(DEC-09 오버라이드).
        foreach (SurvivorUnit unit in units)
        {
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
        LastWave = new WaveSummary(flow.WaveNumber, core.Hp, AliveCount, economy.Scrap, deathsThisWave.ToArray(), _timedOutEnemies, _timeoutDamage, expeditionsThisWave.ToArray(), DestroyedThisWave, DamagedBuildings, growthsThisWave.ToArray());
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
                unit.Halt();
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
        RefreshDefenseMarker(unit);
        if (IsHeldByTreatment(_id))
        {
            _error = "치료 중 — 경보 때 방어 위치로 이동";
        }
        else if (Phase == SessionPhase.Prep && board.WorkOf(_id) == WorkAssignment.Production)
        {
            // 평시 생산은 유지한다. 임시 이동 중이었다면 생산 배정으로 복귀한다.
            if (unit.State.Activity != SurvivorActivity.Working)
            {
                ApplyPeacetimeOrder(unit);
            }
            _error = board.DefenseOf(_id).IsTower ? "생산 중 — 경보 때 탑으로 이동" : "생산 중 — 경보 때 방어 위치로 이동";
        }
        else
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
        if (_station == null || !(_station is BuildingBase building) || !building || building.Hp <= 0 || !buildings.Contains(building))
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
        RefreshDefenseMarker(unit);
        if (IsHeldByTreatment(_id))
        {
            _error = "치료 중 — 경보 때 탑으로 이동";
        }
        else if (Phase == SessionPhase.Prep && board.WorkOf(_id) == WorkAssignment.Production)
        {
            // 평시 생산은 유지한다. 임시 이동 중이었다면 생산 배정으로 복귀한다.
            if (unit.State.Activity != SurvivorActivity.Working)
            {
                ApplyPeacetimeOrder(unit);
            }
            _error = board.DefenseOf(_id).IsTower ? "생산 중 — 경보 때 탑으로 이동" : "생산 중 — 경보 때 방어 위치로 이동";
        }
        else
        {
            OrderToDefense(unit);
        }
        return true;
    }

    // 준비 단계에서 치료에 묶인 인원은 치료소를 떠나지 않는다. 경보 이후는 일시정지 상태라 방어 명령을 그대로 받는다.
    bool IsHeldByTreatment(string _id) => Phase == SessionPhase.Prep && treatment.Involves(_id);

    void RefreshDefenseMarker(SurvivorUnit _unit)
    {
        string id = _unit.State.Id;
        ICombatStation target = FindStation(board.DefenseOf(id).towerId);
        Vector3 point = target is TowerBuilding tower ? tower.transform.position : DefensePosition(id);
        _unit.ShowDefenseMarker(point, target != null ? target.RangeBonus : 0, DefensePosition(id));
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
        if (!CanCommand || unit == null || !unit.State.CanAct || IsHeldByTreatment(_id))
        {
            return false;
        }
        unit.CommandMove(_position);
        return true;
    }

    public bool CommandAttack(string _id, EnemyUnit _target)
    {
        SurvivorUnit unit = FindUnit(_id);
        if (!CanCommand || unit == null || !unit.State.CanAct || !_target || IsHeldByTreatment(_id))
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
        if (!_unit.State.CanAct)
        {
            return;
        }
        if (treatment.Involves(id))
        {
            OrderToTreatment(_unit);
            return;
        }
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

    // 생산에 기여하는 실효 작업량. 경상은 효율이 줄고, 중상·치료 중은 Working이 아니어서 빠진다.
    float ActiveWork()
    {
        float total = 0;
        foreach (SurvivorUnit unit in units)
        {
            if (unit.AtWorkSlot)
            {
                total += unit.State.EffectiveWork;
            }
        }
        return total;
    }

    void RaiseAssignment(string _id)
    {
        DefenseAssignment defense = board.DefenseOf(_id);
        WorkAssignment work = board.WorkOf(_id);
        if (loggedAssignments.TryGetValue(_id, out var previous) && previous.work == work
            && previous.defense.x == defense.x && previous.defense.z == defense.z && previous.defense.towerId == defense.towerId)
        {
            return;
        }
        loggedAssignments[_id] = (work, defense);
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
            if (treatment.Involves(_ids[index]))
            {
                _error = "치료 중인 인원은 치료를 취소해야 파견할 수 있습니다.";
                return false;
            }
            if (unit != null && unit.State.IsCritical)
            {
                _error = "중상인 인원은 파견할 수 없습니다.";
                return false;
            }
            if (unit == null || !unit.State.CanAct)
            {
                _error = "파견할 수 없는 인원입니다.";
                return false;
            }
            members[index] = new ExpeditionMember(unit.State.Id, unit.State.EffectiveExploration, TraitRules.HasTrait(unit.State.Trait, TraitRules.SCOUT));
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
        GameEvents.RaiseExpeditionStarted(_regionId, party, expeditions.Active[expeditions.Active.Count - 1].expeditionId);
        return true;
    }

    // 패널 미리보기가 쓰는 파티 탐사 합. 실제 판정과 같은 함수로 계산한다.
    public int PartyExploration(IReadOnlyList<string> _ids)
    {
        var members = new List<ExpeditionMember>();
        foreach (string id in _ids)
        {
            SurvivorUnit u = FindUnit(id);
            members.Add(new ExpeditionMember(id, u.State.EffectiveExploration, TraitRules.HasTrait(u.State.Trait, TraitRules.SCOUT)));
        }
        return ExpeditionSystem.TotalExploration(members);
    }

    void TickExpeditions(float _deltaTime)
    {
        List<ActiveExpedition> arrived = expeditions.Advance(_deltaTime);
        ReplaceRescuesWithoutCandidates();
        foreach (ActiveExpedition expedition in arrived)
        {
            GameEvents.RaiseExpeditionEventRaised(expedition.expeditionId, expedition.Event.Type.ToString().ToLowerInvariant());
            if (expedition.Event.Type == ExpeditionEventType.Encounter)
            {
                ResolveEncounter(expedition);
            }
        }
        foreach (ActiveExpedition expedition in expeditions.ExpiredEvents())
        {
            // 권위 경로 하나로 끝내므로 미응답도 로그와 정산이 같다.
            TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Timeout, out _);
        }
        SettleFinishedExpeditions();
    }

    bool IsSurvivorAlive(string _id) => FindUnit(_id).State.IsAlive;

    // 시간이 끝났거나 전멸한 탐사를 정산한다. 전멸 탐사는 사건 해결 직후에도 여기로 온다(V2-IMPL-13).
    void SettleFinishedExpeditions()
    {
        foreach (ExpeditionOutcome rolled in expeditions.CollectFinished(IsSurvivorAlive))
        {
            economy.Add(rolled.reward);
            foreach (string id in rolled.casualties)
            {
                FindUnit(id).Kill(CAUSE_EXPEDITION);
            }
            foreach (string id in rolled.returned)
            {
                ReturnFromExpedition(id);
            }
            // 구조 인력은 살아 돌아온 사람이 있을 때만 합류한다. 전멸이면 예약을 풀고 그 사람은 잃는다(기준서 4.1B).
            SurvivorState rescued = null;
            if (rolled.rescued != null)
            {
                recruitment.ReleaseSlot();
                if (rolled.returned.Length > 0)
                {
                    rescued = JoinSurvivor(rolled.rescued, RecruitmentSystem.SOURCE_RESCUE);
                }
            }
            // 귀환 정산 살아 있는 인원에게 탐사 XP 지급(TD-43).
            var livingReturned = new List<string>();
            foreach (string id in rolled.returned)
            {
                if (IsSurvivorAlive(id))
                {
                    livingReturned.Add(id);
                }
            }
            var expGrowths = new List<string>();
            experienceTracker?.SettleExpedition(livingReturned, rolled.respondedToEvent, rolled.hadEncounter, rolled.withdrawn, expGrowths);
            var outcome = new ExpeditionOutcome(rolled.regionId, rolled.reward, rolled.casualties, rolled.returned, rolled.expeditionId, rolled.withdrawn, rescued != null ? rolled.rescued : null, rolled.hadEncounter, rolled.respondedToEvent, expGrowths.ToArray());
            expeditionHistory.Add(outcome);
            expeditionsThisWave.Add(outcome);
            GameEvents.RaiseExpeditionResolved(outcome.regionId, outcome.reward, outcome.casualties, outcome.returned, outcome.expeditionId, outcome.withdrawn, rescued != null ? rescued.Id : null);
        }
    }

    // ── 탐사 사건 ──
    // 후보가 바닥나면 생존자 발견을 추가 보급품으로 대체한다(지시서 규칙).
    void ReplaceRescuesWithoutCandidates()
    {
        if (recruitment.RescueShareCount == 0)
        {
            expeditions.ReplacePendingRescues();
        }
    }

    // 사건 대응의 유일한 권위 경로. 패널 버튼과 미응답 타이머가 모두 여기로 온다.
    // 같은 결정이 두 번 와도 한 번만 정산하고 두 번째는 false다(기준서 7.2 항목 6). 거부되면 사건은 그대로 대기한다.
    public bool TryResolveExpeditionEvent(int _expeditionId, ExpeditionDecision _decision, out string _error)
    {
        ActiveExpedition expedition = expeditions.Find(_expeditionId);
        ExpeditionEvent current = expedition != null ? expedition.Event : null;
        if (current == null || !current.IsAwaitingDecision)
        {
            _error = "이미 끝난 사건입니다.";
            return false;
        }
        if (!SessionFlow.RunsExpeditions(flow.Phase))
        {
            _error = "결과 단계에서는 사건에 대응할 수 없습니다.";
            return false;
        }
        // 강제 조우 뒤에는 사건이 이미 해결됐고 철수 기회만 남아 있다(TD-34).
        if (current.IsWithdrawOffered)
        {
            if (_decision == ExpeditionDecision.Act)
            {
                _error = "강제 조우 뒤에는 임무 계속 또는 철수만 고를 수 있습니다.";
                return false;
            }
            current.IsWithdrawOffered = false;
            GameEvents.RaiseExpeditionEventDecided(_expeditionId, DecisionLabel(current, _decision));
            if (_decision == ExpeditionDecision.Withdraw)
            {
                expeditions.Withdraw(expedition);
            }
            _error = null;
            return true;
        }
        if (_decision == ExpeditionDecision.Act && current.Type == ExpeditionEventType.Rescue && !CheckRescueStart(current, out _error))
        {
            return false;
        }
        if (_decision != ExpeditionDecision.Timeout)
        {
            expedition.RespondedToEvent = true;
        }
        // 검사를 모두 지난 뒤에만 상태를 바꾼다. 여기서부터 이 사건은 해결이다.
        current.State = ExpeditionEventState.Resolved;
        GameEvents.RaiseExpeditionEventDecided(_expeditionId, DecisionLabel(current, _decision));
        switch (_decision)
        {
            case ExpeditionDecision.Act:
                if (current.Type == ExpeditionEventType.Rescue)
                {
                    AttemptRescue(expedition);
                }
                else
                {
                    ExtraSearch(expedition);
                }
                break;
            case ExpeditionDecision.Withdraw:
                expeditions.Withdraw(expedition);
                current.Outcome = ExpeditionEvent.OUTCOME_WITHDRAWN;
                break;
            default:
                current.Outcome = ExpeditionEvent.OUTCOME_CONTINUED;
                break;
        }
        GameEvents.RaiseExpeditionEventResolved(_expeditionId, current.Outcome);
        SettleFinishedExpeditions();
        _error = null;
        return true;
    }

    static string DecisionLabel(ExpeditionEvent _event, ExpeditionDecision _decision) => _decision switch
    {
        ExpeditionDecision.Act => _event.Type == ExpeditionEventType.Rescue ? DECISION_RESCUE : DECISION_SEARCH,
        ExpeditionDecision.Withdraw => DECISION_WITHDRAW,
        ExpeditionDecision.Timeout => DECISION_TIMEOUT,
        _ => DECISION_CONTINUE,
    };

    // 구조를 시작할 수 있는지 본다. 아무것도 소비하지 않는다. 정원이 차면 시작할 수 없고 사건은 남는다(기준서 4.1B).
    bool CheckRescueStart(ExpeditionEvent _event, out string _error)
    {
        if (recruitment.RescueShareCount == 0)
        {
            _event.Type = ExpeditionEventType.Supply;
            _error = "구조할 수 있는 사람이 없어 추가 보급품 수색으로 바뀌었습니다.";
            return false;
        }
        if (!recruitment.HasCapacity(AliveCount))
        {
            _error = "수용 정원이 찼습니다. 다른 대응을 고르세요.";
            GameEvents.RaiseRecruitmentRejected(RecruitmentSystem.REASON_FULL);
            return false;
        }
        _error = null;
        return true;
    }

    // 판정은 고른 순간 끝난다. 성공하면 자리와 후보를 바로 확보하고 합류는 귀환 정산에서 한다(V2-IMPL-11).
    void AttemptRescue(ActiveExpedition _expedition)
    {
        ExpeditionEventData data = table.expeditionEvents;
        ExpeditionEvent current = _expedition.Event;
        AddEventTime(_expedition, data.rescue.extraSeconds);
        if (current.OutcomeRoll < ExpeditionEventRules.RescueChance(data, PartyExploration(LivingParty(_expedition))))
        {
            recruitment.ReserveSlot(AliveCount);
            _expedition.RescuedCandidate = recruitment.TakeRescueCandidate();
            current.Outcome = ExpeditionEvent.OUTCOME_RESCUE_SUCCESS;
            ReplaceRescuesWithoutCandidates();
            return;
        }
        current.Outcome = ExpeditionEvent.OUTCOME_RESCUE_FAILED;
        HurtPartyMember(_expedition, ExpeditionEventRules.RescueFailureFraction(data));
    }

    void ExtraSearch(ActiveExpedition _expedition)
    {
        ExpeditionEventData data = table.expeditionEvents;
        ExpeditionEvent current = _expedition.Event;
        AddEventTime(_expedition, data.supply.extraSeconds);
        _expedition.BonusReward = ExpeditionEventRules.SearchBonus(data, expeditions.BaseRewardOf(_expedition, IsSurvivorAlive));
        bool hasScout = PartyHasScout(_expedition);
        if (current.OutcomeRoll < ExpeditionEventRules.SearchDamageChance(data, hasScout))
        {
            current.Outcome = ExpeditionEvent.OUTCOME_SEARCH_HURT;
            HurtPartyMember(_expedition, ExpeditionEventRules.SearchDamageFraction(data));
        }
        else
        {
            current.Outcome = ExpeditionEvent.OUTCOME_SEARCH_SAFE;
        }
    }

    // 강제 조우는 도착 순간 자동으로 끝난다. 피해와 지연을 적용하고, 전멸이 아니면 철수 기회를 연다(TD-33, TD-34).
    void ResolveEncounter(ActiveExpedition _expedition)
    {
        ExpeditionEventData data = table.expeditionEvents;
        ExpeditionEvent current = _expedition.Event;
        _expedition.HadEncounter = true;
        current.State = ExpeditionEventState.Resolved;
        current.Outcome = ExpeditionEvent.OUTCOME_ENCOUNTER;
        GameEvents.RaiseExpeditionEventDecided(_expedition.expeditionId, DECISION_FORCED);
        AddEventTime(_expedition, data.encounter.delaySeconds);
        bool hasScout = PartyHasScout(_expedition);
        HurtPartyMember(_expedition, ExpeditionEventRules.EncounterDamageFraction(data, PartyCombat(LivingParty(_expedition)), hasScout));
        GameEvents.RaiseExpeditionEventResolved(_expedition.expeditionId, current.Outcome);
        // 전멸이면 철수 기회 없이 이번 틱 끝에 정산된다.
        if (LivingParty(_expedition).Count > 0)
        {
            current.IsWithdrawOffered = true;
            current.TimeoutRemaining = data.timeoutSeconds;
        }
    }

    void AddEventTime(ActiveExpedition _expedition, float _seconds)
    {
        _expedition.remainingSeconds += _seconds;
        _expedition.Event.AddedSeconds = _seconds;
    }

    // 살아 있는 파티원 1명이 최대 HP × 비율의 피해를 입는다. 피해·사망은 SurvivorUnit의 기존 단일 경로로만 낸다(V2-IMPL-13).
    void HurtPartyMember(ActiveExpedition _expedition, float _fraction)
    {
        List<string> living = LivingParty(_expedition);
        if (living.Count == 0)
        {
            return;
        }
        string victimId = living[ExpeditionEventRules.PickVictim(_expedition.Event.VictimRoll, living.Count)];
        SurvivorUnit victim = FindUnit(victimId);
        float amount = victim.State.MaxHp * _fraction;
        _expedition.Event.VictimId = victimId;
        _expedition.Event.Damage = amount;
        victim.ApplyDamage(amount, CAUSE_EXPEDITION);
    }

    public List<string> LivingParty(ActiveExpedition _expedition)
    {
        var living = new List<string>();
        foreach (string id in _expedition.party)
        {
            if (IsSurvivorAlive(id))
            {
                living.Add(id);
            }
        }
        return living;
    }

    // 파티 실효 전투 합. 인원별 실효값을 더한다(V2-IMPL-07).
    public float PartyCombat(IReadOnlyList<string> _ids)
    {
        float total = 0;
        foreach (string id in _ids)
        {
            SurvivorState state = FindUnit(id).State;
            total += state.Combat * state.CombatEfficiency;
        }
        return total;
    }

    // 패널 표시와 실제 정산이 같은 값을 쓴다.
    public int BaseRewardOf(ActiveExpedition _expedition) => expeditions.BaseRewardOf(_expedition, IsSurvivorAlive);

    public float ReturnDeathChanceOf(ActiveExpedition _expedition) => expeditions.DeathChanceOf(_expedition, IsSurvivorAlive, LivingParty(_expedition).Count, PartyHasScout(_expedition));

    public bool PartyHasScout(ActiveExpedition _expedition)
    {
        if (_expedition == null)
        {
            return false;
        }
        foreach (string id in LivingParty(_expedition))
        {
            SurvivorUnit u = FindUnit(id);
            if (u != null && TraitRules.HasTrait(u.State.Trait, TraitRules.SCOUT))
            {
                return true;
            }
        }
        return false;
    }

    public int SearchBonusOf(ActiveExpedition _expedition) => ExpeditionEventRules.SearchBonus(table.expeditionEvents, BaseRewardOf(_expedition));

    // 지금 구조를 시도할 때의 성공률. 패널 표시와 실제 판정이 같은 값을 쓴다.
    public float RescueChanceOf(ActiveExpedition _expedition)
    {
        return ExpeditionEventRules.RescueChance(table.expeditionEvents, PartyExploration(LivingParty(_expedition)));
    }

    public float ExpeditionRemaining(string _id)
    {
        foreach (ActiveExpedition expedition in expeditions.Active)
        {
            foreach (string id in expedition.party)
            {
                if (id == _id)
                {
                    return expedition.remainingSeconds;
                }
            }
        }
        return 0;
    }

    public string RegionName(string _id)
    {
        foreach (ExpeditionRegionData region in table.expeditions)
        {
            if (region.id == _id)
            {
                return region.displayName;
            }
        }
        return _id;
    }

    public string SurvivorNames(string[] _ids)
    {
        var names = new List<string>();
        foreach (string id in _ids)
        {
            names.Add(FindUnit(id).State.DisplayName);
        }
        return names.Count > 0 ? string.Join(", ", names) : "없음";
    }

    public string DescribeExpedition(ExpeditionOutcome _outcome)
    {
        string reward = _outcome.returned.Length == 0 ? "귀환자 없음" : _outcome.withdrawn ? "철수 귀환" : _outcome.reward > 0 ? $"스크랩 +{_outcome.reward}" : "빈손 귀환";
        string rescued = _outcome.rescued != null ? $" · 구조 합류: {_outcome.rescued.displayName}" : string.Empty;
        string growths = _outcome.growths != null && _outcome.growths.Length > 0 ? $" · 성장: {string.Join(", ", _outcome.growths)}" : string.Empty;
        return $"{RegionName(_outcome.regionId)} · {reward}{rescued}{growths}\n귀환: {SurvivorNames(_outcome.returned)} / 사망: {SurvivorNames(_outcome.casualties)}";
    }

    void ReturnFromExpedition(string _id)
    {
        SurvivorUnit unit = FindUnit(_id);
        if (!unit.State.IsAlive)
        {
            return;
        }
        unit.EndAway(returnPoint.position);
        // 귀환 중상자는 준비 단계면 바로, 경보·전투면 다음 준비 시작에 치료소로 옮긴다(V2-IMPL-03).
        if (unit.State.IsCritical)
        {
            if (flow.Phase == SessionPhase.Prep)
            {
                MoveToWard(unit);
            }
            return;
        }
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

    // ── 공통 합류 경로 ──
    // 초기 인원과 합류 인원이 같은 등록 함수를 쓴다(새 인력도 완전히 같은 규칙, V2-DEC-04).
    // 합류 인원은 방어 위치가 합류 지점이지만 플레이어가 정한 것이 아니므로 '미지정' 상태로 둔다(V2-IMPL-08).
    void RegisterSurvivor(SurvivorState _survivor, SurvivorUnit _unit, Vector3 _defensePoint, bool _defenseAssigned = true)
    {
        survivors.Add(_survivor);
        board.Register(_survivor, new DefenseAssignment(_defensePoint.x, _defensePoint.z), _defenseAssigned);
        unitsById.Add(_survivor.Id, _unit);
        units.Add(_unit);
        _unit.Bind(_survivor, table.survivorCombat);
        _unit.ShowDefenseMarker(_defensePoint);
    }

    // 방문·구조·디버그가 모두 거치는 유일한 합류 경로. 합류 지점에 Survivor.prefab을 Instantiate하고
    // 합류 지점을 초기 방어 위치로 둔다(V2-IMPL-08). 상한·비용 판정은 호출 측에서 RecruitmentSystem으로 한다.
    // 후보 ID는 재사용하지 않고 세션 순번으로 새로 발급한다(V2-IMPL-09).
    SurvivorState JoinSurvivor(SurvivorData _candidate, string _source)
    {
        var data = new SurvivorData
        {
            id = recruitment.IssueNewId(),
            displayName = _candidate.displayName,
            combat = _candidate.combat,
            work = _candidate.work,
            exploration = _candidate.exploration,
            maxHp = _candidate.maxHp,
            trait = _candidate.trait,
        };
        var survivor = new SurvivorState(data, _newJoin: true, _injury: table.injury, _traitMultiplier: table.experience.traitMultiplier);
        SurvivorUnit unit = Instantiate(survivorPrefab, joinPoint.position, Quaternion.identity, survivorRoot);
        unit.name = "Survivor_Joined" + data.id;
        DefenseMarkerView marker = Instantiate(defenseMarkerPrefab, joinPoint.position, Quaternion.identity, survivorRoot);
        marker.name = "DefenseMarker_" + data.id;
        unit.SetDefenseMarker(marker);
        RegisterSurvivor(survivor, unit, joinPoint.position, _defenseAssigned: false);
        RaiseAssignment(survivor.Id);
        GameEvents.RaiseSurvivorJoined(survivor.Id, _source);
        if (flow.Phase == SessionPhase.Prep)
        {
            ApplyPeacetimeOrder(unit);
        }
        else
        {
            OrderToDefense(unit);
        }
        return survivor;
    }

    // 방문 영입의 유일한 권위 경로. 순서: 방문 대기 → 상한 → 스크랩 차감 → 공통 합류.
    // 실패하면 자원·인원이 바뀌지 않고 사유를 돌려준다. no_scrap·full은 방문자가 대기한 상태로 남는다.
    public bool TryRecruitVisitor(out string _error)
    {
        VisitorRecord visitor = recruitment.PendingVisitor;
        if (visitor == null)
        {
            _error = "대기 중인 방문자가 없습니다.";
            return false;
        }
        if (Phase != SessionPhase.Prep)
        {
            _error = "영입은 준비 단계에만 할 수 있습니다.";
            return false;
        }
        if (!recruitment.HasCapacity(AliveCount))
        {
            _error = "수용 정원이 찼습니다.";
            GameEvents.RaiseRecruitmentRejected(RecruitmentSystem.REASON_FULL);
            return false;
        }
        if (!economy.TrySpend(recruitment.VisitorCost))
        {
            _error = "스크랩이 부족합니다.";
            GameEvents.RaiseRecruitmentRejected(RecruitmentSystem.REASON_NO_SCRAP);
            return false;
        }
        JoinSurvivor(visitor.Candidate, RecruitmentSystem.SOURCE_VISITOR);
        visitor.Status = VisitorStatus.Accepted;
        GameEvents.RaiseRecruitmentAccepted(visitor.Candidate.id, recruitment.VisitorCost, AliveCount);
        _error = null;
        return true;
    }

    // 방문자를 보낸다. 대기 중일 때만 거절로 기록된다.
    public bool DeclineVisitor()
    {
        if (!recruitment.DeclineVisitor())
        {
            return false;
        }
        GameEvents.RaiseRecruitmentRejected(RecruitmentSystem.REASON_DECLINED);
        return true;
    }

    // ── 치료 ──
    // 치료 시작의 유일한 권위 경로. 실패하면 아무것도 바뀌지 않고 사유를 돌려준다(V2-IMPL-05).
    public bool TryStartTreatment(string _patientId, string _workerId, out string _error)
    {
        SurvivorUnit patient = FindUnit(_patientId);
        SurvivorUnit worker = FindUnit(_workerId);
        if (!treatment.TryStart(flow.Phase, patient != null ? patient.State : null, worker != null ? worker.State : null, economy, out string reason))
        {
            _error = SurvivorLabels.TreatmentReason(reason);
            GameEvents.RaiseTreatmentRejected(reason);
            return false;
        }
        GameEvents.RaiseTreatmentStarted(_patientId, _workerId, treatment.Cost);
        ApplyPeacetimeOrder(worker);
        ApplyPeacetimeOrder(patient);
        _error = null;
        return true;
    }

    // 담당자를 잃은 치료에 새 담당자를 지정한다. 비용을 다시 받지 않는다(V2-IMPL-06).
    public bool TryAssignTreatmentWorker(string _workerId, out string _error)
    {
        SurvivorUnit worker = FindUnit(_workerId);
        if (!treatment.TryAssignWorker(flow.Phase, worker != null ? worker.State : null, out string reason))
        {
            _error = SurvivorLabels.TreatmentReason(reason);
            GameEvents.RaiseTreatmentRejected(reason);
            return false;
        }
        GameEvents.RaiseTreatmentResumed(treatment.Current.Patient.Id, _workerId);
        ApplyPeacetimeOrder(worker);
        _error = null;
        return true;
    }

    // 플레이어 취소. 환불하지 않는다(V2-IMPL-06).
    public bool TryCancelTreatment(out string _error)
    {
        TreatmentJob ended = treatment.End();
        if (ended == null)
        {
            _error = SurvivorLabels.TreatmentReason(TreatmentSystem.REASON_NO_JOB);
            return false;
        }
        GameEvents.RaiseTreatmentCancelled(ended.Patient.Id, TreatmentSystem.END_PLAYER);
        ReleaseFromTreatment(ended);
        _error = null;
        return true;
    }

    void TickTreatment(float _deltaTime)
    {
        if (treatment.DropUnavailableWorker())
        {
            GameEvents.RaiseTreatmentPaused(treatment.Current.Patient.Id);
        }
        TreatmentJob job = treatment.Current;
        if (job == null || job.NeedsWorker)
        {
            return;
        }
        TreatmentJob done = treatment.Tick(_deltaTime, FindUnit(job.Worker.Id).AtTreatmentSlot);
        if (done != null)
        {
            GameEvents.RaiseTreatmentCompleted(done.Patient.Id, done.Patient.Hp);
            if (done.Worker != null)
            {
                experienceTracker?.RecordTreatmentComplete(done.Worker.Id);
            }
            ReleaseFromTreatment(done);
        }
    }

    // 치료가 끝나면 저장된 배정으로 돌아간다. 그동안 정원이 찼으면 방어 대기로 돌린다(V2-IMPL-04, 05).
    void ReleaseFromTreatment(TreatmentJob _job)
    {
        if (_job.Worker != null)
        {
            ReturnToDuty(_job.Worker.Id);
        }
        ReturnToDuty(_job.Patient.Id);
    }

    void ReturnToDuty(string _id)
    {
        SurvivorUnit unit = FindUnit(_id);
        // 아직 중상이면 대기 자리에 남고, 다음 치료를 기다린다.
        if (!unit.State.CanAct)
        {
            return;
        }
        infirmary.Release(_id);
        if (board.ResolveReturn(_id))
        {
            RaiseAssignment(_id);
        }
        if (flow.Phase == SessionPhase.Prep)
        {
            ApplyPeacetimeOrder(unit);
        }
    }

    void OrderToTreatment(SurvivorUnit _unit)
    {
        string id = _unit.State.Id;
        production.Release(id);
        if (treatment.Current.Worker == _unit.State)
        {
            _unit.OrderTreat(infirmary.WorkerPosition);
        }
        else if (infirmary.TryClaimWait(id, out Vector3 seat))
        {
            _unit.OrderTreat(seat);
        }
    }

    void MoveToWard(SurvivorUnit _unit)
    {
        string id = _unit.State.Id;
        production.Release(id);
        if (infirmary.TryClaimWait(id, out Vector3 seat))
        {
            _unit.PlaceAt(seat);
        }
    }

    // 사망한 환자의 치료는 환불 없이 끝난다. 담당자가 사망하면 멈추고 새 담당자를 기다린다.
    void OnTreatmentParticipantDied(string _id)
    {
        TreatmentJob job = treatment.Current;
        if (job == null)
        {
            return;
        }
        if (job.Patient.Id == _id)
        {
            treatment.End();
            GameEvents.RaiseTreatmentCancelled(_id, TreatmentSystem.END_PATIENT_DIED);
            ReleaseFromTreatment(job);
        }
        else if (job.Worker != null && job.Worker.Id == _id && treatment.DropUnavailableWorker())
        {
            GameEvents.RaiseTreatmentPaused(job.Patient.Id);
        }
    }

    // ── 건설 ──
    // 건설 패널과 PocDebug가 호출한다. 격자에 맞춘 위치에 프리팹을 놓는다. 허용 구역과 충돌은 모든 호출에서 동일하게 판정한다.
    public bool TryPlaceBuilding(BuildingType _type, Vector3 _position, Quaternion _rotation, out BuildingBase _building, out string _error)
    {
        _building = null;
        BuildingBase prefab = _type == BuildingType.Wall ? wallPrefab : towerPrefab;
        BuildingStatData stat = _type == BuildingType.Wall ? table.buildings.wall : table.buildings.tower;
        Vector3 snapped = SnapToGrid(_position);
        if (!CanPlaceBuilding(_type, snapped, _rotation, out _error))
        {
            return false;
        }
        if (!economy.TrySpend(stat.cost))
        {
            _error = "스크랩이 부족합니다.";
            return false;
        }
        _building = Instantiate(prefab, snapped, _rotation, buildingRoot);
        _building.Initialize(stat);
        string buildingId = (++buildingSequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (_building is TowerBuilding tower)
        {
            tower.SetStationId(buildingId);
        }
        buildings.Add(_building);
        _error = null;
        GameEvents.RaiseBuildingPlaced(_type, snapped, stat.cost, buildingId);
        return true;
    }

    public int BuildingCost(BuildingType _type) => _type == BuildingType.Wall ? table.buildings.wall.cost : table.buildings.tower.cost;

    public bool CanPlaceBuilding(BuildingType _type, Vector3 _position, Quaternion _rotation, out string _error)
    {
        if (!CanBuild)
        {
            _error = "시설은 준비 단계에만 설치할 수 있습니다.";
            return false;
        }
        BuildingBase prefab = _type == BuildingType.Wall ? wallPrefab : towerPrefab;
        if (!prefab || !prefab.Footprint)
        {
            _error = "설치할 수 없는 시설입니다.";
            return false;
        }
        Vector3 point = SnapToGrid(_position);
        prefab.PlacementBox(point, _rotation, out Vector3 center, out Vector3 half, out Quaternion orientation);
        BuildZone[] zones = FindObjectsByType<BuildZone>(FindObjectsSortMode.None);
        // 모든 풋프린트 모서리가 허용 영역 안이어야 한다. 지형 충돌은 별도로 검사한다.
        for (int x = -1; x <= 1; x += 2)
        {
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = center + orientation * new Vector3(half.x * x, 0, half.z * z);
                bool inside = false;
                foreach (BuildZone zone in zones)
                {
                    inside |= zone.Contains(corner);
                }
                if (!inside)
                {
                    _error = "설치 허용 구역 밖입니다.";
                    return false;
                }
            }
        }
        // 표시 메시가 탑 위로 이동해도 생존자의 지상 점유는 유지한다.
        foreach (SurvivorUnit unit in units)
        {
            // 쓰러진 인력도 그 자리를 차지한다.
            if (!unit.State.IsAlive || unit.State.IsAway)
            {
                continue;
            }
            Vector3 local = Quaternion.Inverse(orientation) * (unit.transform.position - center);
            float dx = Mathf.Max(0, Mathf.Abs(local.x) - half.x);
            float dz = Mathf.Max(0, Mathf.Abs(local.z) - half.z);
            float radius = unit.GetComponent<UnityEngine.AI.NavMeshAgent>().radius;
            if (dx * dx + dz * dz < radius * radius)
            {
                _error = "생존자가 있는 위치입니다.";
                return false;
            }
        }
        // 기존 탑의 접근 자리는 시설 몸체 밖이어도 보행자 반경만큼 비워 둔다.
        foreach (BuildingBase existing in buildings)
        {
            if (!(existing is TowerBuilding existingTower) || !existing || existing.Hp <= 0)
            {
                continue;
            }
            Vector3 local = Quaternion.Inverse(orientation) * (existingTower.StationPosition - center);
            float dx = Mathf.Max(0, Mathf.Abs(local.x) - half.x);
            float dz = Mathf.Max(0, Mathf.Abs(local.z) - half.z);
            float radius = units[0].GetComponent<UnityEngine.AI.NavMeshAgent>().radius;
            if (dx * dx + dz * dz < radius * radius)
            {
                _error = "감시탑의 지상 접근 자리를 비워 주세요.";
                return false;
            }
        }
        // 입력 Update와 물리 틱 사이에 이동한 시설도 동일하게 검사한다.
        Physics.SyncTransforms();
        // 접한 면은 허용하고 겹친 부피만 거부한다. 보정값은 씬에 직렬화한다.
        Collider[] hits = Physics.OverlapBox(center, Vector3.Max(Vector3.zero, half - Vector3.one * placementSkin), orientation,
            GameLayers.OBSTACLE_MASK | GameLayers.PLAYER_MASK, QueryTriggerInteraction.Ignore);
        foreach (Collider hit in hits)
        {
            SurvivorUnit survivor = hit.GetComponentInParent<SurvivorUnit>();
            _error = survivor ? "생존자가 있는 위치입니다." : "시설 또는 지형과 겹칩니다.";
            return false;
        }
        if (prefab is TowerBuilding tower)
        {
            Vector3 access = point + _rotation * prefab.transform.InverseTransformPoint(tower.StationPosition);
            float radius = units[0].GetComponent<UnityEngine.AI.NavMeshAgent>().radius;
            if (Physics.CheckSphere(access + Vector3.up * stationAccessHeight, radius, GameLayers.OBSTACLE_MASK, QueryTriggerInteraction.Ignore))
            {
                _error = "감시탑의 지상 접근 자리가 막혀 있습니다.";
                return false;
            }
        }
        if (Scrap < BuildingCost(_type))
        {
            _error = "스크랩이 부족합니다.";
            return false;
        }
        _error = null;
        return true;
    }

    public bool TryRepairBuilding(BuildingBase _building, out string _error)
    {
        if (!CanBuild)
        {
            _error = "수리는 준비 단계에만 가능합니다.";
            return false;
        }
        if (!_building || _building.Hp <= 0 || !buildings.Contains(_building))
        {
            _error = "수리할 시설이 없습니다. 파괴된 시설은 재설치하세요.";
            return false;
        }
        if (!_building.IsDamaged)
        {
            _error = "손상되지 않은 시설입니다.";
            return false;
        }
        if (!economy.TrySpend(_building.RepairCost))
        {
            _error = "수리 스크랩이 부족합니다.";
            return false;
        }
        _building.Repair();
        _error = null;
        return true;
    }

    public void VacateTower(TowerBuilding _tower)
    {
        var affected = new List<string>();
        foreach (SurvivorUnit unit in units)
        {
            if (board.DefenseOf(unit.State.Id).towerId == _tower.StationId)
            {
                affected.Add(unit.State.Id);
            }
            unit.LeaveTower(_tower);
        }
        board.VacateTower(_tower.StationId);
        foreach (string id in affected)
        {
            RaiseAssignment(id);
            RefreshDefenseMarker(FindUnit(id));
        }
    }

    public Vector3 SnapToGrid(Vector3 _position)
    {
        float grid = table.buildings.gridSize;
        return new Vector3(Mathf.Round(_position.x / grid) * grid, 0, Mathf.Round(_position.z / grid) * grid);
    }

    void OnSurvivorDied(GameEvents.SurvivorDeathInfo _info)
    {
        SurvivorUnit unit = FindUnit(_info.survivorId);
        production.Release(_info.survivorId);
        if (infirmary)
        {
            infirmary.Release(_info.survivorId);
        }
        if (unit != null)
        {
            deathsThisWave.Add(unit.State.DisplayName);
        }
        OnTreatmentParticipantDied(_info.survivorId);
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

    // 디버그 전용: 실제 피해 경로로 HP를 낮춘다. 올리는 경로는 치료뿐이다.
    public void DebugLowerHp(string _id, float _hp)
    {
        SurvivorUnit unit = FindUnit(_id);
        if (unit != null)
        {
            unit.ApplyDamage(unit.State.Hp - _hp, CAUSE_DEBUG);
        }
    }

    // 디버그 전용: 구조 몫 후보를 공통 합류 경로로 바로 붙인다. 상한 판정은 방문 영입과 같다.
    public bool DebugJoinSurvivor(out string _error)
    {
        if (!recruitment.HasCapacity(AliveCount))
        {
            _error = "수용 정원이 찼습니다.";
            return false;
        }
        SurvivorData candidate = recruitment.TakeRescueCandidate();
        if (candidate == null)
        {
            _error = "남은 후보가 없습니다.";
            return false;
        }
        JoinSurvivor(candidate, CAUSE_DEBUG);
        _error = null;
        return true;
    }

    // 디버그 전용: 진행 중인 첫 탐사에 지정한 사건을 일으킨다. 다음 틱에 정규 경로로 도착한다.
    // _outcomeRoll은 0~1 값이고 구조·수색의 성패를 정한다. 음수면 파견 때 뽑아 둔 값을 쓴다.
    public bool DebugRaiseExpeditionEvent(ExpeditionEventType _type, out string _error, double _outcomeRoll = -1)
    {
        foreach (ActiveExpedition expedition in expeditions.Active)
        {
            if (expeditions.ForceEvent(expedition, _type, _outcomeRoll))
            {
                _error = null;
                return true;
            }
        }
        _error = "사건을 일으킬 탐사가 없습니다.";
        return false;
    }

    public void DebugRestartWave()
    {
        if (flow.Phase == SessionPhase.Ended)
        {
            return;
        }
        waveDirector.ClearRemaining();
        flow.RestartWave();
        TreatmentJob job = treatment.End();
        if (job != null)
        {
            GameEvents.RaiseTreatmentCancelled(job.Patient.Id, TreatmentSystem.END_DEBUG);
        }
        // 디버그 재시작은 같은 웨이브를 같은 조건으로 다시 시험하는 기능이라 HP 복구를 남긴다. 정규 진행에는 쓰지 않는다.
        foreach (SurvivorUnit unit in units)
        {
            unit.State.RestoreHp();
            if (infirmary)
            {
                infirmary.Release(unit.State.Id);
            }
        }
        EnterPrep();
    }

    public void DebugAddExperience(string _id, StatType _stat, int _amount)
    {
        experienceTracker?.AddDebugExperience(_id, _stat, _amount);
    }
}
