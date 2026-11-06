using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

// G1 인력 수급 규칙: 상한 판정, 방문 흐름, 추첨 재현, 신규 ID, 테이블 검증.
public sealed class RecruitmentTests
{
    static readonly BindingFlags PRIVATE = BindingFlags.NonPublic | BindingFlags.Instance;

    static void Set(object _target, string _field, object _value)
    {
        _target.GetType().GetField(_field, PRIVATE).SetValue(_target, _value);
    }
    static object Get(object _target, string _field)
    {
        return _target.GetType().GetField(_field, PRIVATE).GetValue(_target);
    }
    static RecruitmentSystem MakeSystem(GameplayTableData _table, int _seed)
    {
        return new RecruitmentSystem(_table.recruitment, new System.Random(_seed));
    }

    // ── 추첨 재현(V2-IMPL-09) ──
    [Test]
    public void SameSeedReproducesVisitorShareAndRescueQueue()
    {
        var table = TableManager.Instance.RequireValid().Gameplay;
        var first = MakeSystem(table, 4242);
        var second = MakeSystem(table, 4242);
        Assert.That(first.Visitors.Count, Is.EqualTo(table.recruitment.visitorWaves.Length));
        for (int index = 0; index < first.Visitors.Count; index++)
        {
            Assert.That(first.Visitors[index].Candidate.id, Is.EqualTo(second.Visitors[index].Candidate.id));
            Assert.That(first.Visitors[index].Wave, Is.EqualTo(table.recruitment.visitorWaves[index]));
        }
        // 구조 몫 순서도 재현된다(G3가 쓴다).
        Assert.That(first.RescueShareCount, Is.EqualTo(second.RescueShareCount));
        for (int index = 0; index < first.RescueShareCount; index++)
        {
            Assert.That(first.TakeRescueCandidate().id, Is.EqualTo(second.TakeRescueCandidate().id));
        }
    }

    [Test]
    public void ShufflingKeepsEveryCandidateExactlyOnce()
    {
        var table = TableManager.Instance.RequireValid().Gameplay;
        var all = new HashSet<string>();
        for (int seed = 0; seed < 20; seed++)
        {
            var system = MakeSystem(table, seed);
            foreach (var visitor in system.Visitors)
            {
                all.Add(visitor.Candidate.id);
            }
            while (system.RescueShareCount > 0)
            {
                all.Add(system.TakeRescueCandidate().id);
            }
            Assert.That(all.Count, Is.EqualTo(table.recruitment.candidates.Length), "후보가 두 번 뽑히거나 빠지면 안 된다.");
            all.Clear();
        }
    }

    [Test]
    public void DifferentSeedCanPickDifferentVisitor()
    {
        var table = TableManager.Instance.RequireValid().Gameplay;
        string baseline = MakeSystem(table, 0).Visitors[0].Candidate.id;
        bool found = false;
        for (int seed = 1; seed < 50 && !found; seed++)
        {
            found = MakeSystem(table, seed).Visitors[0].Candidate.id != baseline;
        }
        Assert.That(found, Is.True, "시드가 달라지면 방문 후보도 달라질 수 있어야 한다.");
    }

    // ── 신규 ID(V2-IMPL-09) ──
    [Test]
    public void NewIdsAreSessionSequenceWithoutReuse()
    {
        var table = TableManager.Instance.RequireValid().Gameplay;
        var system = MakeSystem(table, 1);
        var issued = new HashSet<string>();
        foreach (var survivor in table.survivors)
        {
            issued.Add(survivor.id);
        }
        foreach (var candidate in table.recruitment.candidates)
        {
            issued.Add(candidate.id);
        }
        for (int index = 0; index < 12; index++)
        {
            string id = system.IssueNewId();
            Assert.That(issued.Add(id), Is.True, $"발급된 ID '{id}'가 기존 ID와 겹치거나 재사용됐다.");
        }
    }

    // ── 방문 흐름 ──
    [Test]
    public void VisitorWaitsUntilPrepStartsThenExpiresOnAlert()
    {
        var table = TableManager.Instance.RequireValid().Gameplay;
        var system = MakeSystem(table, 7);
        int wave = table.recruitment.visitorWaves[0];
        Assert.That(system.PendingVisitor, Is.Null, "준비가 시작 전에는 아직 도착하지 않았다.");
        var arrived = system.ArriveVisitor(wave);
        Assert.That(arrived, Is.Not.Null);
        Assert.That(arrived.IsWaiting, Is.True);
        Assert.That(system.ArriveVisitor(wave), Is.Null, "이미 도착한 방문자가 다시 도착하지 않는다.");
        var expired = system.ExpireVisitor(wave);
        Assert.That(expired, Is.Not.Null);
        Assert.That(system.PendingVisitor, Is.Null);
        Assert.That(system.ExpireVisitor(wave), Is.Null, "이미 만료된 방문자가 다시 만료되지 않는다.");
    }

    [Test]
    public void DeclinedCandidateNeverReturns()
    {
        var table = TableManager.Instance.RequireValid().Gameplay;
        var system = MakeSystem(table, 7);
        string first = system.ArriveVisitor(table.recruitment.visitorWaves[0]).Candidate.id;
        Assert.That(system.DeclineVisitor(), Is.True);
        Assert.That(system.PendingVisitor, Is.Null);
        // 다른 웨이브의 방문자는 별개 후보다.
        var next = system.ArriveVisitor(table.recruitment.visitorWaves[1]);
        Assert.That(next, Is.Not.Null);
        Assert.That(next.Candidate.id, Is.Not.EqualTo(first), "거절한 후보가 다시 방문하면 안 된다.");
    }

    // ── 수용 상한 ──
    [Test]
    public void CapacityJudgementCountsAliveAndReservations()
    {
        var table = TableManager.Instance.RequireValid().Gameplay;
        int capacity = table.recruitment.capacity;
        var system = MakeSystem(table, 3);
        Assert.That(system.HasCapacity(capacity - 1), Is.True);
        Assert.That(system.HasCapacity(capacity), Is.False, "9명이면 거부다.");
        Assert.That(system.ReserveSlot(capacity - 1), Is.True, "예약 자리는 상한 안에서만 잡는다.");
        Assert.That(system.HasCapacity(capacity - 1), Is.False, "예약이 있으면 같은 인원이라도 거부다.");
        Assert.That(system.ReserveSlot(capacity - 1), Is.False);
        system.ReleaseSlot();
        Assert.That(system.HasCapacity(capacity - 1), Is.True);
    }

    // ── 세션 경로(권위 판정) ──
    GameObject host;
    GameSession session;
    readonly List<string> rejected = new List<string>();
    readonly List<GameEvents.RecruitmentAcceptedInfo> accepted = new List<GameEvents.RecruitmentAcceptedInfo>();
    readonly List<GameEvents.SurvivorJoinedInfo> joined = new List<GameEvents.SurvivorJoinedInfo>();
    System.Action<GameEvents.RecruitmentRejectedInfo> onRejected;
    System.Action<GameEvents.RecruitmentAcceptedInfo> onAccepted;
    System.Action<GameEvents.SurvivorJoinedInfo> onJoined;

    // 합류 인원 count명으로 세션 픽스처를 만든다. 실제 Survivor.prefab으로 Bind까지 한다.
    void BuildSession(int _aliveCount, int _scrap, bool _arrive)
    {
        host = new GameObject("Fixture");
        host.SetActive(false);
        session = host.AddComponent<GameSession>();
        var table = TableManager.Instance.RequireValid().Gameplay;
        Set(session, "table", table);
        Set(session, "flow", MakeWaveTwoFlow(table));
        Set(session, "economy", new ResourceEconomy(_scrap, table.production.scrapPerSecondPerWork));
        Set(session, "board", new AssignmentBoard(table.production.maxWorkers));
        var recruitment = new RecruitmentSystem(table.recruitment, new System.Random(11));
        Set(session, "recruitment", recruitment);
        // JoinSurvivor의 평시 지시가 생산 슬롯을 본다. 슬롯 하나짜리 시설을 만들어 둔다.
        var slot = new GameObject("WorkSlot").transform;
        slot.SetParent(host.transform);
        var production = host.AddComponent<ProductionFacility>();
        Set(production, "workSlots", new[] { slot });
        production.Initialize();
        Set(session, "production", production);
        var units = new List<SurvivorUnit>();
        var map = (Dictionary<string, SurvivorUnit>)Get(session, "unitsById");
        var survivors = (List<SurvivorState>)Get(session, "survivors");
        var board = (AssignmentBoard)Get(session, "board");
        var prefab = AssetDatabase.LoadAssetAtPath<SurvivorUnit>("Assets/Prefabs/Survivor.prefab");
        var markerPrefab = AssetDatabase.LoadAssetAtPath<DefenseMarkerView>("Assets/Prefabs/DefenseMarker.prefab");
        Assert.That(prefab, Is.Not.Null, "Survivor.prefab이 필요하다.");
        Assert.That(markerPrefab, Is.Not.Null, "DefenseMarker.prefab이 필요하다.");
        for (int index = 0; index < _aliveCount; index++)
        {
            // 초기 7명 다음은 테스트용 합류 데이터로 이어 ID 중복을 피한다.
            SurvivorData data = index < table.survivors.Length
                ? table.survivors[index]
                : new SurvivorData { id = "d" + (index + 1), displayName = "테스트" + (index + 1), combat = 2, work = 2, exploration = 2, maxHp = 90 };
            SurvivorUnit unit = UnityEngine.Object.Instantiate(prefab, host.transform);
            unit.name = "Unit" + index;
            unit.SetDefenseMarker(UnityEngine.Object.Instantiate(markerPrefab, host.transform));
            var state = new SurvivorState(data, _newJoin: index >= table.survivors.Length);
            unit.Bind(state, table.survivorCombat);
            units.Add(unit);
            survivors.Add(state);
            board.Register(state, new DefenseAssignment(0, 0));
            map.Add(state.Id, unit);
        }
        Set(session, "units", units);
        Set(session, "survivorPrefab", prefab);
        Set(session, "defenseMarkerPrefab", markerPrefab);
        Set(session, "survivorRoot", host.transform);
        var joinPoint = new GameObject("JoinPoint").transform;
        joinPoint.SetParent(host.transform);
        joinPoint.position = new Vector3(5, 0, 5);
        Set(session, "joinPoint", joinPoint);
        onRejected = _info => rejected.Add(_info.reason);
        onAccepted = _info => accepted.Add(_info);
        onJoined = _info => joined.Add(_info);
        // 같은 픽스처 인스턴스를 여러 테스트가 쓰므로 기록을 비우고 시작한다.
        rejected.Clear();
        accepted.Clear();
        joined.Clear();
        GameEvents.RecruitmentRejected += onRejected;
        GameEvents.RecruitmentAccepted += onAccepted;
        GameEvents.SurvivorJoined += onJoined;
        if (_arrive)
        {
            recruitment.ArriveVisitor(table.recruitment.visitorWaves[0]);
        }
    }

    // 방문 웨이브가 2이므로 픽스처 세션도 2웨이브 준비로 둔다.
    static SessionFlow MakeWaveTwoFlow(GameplayTableData _table)
    {
        var flow = new SessionFlow(_table.session.prepSeconds, _table.session.alertSeconds, _table.session.combatSeconds, _table.session.timeoutCoreDamagePerEnemy, _table.waves.Length);
        flow.Advance();
        flow.Advance();
        flow.Advance();
        flow.Advance();
        return flow;
    }

    [TearDown]
    public void Teardown()
    {
        if (onRejected != null)
        {
            GameEvents.RecruitmentRejected -= onRejected;
            GameEvents.RecruitmentAccepted -= onAccepted;
            GameEvents.SurvivorJoined -= onJoined;
            onRejected = null;
        }
        if (host != null)
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    [Test]
    public void SessionRejectsRecruitWhenFullWithoutAnyChange()
    {
        BuildSession(TableManager.Instance.Gameplay.recruitment.capacity, 20, _arrive: true);
        int scrapBefore = session.Scrap;
        int aliveBefore = session.AliveCount;
        Assert.That(session.TryRecruitVisitor(out string error), Is.False);
        Assert.That(error, Is.EqualTo("수용 정원이 찼습니다."));
        Assert.That(session.Scrap, Is.EqualTo(scrapBefore), "거부 시 자원이 바뀌면 안 된다.");
        Assert.That(session.AliveCount, Is.EqualTo(aliveBefore), "거부 시 인원이 바뀌면 안 된다.");
        Assert.That(rejected, Is.EqualTo(new[] { "full" }), "full 거절이 기록된다.");
        Assert.That(session.Recruitment.PendingVisitor, Is.Not.Null, "정원이 차도 방문자는 대기 상태로 남는다.");
        Assert.That(joined, Is.Empty);
    }

    [Test]
    public void SessionRejectsRecruitWithoutScrapWithoutAnyChange()
    {
        BuildSession(7, 0, _arrive: true);
        int aliveBefore = session.AliveCount;
        Assert.That(session.TryRecruitVisitor(out string error), Is.False);
        Assert.That(error, Is.EqualTo("스크랩이 부족합니다."));
        Assert.That(session.Scrap, Is.Zero);
        Assert.That(session.AliveCount, Is.EqualTo(aliveBefore));
        Assert.That(rejected, Is.EqualTo(new[] { "no_scrap" }));
        Assert.That(session.Recruitment.PendingVisitor, Is.Not.Null, "비용 부족은 방문자를 사라지게 하지 않는다.");
        Assert.That(joined, Is.Empty);
    }

    [Test]
    public void AwayAndInjuredSurvivorsStillCountTowardCapacity()
    {
        BuildSession(TableManager.Instance.Gameplay.recruitment.capacity, 20, _arrive: true);
        var survivors = (List<SurvivorState>)Get(session, "survivors");
        // 파견 중과 중상 상태를 만들어도 살아 있는 인원 기준으로 상한을 판정한다.
        survivors[0].BeginAway();
        survivors[1].ApplyDamage(survivors[1].MaxHp * 0.8f);
        Assert.That(survivors[0].IsAlive, Is.True);
        Assert.That(survivors[1].IsAlive, Is.True);
        Assert.That(session.AliveCount, Is.EqualTo(survivors.Count));
        Assert.That(session.TryRecruitVisitor(out string error), Is.False);
        Assert.That(error, Is.EqualTo("수용 정원이 찼습니다."));
    }

    [Test]
    public void SessionRecruitSpendsScrapAndJoinsThroughCommonPath()
    {
        BuildSession(7, 10, _arrive: true);
        VisitorRecord visitor = session.Recruitment.PendingVisitor;
        string candidateId = visitor.Candidate.id;
        Assert.That(session.TryRecruitVisitor(out _), Is.True);
        Assert.That(session.Scrap, Is.EqualTo(5), "스크랩 5가 차감된다.");
        Assert.That(session.AliveCount, Is.EqualTo(8), "영입 후 8명이다.");
        Assert.That(session.Units.Count, Is.EqualTo(8));
        Assert.That(visitor.Status, Is.EqualTo(VisitorStatus.Accepted));
        Assert.That(joined.Count, Is.EqualTo(1));
        Assert.That(joined[0].source, Is.EqualTo("visitor"));
        string newId = joined[0].newId;
        Assert.That(newId, Is.Not.EqualTo(candidateId), "신규 ID는 후보 ID와 무관하다.");
        Assert.That(session.FindUnit(newId), Is.Not.Null);
        SurvivorState joinedState = session.FindUnit(newId).State;
        Assert.That(joinedState.Hp, Is.EqualTo(joinedState.MaxHp), "합류 인원은 HP 최대다.");
        Assert.That(session.Assignments.WorkOf(newId), Is.EqualTo(WorkAssignment.DefenseStandby), "합류 인원은 방어 대기로 시작한다(V2-IMPL-08).");
        var joinPoint = (Transform)Get(session, "joinPoint");
        var defense = session.Assignments.DefenseOf(newId);
        Assert.That(defense.x, Is.EqualTo(joinPoint.position.x).Within(0.001f), "초기 방어 위치는 합류 지점이다.");
        Assert.That(defense.z, Is.EqualTo(joinPoint.position.z).Within(0.001f));
        Assert.That(session.Assignments.IsDefenseAssigned(newId), Is.False, "플레이어가 정하기 전까지는 '미지정'이다.");
        Assert.That(joinedState.IsNewJoin, Is.True);
        Assert.That(accepted.Count, Is.EqualTo(1));
        Assert.That(accepted[0].candidateId, Is.EqualTo(candidateId));
        Assert.That(accepted[0].cost, Is.EqualTo(5));
        Assert.That(accepted[0].aliveCount, Is.EqualTo(8));
        Assert.That(rejected, Is.Empty);
        Assert.That(session.Recruitment.PendingVisitor, Is.Null, "영입된 방문자는 사라진다.");
    }

    [Test]
    public void DebugJoinAtFullKeepsRescueCandidate()
    {
        BuildSession(TableManager.Instance.Gameplay.recruitment.capacity, 20, _arrive: false);
        int before = session.Recruitment.RescueShareCount;
        Assert.That(session.DebugJoinSurvivor(out string error), Is.False);
        Assert.That(error, Is.EqualTo("수용 정원이 찼습니다."));
        Assert.That(session.Recruitment.RescueShareCount, Is.EqualTo(before), "상한에 막히면 구조 몫 후보를 소비하지 않는다.");
    }

    [Test]
    public void DebugJoinIssuesNewIdLikeVisitor()
    {
        BuildSession(7, 20, _arrive: false);
        Assert.That(session.DebugJoinSurvivor(out _), Is.True);
        Assert.That(joined.Count, Is.EqualTo(1));
        Assert.That(joined[0].newId, Is.EqualTo("n1"), "합류 경로는 후보 ID가 아니라 세션 순번을 쓴다.");
        Assert.That(session.FindUnit("n1"), Is.Not.Null);
    }

    [Test]
    public void SessionExpiresVisitorOnAlertEntry()
    {
        BuildSession(7, 20, _arrive: true);
        Assert.That(session.Recruitment.PendingVisitor, Is.Not.Null);
        session.DebugAdvancePhase();
        Assert.That(rejected, Is.EqualTo(new[] { "expired" }), "경보 진입 시 미결정 방문자는 만료된다.");
        Assert.That(session.Recruitment.PendingVisitor, Is.Null);
        Assert.That(session.TryRecruitVisitor(out string error), Is.False);
        Assert.That(error, Is.EqualTo("대기 중인 방문자가 없습니다."));
        Assert.That(joined, Is.Empty);
        Assert.That(session.Scrap, Is.EqualTo(20), "만료에는 자원 변화가 없다.");
    }

    [Test]
    public void SessionDeclineVisitorLogsDeclined()
    {
        BuildSession(7, 20, _arrive: true);
        Assert.That(session.DeclineVisitor(), Is.True);
        Assert.That(rejected, Is.EqualTo(new[] { "declined" }));
        Assert.That(session.Recruitment.PendingVisitor, Is.Null);
        Assert.That(joined, Is.Empty);
        Assert.That(session.DeclineVisitor(), Is.False, "이미 결정된 방문자는 다시 보낼 수 없다.");
    }

    // ── 테이블 검증 ──
    [Test]
    public void ActualProjectRecruitmentTableIsValid()
    {
        TableManager.Reload();
        Assert.That(TableManager.Initialize().IsValid, Is.True);
        var table = TableManager.Instance.Gameplay;
        Assert.That(table.recruitment.capacity, Is.EqualTo(9));
        Assert.That(table.recruitment.visitorCost, Is.EqualTo(5));
        Assert.That(table.recruitment.visitorWaves, Is.EqualTo(new[] { 2, 4 }));
        Assert.That(table.recruitment.candidates.Length, Is.GreaterThanOrEqualTo(table.recruitment.visitorWaves.Length));
    }

    [Test]
    public void RecruitmentTableRejectsDuplicateNamesSmallCandidatePoolAndBadTraits()
    {
        var tables = TableManager.Instance.RequireValid();
        var table = tables.Gameplay;
        var saved = table.recruitment;
        try
        {
            // 이름 중복(초기 인원과 겹침).
            table.recruitment = Clone(saved);
            table.recruitment.candidates[0].displayName = table.survivors[0].displayName;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Gameplay.recruitment"));
            var rules = new GameTableRules();
            rules.Validate(tables);
            Assert.That(rules.IsValid, Is.False);

            // 후보 수가 방문 수보다 적음.
            table.recruitment = Clone(saved);
            table.recruitment.candidates = new[] { saved.candidates[0] };
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Gameplay.recruitment"));
            rules = new GameTableRules();
            rules.Validate(tables);
            Assert.That(rules.IsValid, Is.False);

            // 초기 인원이 capacity보다 많음.
            table.recruitment = Clone(saved);
            table.recruitment.capacity = 6;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Gameplay.recruitment"));
            rules = new GameTableRules();
            rules.Validate(tables);
            Assert.That(rules.IsValid, Is.False);

            // 알 수 없는 특성 키.
            table.recruitment = Clone(saved);
            table.recruitment.candidates[0].trait = "sniper";
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Gameplay.recruitment"));
            rules = new GameTableRules();
            rules.Validate(tables);
            Assert.That(rules.IsValid, Is.False);

            // 방문 웨이브 중복.
            table.recruitment = Clone(saved);
            table.recruitment.visitorWaves = new[] { 2, 2 };
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Gameplay.recruitment"));
            rules = new GameTableRules();
            rules.Validate(tables);
            Assert.That(rules.IsValid, Is.False);
        }
        finally
        {
            table.recruitment = saved;
        }
    }

    static RecruitmentData Clone(RecruitmentData _source)
    {
        // 후보 개체까지 복사한다. 얕은 복사면 테스트가 공유 테이블의 후보를 바꿔 놓는다.
        var candidates = new SurvivorData[_source.candidates.Length];
        for (int index = 0; index < candidates.Length; index++)
        {
            SurvivorData entry = _source.candidates[index];
            candidates[index] = new SurvivorData
            {
                id = entry.id,
                displayName = entry.displayName,
                combat = entry.combat,
                work = entry.work,
                exploration = entry.exploration,
                maxHp = entry.maxHp,
                trait = entry.trait,
            };
        }
        return new RecruitmentData
        {
            capacity = _source.capacity,
            visitorCost = _source.visitorCost,
            visitorWaves = (int[])_source.visitorWaves.Clone(),
            candidates = candidates,
        };
    }
}
