using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

// G5 6웨이브와 결과: 테이블, 승리 조건, W4 방문, 이월 항목별, WaveEnded·SessionEnded 값, 누적 기록, 엣지 8 우선순위, 7.4 로그, 패널 길이.
public sealed class SixWaveResultTests
{
    const BindingFlags PRIVATE = BindingFlags.NonPublic | BindingFlags.Instance;
    const double ROLL_LOW = 0;
    const int SEED = 777;
    const int WAVE_COUNT = 6;
    const string BASIC = "basic";
    const string BREAKER = "breaker";
    const string WEST = "west";
    const string EAST = "east";
    // 기준 해상도의 상단바 폭. 프리팹 CanvasScaler의 referenceResolution과 같다.
    const float HUD_WIDTH = 1920;

    static void Set(object _target, string _field, object _value)
    {
        _target.GetType().GetField(_field, PRIVATE).SetValue(_target, _value);
    }
    static object Get(object _target, string _field)
    {
        return _target.GetType().GetField(_field, PRIVATE).GetValue(_target);
    }
    static object Invoke(object _target, string _method, params object[] _args)
    {
        return _target.GetType().GetMethod(_method, PRIVATE).Invoke(_target, _args);
    }

    static GameplayTableData Table => TableManager.Instance.RequireValid().Gameplay;

    GameObject host;
    GameSession session;
    SessionFlow flow;
    CoreFacility core;
    WaveDirector director;
    string logFolder;
    readonly List<GameEvents.WaveEndInfo> waveEnded = new List<GameEvents.WaveEndInfo>();
    readonly List<GameEvents.SessionEndInfo> sessionEnded = new List<GameEvents.SessionEndInfo>();
    readonly List<GameEvents.VisitorArrivedInfo> arrived = new List<GameEvents.VisitorArrivedInfo>();
    readonly List<GameEvents.SurvivorJoinedInfo> joined = new List<GameEvents.SurvivorJoinedInfo>();
    readonly List<string> rejected = new List<string>();
    readonly List<SessionPhase> phases = new List<SessionPhase>();
    Action<GameEvents.WaveEndInfo> onWaveEnded;
    Action<GameEvents.SessionEndInfo> onSessionEnded;
    Action<GameEvents.VisitorArrivedInfo> onArrived;
    Action<GameEvents.SurvivorJoinedInfo> onJoined;
    Action<GameEvents.RecruitmentRejectedInfo> onRejected;
    Action<GameEvents.PhaseInfo> onPhase;

    static ExpeditionRegionData[] QuietRegions(float _death)
    {
        var regions = new ExpeditionRegionData[Table.expeditions.Length];
        for (int index = 0; index < regions.Length; index++)
        {
            ExpeditionRegionData region = JsonUtility.FromJson<ExpeditionRegionData>(JsonUtility.ToJson(Table.expeditions[index]));
            region.eventChance = 0;
            region.rewardChanceBase = 1;
            region.rewardChancePerExploration = 0;
            region.deathChanceBase = _death;
            region.deathChancePerExploration = 0;
            region.deathChancePerExtraMember = 0;
            regions[index] = region;
        }
        return regions;
    }

    // 실제 Survivor.prefab으로 만든 세션을 1웨이브 준비로 시작한다. 스폰은 하지 않고 단계 함수만 부른다.
    void BuildSession(int _scrap = 20, float _regionDeath = 0)
    {
        host = new GameObject("Fixture");
        host.SetActive(false);
        session = host.AddComponent<GameSession>();
        GameplayTableData table = Table;
        var treatment = new TreatmentSystem(table.treatment);
        flow = new SessionFlow(table.session.prepSeconds, table.session.alertSeconds, table.session.combatSeconds, table.session.timeoutCoreDamagePerEnemy, table.waves.Length);
        Set(session, "table", table);
        Set(session, "seed", SEED);
        Set(session, "flow", flow);
        Set(session, "economy", new ResourceEconomy(_scrap, table.production.scrapPerSecondPerWork));
        Set(session, "treatment", treatment);
        var board = new AssignmentBoard(table.production.maxWorkers, treatment.Involves);
        Set(session, "board", board);
        Set(session, "recruitment", new RecruitmentSystem(table.recruitment, new System.Random(11)));
        Set(session, "expeditions", new ExpeditionSystem(QuietRegions(_regionDeath), new System.Random(1), table.expeditionEvents, new System.Random(2)));
        Set(session, "experienceTracker", new ExperienceTracker(table.experience, _id => session.FindUnit(_id)?.State));
        Set(session, "record", new SessionRecord(table.survivors.Length));
        core = host.AddComponent<CoreFacility>();
        core.Initialize(table.core.maxHp);
        Set(session, "core", core);
        director = host.AddComponent<WaveDirector>();
        Set(session, "waveDirector", director);
        var production = host.AddComponent<ProductionFacility>();
        Set(production, "workSlots", new[] { Point("WorkA", 1, 0), Point("WorkB", 2, 0) });
        production.Initialize();
        Set(session, "production", production);
        var infirmary = host.AddComponent<InfirmaryFacility>();
        Set(infirmary, "workerSlot", Point("TreatSlot", 50, 50));
        var seats = new Transform[table.recruitment.capacity];
        for (int index = 0; index < seats.Length; index++)
        {
            seats[index] = Point("Seat" + index, 60 + index * 2, 60);
        }
        Set(infirmary, "waitSlots", seats);
        infirmary.Initialize();
        Set(session, "infirmary", infirmary);
        Set(session, "returnPoint", Point("Return", 3, 3));

        var prefab = AssetDatabase.LoadAssetAtPath<SurvivorUnit>("Assets/Prefabs/Survivor.prefab");
        var markerPrefab = AssetDatabase.LoadAssetAtPath<DefenseMarkerView>("Assets/Prefabs/DefenseMarker.prefab");
        var units = new List<SurvivorUnit>();
        var map = (Dictionary<string, SurvivorUnit>)Get(session, "unitsById");
        var survivors = (List<SurvivorState>)Get(session, "survivors");
        for (int index = 0; index < table.survivors.Length; index++)
        {
            SurvivorUnit unit = UnityEngine.Object.Instantiate(prefab, host.transform);
            unit.SetDefenseMarker(UnityEngine.Object.Instantiate(markerPrefab, host.transform));
            var state = new SurvivorState(table.survivors[index], _injury: table.injury, _traitMultiplier: table.experience.traitMultiplier);
            unit.Bind(state, table.survivorCombat);
            units.Add(unit);
            survivors.Add(state);
            board.Register(state, new DefenseAssignment(index, 10));
            map.Add(state.Id, unit);
        }
        Set(session, "units", units);
        Set(session, "survivorPrefab", prefab);
        Set(session, "defenseMarkerPrefab", markerPrefab);
        Set(session, "survivorRoot", host.transform);
        Set(session, "joinPoint", Point("Join", 5, 5));

        waveEnded.Clear();
        sessionEnded.Clear();
        arrived.Clear();
        joined.Clear();
        rejected.Clear();
        phases.Clear();
        onWaveEnded = _info => waveEnded.Add(_info);
        onSessionEnded = _info => sessionEnded.Add(_info);
        onArrived = _info => arrived.Add(_info);
        onJoined = _info => joined.Add(_info);
        onRejected = _info => rejected.Add(_info.reason);
        onPhase = _info => phases.Add(_info.phase);
        GameEvents.WaveEnded += onWaveEnded;
        GameEvents.SessionEnded += onSessionEnded;
        GameEvents.VisitorArrived += onArrived;
        GameEvents.SurvivorJoined += onJoined;
        GameEvents.RecruitmentRejected += onRejected;
        GameEvents.PhaseChanged += onPhase;
        // 사망·성장 구독은 OnEnable에서 한다. 비활성 픽스처라 직접 연결한다.
        Invoke(session, "OnEnable");
        Invoke(session, "EnterPrep");
    }

    Transform Point(string _name, float _x, float _z)
    {
        var point = new GameObject(_name).transform;
        point.SetParent(host.transform);
        point.position = new Vector3(_x, 0, _z);
        return point;
    }

    [TearDown]
    public void Teardown()
    {
        if (onWaveEnded != null)
        {
            GameEvents.WaveEnded -= onWaveEnded;
            GameEvents.SessionEnded -= onSessionEnded;
            GameEvents.VisitorArrived -= onArrived;
            GameEvents.SurvivorJoined -= onJoined;
            GameEvents.RecruitmentRejected -= onRejected;
            GameEvents.PhaseChanged -= onPhase;
            onWaveEnded = null;
        }
        if (session != null)
        {
            Invoke(session, "OnDisable");
        }
        if (host != null)
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
        if (logFolder != null && Directory.Exists(logFolder))
        {
            Directory.Delete(logFolder, true);
        }
        logFolder = null;
    }

    // 경보를 거쳐 전투로 간다. 스폰은 건너뛰므로 적은 없다.
    void GoToCombat()
    {
        Invoke(session, "EnterAlert");
        flow.Advance();
        Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Combat));
    }

    void GoToResult()
    {
        GoToCombat();
        Invoke(session, "EnterResult", 0, 0f);
        Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Result));
    }

    void NextWave()
    {
        GoToResult();
        session.ConfirmResult();
    }

    void GoToWave(int _wave)
    {
        while (flow.WaveNumber < _wave)
        {
            NextWave();
        }
        Assert.That(flow.WaveNumber, Is.EqualTo(_wave));
        Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Prep));
    }

    void TickSession(float _seconds) => Invoke(session, "TickSession", _seconds);

    SurvivorState StateOf(string _id) => session.FindUnit(_id).State;

    ActiveExpedition Dispatch(params string[] _ids)
    {
        Assert.That(session.TryStartExpedition(Table.expeditions[0].id, _ids, out string error), Is.True, error);
        return session.Expeditions.Active[session.Expeditions.Active.Count - 1];
    }

    void RaiseEvent(ExpeditionEventType _type, double _roll = -1)
    {
        Assert.That(session.DebugRaiseExpeditionEvent(_type, out string error, _roll), Is.True, error);
        Invoke(session, "TickExpeditions", 0f);
    }

    // 남은 적 1기를 대기열에 넣는다. 시간 초과 판정은 RemainingCount만 본다.
    void AddPendingEnemy()
    {
        object pending = Get(director, "pending");
        Type spawnType = typeof(WaveDirector).GetNestedType("PendingSpawn", BindingFlags.NonPublic);
        pending.GetType().GetMethod("Enqueue").Invoke(pending, new[] { Activator.CreateInstance(spawnType) });
    }

    // ── 테이블 ──
    static int Count(WaveData _wave, string _enemy, string _lane)
    {
        int total = 0;
        foreach (WaveSpawnData spawn in _wave.spawns)
        {
            if (spawn.enemyId == _enemy && spawn.lane == _lane)
            {
                total += spawn.count;
            }
        }
        return total;
    }

    // W1~W3는 v1 값 그대로, W4~W6은 TD-50이다.
    [TestCase(0, 1.5f, 3, 2, 0, 0, 5)]
    [TestCase(1, 0.8f, 3, 3, 1, 1, 8)]
    [TestCase(2, 0.4f, 2, 6, 1, 2, 11)]
    [TestCase(3, 0.4f, 6, 3, 1, 2, 12)]
    [TestCase(4, 0.4f, 4, 6, 3, 1, 14)]
    [TestCase(5, 0.4f, 6, 6, 2, 3, 17)]
    public void WaveTableKeepsFirstThreeAndMatchesTd50(int _index, float _interval, int _basicWest, int _basicEast, int _breakerWest, int _breakerEast, int _total)
    {
        WaveData wave = Table.waves[_index];
        Assert.That(wave.spawnInterval, Is.EqualTo(_interval).Within(0.0001f));
        Assert.That(Count(wave, BASIC, WEST), Is.EqualTo(_basicWest));
        Assert.That(Count(wave, BASIC, EAST), Is.EqualTo(_basicEast));
        Assert.That(Count(wave, BREAKER, WEST), Is.EqualTo(_breakerWest));
        Assert.That(Count(wave, BREAKER, EAST), Is.EqualTo(_breakerEast));
        int all = 0;
        foreach (WaveSpawnData spawn in wave.spawns)
        {
            all += spawn.count;
        }
        Assert.That(all, Is.EqualTo(_total), "다른 적이나 진입로가 섞이지 않는다.");
    }

    [Test]
    public void TableHasSixWavesAndEveryWaveUsesBothLanes()
    {
        Assert.That(Table.waves.Length, Is.EqualTo(WAVE_COUNT));
        foreach (WaveData wave in Table.waves)
        {
            Assert.That(Count(wave, BASIC, WEST) + Count(wave, BREAKER, WEST), Is.GreaterThan(0));
            Assert.That(Count(wave, BASIC, EAST) + Count(wave, BREAKER, EAST), Is.GreaterThan(0));
        }
    }

    [Test]
    public void FlowWaveCountComesFromTable()
    {
        var flowFromShortTable = new SessionFlow(1, 1, 1, 1, 2);
        Assert.That(flowFromShortTable.WaveCount, Is.EqualTo(2), "코드 상수가 아니라 넘긴 수를 따른다.");
        BuildSession();
        Assert.That(session.WaveCount, Is.EqualTo(Table.waves.Length));
    }

    static bool ValidateWith(TableManager _tables)
    {
        var rules = new GameTableRules();
        rules.Validate(_tables);
        return rules.IsValid;
    }

    [Test]
    public void ValidationRejectsVisitorWaveBeyondWaveCount()
    {
        TableManager tables = TableManager.Instance.RequireValid();
        int[] saved = tables.Gameplay.recruitment.visitorWaves;
        try
        {
            tables.Gameplay.recruitment.visitorWaves = new[] { 2, WAVE_COUNT };
            Assert.That(ValidateWith(tables), Is.True, "마지막 웨이브까지는 도달한다.");

            tables.Gameplay.recruitment.visitorWaves = new[] { 2, WAVE_COUNT + 1 };
            LogAssert.Expect(LogType.Error, new Regex("Gameplay.recruitment.visitorWaves"));
            Assert.That(ValidateWith(tables), Is.False);
        }
        finally
        {
            tables.Gameplay.recruitment.visitorWaves = saved;
        }
    }

    [Test]
    public void ValidationRejectsWaveUsingOneLaneAndWrongWaveCount()
    {
        TableManager tables = TableManager.Instance.RequireValid();
        WaveData[] saved = tables.Gameplay.waves;
        try
        {
            WaveData[] oneLane = (WaveData[])saved.Clone();
            oneLane[3] = new WaveData { spawnInterval = 0.4f, spawns = new[] { new WaveSpawnData { enemyId = BASIC, lane = WEST, count = 1 } } };
            tables.Gameplay.waves = oneLane;
            LogAssert.Expect(LogType.Error, new Regex(@"Gameplay\.waves\[3\]"));
            Assert.That(ValidateWith(tables), Is.False);

            tables.Gameplay.waves = new[] { saved[0], saved[1], saved[2], saved[3], saved[4] };
            LogAssert.Expect(LogType.Error, new Regex("Gameplay.waves"));
            Assert.That(ValidateWith(tables), Is.False);
        }
        finally
        {
            tables.Gameplay.waves = saved;
        }
    }

    // ── 승리 ──
    [Test]
    public void OnlySixthWaveResultConfirmationIsVictory()
    {
        BuildSession();
        for (int wave = 1; wave < WAVE_COUNT; wave++)
        {
            GoToResult();
            Assert.That(session.IsLastWave, Is.False);
            session.ConfirmResult();
            Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Prep), $"W{wave} 성공은 승리가 아니다.");
            Assert.That(flow.WaveNumber, Is.EqualTo(wave + 1));
            Assert.That(session.Result, Is.EqualTo(SessionResult.None));
            Assert.That(sessionEnded, Is.Empty);
        }
        GoToResult();
        Assert.That(session.IsLastWave, Is.True);
        Assert.That(sessionEnded, Is.Empty, "결과를 확인하기 전에는 끝나지 않는다.");
        session.ConfirmResult();
        Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Ended));
        Assert.That(session.Result, Is.EqualTo(SessionResult.Victory));
        Assert.That(sessionEnded.Count, Is.EqualTo(1));
        Assert.That(sessionEnded[0].result, Is.EqualTo(SessionResult.Victory));
        Assert.That(sessionEnded[0].reason, Is.EqualTo("all_waves_cleared"));
        Assert.That(sessionEnded[0].seed, Is.EqualTo(SEED));
        Assert.That(waveEnded.ConvertAll(_info => _info.waveIndex), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6 }));
        session.ConfirmResult();
        Assert.That(sessionEnded.Count, Is.EqualTo(1), "두 번 눌러도 한 번만 끝난다.");
    }

    // ── 방문 ──
    [Test]
    public void VisitorArrivesAtWaveFourAndCanBeRecruited()
    {
        BuildSession();
        GoToWave(3);
        Assert.That(arrived.Count, Is.EqualTo(1));
        Assert.That(arrived[0].wave, Is.EqualTo(2));
        Assert.That(rejected, Is.EqualTo(new[] { RecruitmentSystem.REASON_EXPIRED }), "W2 방문자는 경보에 만료됐다.");
        GoToWave(4);
        Assert.That(arrived.Count, Is.EqualTo(2));
        Assert.That(arrived[1].wave, Is.EqualTo(4));
        Assert.That(arrived[1].candidateId, Is.Not.EqualTo(arrived[0].candidateId));
        Assert.That(session.Recruitment.PendingVisitor, Is.Not.Null);
        string name = session.Recruitment.PendingVisitor.Candidate.displayName;

        Assert.That(session.TryRecruitVisitor(out string error), Is.True, error);

        Assert.That(session.AliveCount, Is.EqualTo(Table.survivors.Length + 1));
        Assert.That(joined.Count, Is.EqualTo(1));
        Assert.That(joined[0].source, Is.EqualTo(RecruitmentSystem.SOURCE_VISITOR));
        Assert.That(session.Record.VisitorLog.Count, Is.EqualTo(2));
        Assert.That(session.Record.VisitorLog[1], Is.EqualTo($"W4 {name} 영입"));
        GoToWave(WAVE_COUNT);
        Assert.That(arrived.Count, Is.EqualTo(2), "W5·W6에는 오지 않는다.");
    }

    // ── 이월 ──
    [Test]
    public void DeathCarriesAcrossWavesAndIdIsNotReused()
    {
        BuildSession();
        session.DebugKillSurvivor("s2");
        session.DebugJoinSurvivor(out _);
        GoToWave(3);
        Assert.That(StateOf("s2").IsAlive, Is.False);
        Assert.That(StateOf("s2").Hp, Is.Zero);
        Assert.That(session.AliveCount, Is.EqualTo(Table.survivors.Length));
        Assert.That(StateOf("n1").IsAlive, Is.True, "신입도 웨이브를 넘어 남는다.");
        session.DebugJoinSurvivor(out _);
        Assert.That(joined[1].newId, Is.EqualTo("n2"));
    }

    [Test]
    public void HpCarriesAcrossWaves()
    {
        BuildSession();
        session.DebugLowerHp("s1", 60);
        session.DebugLowerHp("s6", 10);
        GoToWave(3);
        Assert.That(StateOf("s1").Hp, Is.EqualTo(60f));
        Assert.That(StateOf("s1").Injury, Is.EqualTo(InjuryStatus.Light));
        Assert.That(StateOf("s6").Hp, Is.EqualTo(10f));
        Assert.That(StateOf("s6").Injury, Is.EqualTo(InjuryStatus.Critical));
    }

    [Test]
    public void ExperienceAndGrownStatsCarryAndOnlyWaveCapResets()
    {
        BuildSession();
        ExperienceTracker tracker = session.Experience;
        int workBefore = StateOf("s1").Work;
        int needed = tracker.NeededXp(workBefore);
        session.DebugAddExperience("s1", StatType.Work, needed + 3);
        Assert.That(StateOf("s1").Work, Is.EqualTo(workBefore + 1));
        Assert.That(tracker.GetXp("s1", StatType.Work), Is.EqualTo(3), "초과분을 이어 간다.");
        tracker.RecordCombatHit("s2");
        int combatXp = tracker.GetXp("s2", StatType.Combat);
        Assert.That(combatXp, Is.GreaterThan(0));
        Assert.That(tracker.GetProgress("s2").WaveCombatXp, Is.EqualTo(combatXp));

        GoToWave(2);

        Assert.That(StateOf("s1").Work, Is.EqualTo(workBefore + 1));
        Assert.That(tracker.GetXp("s1", StatType.Work), Is.EqualTo(3));
        Assert.That(tracker.GetXp("s2", StatType.Combat), Is.EqualTo(combatXp));
        Assert.That(tracker.GetProgress("s2").WaveCombatXp, Is.Zero, "웨이브 상한 카운터만 초기화한다.");
    }

    [Test]
    public void TraitCarriesAcrossWaves()
    {
        BuildSession();
        SurvivorData candidate = Array.Find(Table.recruitment.candidates, _entry => !string.IsNullOrEmpty(_entry.trait));
        Invoke(session, "JoinSurvivor", candidate, RecruitmentSystem.SOURCE_VISITOR);
        GoToWave(3);
        Assert.That(StateOf("n1").Trait, Is.EqualTo(candidate.trait));
    }

    [Test]
    public void WorkAndDefenseAssignmentsCarryAcrossWaves()
    {
        BuildSession();
        Assert.That(session.TrySetWork("s3", WorkAssignment.Production, out string workError), Is.True, workError);
        session.TrySetDefensePoint("s3", new Vector3(12, 0, 7), out _);
        session.DebugLowerHp("s6", 10);
        var woundedDefense = session.Assignments.DefenseOf("s6");
        session.DebugJoinSurvivor(out _);
        session.TrySetDefensePoint("n1", new Vector3(-4, 0, 9), out _);
        GoToWave(3);
        Assert.That(session.Assignments.WorkOf("s3"), Is.EqualTo(WorkAssignment.Production));
        Assert.That(session.Assignments.DefenseOf("s3").x, Is.EqualTo(12f));
        Assert.That(session.Assignments.DefenseOf("s3").z, Is.EqualTo(7f));
        Assert.That(session.Assignments.DefenseOf("s6").x, Is.EqualTo(woundedDefense.x), "중상자의 배정도 유지한다.");
        Assert.That(session.Assignments.DefenseOf("n1").x, Is.EqualTo(-4f));
        Assert.That(session.Assignments.IsDefenseAssigned("n1"), Is.True);
    }

    [Test]
    public void ScrapCarriesAcrossWaves()
    {
        BuildSession(_scrap: 17);
        session.Economy.TrySpend(4);
        GoToWave(3);
        Assert.That(session.Scrap, Is.EqualTo(13));
    }

    [Test]
    public void BuildingDamageCarriesAcrossWaves()
    {
        BuildSession();
        BuildingBase wall = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<BuildingBase>("Assets/Prefabs/Wall.prefab"), host.transform);
        wall.Initialize(Table.buildings.wall);
        ((List<BuildingBase>)Get(session, "buildings")).Add(wall);
        wall.ApplyDamage(10);
        float hp = wall.Hp;
        Assert.That(hp, Is.LessThan(wall.MaxHp));
        GoToWave(3);
        Assert.That(wall.Hp, Is.EqualTo(hp));
        Assert.That(session.Buildings.Count, Is.EqualTo(1));
        Assert.That(session.DamagedBuildings, Is.EqualTo(1));
    }

    [Test]
    public void ExpeditionProgressCarriesAcrossWavesAndSettlesOnce()
    {
        BuildSession();
        ActiveExpedition expedition = Dispatch("s5");
        Invoke(session, "TickExpeditions", 1f);
        float remaining = expedition.remainingSeconds;
        GoToResult();
        TickSession(10);
        Assert.That(expedition.remainingSeconds, Is.EqualTo(remaining), "결과 단계에서는 시간이 멈춘다.");
        session.ConfirmResult();
        Assert.That(flow.WaveNumber, Is.EqualTo(2));
        Assert.That(session.Expeditions.Active, Does.Contain(expedition));
        Assert.That(expedition.remainingSeconds, Is.EqualTo(remaining));
        Assert.That(StateOf("s5").IsAway, Is.True);
        int scrap = session.Scrap;
        Invoke(session, "TickExpeditions", remaining);
        Assert.That(StateOf("s5").IsAway, Is.False);
        Assert.That(session.Scrap, Is.GreaterThan(scrap));
        Assert.That(session.ExpeditionHistory.Count, Is.EqualTo(1));
    }

    [Test]
    public void PausedTreatmentCarriesToNextWaveWithProgressAndNoExtraCost()
    {
        BuildSession();
        session.DebugLowerHp("s1", 24);
        Assert.That(session.TryStartTreatment("s1", "s6", out string error), Is.True, error);
        int scrap = session.Scrap;
        Set(session.FindUnit("s6"), "arrived", true);
        Invoke(session, "TickTreatment", 5f);
        float progress = session.Treatment.Current.Progress;
        Assert.That(progress, Is.GreaterThan(0));
        GoToResult();
        Assert.That(session.Treatment.Current.IsPaused, Is.True);
        Assert.That(session.Treatment.Current.Progress, Is.EqualTo(progress));
        Assert.That(StateOf("s1").Hp, Is.EqualTo(24f), "완료 전에는 HP가 오르지 않는다.");
        session.ConfirmResult();
        Assert.That(session.Treatment.Current, Is.Not.Null);
        Assert.That(session.Treatment.Current.IsPaused, Is.False, "같은 담당자로 자동 재개한다.");
        Assert.That(session.Treatment.Current.Progress, Is.EqualTo(progress));
        Assert.That(session.Scrap, Is.EqualTo(scrap), "재개는 무료다.");
    }

    // ── WaveEnded·SessionEnded ──
    [Test]
    public void WaveEndedCountsInjuredAndRecruitedAndResetsRecruitedAtPrep()
    {
        BuildSession();
        session.DebugLowerHp("s1", 60);
        session.DebugLowerHp("s2", 10);
        ActiveExpedition expedition = Dispatch("s5");
        session.DebugLowerHp("s5", 40);
        session.DebugJoinSurvivor(out _);
        session.DebugJoinSurvivor(out _);
        session.DebugKillSurvivor("s7");
        GoToResult();
        Assert.That(waveEnded.Count, Is.EqualTo(1));
        Assert.That(waveEnded[0].waveIndex, Is.EqualTo(1));
        Assert.That(waveEnded[0].injured, Is.EqualTo(3), "경상 2 + 중상 1. 파견 중 부상도 센다.");
        Assert.That(waveEnded[0].recruited, Is.EqualTo(2));
        Assert.That(waveEnded[0].alive, Is.EqualTo(8));
        Assert.That(waveEnded[0].scrap, Is.EqualTo(session.Scrap));
        Assert.That(session.LastWave.injuredLight, Is.EqualTo(2));
        Assert.That(session.LastWave.injuredCritical, Is.EqualTo(1));
        Assert.That(session.LastWave.joined.Length, Is.EqualTo(2));
        session.ConfirmResult();
        GoToResult();
        Assert.That(waveEnded[1].recruited, Is.Zero, "준비 시작에 초기화한다.");
        Assert.That(waveEnded[1].injured, Is.EqualTo(3), "부상은 이어진다.");
        Assert.That(session.LastWave.joined, Is.Empty);
        Assert.That(expedition.IsSettled, Is.False);
    }

    [Test]
    public void RescueReturnDuringCombatCountsTowardThatWave()
    {
        BuildSession();
        ActiveExpedition expedition = Dispatch("s5");
        RaiseEvent(ExpeditionEventType.Rescue, ROLL_LOW);
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out string error), Is.True, error);
        GoToCombat();
        Invoke(session, "TickExpeditions", expedition.remainingSeconds);
        Assert.That(joined.Count, Is.EqualTo(1));
        Invoke(session, "EnterResult", 0, 0f);
        Assert.That(waveEnded[0].recruited, Is.EqualTo(1));
    }

    [Test]
    public void SessionEndedCarriesSeedOnDefeatToo()
    {
        BuildSession();
        GoToCombat();
        core.ApplyDamage(core.MaxHp);
        TickSession(0);
        Assert.That(sessionEnded.Count, Is.EqualTo(1));
        Assert.That(sessionEnded[0].result, Is.EqualTo(SessionResult.Defeat));
        Assert.That(sessionEnded[0].reason, Is.EqualTo("core_destroyed"));
        Assert.That(sessionEnded[0].seed, Is.EqualTo(SEED));
    }

    // ── 누적 기록 ──
    [Test]
    public void RecordMergesGrowthPerSurvivorAndStat()
    {
        var record = new SessionRecord(7);
        record.RecordGrowth("s1", "서준", "전투", 1, 2);
        record.RecordGrowth("s2", "하윤", "작업", 2, 3);
        record.RecordGrowth("s1", "서준", "작업", 2, 3);
        record.RecordGrowth("s1", "서준", "전투", 2, 3);
        Assert.That(record.GrowthSummary(), Is.EqualTo(new[] { "서준 전투 1→3·작업 2→3", "하윤 작업 2→3" }));
    }

    [Test]
    public void RecordCountsExpeditionOutcomesInFirstSeenOrder()
    {
        var record = new SessionRecord(7);
        record.RecordExpeditionOutcome("구조 성공");
        record.RecordExpeditionOutcome("철수");
        record.RecordExpeditionOutcome("구조 성공");
        Assert.That(record.ExpeditionSummary(), Is.EqualTo(new[] { "구조 성공 2건", "철수 1건" }));
    }

    [Test]
    public void HeadcountArithmeticMatchesActualStateThroughEveryPath()
    {
        BuildSession(_regionDeath: 1);
        SessionRecord record = session.Record;
        Assert.That(record.InitialCount, Is.EqualTo(Table.survivors.Length));

        // 방문 합류.
        GoToWave(2);
        Assert.That(session.TryRecruitVisitor(out string error), Is.True, error);
        // 탐사 사망. 지역 사망 확률이 1이라 귀환 판정에서 죽는다.
        ActiveExpedition doomed = Dispatch("s5");
        Invoke(session, "TickExpeditions", doomed.remainingSeconds);
        Assert.That(StateOf("s5").IsAlive, Is.False);
        // 전멸 탐사의 구조는 합류하지 않고 예약만 풀린다.
        ActiveExpedition rescue = Dispatch("s6");
        RaiseEvent(ExpeditionEventType.Rescue, ROLL_LOW);
        Assert.That(session.TryResolveExpeditionEvent(rescue.expeditionId, ExpeditionDecision.Act, out error), Is.True, error);
        Assert.That(session.Recruitment.ReservedSlots, Is.EqualTo(1));
        Invoke(session, "TickExpeditions", rescue.remainingSeconds);
        Assert.That(session.Recruitment.ReservedSlots, Is.Zero);
        // 직접 사망. 신입도 같은 규칙이다.
        session.DebugKillSurvivor("n1");

        Assert.That(record.JoinedCount, Is.EqualTo(1));
        Assert.That(record.DeathCount, Is.EqualTo(3));
        Assert.That(record.InitialCount + record.JoinedCount - record.DeathCount, Is.EqualTo(session.AliveCount));
        Assert.That(session.AliveCount, Is.EqualTo(5));
    }

    [Test]
    public void RescueJoinIsRecordedWithSourceAndEventOutcome()
    {
        BuildSession();
        ActiveExpedition expedition = Dispatch("s5");
        RaiseEvent(ExpeditionEventType.Rescue, ROLL_LOW);
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out string error), Is.True, error);
        string name = expedition.RescuedCandidate.displayName;
        Assert.That(session.Record.JoinedCount, Is.Zero, "귀환 전에는 추가 인원이 아니다.");
        Invoke(session, "TickExpeditions", expedition.remainingSeconds);
        Assert.That(session.Record.Joined, Is.EqualTo(new[] { $"{name} (구조)" }));
        Assert.That(session.Record.ExpeditionSummary(), Is.EqualTo(new[] { "구조 성공 1건" }));
    }

    [Test]
    public void EncounterWithdrawAndTimeoutAreRecorded()
    {
        BuildSession();
        ActiveExpedition first = Dispatch("s5", "s6");
        RaiseEvent(ExpeditionEventType.Encounter);
        Assert.That(session.TryResolveExpeditionEvent(first.expeditionId, ExpeditionDecision.Withdraw, out string error), Is.True, error);
        ActiveExpedition second = Dispatch("s7");
        RaiseEvent(ExpeditionEventType.Supply);
        Assert.That(session.TryResolveExpeditionEvent(second.expeditionId, ExpeditionDecision.Timeout, out error), Is.True, error);
        Assert.That(session.Record.ExpeditionSummary(), Is.EqualTo(new[] { "적대 조우 1건", "철수 1건", "임무 계속 1건" }));
    }

    [Test]
    public void GrowthEventsAreAccumulatedInRecordAndWaveList()
    {
        BuildSession();
        session.DebugAddExperience("s1", StatType.Work, session.Experience.NeededXp(StateOf("s1").Work));
        GoToResult();
        Assert.That(session.LastWave.growths.Length, Is.EqualTo(1));
        session.ConfirmResult();
        session.DebugAddExperience("s1", StatType.Work, session.Experience.NeededXp(StateOf("s1").Work));
        Assert.That(session.Record.GrowthSummary().Count, Is.EqualTo(1));
        Assert.That(session.Record.GrowthSummary()[0], Does.Contain("작업 1→3"));
    }

    // ── 엣지 8: 승패 우선순위 ──
    [Test]
    public void LastWaveClearedWithCoreDestroyedIsDefeat()
    {
        BuildSession();
        GoToWave(WAVE_COUNT);
        GoToCombat();
        Assert.That(director.IsCleared, Is.True);
        core.ApplyDamage(core.MaxHp);
        TickSession(0);
        Assert.That(session.Result, Is.EqualTo(SessionResult.Defeat));
        Assert.That(session.EndReason, Is.EqualTo("core_destroyed"));
        Assert.That(sessionEnded.Count, Is.EqualTo(1));
        Assert.That(waveEnded.FindAll(_info => _info.waveIndex == WAVE_COUNT).Count, Is.EqualTo(1));
        session.ConfirmResult();
        Assert.That(session.Result, Is.EqualTo(SessionResult.Defeat), "끝난 뒤 승리로 바뀌지 않는다.");
        Assert.That(sessionEnded.Count, Is.EqualTo(1));
    }

    // 전멸이 클리어와 같은 틱 안에서 일어난다. 결과 단계에 들어가도 틱 끝 판정이 패배로 끝낸다.
    [Test]
    public void LastWaveClearedWithWipeInSameTickIsDefeat()
    {
        BuildSession(_regionDeath: 1);
        GoToWave(WAVE_COUNT);
        foreach (SurvivorData data in Table.survivors)
        {
            if (data.id != "s5")
            {
                session.DebugKillSurvivor(data.id);
            }
        }
        ActiveExpedition expedition = Dispatch("s5");
        GoToCombat();
        phases.Clear();
        TickSession(expedition.remainingSeconds);
        Assert.That(StateOf("s5").IsAlive, Is.False);
        Assert.That(phases, Is.EqualTo(new[] { SessionPhase.Result, SessionPhase.Ended }), "클리어를 거쳐 패배로 끝난다.");
        Assert.That(session.Result, Is.EqualTo(SessionResult.Defeat));
        Assert.That(session.EndReason, Is.EqualTo("all_survivors_dead"));
        Assert.That(sessionEnded.Count, Is.EqualTo(1));
        Assert.That(waveEnded.FindAll(_info => _info.waveIndex == WAVE_COUNT).Count, Is.EqualTo(1));
        Assert.That(waveEnded[waveEnded.Count - 1].alive, Is.Zero);
    }

    [Test]
    public void CoreDestroyedWinsOverWipeAsReason()
    {
        BuildSession();
        GoToWave(WAVE_COUNT);
        GoToCombat();
        foreach (SurvivorData data in Table.survivors)
        {
            session.DebugKillSurvivor(data.id);
        }
        core.ApplyDamage(core.MaxHp);
        TickSession(0);
        Assert.That(session.EndReason, Is.EqualTo("core_destroyed"));
        Assert.That(session.Result, Is.EqualTo(SessionResult.Defeat));
    }

    [Test]
    public void LastWaveTimeoutDestroyingCoreIsDefeatWithSummary()
    {
        BuildSession();
        GoToWave(WAVE_COUNT);
        GoToCombat();
        core.ApplyDamage(core.MaxHp - 1);
        AddPendingEnemy();
        session.DebugExpireTimer();
        TickSession(0);
        Assert.That(session.Result, Is.EqualTo(SessionResult.Defeat));
        Assert.That(session.EndReason, Is.EqualTo("core_destroyed"));
        Assert.That(session.LastWave.timedOutEnemies, Is.EqualTo(1));
        Assert.That(waveEnded.FindAll(_info => _info.waveIndex == WAVE_COUNT).Count, Is.EqualTo(1));
        Assert.That(sessionEnded.Count, Is.EqualTo(1));
    }

    // DEC-15: 시간 초과로 코어가 버티면 웨이브는 끝나고, 마지막이면 확인 후 승리다.
    [Test]
    public void LastWaveTimeoutWithCoreAliveStillWinsAfterConfirm()
    {
        BuildSession();
        GoToWave(WAVE_COUNT);
        GoToCombat();
        AddPendingEnemy();
        session.DebugExpireTimer();
        TickSession(0);
        Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Result));
        Assert.That(core.Hp, Is.EqualTo(core.MaxHp - Table.session.timeoutCoreDamagePerEnemy));
        session.ConfirmResult();
        Assert.That(session.Result, Is.EqualTo(SessionResult.Victory));
    }

    [Test]
    public void FewDeathsDoNotEndSession()
    {
        BuildSession();
        GoToCombat();
        session.DebugKillSurvivor("s1");
        session.DebugKillSurvivor("s2");
        session.DebugKillSurvivor("s3");
        AddPendingEnemy();
        TickSession(0);
        Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Combat));
        Assert.That(sessionEnded, Is.Empty);
    }

    // ── 결과 패널 ──
    T HudPart<T>() where T : Component
    {
        GameObject hud = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Hud.prefab"), host.transform);
        return hud.GetComponentInChildren<T>(true);
    }

    string BodyOf(ResultPanelView _view) => ((Text)Get(_view, "bodyText")).text;

    void SetLastWave(WaveSummary _wave)
    {
        typeof(GameSession).GetProperty("LastWave").SetValue(session, _wave);
    }

    [Test]
    public void WaveResultShowsLossesRecruitsGrowthFacilityAndNextGaps()
    {
        BuildSession();
        var view = HudPart<ResultPanelView>();
        var presenter = new ResultPresenter(view, session);
        session.TrySetWork("s3", WorkAssignment.Production, out _);
        session.TrySetWork("s4", WorkAssignment.Production, out _);
        session.DebugLowerHp("s1", 60);
        session.DebugLowerHp("s4", 10);
        session.DebugKillSurvivor("s7");
        session.DebugJoinSurvivor(out _);
        string joinedName = StateOf("n1").DisplayName;
        session.DebugAddExperience("s2", StatType.Combat, session.Experience.NeededXp(StateOf("s2").Combat));
        BuildingBase wall = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<BuildingBase>("Assets/Prefabs/Wall.prefab"), host.transform);
        wall.Initialize(Table.buildings.wall);
        ((List<BuildingBase>)Get(session, "buildings")).Add(wall);
        wall.ApplyDamage(10);
        Assert.That(session.TryStartTreatment("s4", "s6", out string error), Is.True, error);
        Set(session.FindUnit("s6"), "arrived", true);
        Invoke(session, "TickTreatment", session.Treatment.SecondsFor(StateOf("s6")) * 0.5f);

        GoToResult();
        presenter.Refresh();

        string body = BodyOf(view);
        Assert.That(body, Does.Contain("부상: 경상 1명 · 중상 1명"));
        Assert.That(body, Does.Contain("사망: 수아"));
        Assert.That(body, Does.Contain($"충원: {joinedName}"));
        Assert.That(body, Does.Contain($"성장: 하윤 전투 {Table.survivors[1].combat}→{Table.survivors[1].combat + 1}"));
        Assert.That(body, Does.Contain("시설 피해: 파괴 0개 · 손상 1개"));
        Assert.That(body, Does.Contain("다음 운영 공백: 중상 1명 대기 · 치료 일시정지 50% · 생산 공백 1자리"));
    }

    [Test]
    public void WaveResultShowsNoGapWhenNothingIsMissing()
    {
        BuildSession();
        var view = HudPart<ResultPanelView>();
        var presenter = new ResultPresenter(view, session);
        session.TrySetWork("s3", WorkAssignment.Production, out _);
        session.TrySetWork("s4", WorkAssignment.Production, out _);
        GoToResult();
        presenter.Refresh();
        string body = BodyOf(view);
        Assert.That(body, Does.Contain("다음 운영 공백: 없음"));
        Assert.That(body, Does.Contain("사망: 없음"));
        Assert.That(body, Does.Contain("충원: 없음"));
    }

    [Test]
    public void WaveResultNamesMissingTreatmentWorker()
    {
        BuildSession();
        var view = HudPart<ResultPanelView>();
        var presenter = new ResultPresenter(view, session);
        session.TrySetWork("s3", WorkAssignment.Production, out _);
        session.TrySetWork("s4", WorkAssignment.Production, out _);
        session.DebugLowerHp("s1", 24);
        Assert.That(session.TryStartTreatment("s1", "s6", out string error), Is.True, error);
        session.DebugKillSurvivor("s6");
        GoToResult();
        presenter.Refresh();
        Assert.That(BodyOf(view), Does.Contain("치료소 담당자 필요"));
    }

    [Test]
    public void VictoryTextUsesCumulativeRecordAndActualState()
    {
        BuildSession();
        var view = HudPart<ResultPanelView>();
        var presenter = new ResultPresenter(view, session);
        GoToWave(2);
        string visitorName = session.Recruitment.PendingVisitor.Candidate.displayName;
        Assert.That(session.TryRecruitVisitor(out string error), Is.True, error);
        session.DebugKillSurvivor("s7");
        session.DebugLowerHp("s1", 60);
        session.DebugAddExperience("s2", StatType.Work, session.Experience.NeededXp(StateOf("s2").Work));
        GoToWave(3);
        session.DebugKillSurvivor("s6");
        GoToWave(WAVE_COUNT);
        core.ApplyDamage(10);
        NextWave();
        Assert.That(session.Result, Is.EqualTo(SessionResult.Victory));
        presenter.Refresh();

        string body = BodyOf(view);
        Assert.That(body, Does.Contain($"{WAVE_COUNT}개 웨이브를 모두 막아 냈습니다."));
        Assert.That(body, Does.Contain("인원: 초기 7명 + 추가 1명 − 사망 2명 = 최종 6명"));
        Assert.That(body, Does.Contain("사망자: 수아, 민재"), "웨이브를 넘어 누적한다.");
        Assert.That(body, Does.Contain($"추가 인원: {visitorName} (방문)"));
        Assert.That(body, Does.Contain($"남은 상태: 핵심 시설 {Mathf.CeilToInt(core.Hp)}/{Mathf.CeilToInt(core.MaxHp)} · 스크랩 {session.Scrap} · 경상 1명 · 중상 0명"));
        Assert.That(body, Does.Contain("성장 결과: 하윤 작업 2→3"));
        Assert.That(body, Does.Contain($"방문 기록: W2 {visitorName} 영입"));
        Assert.That(body, Does.Contain("탐사 사건: 없음"));
    }

    [TestCase(true, "핵심 시설이 파괴되었습니다.")]
    [TestCase(false, "파견 인력까지 전원 사망했습니다.")]
    public void DefeatTextHasSameRecordFieldsAndReason(bool _coreDestroyed, string _reason)
    {
        BuildSession();
        var view = HudPart<ResultPanelView>();
        var presenter = new ResultPresenter(view, session);
        GoToWave(3);
        GoToCombat();
        if (_coreDestroyed)
        {
            core.ApplyDamage(core.MaxHp);
        }
        else
        {
            foreach (SurvivorData data in Table.survivors)
            {
                session.DebugKillSurvivor(data.id);
            }
        }
        TickSession(0);
        presenter.Refresh();
        string body = BodyOf(view);
        Assert.That(body, Does.Contain($"웨이브 3에서 패배했습니다. {_reason}"));
        int deaths = _coreDestroyed ? 0 : Table.survivors.Length;
        Assert.That(body, Does.Contain($"인원: 초기 7명 + 추가 0명 − 사망 {deaths}명 = 최종 {7 - deaths}명"));
        foreach (string field in new[] { "사망자:", "추가 인원:", "남은 상태:", "성장 결과:", "방문 기록:", "탐사 사건:" })
        {
            Assert.That(body, Does.Contain(field));
        }
    }

    static void AssertFitsScroll(ResultPanelView _view)
    {
        var body = (Text)Get(_view, "bodyText");
        var scroll = body.GetComponentInParent<ScrollRect>(true);
        Assert.That(scroll, Is.Not.Null);
        Assert.That(scroll.content, Is.EqualTo(body.rectTransform), "넘쳐도 스크롤로 볼 수 있어야 한다.");
        Assert.That(scroll.vertical, Is.True);
        float height = ((RectTransform)scroll.transform).rect.height;
        int lines = body.text.Split('\n').Length;
        Assert.That(body.preferredHeight, Is.GreaterThanOrEqualTo(lines * body.fontSize), "측정이 실제 줄 수를 반영해야 한다.");
        Assert.That(body.preferredHeight, Is.LessThanOrEqualTo(height), "가장 긴 문자열도 스크롤 없이 들어간다:\n" + body.text);
    }

    // 가능한 최대치: 후보 전원 합류, 6명 사망, 9명 생존, 15명 전원이 세 능력치 모두 성장, 사건 결과 7종.
    [Test]
    public void LongestSessionTextFitsPanelWithoutScrolling()
    {
        BuildSession(_scrap: 999);
        var view = HudPart<ResultPanelView>();
        var presenter = new ResultPresenter(view, session);
        SessionRecord record = session.Record;
        var everyone = new List<SurvivorData>(Table.survivors);
        everyone.AddRange(Table.recruitment.candidates);
        foreach (SurvivorData data in everyone)
        {
            record.RecordGrowth(data.id, data.displayName, "전투", 1, 5);
            record.RecordGrowth(data.id, data.displayName, "작업", 1, 5);
            record.RecordGrowth(data.id, data.displayName, "탐사", 1, 5);
        }
        foreach (SurvivorData data in Table.recruitment.candidates)
        {
            record.RecordJoin(data.displayName, RecruitmentSystem.SOURCE_RESCUE);
        }
        for (int index = 0; index < 6; index++)
        {
            record.RecordDeath(Table.survivors[index].displayName);
        }
        record.RecordVisitorOutcome(2, Table.recruitment.candidates[0].displayName, "영입");
        record.RecordVisitorOutcome(4, Table.recruitment.candidates[1].displayName, "거절");
        foreach (string outcome in new[] { ExpeditionEvent.OUTCOME_RESCUE_SUCCESS, ExpeditionEvent.OUTCOME_RESCUE_FAILED, ExpeditionEvent.OUTCOME_SEARCH_SAFE, ExpeditionEvent.OUTCOME_SEARCH_HURT, ExpeditionEvent.OUTCOME_ENCOUNTER, ExpeditionEvent.OUTCOME_WITHDRAWN, ExpeditionEvent.OUTCOME_CONTINUED })
        {
            record.RecordExpeditionOutcome(ExpeditionLabels.OutcomeLabel(outcome));
        }
        GoToCombat();
        core.ApplyDamage(core.MaxHp);
        TickSession(0);
        presenter.Refresh();
        AssertFitsScroll(view);
    }

    // 가능한 최대치: 9명 모두 전투·작업 성장, 3건 귀환(각 3명, 성장 포함), 시간 초과, 공백 3종, 사망·충원 다수.
    [Test]
    public void LongestWaveTextFitsPanelWithoutScrolling()
    {
        BuildSession();
        var view = HudPart<ResultPanelView>();
        var presenter = new ResultPresenter(view, session);
        session.DebugLowerHp("s1", 24);
        Assert.That(session.TryStartTreatment("s1", "s6", out string error), Is.True, error);
        GoToCombat();
        Invoke(session, "EnterResult", 0, 0f);
        var growths = new List<string>();
        foreach (SurvivorData data in Table.survivors)
        {
            growths.Add($"{data.displayName} 전투 4→5");
            growths.Add($"{data.displayName} 작업 4→5");
        }
        growths.Add("태양 전투 4→5");
        growths.Add("태양 작업 4→5");
        growths.Add("바다 전투 4→5");
        growths.Add("바다 작업 4→5");
        var party = new[] { "s1", "s2", "s3" };
        var outcomes = new ExpeditionOutcome[3];
        for (int index = 0; index < outcomes.Length; index++)
        {
            outcomes[index] = new ExpeditionOutcome(Table.expeditions[1].id, 99, new[] { "s4" }, party, index + 1, false, Table.recruitment.candidates[index], true, true, new[] { "서준 탐사 4→5", "하윤 탐사 4→5", "도현 탐사 4→5" });
        }
        SetLastWave(new WaveSummary(6, core.MaxHp, 9, 999, new[] { "서준", "하윤", "도현", "지우" }, 17, 255, outcomes, 9, 9, growths.ToArray(), new[] { "태양", "바다" }, 5, 4));
        presenter.Refresh();
        AssertFitsScroll(view);
    }

    // ── HUD ──
    [Test]
    public void HudShowsInjuryCounts()
    {
        BuildSession();
        var view = HudPart<HudView>();
        var presenter = new HudPresenter(view, session);
        presenter.Refresh();
        Assert.That(((Text)Get(view, "injuryText")).text, Is.EqualTo("부상 0 · 중상 0"));
        session.DebugLowerHp("s1", 60);
        session.DebugLowerHp("s2", 70);
        session.DebugLowerHp("s3", 10);
        presenter.Refresh();
        Assert.That(((Text)Get(view, "injuryText")).text, Is.EqualTo("부상 2 · 중상 1"));
        Assert.That(((Text)Get(view, "waveText")).text, Does.Contain($"웨이브 1/{WAVE_COUNT}"));
    }

    // 실제 레이아웃을 돌려 가장 긴 상태에서 어떤 칸도 글자보다 좁지 않은지 본다. 상단바만 활성 부모에 복제한다.
    [Test]
    public void HudTopBarFitsLongestStateWithoutWrapping()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Hud.prefab");
        GameObject root = new GameObject("HudFit", typeof(RectTransform));
        try
        {
            var rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(HUD_WIDTH, 1080);
            var view = UnityEngine.Object.Instantiate(prefab.GetComponentInChildren<HudView>(true).gameObject, root.transform).GetComponent<HudView>();
            view.Show($"시드 {int.MaxValue} · 웨이브 {WAVE_COUNT}/{WAVE_COUNT} · 공습 경보", $"{Table.session.prepSeconds}초", "스크랩 999", "핵심 시설 200/200", "인원 9/9 (파견 8, 귀환 예정 1)", "부상 8 · 중상 8");
            view.ShowVisitorNotice(true);
            view.ShowExpeditionEventNotice(2, "탐사 사건 2건 · 15초");
            var bar = (RectTransform)view.transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(bar);
            float right = 0;
            var problems = new List<string>();
            for (int index = 0; index < bar.childCount; index++)
            {
                var child = (RectTransform)bar.GetChild(index);
                if (!child.gameObject.activeSelf)
                {
                    continue;
                }
                right = Mathf.Max(right, child.anchoredPosition.x + child.rect.width * (1 - child.pivot.x));
                foreach (Text label in child.GetComponentsInChildren<Text>(true))
                {
                    if (label.preferredWidth > label.rectTransform.rect.width + 0.5f)
                    {
                        problems.Add($"{label.text}: 글자 {label.preferredWidth:0} > 칸 {label.rectTransform.rect.width:0}");
                    }
                }
            }
            Assert.That(problems, Is.Empty);
            Assert.That(right, Is.LessThanOrEqualTo(bar.rect.width));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    // ── 7.4 로그: 권위 경로로 실제 세션을 돌려 JSONL에 남은 줄을 본다 ──
    [Test]
    public void AllScopeEventsReachJsonlWithScopeFields()
    {
        BuildSession();
        logFolder = Path.Combine(Path.GetTempPath(), "SixWaveResultTests", Guid.NewGuid().ToString("N"));
        var store = new FileLogStore(logFolder);
        var logger = host.AddComponent<LogGameLifecycle>();
        Set(logger, "session", new LogSession(store, "g5"));
        Set(logger, "currentSeed", SEED);
        Invoke(logger, "OnEnable");
        try
        {
            // 방문: W2 영입, W4 거절.
            GoToWave(2);
            Assert.That(session.TryRecruitVisitor(out string error), Is.True, error);
            // 탐사 사건과 구조 합류.
            ActiveExpedition expedition = Dispatch("s5");
            RaiseEvent(ExpeditionEventType.Rescue, ROLL_LOW);
            Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out error), Is.True, error);
            Invoke(session, "TickExpeditions", expedition.remainingSeconds);
            // 부상과 치료: 시작 → 경보에 일시정지 → 재개 → 완료.
            session.DebugLowerHp("s1", 24);
            Assert.That(session.TryStartTreatment("s1", "s6", out error), Is.True, error);
            GoToWave(3);
            Set(session.FindUnit("s6"), "arrived", true);
            Invoke(session, "TickTreatment", 1000f);
            // 성장과 사망.
            session.DebugAddExperience("s2", StatType.Work, session.Experience.NeededXp(StateOf("s2").Work));
            session.DebugKillSurvivor("s7");
            GoToWave(4);
            Assert.That(session.DeclineVisitor(), Is.True);
            GoToWave(WAVE_COUNT);
            NextWave();
        }
        finally
        {
            Invoke(logger, "OnDisable");
        }

        var seen = new Dictionary<string, List<JObject>>();
        foreach (string line in File.ReadAllLines(store.PathFor("g5")))
        {
            JObject record = JObject.Parse(line);
            string type = (string)record["type"];
            if (!seen.ContainsKey(type))
            {
                seen[type] = new List<JObject>();
            }
            seen[type].Add((JObject)record["payload"]);
        }
        var scope = new Dictionary<string, string[]>
        {
            { "VisitorArrived", new[] { "candidateId", "wave" } },
            { "RecruitmentAccepted", new[] { "candidateId", "cost", "aliveCount" } },
            { "RecruitmentRejected", new[] { "reason" } },
            { "SurvivorJoined", new[] { "newId", "source" } },
            { "ExpeditionEventRaised", new[] { "expeditionId", "type" } },
            { "ExpeditionEventDecision", new[] { "expeditionId", "decision" } },
            { "ExpeditionEventResolved", new[] { "expeditionId", "outcome" } },
            { "SurvivorInjured", new[] { "id", "hp", "status" } },
            { "TreatmentStarted", new[] { "patientId", "workerId", "cost" } },
            { "TreatmentPaused", new[] { "patientId" } },
            { "TreatmentCompleted", new[] { "patientId", "hp" } },
            { "ExperienceGained", new[] { "id", "stat", "amount" } },
            { "StatImproved", new[] { "id", "stat", "old", "new" } },
            { "SurvivorDied", new[] { "id", "cause" } },
            { "WaveEnded", new[] { "index", "alive", "injured", "recruited", "scrap" } },
            { "SessionEnded", new[] { "result", "seed" } },
        };
        foreach (KeyValuePair<string, string[]> entry in scope)
        {
            Assert.That(seen.ContainsKey(entry.Key), Is.True, entry.Key + "가 기록되지 않았다.");
            foreach (string field in entry.Value)
            {
                Assert.That(seen[entry.Key][0].ContainsKey(field), Is.True, $"{entry.Key}.{field}");
            }
        }

        // 값까지 확인한다.
        Assert.That((int)seen["VisitorArrived"][0]["wave"], Is.EqualTo(2));
        Assert.That((int)seen["VisitorArrived"][1]["wave"], Is.EqualTo(4));
        Assert.That((int)seen["RecruitmentAccepted"][0]["cost"], Is.EqualTo(Table.recruitment.visitorCost));
        Assert.That((int)seen["RecruitmentAccepted"][0]["aliveCount"], Is.EqualTo(8));
        Assert.That((string)seen["RecruitmentRejected"][0]["reason"], Is.EqualTo(RecruitmentSystem.REASON_DECLINED));
        Assert.That(seen["SurvivorJoined"].ConvertAll(_payload => (string)_payload["source"]), Is.EqualTo(new[] { RecruitmentSystem.SOURCE_VISITOR, RecruitmentSystem.SOURCE_RESCUE }));
        Assert.That((string)seen["ExpeditionEventRaised"][0]["type"], Is.EqualTo("rescue"));
        Assert.That((string)seen["ExpeditionEventDecision"][0]["decision"], Is.EqualTo(GameSession.DECISION_RESCUE));
        Assert.That((string)seen["ExpeditionEventResolved"][0]["outcome"], Is.EqualTo(ExpeditionEvent.OUTCOME_RESCUE_SUCCESS));
        Assert.That((string)seen["SurvivorInjured"][0]["status"], Is.EqualTo("Critical"));
        Assert.That((string)seen["TreatmentStarted"][0]["patientId"], Is.EqualTo("s1"));
        Assert.That((string)seen["TreatmentPaused"][0]["patientId"], Is.EqualTo("s1"));
        Assert.That((string)seen["TreatmentCompleted"][0]["patientId"], Is.EqualTo("s1"));
        Assert.That((string)seen["SurvivorDied"][0]["id"], Is.EqualTo("s7"));
        Assert.That((int)seen["WaveEnded"][1]["index"], Is.EqualTo(2));
        Assert.That((int)seen["WaveEnded"][1]["recruited"], Is.EqualTo(2), "W2 방문과 구조 합류");
        Assert.That((int)seen["WaveEnded"][0]["recruited"], Is.Zero);
        Assert.That(seen["WaveEnded"].Count, Is.EqualTo(WAVE_COUNT));
        Assert.That(seen["SessionEnded"].Count, Is.EqualTo(1));
        Assert.That((string)seen["SessionEnded"][0]["result"], Is.EqualTo("Victory"));
        Assert.That((int)seen["SessionEnded"][0]["seed"], Is.EqualTo(SEED));
    }

    [Test]
    public void QuitAfterStartWritesSeedInSessionEnded()
    {
        host = new GameObject("Fixture");
        host.SetActive(false);
        logFolder = Path.Combine(Path.GetTempPath(), "SixWaveResultTests", Guid.NewGuid().ToString("N"));
        var store = new FileLogStore(logFolder);
        var logger = host.AddComponent<LogGameLifecycle>();
        Set(logger, "session", new LogSession(store, "quit"));
        Set(logger, "currentSeed", SEED);
        Invoke(logger, "EndForQuit");
        JObject record = JObject.Parse(File.ReadAllLines(store.PathFor("quit"))[0]);
        Assert.That((string)record["payload"]["reason"], Is.EqualTo("quit"));
        Assert.That((int)record["payload"]["seed"], Is.EqualTo(SEED));
    }
}
