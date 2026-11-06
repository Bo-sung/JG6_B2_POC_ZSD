using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

// G3 탐사 사건과 구조 귀환: 사건 재현, v1 결과 불변, 선택별 정산, 중복 방지, 타임아웃, 상한·예약, 전멸, 합류.
public sealed class ExpeditionEventTests
{
    const BindingFlags PRIVATE = BindingFlags.NonPublic | BindingFlags.Instance;
    // 구조·수색 판정 난수. 0이면 항상 성공 쪽, 1에 가까우면 실패 쪽이다.
    const double ROLL_LOW = 0;
    const double ROLL_HIGH = 0.999;

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

    static ExpeditionRegionData Clone(ExpeditionRegionData _source)
    {
        return JsonUtility.FromJson<ExpeditionRegionData>(JsonUtility.ToJson(_source));
    }

    // 사건을 디버그로만 일으키도록 발생률을 0으로 두고, 필요하면 보상·사망 확률을 고정한 지역 표.
    static ExpeditionRegionData[] QuietRegions(float? _reward = null, float? _death = null)
    {
        var regions = new ExpeditionRegionData[Table.expeditions.Length];
        for (int index = 0; index < regions.Length; index++)
        {
            ExpeditionRegionData region = Clone(Table.expeditions[index]);
            region.eventChance = 0;
            if (_reward.HasValue)
            {
                region.rewardChanceBase = _reward.Value;
                region.rewardChancePerExploration = 0;
            }
            if (_death.HasValue)
            {
                region.deathChanceBase = _death.Value;
                region.deathChancePerExploration = 0;
                region.deathChancePerExtraMember = 0;
            }
            regions[index] = region;
        }
        return regions;
    }

    // ── 재현과 v1 결과 불변(V2-IMPL-10) ──
    static ExpeditionSystem MakeSystem(int _seed, ExpeditionRegionData[] _regions = null)
    {
        return new ExpeditionSystem(_regions ?? Table.expeditions, new System.Random(_seed), Table.expeditionEvents, new System.Random(_seed + 1));
    }

    static bool StartMember(ExpeditionSystem _system, string _regionId, string _id, float _exploration = 2)
    {
        return _system.TryStart(_regionId, new[] { new ExpeditionMember(_id, _exploration) }, out _);
    }

    // 사건 스트림만 쓰는지 보려고 대기 사건을 '임무 계속'처럼 끝낸다. 시간은 바뀌지 않는다.
    static void CloseEvents(ExpeditionSystem _system)
    {
        foreach (ActiveExpedition expedition in _system.Active)
        {
            if (expedition.Event != null && expedition.Event.State == ExpeditionEventState.Pending)
            {
                typeof(ExpeditionEvent).GetProperty("State").SetValue(expedition.Event, ExpeditionEventState.Resolved);
            }
        }
    }

    [Test]
    public void SameSeedReproducesEventPlanAndDifferentSeedDiffers()
    {
        string first = Plan(77);
        Assert.That(Plan(77), Is.EqualTo(first));
        Assert.That(Plan(78), Is.Not.EqualTo(first));
        Assert.That(first, Does.Contain("Rescue").Or.Contain("Supply").Or.Contain("Encounter"), "표본에 실제 사건이 있어야 비교가 뜻이 있다.");
    }

    static string Plan(int _seed)
    {
        ExpeditionSystem system = MakeSystem(_seed);
        var plan = new List<string>();
        for (int index = 0; index < 40; index++)
        {
            string region = Table.expeditions[index % 2].id;
            Assert.That(StartMember(system, region, "m" + index), Is.True);
            ExpeditionEvent planned = system.Active[index].Event;
            plan.Add(planned == null ? "-" : $"{planned.Type}@{planned.TriggerSeconds:0.###}");
        }
        return string.Join(",", plan);
    }

    [Test]
    public void EventStreamDoesNotShiftExpeditionResultStream()
    {
        // v1의 판정 순서를 그대로 옮긴 기준값과, 사건 스트림을 쓰는 시스템의 결과를 비교한다.
        foreach (int seed in new[] { 3, 19, 101 })
        {
            ExpeditionRegionData region = Table.expeditions[1];
            var golden = new System.Random(seed);
            ExpeditionSystem formula = MakeSystem(seed);
            ExpeditionSystem system = MakeSystem(seed);
            int raised = 0;
            for (int round = 0; round < 60; round++)
            {
                var members = new[] { new ExpeditionMember("a" + round, 2), new ExpeditionMember("b" + round, 3) };
                Assert.That(system.TryStart(region.id, members, out _), Is.True);
                raised += system.Active[0].Event != null ? 1 : 0;
                system.Advance(region.durationSeconds);
                CloseEvents(system);
                ExpeditionOutcome actual = system.CollectFinished()[0];

                bool rewarded = golden.NextDouble() < formula.RewardChance(region.id, 5);
                bool died = golden.NextDouble() < formula.DeathChance(region.id, 5, 2);
                int victim = golden.Next(2);
                string expectedDead = died ? (victim == 0 ? members[0].id : members[1].id) : null;
                Assert.That(actual.reward, Is.EqualTo(rewarded ? formula.ExpectedReward(region.id, 5) : 0), $"시드 {seed} {round}회차 보상");
                Assert.That(actual.casualties, Is.EqualTo(expectedDead != null ? new[] { expectedDead } : Array.Empty<string>()), $"시드 {seed} {round}회차 사망");
            }
            Assert.That(raised, Is.GreaterThan(0), "사건이 실제로 도착한 표본이어야 한다.");
        }
    }

    [Test]
    public void WithdrawAndWipeConsumeTheSameResultDraws()
    {
        ExpeditionRegionData region = Table.expeditions[0];
        ExpeditionRegionData[] regions = QuietRegions();
        double baseline = NextDrawAfterTwoExpeditions(regions, region.id, _system => { });
        Assert.That(NextDrawAfterTwoExpeditions(regions, region.id, _system => _system.Withdraw(_system.Active[0])), Is.EqualTo(baseline), "철수");
        Assert.That(NextDrawAfterTwoExpeditions(regions, region.id, _system => _system.CollectFinished(_id => _id != "first")), Is.EqualTo(baseline), "전멸");
    }

    // 탐사 두 건을 끝까지 진행한 뒤 결과 스트림의 다음 값. 소비 횟수가 다르면 값이 달라진다.
    static double NextDrawAfterTwoExpeditions(ExpeditionRegionData[] _regions, string _regionId, Action<ExpeditionSystem> _first)
    {
        var random = new System.Random(5);
        var system = new ExpeditionSystem(_regions, random, Table.expeditionEvents, new System.Random(6));
        StartMember(system, _regionId, "first");
        StartMember(system, _regionId, "second");
        _first(system);
        system.Tick(1000);
        return random.NextDouble();
    }

    [Test]
    public void SessionStreamsAreIndependent()
    {
        var plain = new SessionRng(9).For("expedition");
        var rng = new SessionRng(9);
        System.Random mixed = rng.For("expedition");
        System.Random events = rng.For("expedition_event");
        for (int index = 0; index < 50; index++)
        {
            events.NextDouble();
            Assert.That(mixed.NextDouble(), Is.EqualTo(plain.NextDouble()));
        }
    }

    // ── 발생률·유형·시점(TD-31) ──
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void RateTypeAndTimingStayInRange(int _seed)
    {
        const int SAMPLES = 2000;
        const double TOLERANCE = 0.05;
        foreach (ExpeditionRegionData region in Table.expeditions)
        {
            ExpeditionSystem system = MakeSystem(_seed);
            int total = 0, rescue = 0, supply = 0, encounter = 0;
            for (int index = 0; index < SAMPLES; index++)
            {
                Assert.That(StartMember(system, region.id, "m" + index), Is.True);
                ExpeditionEvent planned = system.Active[index].Event;
                if (planned == null)
                {
                    continue;
                }
                total++;
                rescue += planned.Type == ExpeditionEventType.Rescue ? 1 : 0;
                supply += planned.Type == ExpeditionEventType.Supply ? 1 : 0;
                encounter += planned.Type == ExpeditionEventType.Encounter ? 1 : 0;
                Assert.That(planned.TriggerSeconds, Is.InRange(region.durationSeconds * 0.3f, region.durationSeconds * 0.7f));
                Assert.That(planned.State, Is.EqualTo(ExpeditionEventState.Scheduled));
            }
            double weights = region.rescueWeight + region.supplyWeight + region.encounterWeight;
            Assert.That((double)total / SAMPLES, Is.EqualTo(region.eventChance).Within(TOLERANCE), region.id + " 발생률");
            Assert.That((double)rescue / total, Is.EqualTo(region.rescueWeight / weights).Within(TOLERANCE), region.id + " 구조");
            Assert.That((double)supply / total, Is.EqualTo(region.supplyWeight / weights).Within(TOLERANCE), region.id + " 보급");
            Assert.That((double)encounter / total, Is.EqualTo(region.encounterWeight / weights).Within(TOLERANCE), region.id + " 조우");
        }
    }

    [Test]
    public void TableValuesMatchProvisionalDecisions()
    {
        ExpeditionEventData data = Table.expeditionEvents;
        Assert.That(Table.expeditions[0].eventChance, Is.EqualTo(0.4f));
        Assert.That(Table.expeditions[1].eventChance, Is.EqualTo(0.6f));
        Assert.That(ExpeditionEventRules.RescueChance(data, 0), Is.EqualTo(0.4f).Within(0.0001f));
        Assert.That(ExpeditionEventRules.RescueChance(data, 3), Is.EqualTo(0.58f).Within(0.0001f));
        Assert.That(ExpeditionEventRules.RescueChance(data, 20), Is.EqualTo(0.9f).Within(0.0001f), "상한 90%");
        Assert.That(ExpeditionEventRules.SearchBonus(data, 5), Is.EqualTo(3), "기본 보상의 50%는 올림이다.");
        Assert.That(ExpeditionEventRules.EncounterDamageFraction(data, 0), Is.EqualTo(0.4f).Within(0.0001f));
        Assert.That(ExpeditionEventRules.EncounterDamageFraction(data, 5), Is.EqualTo(0.3f).Within(0.0001f));
        Assert.That(ExpeditionEventRules.EncounterDamageFraction(data, 50), Is.EqualTo(0.2f).Within(0.0001f), "최소 20%");
        Assert.That(ExpeditionEventRules.PickVictim(0.999, 3), Is.EqualTo(2));
        Assert.That(ExpeditionEventRules.PickVictim(0, 3), Is.Zero);
    }

    [Test]
    public void TableRulesRejectBadEventValues()
    {
        TableManager tables = TableManager.Instance.RequireValid();
        ExpeditionEventData saved = tables.Gameplay.expeditionEvents;
        float savedChance = tables.Gameplay.expeditions[0].eventChance;
        try
        {
            tables.Gameplay.expeditionEvents = JsonUtility.FromJson<ExpeditionEventData>(JsonUtility.ToJson(saved));
            tables.Gameplay.expeditionEvents.triggerMinFraction = 0.9f;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("expeditionEvents"));
            var rules = new GameTableRules();
            rules.Validate(tables);
            Assert.That(rules.IsValid, Is.False);

            tables.Gameplay.expeditionEvents = saved;
            tables.Gameplay.expeditions[0].eventChance = 1.5f;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("eventChance"));
            rules = new GameTableRules();
            rules.Validate(tables);
            Assert.That(rules.IsValid, Is.False);
        }
        finally
        {
            tables.Gameplay.expeditionEvents = saved;
            tables.Gameplay.expeditions[0].eventChance = savedChance;
        }
    }

    // ── 세션 픽스처 ──
    GameObject host;
    GameObject hudInstance;
    GameSession session;
    SessionFlow flow;
    readonly List<GameEvents.ExpeditionEventRaisedInfo> raised = new List<GameEvents.ExpeditionEventRaisedInfo>();
    readonly List<GameEvents.ExpeditionEventDecisionInfo> decided = new List<GameEvents.ExpeditionEventDecisionInfo>();
    readonly List<GameEvents.ExpeditionEventResolvedInfo> resolved = new List<GameEvents.ExpeditionEventResolvedInfo>();
    readonly List<GameEvents.ExpeditionResultInfo> settled = new List<GameEvents.ExpeditionResultInfo>();
    readonly List<GameEvents.SurvivorJoinedInfo> joined = new List<GameEvents.SurvivorJoinedInfo>();
    readonly List<GameEvents.SurvivorDeathInfo> died = new List<GameEvents.SurvivorDeathInfo>();
    readonly List<GameEvents.SurvivorInjuredInfo> injured = new List<GameEvents.SurvivorInjuredInfo>();
    readonly List<string> rejected = new List<string>();
    Action<GameEvents.ExpeditionEventRaisedInfo> onRaised;
    Action<GameEvents.ExpeditionEventDecisionInfo> onDecided;
    Action<GameEvents.ExpeditionEventResolvedInfo> onResolved;
    Action<GameEvents.ExpeditionResultInfo> onSettled;
    Action<GameEvents.SurvivorJoinedInfo> onJoined;
    Action<GameEvents.SurvivorDeathInfo> onDied;
    Action<GameEvents.SurvivorInjuredInfo> onInjured;
    Action<GameEvents.RecruitmentRejectedInfo> onRejected;

    // 실제 Survivor.prefab으로 만든 세션. _count가 7을 넘으면 테스트용 인원을 붙인다.
    void BuildSession(int _count = 7, int _scrap = 20, ExpeditionRegionData[] _regions = null)
    {
        host = new GameObject("Fixture");
        host.SetActive(false);
        session = host.AddComponent<GameSession>();
        GameplayTableData table = Table;
        var treatment = new TreatmentSystem(table.treatment);
        flow = new SessionFlow(table.session.prepSeconds, table.session.alertSeconds, table.session.combatSeconds, table.session.timeoutCoreDamagePerEnemy, table.waves.Length);
        Set(session, "table", table);
        Set(session, "flow", flow);
        Set(session, "economy", new ResourceEconomy(_scrap, table.production.scrapPerSecondPerWork));
        Set(session, "treatment", treatment);
        var board = new AssignmentBoard(table.production.maxWorkers, treatment.Involves);
        Set(session, "board", board);
        Set(session, "recruitment", new RecruitmentSystem(table.recruitment, new System.Random(11)));
        Set(session, "expeditions", MakeSystem(21, _regions ?? QuietRegions()));
        var core = host.AddComponent<CoreFacility>();
        core.Initialize(table.core.maxHp);
        Set(session, "core", core);
        Set(session, "waveDirector", host.AddComponent<WaveDirector>());
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
        for (int index = 0; index < _count; index++)
        {
            SurvivorData data = index < table.survivors.Length
                ? table.survivors[index]
                : new SurvivorData { id = "d" + (index + 1), displayName = "테스트" + (index + 1), combat = 2, work = 2, exploration = 2, maxHp = 90 };
            SurvivorUnit unit = UnityEngine.Object.Instantiate(prefab, host.transform);
            unit.SetDefenseMarker(UnityEngine.Object.Instantiate(markerPrefab, host.transform));
            var state = new SurvivorState(data, _injury: table.injury);
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

        raised.Clear();
        decided.Clear();
        resolved.Clear();
        settled.Clear();
        joined.Clear();
        died.Clear();
        injured.Clear();
        rejected.Clear();
        onRaised = _info => raised.Add(_info);
        onDecided = _info => decided.Add(_info);
        onResolved = _info => resolved.Add(_info);
        onSettled = _info => settled.Add(_info);
        onJoined = _info => joined.Add(_info);
        onDied = _info => died.Add(_info);
        onInjured = _info => injured.Add(_info);
        onRejected = _info => rejected.Add(_info.reason);
        GameEvents.ExpeditionEventRaised += onRaised;
        GameEvents.ExpeditionEventDecided += onDecided;
        GameEvents.ExpeditionEventResolved += onResolved;
        GameEvents.ExpeditionResolved += onSettled;
        GameEvents.SurvivorJoined += onJoined;
        GameEvents.SurvivorDied += onDied;
        GameEvents.SurvivorInjured += onInjured;
        GameEvents.RecruitmentRejected += onRejected;
        // 사망 구독은 OnEnable에서 한다. 비활성 픽스처라 직접 연결한다.
        Invoke(session, "OnEnable");
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
        if (onRaised != null)
        {
            GameEvents.ExpeditionEventRaised -= onRaised;
            GameEvents.ExpeditionEventDecided -= onDecided;
            GameEvents.ExpeditionEventResolved -= onResolved;
            GameEvents.ExpeditionResolved -= onSettled;
            GameEvents.SurvivorJoined -= onJoined;
            GameEvents.SurvivorDied -= onDied;
            GameEvents.SurvivorInjured -= onInjured;
            GameEvents.RecruitmentRejected -= onRejected;
            onRaised = null;
        }
        if (session != null)
        {
            Invoke(session, "OnDisable");
        }
        if (host != null)
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
        hudInstance = null;
    }

    static string NearId => Table.expeditions[0].id;

    ActiveExpedition Dispatch(params string[] _ids)
    {
        Assert.That(session.TryStartExpedition(NearId, _ids, out string error), Is.True, error);
        return session.Expeditions.Active[session.Expeditions.Active.Count - 1];
    }

    void Tick(float _seconds) => Invoke(session, "TickExpeditions", _seconds);

    // 사건을 일으켜 정규 경로로 도착시킨다. 시간은 거의 흐르지 않는다.
    void Raise(ExpeditionEventType _type, double _roll = -1)
    {
        Assert.That(session.DebugRaiseExpeditionEvent(_type, out string error, _roll), Is.True, error);
        Tick(0);
    }

    SurvivorState StateOf(string _id) => session.FindUnit(_id).State;

    // ── 구조 ──
    [Test]
    public void RescueSuccessReservesAtOnceAndJoinsOnlyOnReturn()
    {
        BuildSession();
        ActiveExpedition expedition = Dispatch("s5");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        Assert.That(raised.Count, Is.EqualTo(1));
        Assert.That(raised[0].expeditionId, Is.EqualTo(expedition.expeditionId));
        Assert.That(raised[0].type, Is.EqualTo("rescue"));
        Assert.That(session.Recruitment.ReservedSlots, Is.Zero, "고르기 전에는 예약이 없다.");
        int shareBefore = session.Recruitment.RescueShareCount;
        float remainingBefore = expedition.remainingSeconds;

        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out string error), Is.True, error);

        Assert.That(session.Recruitment.ReservedSlots, Is.EqualTo(1), "성공한 순간 예약한다.");
        Assert.That(session.Recruitment.RescueShareCount, Is.EqualTo(shareBefore - 1), "후보를 그 순간 확보한다.");
        Assert.That(expedition.remainingSeconds, Is.EqualTo(remainingBefore + Table.expeditionEvents.rescue.extraSeconds).Within(0.001f));
        Assert.That(decided[0].decision, Is.EqualTo("rescue"));
        Assert.That(resolved[0].outcome, Is.EqualTo("rescue_success"));
        Assert.That(joined, Is.Empty, "귀환 전에는 합류하지 않는다.");
        Assert.That(session.Units.Count, Is.EqualTo(7), "유령 인력이 없다.");
        Assert.That(session.AliveCount, Is.EqualTo(7));
        Assert.That(expedition.RescuedCandidate, Is.Not.Null);
    }

    [Test]
    public void RescueReturnsJoinsExactlyOnceWithNewIdAndReleasesReservation()
    {
        BuildSession(_regions: QuietRegions(_death: 0));
        ActiveExpedition expedition = Dispatch("s5");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out _), Is.True);
        string candidateId = expedition.RescuedCandidate.id;

        Tick(expedition.remainingSeconds);

        Assert.That(joined.Count, Is.EqualTo(1));
        Assert.That(joined[0].source, Is.EqualTo("rescue"));
        Assert.That(joined[0].newId, Is.EqualTo("n1"), "ID는 합류 때 세션 순번으로 새로 발급한다.");
        Assert.That(joined[0].newId, Is.Not.EqualTo(candidateId));
        Assert.That(session.Recruitment.ReservedSlots, Is.Zero);
        Assert.That(session.AliveCount, Is.EqualTo(8));
        Assert.That(session.Units.Count, Is.EqualTo(8));
        SurvivorState rescued = StateOf("n1");
        Assert.That(rescued.Hp, Is.EqualTo(rescued.MaxHp));
        Assert.That(rescued.IsNewJoin, Is.True);
        Assert.That(session.Assignments.WorkOf("n1"), Is.EqualTo(WorkAssignment.DefenseStandby));
        Assert.That(session.Assignments.IsDefenseAssigned("n1"), Is.False);
        Assert.That(settled.Count, Is.EqualTo(1));
        Assert.That(settled[0].rescuedId, Is.EqualTo("n1"));
        Assert.That(expedition.IsSettled, Is.True);

        // 이후 시간이 흘러도, 같은 결정이 다시 와도 추가 합류는 없다.
        Tick(1000);
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out _), Is.False);
        Assert.That(joined.Count, Is.EqualTo(1));
        Assert.That(session.Units.Count, Is.EqualTo(8));
        Assert.That(session.ExpeditionHistory.Count, Is.EqualTo(1));
    }

    [Test]
    public void RescueFailureHurtsOneMemberWithoutJoiningAndCostsTime()
    {
        BuildSession(_regions: QuietRegions(_death: 0));
        ActiveExpedition expedition = Dispatch("s5", "s6");
        Raise(ExpeditionEventType.Rescue, ROLL_HIGH);
        int shareBefore = session.Recruitment.RescueShareCount;
        float remainingBefore = expedition.remainingSeconds;

        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out _), Is.True);

        Assert.That(resolved[0].outcome, Is.EqualTo("rescue_failed"));
        Assert.That(session.Recruitment.ReservedSlots, Is.Zero);
        Assert.That(session.Recruitment.RescueShareCount, Is.EqualTo(shareBefore), "실패는 후보를 소비하지 않는다.");
        Assert.That(expedition.remainingSeconds, Is.EqualTo(remainingBefore + Table.expeditionEvents.rescue.extraSeconds).Within(0.001f));
        SurvivorState victim = StateOf("s5");
        Assert.That(victim.Hp, Is.EqualTo(victim.MaxHp * (1 - Table.expeditionEvents.rescue.failureDamageFraction)).Within(0.001f), "사건 해결 순간 HP에 반영한다.");
        Assert.That(StateOf("s6").Hp, Is.EqualTo(StateOf("s6").MaxHp), "피해는 1명만 입는다.");
        Tick(expedition.remainingSeconds);
        Assert.That(joined, Is.Empty);
        Assert.That(session.Units.Count, Is.EqualTo(7));
    }

    [Test]
    public void RescueChanceUsesEffectiveExplorationOfLivingMembers()
    {
        BuildSession();
        ActiveExpedition expedition = Dispatch("s5", "s6");
        Assert.That(session.RescueChanceOf(expedition), Is.EqualTo(ExpeditionEventRules.RescueChance(Table.expeditionEvents, 8)).Within(0.0001f));
        session.DebugLowerHp("s5", StateOf("s5").MaxHp * 0.5f);
        // 경상 s5의 실효 탐사 3.75 + s6의 3 = 6.75, 반올림 7.
        Assert.That(session.RescueChanceOf(expedition), Is.EqualTo(ExpeditionEventRules.RescueChance(Table.expeditionEvents, 7)).Within(0.0001f));
        session.DebugKillSurvivor("s6");
        Assert.That(session.RescueChanceOf(expedition), Is.EqualTo(ExpeditionEventRules.RescueChance(Table.expeditionEvents, 4)).Within(0.0001f), "죽은 파티원은 판정에서 빠진다.");
    }

    // ── 중복 정산(엣지 6) ──
    [TestCase(ExpeditionEventType.Rescue, ExpeditionDecision.Act)]
    [TestCase(ExpeditionEventType.Rescue, ExpeditionDecision.Continue)]
    [TestCase(ExpeditionEventType.Rescue, ExpeditionDecision.Withdraw)]
    [TestCase(ExpeditionEventType.Supply, ExpeditionDecision.Act)]
    [TestCase(ExpeditionEventType.Supply, ExpeditionDecision.Continue)]
    [TestCase(ExpeditionEventType.Supply, ExpeditionDecision.Withdraw)]
    public void DuplicateDecisionSettlesOnceAndSecondReturnsFalse(ExpeditionEventType _type, ExpeditionDecision _decision)
    {
        BuildSession();
        ActiveExpedition expedition = Dispatch("s5", "s6");
        Raise(_type, ROLL_LOW);
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, _decision, out _), Is.True);
        int reservedAfter = session.Recruitment.ReservedSlots;
        int shareAfter = session.Recruitment.RescueShareCount;
        int bonusAfter = expedition.BonusReward;
        float remainingAfter = expedition.remainingSeconds;

        foreach (ExpeditionDecision again in new[] { _decision, ExpeditionDecision.Act, ExpeditionDecision.Continue, ExpeditionDecision.Withdraw, ExpeditionDecision.Timeout })
        {
            Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, again, out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        Assert.That(session.Recruitment.ReservedSlots, Is.EqualTo(reservedAfter));
        Assert.That(session.Recruitment.RescueShareCount, Is.EqualTo(shareAfter));
        Assert.That(expedition.BonusReward, Is.EqualTo(bonusAfter));
        Assert.That(expedition.remainingSeconds, Is.EqualTo(remainingAfter));
        Assert.That(decided.Count, Is.EqualTo(1));
        Assert.That(resolved.Count, Is.EqualTo(1));
    }

    [Test]
    public void DecisionOnUnknownOrFinishedExpeditionIsRefused()
    {
        BuildSession(_regions: QuietRegions(_death: 0));
        Assert.That(session.TryResolveExpeditionEvent(999, ExpeditionDecision.Continue, out _), Is.False);
        ActiveExpedition expedition = Dispatch("s5");
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Continue, out _), Is.False, "사건이 없는 탐사");
        Tick(expedition.remainingSeconds);
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Continue, out _), Is.False, "끝난 탐사");
        Assert.That(decided, Is.Empty);
    }

    // ── 타임아웃 ──
    [Test]
    public void TimeoutContinuesMissionAfterGameSeconds()
    {
        BuildSession();
        ActiveExpedition expedition = Dispatch("s5");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        float timeout = Table.expeditionEvents.timeoutSeconds;
        Assert.That(expedition.Event.TimeoutRemaining, Is.EqualTo(timeout));
        Tick(timeout - 0.5f);
        Assert.That(expedition.Event.State, Is.EqualTo(ExpeditionEventState.Pending));
        Assert.That(session.Expeditions.PendingEventCount, Is.EqualTo(1));
        float remainingBefore = expedition.remainingSeconds;

        Tick(0.5f);

        Assert.That(expedition.Event.State, Is.EqualTo(ExpeditionEventState.Resolved));
        Assert.That(decided.Count, Is.EqualTo(1));
        Assert.That(decided[0].decision, Is.EqualTo("timeout"), "미응답은 임무 계속과 구별된다.");
        Assert.That(resolved[0].outcome, Is.EqualTo("continued"));
        Assert.That(session.Recruitment.ReservedSlots, Is.Zero);
        Assert.That(expedition.remainingSeconds, Is.EqualTo(remainingBefore - 0.5f).Within(0.001f), "임무 계속은 시간을 더하지 않는다.");
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out _), Is.False, "타임아웃 뒤 늦은 클릭은 거부한다.");
        Assert.That(session.Recruitment.ReservedSlots, Is.Zero);
    }

    [Test]
    public void PendingEventIsClosedBeforeReturnSettlement()
    {
        BuildSession(_regions: QuietRegions(_death: 0));
        ActiveExpedition expedition = Dispatch("s5");
        Tick(expedition.remainingSeconds - 1);
        Raise(ExpeditionEventType.Supply, ROLL_LOW);
        Assert.That(expedition.Event.TimeoutRemaining, Is.GreaterThan(expedition.remainingSeconds), "귀환이 타임아웃보다 먼저 온다.");
        Tick(1);
        Assert.That(decided.Count, Is.EqualTo(1));
        Assert.That(decided[0].decision, Is.EqualTo("timeout"));
        Assert.That(settled.Count, Is.EqualTo(1));
        Assert.That(session.Expeditions.Active, Is.Empty);
    }

    [Test]
    public void TimerRunsInPrepAndCombatButStopsInResult()
    {
        BuildSession();
        ActiveExpedition expedition = Dispatch("s5");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        float start = expedition.Event.TimeoutRemaining;
        Invoke(session, "TickSession", 1f);
        Assert.That(expedition.Event.TimeoutRemaining, Is.EqualTo(start - 1).Within(0.001f), "준비 단계에서 흐른다.");
        flow.Advance();
        flow.Advance();
        Assert.That(session.Phase, Is.EqualTo(SessionPhase.Combat));
        Invoke(session, "TickSession", 1f);
        Assert.That(expedition.Event.TimeoutRemaining, Is.EqualTo(start - 2).Within(0.001f), "전투 중에도 흐른다.");
        while (flow.Phase != SessionPhase.Result)
        {
            flow.Advance();
        }
        float remaining = expedition.remainingSeconds;
        float timer = expedition.Event.TimeoutRemaining;
        Invoke(session, "TickSession", 100f);
        Assert.That(expedition.Event.TimeoutRemaining, Is.EqualTo(timer), "결과 단계에서는 멈춘다.");
        Assert.That(expedition.remainingSeconds, Is.EqualTo(remaining));
        Assert.That(expedition.Event.State, Is.EqualTo(ExpeditionEventState.Pending));
    }

    [Test]
    public void DecisionWorksDuringCombatAndIsRefusedInResultAndAfterEnd()
    {
        BuildSession();
        ActiveExpedition first = Dispatch("s5");
        ActiveExpedition second = Dispatch("s6");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        flow.Advance();
        flow.Advance();
        Assert.That(session.Phase, Is.EqualTo(SessionPhase.Combat));
        Assert.That(session.TryResolveExpeditionEvent(first.expeditionId, ExpeditionDecision.Act, out _), Is.True, "전투 중에도 처리한다.");
        Assert.That(session.Recruitment.ReservedSlots, Is.EqualTo(1));

        while (flow.Phase != SessionPhase.Result)
        {
            flow.Advance();
        }
        Assert.That(session.TryResolveExpeditionEvent(second.expeditionId, ExpeditionDecision.Act, out string error), Is.False);
        Assert.That(error, Does.Contain("결과"));
        Assert.That(second.Event.State, Is.EqualTo(ExpeditionEventState.Pending));

        // DEC-30: 세션이 끝나면 진행 중 사건과 탐사는 결과 없이 끝난다.
        flow.EndDefeat();
        Assert.That(session.TryResolveExpeditionEvent(second.expeditionId, ExpeditionDecision.Act, out _), Is.False);
        Invoke(session, "TickSession", 1000f);
        Assert.That(joined, Is.Empty);
        Assert.That(settled, Is.Empty);
        Assert.That(session.Recruitment.ReservedSlots, Is.EqualTo(1), "정산이 일어나지 않는다.");
    }

    // ── 상한과 예약 ──
    [Test]
    public void FullCapacityRejectsRescueAndKeepsEventPending()
    {
        BuildSession(_count: 9);
        ActiveExpedition expedition = Dispatch("s5");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        Assert.That(expedition.Event.Type, Is.EqualTo(ExpeditionEventType.Rescue), "정원이 차도 사건은 나타난다.");
        int shareBefore = session.Recruitment.RescueShareCount;
        float remainingBefore = expedition.remainingSeconds;

        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out string error), Is.False);

        Assert.That(error, Does.Contain("정원"));
        Assert.That(rejected, Is.EqualTo(new[] { "full" }), "권위 경로까지 가서 사유가 기록된다.");
        Assert.That(expedition.Event.State, Is.EqualTo(ExpeditionEventState.Pending), "거부는 사건을 끝내지 않는다.");
        Assert.That(session.Recruitment.ReservedSlots, Is.Zero);
        Assert.That(session.Recruitment.RescueShareCount, Is.EqualTo(shareBefore), "후보를 소비하지 않는다.");
        Assert.That(expedition.remainingSeconds, Is.EqualTo(remainingBefore));
        Assert.That(decided, Is.Empty);
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Continue, out _), Is.True, "다른 대응은 가능하다.");
    }

    [Test]
    public void ReservationCountsAgainstRescueOfOtherExpedition()
    {
        BuildSession(_count: 8);
        ActiveExpedition first = Dispatch("s5");
        ActiveExpedition second = Dispatch("s6");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        Assert.That(session.TryResolveExpeditionEvent(first.expeditionId, ExpeditionDecision.Act, out _), Is.True);
        Assert.That(session.AliveCount + session.Recruitment.ReservedSlots, Is.EqualTo(9));

        Assert.That(session.TryResolveExpeditionEvent(second.expeditionId, ExpeditionDecision.Act, out string error), Is.False);

        Assert.That(error, Does.Contain("정원"));
        Assert.That(rejected, Is.EqualTo(new[] { "full" }));
        Assert.That(second.Event.State, Is.EqualTo(ExpeditionEventState.Pending));
        Assert.That(session.Recruitment.ReservedSlots, Is.EqualTo(1));
    }

    [Test]
    public void ThreeSimultaneousRescuesNeverExceedCapacity()
    {
        BuildSession();
        ActiveExpedition first = Dispatch("s5");
        ActiveExpedition second = Dispatch("s6");
        ActiveExpedition third = Dispatch("s7");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        Assert.That(session.TryResolveExpeditionEvent(first.expeditionId, ExpeditionDecision.Act, out _), Is.True);
        Assert.That(session.TryResolveExpeditionEvent(second.expeditionId, ExpeditionDecision.Act, out _), Is.True);
        Assert.That(session.TryResolveExpeditionEvent(third.expeditionId, ExpeditionDecision.Act, out _), Is.False, "7 + 예약 2 = 9");
        Assert.That(session.Recruitment.ReservedSlots, Is.EqualTo(2));
    }

    [Test]
    public void VisitorRecruitIsBlockedByReservationAndScrapIsUntouched()
    {
        BuildSession(_count: 8);
        session.Recruitment.ArriveVisitor(Table.recruitment.visitorWaves[0]);
        ActiveExpedition expedition = Dispatch("s5");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out _), Is.True);
        int scrapBefore = session.Scrap;

        Assert.That(session.TryRecruitVisitor(out string error), Is.False);

        Assert.That(error, Does.Contain("정원"));
        Assert.That(rejected, Is.EqualTo(new[] { "full" }));
        Assert.That(session.Scrap, Is.EqualTo(scrapBefore));
        Assert.That(session.AliveCount, Is.EqualTo(8));
    }

    [Test]
    public void VisitorFirstThenRescueIsRejectedSoCountStaysNine()
    {
        BuildSession(_count: 8);
        session.Recruitment.ArriveVisitor(Table.recruitment.visitorWaves[0]);
        ActiveExpedition expedition = Dispatch("s5");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        Assert.That(session.TryRecruitVisitor(out string recruitError), Is.True, recruitError);
        Assert.That(session.AliveCount, Is.EqualTo(9));

        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out _), Is.False);

        Assert.That(session.Recruitment.ReservedSlots, Is.Zero);
        Assert.That(session.AliveCount, Is.EqualTo(9));
        Assert.That(rejected, Is.EqualTo(new[] { "full" }));
    }

    [Test]
    public void RescuedAndVisitorNeverShareIdOrPerson()
    {
        BuildSession(_regions: QuietRegions(_death: 0));
        session.Recruitment.ArriveVisitor(Table.recruitment.visitorWaves[0]);
        string visitorName = session.Recruitment.PendingVisitor.Candidate.displayName;
        ActiveExpedition expedition = Dispatch("s5");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out _), Is.True);
        Assert.That(session.TryRecruitVisitor(out _), Is.True);
        Tick(expedition.remainingSeconds);

        Assert.That(joined.Count, Is.EqualTo(2));
        Assert.That(joined[0].source, Is.EqualTo("visitor"));
        Assert.That(joined[1].source, Is.EqualTo("rescue"));
        Assert.That(joined[0].newId, Is.Not.EqualTo(joined[1].newId));
        Assert.That(StateOf(joined[1].newId).DisplayName, Is.Not.EqualTo(visitorName), "방문 몫과 구조 몫은 다른 사람이다.");
    }

    // ── 전멸 ──
    [Test]
    public void WipeAfterRescueSuccessReleasesReservationAndLosesCandidate()
    {
        BuildSession();
        ActiveExpedition expedition = Dispatch("s5");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out _), Is.True);
        int shareAfterTake = session.Recruitment.RescueShareCount;

        session.DebugKillSurvivor("s5");
        Tick(0);

        Assert.That(session.Recruitment.ReservedSlots, Is.Zero, "예약을 푼다.");
        Assert.That(session.Recruitment.RescueShareCount, Is.EqualTo(shareAfterTake), "그 사람은 잃었다. 큐로 돌아가지 않는다.");
        Assert.That(joined, Is.Empty, "유령 인력 없음");
        Assert.That(session.Units.Count, Is.EqualTo(7));
        Assert.That(session.Expeditions.Active, Is.Empty, "전멸이면 바로 정산해 끝낸다.");
        Assert.That(settled.Count, Is.EqualTo(1));
        Assert.That(settled[0].returned, Is.Empty);
        Assert.That(settled[0].reward, Is.Zero);
        Assert.That(settled[0].rescuedId, Is.Null);
    }

    [Test]
    public void ReturnRollKillingLastMemberAlsoLosesRescue()
    {
        BuildSession(_regions: QuietRegions(_reward: 1, _death: 1));
        ActiveExpedition expedition = Dispatch("s5");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out _), Is.True);
        int scrapBefore = session.Scrap;

        Tick(expedition.remainingSeconds);

        Assert.That(StateOf("s5").IsAlive, Is.False);
        Assert.That(settled[0].reward, Is.Zero, "DEC-29: 1인 파티가 사망하면 보상 없음");
        Assert.That(session.Scrap, Is.EqualTo(scrapBefore));
        Assert.That(session.Recruitment.ReservedSlots, Is.Zero);
        Assert.That(joined, Is.Empty);
    }

    [Test]
    public void RescueFailureDamageCanKillAndWipeImmediately()
    {
        BuildSession();
        ActiveExpedition expedition = Dispatch("s5");
        Raise(ExpeditionEventType.Rescue, ROLL_HIGH);
        session.DebugLowerHp("s5", 1);
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out _), Is.True);

        Assert.That(StateOf("s5").IsAlive, Is.False, "HP 0은 그 자리에서 영구 사망이다.");
        Assert.That(died.Count, Is.EqualTo(1));
        Assert.That(died[0].cause, Is.EqualTo(GameSession.CAUSE_EXPEDITION));
        Assert.That(session.Expeditions.Active, Is.Empty, "그 탐사를 바로 정산해 끝낸다.");
        Assert.That(settled[0].casualties, Is.EqualTo(new[] { "s5" }));
        Assert.That(settled[0].returned, Is.Empty);
        Assert.That(expedition.IsSettled, Is.True);
    }

    [Test]
    public void ReturnRollTargetsOnlyLivingMembers()
    {
        BuildSession(_regions: QuietRegions(_reward: 1, _death: 1));
        ActiveExpedition expedition = Dispatch("s5", "s6");
        session.DebugKillSurvivor("s5");
        died.Clear();

        Tick(expedition.remainingSeconds);

        Assert.That(died.Count, Is.EqualTo(1), "이미 죽은 사람에게 사망이 다시 나지 않는다.");
        Assert.That(died[0].survivorId, Is.EqualTo("s6"), "살아 있는 파티원만 판정 대상이다.");
        Assert.That(settled[0].casualties, Is.EquivalentTo(new[] { "s5", "s6" }));
        Assert.That(settled[0].returned, Is.Empty);
        Assert.That(settled[0].reward, Is.Zero, "DEC-29: 귀환자가 없으면 보상 없음");
    }

    [Test]
    public void SurvivorOfPartyStillBringsRewardWhenOtherDied()
    {
        BuildSession(_regions: QuietRegions(_reward: 1, _death: 0));
        ActiveExpedition expedition = Dispatch("s5", "s6");
        int scrapBefore = session.Scrap;
        session.DebugKillSurvivor("s5");
        Tick(expedition.remainingSeconds);
        Assert.That(settled[0].returned, Is.EqualTo(new[] { "s6" }));
        Assert.That(settled[0].reward, Is.EqualTo(session.Expeditions.ExpectedReward(NearId, 3)), "보상은 살아 있는 파티원의 탐사 합으로 계산한다.");
        Assert.That(session.Scrap, Is.EqualTo(scrapBefore + settled[0].reward));
    }

    // ── 추가 보급품 ──
    [Test]
    public void ExtraSearchAddsBonusOnceAtReturnAndCostsTime()
    {
        BuildSession(_regions: QuietRegions(_reward: 1, _death: 0));
        ActiveExpedition expedition = Dispatch("s5", "s6");
        Raise(ExpeditionEventType.Supply, ROLL_HIGH);
        int baseReward = session.Expeditions.ExpectedReward(NearId, 8);
        int bonus = ExpeditionEventRules.SearchBonus(Table.expeditionEvents, baseReward);
        Assert.That(session.SearchBonusOf(expedition), Is.EqualTo(bonus), "패널 표시와 정산이 같다.");
        float remainingBefore = expedition.remainingSeconds;
        int scrapBefore = session.Scrap;

        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out _), Is.True);

        Assert.That(decided[0].decision, Is.EqualTo("search"));
        Assert.That(resolved[0].outcome, Is.EqualTo("search_safe"));
        Assert.That(expedition.remainingSeconds, Is.EqualTo(remainingBefore + Table.expeditionEvents.supply.extraSeconds).Within(0.001f));
        Assert.That(session.Scrap, Is.EqualTo(scrapBefore), "보상은 귀환 정산에서 받는다.");
        Assert.That(StateOf("s5").Hp, Is.EqualTo(StateOf("s5").MaxHp));

        Tick(expedition.remainingSeconds);
        Assert.That(settled[0].reward, Is.EqualTo(baseReward + bonus));
        Assert.That(session.Scrap, Is.EqualTo(scrapBefore + baseReward + bonus));
    }

    [Test]
    public void ExtraSearchRiskHurtsOneMember()
    {
        BuildSession();
        ActiveExpedition expedition = Dispatch("s5", "s6");
        Raise(ExpeditionEventType.Supply, ROLL_LOW);
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out _), Is.True);
        Assert.That(resolved[0].outcome, Is.EqualTo("search_hurt"));
        Assert.That(StateOf("s5").Hp, Is.EqualTo(StateOf("s5").MaxHp * (1 - Table.expeditionEvents.supply.damageFraction)).Within(0.001f));
        Assert.That(expedition.Event.VictimId, Is.EqualTo("s5"));
    }

    // ── 철수 ──
    [Test]
    public void WithdrawRemovesRewardAndDeathRollButKeepsInjury()
    {
        BuildSession(_regions: QuietRegions(_reward: 1, _death: 1));
        ActiveExpedition expedition = Dispatch("s5", "s6");
        session.DebugLowerHp("s5", StateOf("s5").MaxHp * 0.5f);
        float hpBefore = StateOf("s5").Hp;
        Raise(ExpeditionEventType.Supply, ROLL_LOW);
        int scrapBefore = session.Scrap;

        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Withdraw, out _), Is.True);

        Assert.That(decided[0].decision, Is.EqualTo("withdraw"));
        Assert.That(resolved[0].outcome, Is.EqualTo("withdrawn"));
        Assert.That(expedition.remainingSeconds, Is.LessThanOrEqualTo(Table.expeditionEvents.withdrawSeconds));
        Tick(expedition.remainingSeconds);
        Assert.That(settled[0].withdrawn, Is.True);
        Assert.That(settled[0].reward, Is.Zero, "보상 없음");
        Assert.That(settled[0].casualties, Is.Empty, "기존 사망 판정 없음");
        Assert.That(settled[0].returned, Is.EquivalentTo(new[] { "s5", "s6" }));
        Assert.That(session.Scrap, Is.EqualTo(scrapBefore));
        Assert.That(StateOf("s5").Hp, Is.EqualTo(hpBefore), "이미 생긴 부상은 남는다.");
        Assert.That(StateOf("s5").IsAway, Is.False);
    }

    [Test]
    public void WithdrawNeverLengthensTheTrip()
    {
        BuildSession();
        ActiveExpedition expedition = Dispatch("s5");
        Tick(expedition.remainingSeconds - 5);
        Raise(ExpeditionEventType.Supply, ROLL_LOW);
        float before = expedition.remainingSeconds;
        Assert.That(before, Is.LessThan(Table.expeditionEvents.withdrawSeconds));
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Withdraw, out _), Is.True);
        Assert.That(expedition.remainingSeconds, Is.EqualTo(before));
    }

    // ── 조우 ──
    [Test]
    public void ContinuingAfterEncounterIsNotASelectionResponse()
    {
        BuildSession(_regions: QuietRegions(_death: 0));
        ActiveExpedition expedition = Dispatch("s1", "s2");
        Raise(ExpeditionEventType.Encounter);
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Continue, out _), Is.True);
        Assert.That(expedition.RespondedToEvent, Is.False, "조우 뒤 임무 계속으로 선택 사건 보너스를 받지 않는다.");
        Assert.That(expedition.HadEncounter, Is.True);
    }

    [Test]
    public void EncounterResolvesOnArrivalWithDamageAndDelayThenOffersWithdraw()
    {
        BuildSession(_regions: QuietRegions(_death: 0));
        ActiveExpedition expedition = Dispatch("s1", "s2");
        float remainingBefore = expedition.remainingSeconds;
        Raise(ExpeditionEventType.Encounter);

        Assert.That(raised[0].type, Is.EqualTo("encounter"));
        Assert.That(decided[0].decision, Is.EqualTo("forced"));
        Assert.That(resolved.Count, Is.EqualTo(1));
        Assert.That(resolved[0].outcome, Is.EqualTo("encounter"));
        Assert.That(expedition.Event.State, Is.EqualTo(ExpeditionEventState.Resolved), "선택 없이 도착 순간 해결된다.");
        Assert.That(expedition.remainingSeconds, Is.EqualTo(remainingBefore + Table.expeditionEvents.encounter.delaySeconds).Within(0.001f));
        // 전투 합 5 + 4 = 9, 40% - 18% = 22%.
        Assert.That(StateOf("s1").Hp, Is.EqualTo(StateOf("s1").MaxHp * (1 - 0.22f)).Within(0.01f));
        Assert.That(StateOf("s2").Hp, Is.EqualTo(StateOf("s2").MaxHp));

        // TD-34: 해결 뒤 철수 기회. 맥락 행동은 없다.
        Assert.That(expedition.Event.IsWithdrawOffered, Is.True);
        Assert.That(session.Expeditions.PendingEventCount, Is.EqualTo(1));
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out _), Is.False);
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Withdraw, out _), Is.True);
        Assert.That(expedition.IsWithdrawn, Is.True);
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Withdraw, out _), Is.False, "두 번째는 false");
        Assert.That(resolved.Count, Is.EqualTo(1), "사건 해결은 한 번이다.");
        Assert.That(decided[1].decision, Is.EqualTo("withdraw"));
        float hp = StateOf("s1").Hp;
        Tick(expedition.remainingSeconds);
        Assert.That(settled[0].reward, Is.Zero);
        Assert.That(StateOf("s1").Hp, Is.EqualTo(hp), "조우 부상은 남는다.");
    }

    [Test]
    public void EncounterDamageUsesCombatEfficiencyOfParty()
    {
        BuildSession();
        ActiveExpedition expedition = Dispatch("s1", "s2");
        Assert.That(session.PartyCombat(expedition.party), Is.EqualTo(9).Within(0.001f));
        session.DebugLowerHp("s1", StateOf("s1").MaxHp * 0.5f);
        Assert.That(session.PartyCombat(expedition.party), Is.EqualTo(5 * 0.75f + 4).Within(0.001f), "경상 효율을 반영한다.");
    }

    [Test]
    public void EncounterOfferTimesOutWithoutChangingTheExpedition()
    {
        BuildSession();
        ActiveExpedition expedition = Dispatch("s1", "s2");
        Raise(ExpeditionEventType.Encounter);
        float remaining = expedition.remainingSeconds;
        Tick(Table.expeditionEvents.timeoutSeconds);
        Assert.That(expedition.Event.IsWithdrawOffered, Is.False);
        Assert.That(expedition.IsWithdrawn, Is.False);
        Assert.That(session.Expeditions.PendingEventCount, Is.Zero);
        Assert.That(decided[decided.Count - 1].decision, Is.EqualTo("timeout"));
        Assert.That(expedition.remainingSeconds, Is.EqualTo(remaining - Table.expeditionEvents.timeoutSeconds).Within(0.001f));
    }

    [Test]
    public void EncounterCanKillAndOtherMemberContinues()
    {
        BuildSession(_regions: QuietRegions(_reward: 1, _death: 0));
        ActiveExpedition expedition = Dispatch("s1", "s2");
        session.DebugLowerHp("s1", 1);
        injured.Clear();
        Raise(ExpeditionEventType.Encounter);

        Assert.That(StateOf("s1").IsAlive, Is.False);
        Assert.That(died.Count, Is.EqualTo(1));
        Assert.That(died[0].survivorId, Is.EqualTo("s1"));
        Assert.That(injured, Is.Empty, "사망은 별도 이벤트다. 부상 이벤트가 겹치지 않는다.");
        Assert.That(session.Expeditions.Active.Count, Is.EqualTo(1), "한 명이 살아 있으면 탐사는 계속된다.");
        Assert.That(session.LivingParty(expedition), Is.EqualTo(new[] { "s2" }));
        Assert.That(expedition.Event.IsWithdrawOffered, Is.True);
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Continue, out _), Is.True);
        Tick(expedition.remainingSeconds);
        Assert.That(settled[0].casualties, Is.EqualTo(new[] { "s1" }));
        Assert.That(settled[0].returned, Is.EqualTo(new[] { "s2" }));
        Assert.That(died.Count, Is.EqualTo(1), "귀환 정산이 사망을 다시 내지 않는다.");
    }

    [Test]
    public void EncounterWipingPartySettlesInSameTick()
    {
        BuildSession();
        ActiveExpedition expedition = Dispatch("s1");
        session.DebugLowerHp("s1", 1);
        Raise(ExpeditionEventType.Encounter);
        Assert.That(session.Expeditions.Active, Is.Empty);
        Assert.That(expedition.IsSettled, Is.True);
        Assert.That(expedition.Event.IsWithdrawOffered, Is.False, "전멸하면 철수할 사람이 없다.");
        Assert.That(settled.Count, Is.EqualTo(1));
        Assert.That(settled[0].returned, Is.Empty);
        Assert.That(session.Expeditions.PendingEventCount, Is.Zero);
    }

    [Test]
    public void CriticalReturnGoesToWardAndRescueStillJoins()
    {
        BuildSession(_regions: QuietRegions(_death: 0));
        ActiveExpedition expedition = Dispatch("s5");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        session.DebugLowerHp("s5", StateOf("s5").MaxHp * 0.2f);
        Assert.That(StateOf("s5").IsAway, Is.True);
        Assert.That(StateOf("s5").IsCritical, Is.True);
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out _), Is.True);
        Tick(expedition.remainingSeconds);
        Assert.That(StateOf("s5").IsCritical, Is.True);
        Assert.That(StateOf("s5").IsAway, Is.False);
        Assert.That(StateOf("s5").CanAct, Is.False, "TD-74 귀환 중상자");
        Assert.That(joined.Count, Is.EqualTo(1), "데려온 사람이 중상이어도 합류한다.");
        Assert.That(session.AliveCount, Is.EqualTo(8));
    }

    // ── 후보 고갈 ──
    [Test]
    public void ExhaustedCandidatesReplaceRescueWithSupply()
    {
        BuildSession();
        while (session.Recruitment.RescueShareCount > 0)
        {
            session.Recruitment.TakeRescueCandidate();
        }
        ActiveExpedition expedition = Dispatch("s5");
        Raise(ExpeditionEventType.Rescue, ROLL_HIGH);
        Assert.That(expedition.Event.Type, Is.EqualTo(ExpeditionEventType.Supply));
        Assert.That(raised[0].type, Is.EqualTo("supply"), "대체된 유형으로 기록한다.");
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out _), Is.True);
        Assert.That(decided[0].decision, Is.EqualTo("search"));
        Assert.That(session.Recruitment.ReservedSlots, Is.Zero);
    }

    [Test]
    public void LastCandidateTakenTurnsOtherPendingRescueIntoSupply()
    {
        BuildSession();
        while (session.Recruitment.RescueShareCount > 1)
        {
            session.Recruitment.TakeRescueCandidate();
        }
        ActiveExpedition first = Dispatch("s5");
        ActiveExpedition second = Dispatch("s6");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        Assert.That(session.TryResolveExpeditionEvent(first.expeditionId, ExpeditionDecision.Act, out _), Is.True);
        Assert.That(first.RescuedCandidate, Is.Not.Null);
        Assert.That(second.Event.Type, Is.EqualTo(ExpeditionEventType.Supply));
        Assert.That(second.Event.State, Is.EqualTo(ExpeditionEventState.Pending));
    }

    // ── 파견 중 사망자의 정리 ──
    [Test]
    public void DeathWhileAwayClearsAssignmentsAndTreatmentEligibility()
    {
        BuildSession();
        Assert.That(session.TrySetWork("s5", WorkAssignment.Production, out string workError), Is.True, workError);
        ActiveExpedition expedition = Dispatch("s5", "s6");
        Raise(ExpeditionEventType.Rescue, ROLL_HIGH);
        session.DebugLowerHp("s5", 1);
        int workersBefore = session.Assignments.ActiveWorkerCount;
        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out _), Is.True);

        SurvivorState dead = StateOf("s5");
        Assert.That(dead.IsAlive, Is.False);
        Assert.That(dead.Injury, Is.EqualTo(InjuryStatus.Dead));
        Assert.That(session.AliveCount, Is.EqualTo(6));
        Assert.That(session.AwayCount, Is.EqualTo(1));
        Assert.That(session.Assignments.ActiveWorkerCount, Is.EqualTo(workersBefore));
        Assert.That(session.Assignments.TrySetWork("s5", WorkAssignment.Production, out string error), Is.False);
        Assert.That(error, Does.Contain("사망"));
        Assert.That(session.TryStartTreatment("s5", "s1", out string patientError), Is.False);
        Assert.That(patientError, Does.Contain("사망"));
        Assert.That(session.TryStartTreatment("s1", "s5", out _), Is.False, "죽은 사람을 담당자로 쓸 수 없다.");
        Assert.That(session.FindUnit("s5").gameObject.activeSelf, Is.False);
        var deaths = (List<string>)Get(session, "deathsThisWave");
        Assert.That(deaths, Is.EqualTo(new[] { dead.DisplayName }), "결과 화면 사망 목록에 한 번 오른다.");

        // 죽은 인원은 탑 자리를 막지 않는다.
        var board = session.Assignments;
        Assert.That(board.TrySetDefenseTower("s1", "t1", 0, 0, out string towerError), Is.True, towerError);
        Tick(expedition.remainingSeconds);
        Assert.That(dead.IsAlive, Is.False, "귀환 정산이 사망자를 되살리지 않는다.");
        Assert.That(session.FindUnit("s5").gameObject.activeSelf, Is.False);
        Assert.That(session.FindUnit("s6").State.IsAway, Is.False);
    }

    [Test]
    public void AwayMemberTakesDamageThroughSingleUnitPathWithoutActivating()
    {
        BuildSession();
        ActiveExpedition expedition = Dispatch("s5");
        SurvivorUnit unit = session.FindUnit("s5");
        Assert.That(unit.gameObject.activeSelf, Is.False);
        unit.ApplyDamage(unit.State.MaxHp * 0.5f, GameSession.CAUSE_EXPEDITION);
        Assert.That(injured.Count, Is.EqualTo(1));
        Assert.That(injured[0].id, Is.EqualTo("s5"));
        Assert.That(injured[0].status, Is.EqualTo(InjuryStatus.Light.ToString()));
        Assert.That(unit.State.IsAway, Is.True, "부상이 파견 상태를 바꾸지 않는다.");
        Assert.That(unit.gameObject.activeSelf, Is.False);
        unit.ApplyDamage(unit.State.MaxHp * 0.3f, GameSession.CAUSE_EXPEDITION);
        Assert.That(injured.Count, Is.EqualTo(2));
        Assert.That(injured[1].status, Is.EqualTo(InjuryStatus.Critical.ToString()));
        Assert.That(unit.State.IsAway, Is.True);
        Assert.That(expedition.party, Does.Contain("s5"));
    }

    // ── 탐사당 최대 1건과 ID ──
    [Test]
    public void AtMostOneEventPerExpeditionOverManyDispatches()
    {
        var regions = new[] { Clone(Table.expeditions[0]), Clone(Table.expeditions[1]) };
        foreach (ExpeditionRegionData region in regions)
        {
            region.eventChance = 1;
            region.deathChanceBase = 0;
            region.deathChancePerExploration = 0;
            region.deathChancePerExtraMember = 0;
        }
        BuildSession(_regions: regions);
        for (int round = 0; round < 40; round++)
        {
            foreach (SurvivorUnit unit in session.Units)
            {
                unit.State.RestoreHp();
            }
            Assert.That(session.TryStartExpedition(regions[round % 2].id, new[] { "s5", "s6" }, out string error), Is.True, error);
            ActiveExpedition expedition = session.Expeditions.Active[0];
            for (int step = 0; step < 60 && session.Expeditions.Active.Count > 0; step++)
            {
                Tick(5);
                Assert.That(session.DebugRaiseExpeditionEvent(ExpeditionEventType.Supply, out _), Is.EqualTo(expedition.Event.State == ExpeditionEventState.Scheduled && session.Expeditions.Active.Count > 0), "도착한 탐사에는 더 일으킬 수 없다.");
            }
            Assert.That(session.Expeditions.Active, Is.Empty);
        }
        var perExpedition = new Dictionary<int, int>();
        foreach (GameEvents.ExpeditionEventRaisedInfo info in raised)
        {
            perExpedition.TryGetValue(info.expeditionId, out int count);
            perExpedition[info.expeditionId] = count + 1;
        }
        Assert.That(perExpedition.Count, Is.EqualTo(40), "발생률 100%이면 탐사마다 1건이다.");
        foreach (int count in perExpedition.Values)
        {
            Assert.That(count, Is.EqualTo(1));
        }
        Assert.That(settled.Count, Is.EqualTo(40));
    }

    [Test]
    public void ExpeditionIdsAreSessionSequenceAndLogged()
    {
        BuildSession(_regions: QuietRegions(_death: 0));
        ActiveExpedition first = Dispatch("s5");
        Tick(first.remainingSeconds);
        ActiveExpedition second = Dispatch("s5");
        Assert.That(first.expeditionId, Is.EqualTo(1));
        Assert.That(second.expeditionId, Is.EqualTo(2));
        Assert.That(settled[0].expeditionId, Is.EqualTo(1));
    }

    // ── 표시와 패널 ──
    [Test]
    public void HeadcountShowsAwayAndReservation()
    {
        Assert.That(ExpeditionLabels.Headcount(8, 9, 0, 0), Is.EqualTo("인원 8/9"));
        Assert.That(ExpeditionLabels.Headcount(8, 9, 0, 1), Is.EqualTo("인원 8/9 (귀환 예정 1)"));
        Assert.That(ExpeditionLabels.Headcount(8, 9, 2, 1), Is.EqualTo("인원 8/9 (파견 2, 귀환 예정 1)"));
    }

    T HudPart<T>() where T : Component
    {
        if (hudInstance == null)
        {
            hudInstance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Hud.prefab"), host.transform);
        }
        return hudInstance.GetComponentInChildren<T>(true);
    }

    static string TextOf(object _view, string _field) => ((Text)Get(_view, _field)).text;

    static void ClickEvent(ExpeditionPanelView _view, int _index)
    {
        ((Action<int>)Get(_view, "EventActionClicked")).Invoke(_index);
    }

    [Test]
    public void PanelShowsPendingEventAndButtonReachesAuthorityPath()
    {
        BuildSession(_count: 9);
        var view = HudPart<ExpeditionPanelView>();
        var presenter = new ExpeditionPanelPresenter(view, session);
        var root = (GameObject)Get(view, "eventRoot");
        presenter.Refresh();
        Assert.That(root.activeSelf, Is.False, "대기 사건이 없으면 영역이 꺼진다.");

        ActiveExpedition expedition = Dispatch("s5", "s6");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        presenter.Refresh();

        Assert.That(root.activeSelf, Is.True);
        string text = TextOf(view, "eventText");
        Assert.That(text, Does.Contain("생존자 발견"));
        Assert.That(text, Does.Contain("남은 15초"));
        Assert.That(text, Does.Contain("유나 90/90(정상)"));
        Assert.That(text, Does.Contain("민재 100/100(정상)"));
        Assert.That(text, Does.Contain("성공 "));
        Assert.That(text, Does.Contain("철수"));
        Assert.That(text, Does.Contain($"보상 성공 시 {session.BaseRewardOf(expedition)} 스크랩"), "기대 보상을 표시한다.");
        Assert.That(text, Does.Contain($"귀환 사망 {session.ReturnDeathChanceOf(expedition):P0}"), "위험을 표시한다.");
        var buttons = (Button[])Get(view, "eventButtons");
        Assert.That(buttons.Length, Is.EqualTo(3));
        Assert.That(buttons[0].interactable, Is.True, "정원이 차도 구조 버튼을 막지 않는다.");
        Assert.That(((Text[])Get(view, "eventButtonLabels"))[0].text, Is.EqualTo("구조 시도"));

        ClickEvent(view, 0);
        presenter.Refresh();

        Assert.That(TextOf(view, "eventStatusText"), Does.Contain("정원"), "거부 사유를 표시한다.");
        Assert.That(rejected, Is.EqualTo(new[] { "full" }));
        Assert.That(root.activeSelf, Is.True, "거부는 사건을 끝내지 않는다.");

        ClickEvent(view, 1);
        presenter.Refresh();

        Assert.That(decided[0].decision, Is.EqualTo("continue"));
        Assert.That(root.activeSelf, Is.False);
        Assert.That(TextOf(view, "activeText"), Does.Contain("임무 계속"));
    }

    [Test]
    public void PanelShowsEarliestExpiringEventAndPendingCount()
    {
        BuildSession();
        var view = HudPart<ExpeditionPanelView>();
        var presenter = new ExpeditionPanelPresenter(view, session);
        Dispatch("s5");
        Dispatch("s6");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        Tick(5);
        Raise(ExpeditionEventType.Supply, ROLL_LOW);
        presenter.Refresh();
        string text = TextOf(view, "eventText");
        Assert.That(text, Does.Contain("생존자 발견"), "먼저 만료되는 사건을 보여 준다.");
        Assert.That(text, Does.Contain("남은 10초"));
        Assert.That(text, Does.Contain("대기 사건 2건"));
    }

    [Test]
    public void PanelShowsEncounterOfferWithoutActionButtonAndReturningPerson()
    {
        BuildSession();
        var view = HudPart<ExpeditionPanelView>();
        var presenter = new ExpeditionPanelPresenter(view, session);
        ActiveExpedition first = Dispatch("s1");
        Raise(ExpeditionEventType.Encounter);
        presenter.Refresh();
        var buttons = (Button[])Get(view, "eventButtons");
        Assert.That(buttons[0].gameObject.activeSelf, Is.False, "조우 뒤에는 맥락 행동이 없다.");
        Assert.That(TextOf(view, "eventText"), Does.Contain("적대 생물 조우"));
        Assert.That(TextOf(view, "eventText"), Does.Contain("HP -"));
        ClickEvent(view, 2);
        presenter.Refresh();
        Assert.That(first.IsWithdrawn, Is.True);
        Assert.That(TextOf(view, "activeText"), Does.Contain("철수"));

        ActiveExpedition second = Dispatch("s5");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        presenter.Refresh();
        Assert.That(buttons[0].gameObject.activeSelf, Is.True, "다음 사건에서는 다시 보인다.");
        ClickEvent(view, 0);
        presenter.Refresh();
        string candidate = second.RescuedCandidate.displayName;
        string active = TextOf(view, "activeText");
        Assert.That(active.Substring(0, active.IndexOf('\n')), Does.Contain("귀환 예정 인력 " + candidate), "휠로 넘기지 않아도 제목 줄에서 보인다.");
        Assert.That(active, Does.Contain("구조 성공"));
        Assert.That(active, Does.Contain($"+{Table.expeditionEvents.rescue.extraSeconds:0}초"), "귀환 시간 변화를 표시한다.");
    }

    [Test]
    public void HudWarningFollowsPendingEventsAndHeadcountShowsReservation()
    {
        BuildSession(_count: 8);
        var view = HudPart<HudView>();
        var presenter = new HudPresenter(view, session);
        var notice = (GameObject)Get(view, "expeditionEventNotice");
        presenter.Refresh();
        Assert.That(notice.activeSelf, Is.False);

        ActiveExpedition expedition = Dispatch("s5");
        Raise(ExpeditionEventType.Rescue, ROLL_LOW);
        presenter.Refresh();
        Assert.That(notice.activeSelf, Is.True);
        Assert.That(TextOf(view, "expeditionEventLabel"), Does.Contain("탐사 사건 1건"));

        Assert.That(session.TryResolveExpeditionEvent(expedition.expeditionId, ExpeditionDecision.Act, out _), Is.True);
        presenter.Refresh();
        Assert.That(notice.activeSelf, Is.False);
        Assert.That(TextOf(view, "survivorText"), Is.EqualTo("인원 8/9 (파견 1, 귀환 예정 1)"));
    }

    [Test]
    public void PrefabWiresEventAreaAndWarning()
    {
        BuildSession();
        var panel = HudPart<ExpeditionPanelView>();
        foreach (string field in new[] { "eventRoot", "eventText", "eventStatusText" })
        {
            Assert.That((UnityEngine.Object)Get(panel, field), Is.Not.Null, field);
        }
        var buttons = (Button[])Get(panel, "eventButtons");
        var labels = (Text[])Get(panel, "eventButtonLabels");
        Assert.That(buttons.Length, Is.EqualTo(3));
        Assert.That(labels.Length, Is.EqualTo(3));
        for (int index = 0; index < buttons.Length; index++)
        {
            Assert.That(buttons[index], Is.Not.Null);
            Assert.That(labels[index], Is.Not.Null);
            Assert.That(buttons[index].onClick.GetPersistentEventCount(), Is.Zero);
        }
        var hud = HudPart<HudView>();
        Assert.That((UnityEngine.Object)Get(hud, "expeditionEventNotice"), Is.Not.Null);
        Assert.That((UnityEngine.Object)Get(hud, "expeditionEventLabel"), Is.Not.Null);
    }

    [Test]
    public void DebugEventNeedsActiveExpedition()
    {
        BuildSession();
        Assert.That(session.DebugRaiseExpeditionEvent(ExpeditionEventType.Rescue, out string error), Is.False);
        Assert.That(error, Is.Not.Empty);
        Assert.That(raised, Is.Empty);
    }
}
