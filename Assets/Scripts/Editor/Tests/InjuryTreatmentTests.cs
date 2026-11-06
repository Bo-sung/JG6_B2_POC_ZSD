using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

// G2 부상·치료 규칙: 상태 파생과 경계, 효율, 치료 조건과 비용, 일시정지·재개, 정원, 표적, 패널의 권위 경로.
public sealed class InjuryTreatmentTests
{
    const BindingFlags PRIVATE = BindingFlags.NonPublic | BindingFlags.Instance;

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

    static SurvivorState MakeState(float _maxHp = 100, int _work = 3, int _exploration = 2, int _combat = 2, string _id = "a")
    {
        return new SurvivorState(new SurvivorData { id = _id, displayName = _id, combat = _combat, work = _work, exploration = _exploration, maxHp = _maxHp }, _injury: Table.injury);
    }

    // ── 상태 경계와 파생 ──
    [TestCase(120f)]
    [TestCase(110f)]
    [TestCase(95f)]
    [TestCase(90f)]
    [TestCase(100f)]
    [TestCase(97f)]
    [TestCase(33f)]
    [TestCase(7f)]
    public void StatusBoundaryIsExactAtSeventyAndThirtyPercent(float _maxHp)
    {
        InjuryData injury = Table.injury;
        Assert.That(InjuryRules.StatusOf(_maxHp, _maxHp, injury), Is.EqualTo(InjuryStatus.Normal));
        Assert.That(InjuryRules.StatusOf(_maxHp * 0.7f, _maxHp, injury), Is.EqualTo(InjuryStatus.Normal), "정확히 70%는 정상이다.");
        Assert.That(InjuryRules.StatusOf(_maxHp * 0.7f - 0.01f, _maxHp, injury), Is.EqualTo(InjuryStatus.Light));
        Assert.That(InjuryRules.StatusOf(_maxHp * 0.3f, _maxHp, injury), Is.EqualTo(InjuryStatus.Light), "정확히 30%는 경상이다.");
        Assert.That(InjuryRules.StatusOf(_maxHp * 0.3f - 0.01f, _maxHp, injury), Is.EqualTo(InjuryStatus.Critical));
        Assert.That(InjuryRules.StatusOf(0.001f, _maxHp, injury), Is.EqualTo(InjuryStatus.Critical), "0 초과는 중상이다.");
        Assert.That(InjuryRules.StatusOf(0, _maxHp, injury), Is.EqualTo(InjuryStatus.Dead));
    }

    // 정수 피해를 쌓아 도달한 HP도 같은 구간이어야 한다.
    [Test]
    public void BoundaryHoldsWhenReachedByAccumulatedDamageAndHealing()
    {
        SurvivorState state = MakeState(90);
        for (int hit = 0; hit < 27; hit++)
        {
            state.ApplyDamage(1);
        }
        Assert.That(state.Hp, Is.EqualTo(63f));
        Assert.That(state.Injury, Is.EqualTo(InjuryStatus.Normal));
        state.ApplyDamage(36);
        Assert.That(state.Hp, Is.EqualTo(27f));
        Assert.That(state.Injury, Is.EqualTo(InjuryStatus.Light));
        state.ApplyDamage(0.5f);
        Assert.That(state.Injury, Is.EqualTo(InjuryStatus.Critical));
        state.Heal(state.MaxHp * Table.treatment.healFraction);
        Assert.That(state.Injury, Is.EqualTo(InjuryStatus.Light), "치료로 HP가 오르면 상태도 따라 바뀐다.");
    }

    [Test]
    public void CanActIsDerivedFromHpAndHasNoStoredFlag()
    {
        SurvivorState state = MakeState();
        Assert.That(state.CanAct, Is.True);
        state.ApplyDamage(35);
        Assert.That(state.Injury, Is.EqualTo(InjuryStatus.Light));
        Assert.That(state.CanAct, Is.True, "경상은 행동할 수 있다.");
        state.ApplyDamage(40);
        Assert.That(state.Injury, Is.EqualTo(InjuryStatus.Critical));
        Assert.That(state.CanAct, Is.False);
        Assert.That(state.IsAlive, Is.True);
        Assert.That(state.SetActivity(SurvivorActivity.Guarding), Is.False, "중상은 행동을 받지 않는다.");
        Assert.That(state.BeginAway(), Is.False, "중상은 파견되지 않는다.");
        state.Heal(40);
        Assert.That(state.CanAct, Is.True, "저장된 값 없이 HP만으로 돌아온다.");
        state.ApplyDamage(state.Hp);
        Assert.That(state.Injury, Is.EqualTo(InjuryStatus.Dead));
        state.Heal(50);
        Assert.That(state.Hp, Is.Zero, "사망자는 치료로 돌아오지 않는다.");
        Assert.That(state.CanAct, Is.False);
    }

    // ── 효율 ──
    [Test]
    public void LightInjuryScalesEffectiveValuesButNotDisplayedStats()
    {
        SurvivorState state = MakeState(100, _work: 4, _exploration: 3, _combat: 5);
        Assert.That(state.EffectiveWork, Is.EqualTo(4f));
        Assert.That(state.EffectiveExploration, Is.EqualTo(3f));
        state.ApplyDamage(40);
        float efficiency = Table.injury.lightEfficiency;
        Assert.That(efficiency, Is.EqualTo(0.75f), "초깃값 경상 효율");
        Assert.That(state.CombatEfficiency, Is.EqualTo(efficiency));
        Assert.That(state.EffectiveWork, Is.EqualTo(4 * efficiency));
        Assert.That(state.EffectiveExploration, Is.EqualTo(3 * efficiency));
        Assert.That(state.Work, Is.EqualTo(4), "표시 능력치는 그대로다.");
        Assert.That(state.Exploration, Is.EqualTo(3));
        Assert.That(state.Combat, Is.EqualTo(5));
    }

    [Test]
    public void DamagePerSecondDropsToSeventyFivePercentWhileRangeStaysTheSame()
    {
        BuildSession();
        SurvivorUnit unit = session.FindUnit("s1");
        float dps = unit.DamagePerSecond;
        float range = unit.AttackRange;
        session.DebugLowerHp("s1", unit.State.MaxHp * 0.5f);
        Assert.That(unit.State.Injury, Is.EqualTo(InjuryStatus.Light));
        Assert.That(unit.DamagePerSecond, Is.EqualTo(dps * 0.75f).Within(0.0001f));
        Assert.That(unit.AttackRange, Is.EqualTo(range), "사거리는 부상과 무관하다(V2-IMPL-07).");
    }

    [Test]
    public void ProductionContributionDropsForLightCriticalAndTreating()
    {
        BuildSession();
        Assert.That(session.TrySetWork("s3", WorkAssignment.Production, out _), Is.True);
        Assert.That(session.TrySetWork("s4", WorkAssignment.Production, out _), Is.True);
        Arrive("s3");
        Arrive("s4");
        Assert.That((float)Invoke(session, "ActiveWork"), Is.EqualTo(9f), "정상 작업 5+4");
        session.DebugLowerHp("s3", 45);
        Assert.That((float)Invoke(session, "ActiveWork"), Is.EqualTo(5 * 0.75f + 4), "경상은 75%");
        session.DebugLowerHp("s3", 20);
        Assert.That((float)Invoke(session, "ActiveWork"), Is.EqualTo(4f), "중상은 기여하지 않는다.");
        Assert.That(session.TryStartTreatment("s3", "s4", out _), Is.True);
        Assert.That((float)Invoke(session, "ActiveWork"), Is.Zero, "치료 중인 담당자도 기여하지 않는다.");
    }

    [Test]
    public void ProductionPaysProportionallyToEffectiveWork()
    {
        var healthy = new ResourceEconomy(0, 1);
        var injured = new ResourceEconomy(0, 1);
        Assert.That(healthy.TickProduction(100, 4f), Is.EqualTo(400));
        Assert.That(injured.TickProduction(100, 4 * 0.75f), Is.EqualTo(300), "경상 생산은 정상의 75%다.");
    }

    [Test]
    public void PartyExplorationRoundsSumAndPreviewMatchesActualJudgement()
    {
        Assert.That(ExpeditionSystem.TotalExploration(new[] { new ExpeditionMember("a", 3.75f), new ExpeditionMember("b", 2f) }), Is.EqualTo(6), "5.75 → 6");
        Assert.That(ExpeditionSystem.TotalExploration(new[] { new ExpeditionMember("a", 2.25f), new ExpeditionMember("b", 0.25f) }), Is.EqualTo(3), ".5는 올림");
        Assert.That(ExpeditionSystem.TotalExploration(new[] { new ExpeditionMember("a", 0.75f), new ExpeditionMember("b", 0.75f), new ExpeditionMember("c", 0.75f) }), Is.EqualTo(2), "인원별로 더한 뒤 반올림(TD-29d)");

        BuildSession();
        // s5 탐사 5를 경상으로 만들면 합이 7에서 5.75 → 6으로 준다. s2 탐사 2는 그대로다.
        var party = new[] { "s5", "s2" };
        Assert.That(session.PartyExploration(party), Is.EqualTo(7));
        session.DebugLowerHp("s5", 45);
        int preview = session.PartyExploration(party);
        Assert.That(preview, Is.EqualTo(6));
        Assert.That(session.TryStartExpedition(Table.expeditions[0].id, party, out string error), Is.True, error);
        ActiveExpedition active = session.Expeditions.Active[0];
        int actual = (int)typeof(ActiveExpedition).GetProperty("Exploration", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(active);
        Assert.That(actual, Is.EqualTo(preview), "미리보기와 실제 판정이 같은 값을 쓴다.");
    }

    // ── 치료 시작 조건 ──
    static TreatmentSystem MakeTreatment() => new TreatmentSystem(Table.treatment);

    static void AssertRejected(TreatmentSystem _system, SessionPhase _phase, SurvivorState _patient, SurvivorState _worker, int _scrap, string _reason)
    {
        var economy = new ResourceEconomy(_scrap, 1);
        TreatmentJob before = _system.Current;
        float patientHp = _patient != null ? _patient.Hp : 0;
        Assert.That(_system.TryStart(_phase, _patient, _worker, economy, out string reason), Is.False);
        Assert.That(reason, Is.EqualTo(_reason));
        Assert.That(economy.Scrap, Is.EqualTo(_scrap), "거부되면 스크랩이 바뀌면 안 된다.");
        Assert.That(_system.Current, Is.SameAs(before), "거부되면 치료 상태가 바뀌면 안 된다.");
        if (_patient != null)
        {
            Assert.That(_patient.Hp, Is.EqualTo(patientHp));
        }
    }

    static SurvivorState Wounded(string _id = "p")
    {
        SurvivorState state = MakeState(_id: _id);
        state.ApplyDamage(50);
        return state;
    }

    [TestCase(SessionPhase.Alert)]
    [TestCase(SessionPhase.Combat)]
    [TestCase(SessionPhase.Result)]
    [TestCase(SessionPhase.Ended)]
    public void StartRejectedOutsidePrep(SessionPhase _phase)
    {
        AssertRejected(MakeTreatment(), _phase, Wounded(), MakeState(_id: "w"), 10, TreatmentSystem.REASON_NOT_PREP);
    }

    [Test]
    public void StartRejectedWhileAnotherTreatmentRunsOrIsPaused()
    {
        TreatmentSystem system = MakeTreatment();
        Assert.That(system.TryStart(SessionPhase.Prep, Wounded(), MakeState(_id: "w"), new ResourceEconomy(10, 1), out _), Is.True);
        AssertRejected(system, SessionPhase.Prep, Wounded("p2"), MakeState(_id: "w2"), 10, TreatmentSystem.REASON_BUSY);
        system.Pause();
        AssertRejected(system, SessionPhase.Prep, Wounded("p2"), MakeState(_id: "w2"), 10, TreatmentSystem.REASON_BUSY);
    }

    [Test]
    public void StartRejectedForUnavailableWorker()
    {
        TreatmentSystem system = MakeTreatment();
        AssertRejected(system, SessionPhase.Prep, Wounded(), null, 10, TreatmentSystem.REASON_NO_WORKER);
        SurvivorState critical = MakeState(_id: "w");
        critical.ApplyDamage(80);
        AssertRejected(system, SessionPhase.Prep, Wounded(), critical, 10, TreatmentSystem.REASON_WORKER_UNAVAILABLE);
        SurvivorState dead = MakeState(_id: "w");
        dead.Kill();
        AssertRejected(system, SessionPhase.Prep, Wounded(), dead, 10, TreatmentSystem.REASON_WORKER_UNAVAILABLE);
        SurvivorState away = MakeState(_id: "w");
        away.BeginAway();
        AssertRejected(system, SessionPhase.Prep, Wounded(), away, 10, TreatmentSystem.REASON_WORKER_UNAVAILABLE);
    }

    [Test]
    public void StartRejectedForInvalidPatient()
    {
        TreatmentSystem system = MakeTreatment();
        SurvivorState worker = MakeState(_id: "w");
        AssertRejected(system, SessionPhase.Prep, null, worker, 10, TreatmentSystem.REASON_NO_PATIENT);
        SurvivorState dead = MakeState(_id: "p");
        dead.Kill();
        AssertRejected(system, SessionPhase.Prep, dead, worker, 10, TreatmentSystem.REASON_PATIENT_DEAD);
        SurvivorState away = Wounded();
        away.BeginAway();
        AssertRejected(system, SessionPhase.Prep, away, worker, 10, TreatmentSystem.REASON_PATIENT_AWAY);
        AssertRejected(system, SessionPhase.Prep, MakeState(_id: "p"), worker, 10, TreatmentSystem.REASON_PATIENT_FULL_HP);
    }

    [Test]
    public void SelfTreatmentAndMissingScrapAreRejected()
    {
        TreatmentSystem system = MakeTreatment();
        SurvivorState both = Wounded();
        AssertRejected(system, SessionPhase.Prep, both, both, 10, TreatmentSystem.REASON_SELF);
        AssertRejected(system, SessionPhase.Prep, Wounded(), MakeState(_id: "w"), Table.treatment.cost - 1, TreatmentSystem.REASON_NO_SCRAP);
    }

    [Test]
    public void LightPatientCanBeTreatedAndCriticalPatientCanToo()
    {
        Assert.That(MakeTreatment().TryStart(SessionPhase.Prep, Wounded(), MakeState(_id: "w"), new ResourceEconomy(10, 1), out _), Is.True, "경상도 치료할 수 있다(TD-29c).");
        SurvivorState critical = MakeState(_id: "p");
        critical.ApplyDamage(80);
        Assert.That(MakeTreatment().TryStart(SessionPhase.Prep, critical, MakeState(_id: "w"), new ResourceEconomy(10, 1), out _), Is.True);
    }

    // ── 비용과 진행 ──
    [Test]
    public void CostIsChargedOnceAcrossPauseResumeAndWorkerChange()
    {
        TreatmentSystem system = MakeTreatment();
        var economy = new ResourceEconomy(10, 1);
        SurvivorState worker = MakeState(_id: "w");
        Assert.That(system.TryStart(SessionPhase.Prep, Wounded(), worker, economy, out _), Is.True);
        Assert.That(economy.Scrap, Is.EqualTo(10 - Table.treatment.cost));
        Assert.That(system.Current.CostPaid, Is.True);
        Assert.That(system.Pause(), Is.True);
        Assert.That(system.Pause(), Is.False, "이미 멈춘 치료를 다시 멈추지 않는다.");
        Assert.That(system.ResumeAtPrep(), Is.True);
        Assert.That(economy.Scrap, Is.EqualTo(10 - Table.treatment.cost), "재개는 무료다.");
        system.Pause();
        worker.Kill();
        Assert.That(system.ResumeAtPrep(), Is.False);
        Assert.That(system.Current.NeedsWorker, Is.True);
        Assert.That(system.TryAssignWorker(SessionPhase.Prep, MakeState(_id: "w2"), out _), Is.True);
        Assert.That(economy.Scrap, Is.EqualTo(10 - Table.treatment.cost), "담당자 교체도 무료다.");
    }

    [TestCase(1, 20f / 0.7f)]
    [TestCase(3, 20f)]
    [TestCase(5, 20f / 1.3f)]
    public void TreatmentSecondsFollowWorkFormula(int _work, float _seconds)
    {
        Assert.That(MakeTreatment().SecondsFor(MakeState(_work: _work)), Is.EqualTo(_seconds).Within(0.001f), "TD-29b");
    }

    [Test]
    public void LightWorkerTreatsAtSeventyFivePercentSpeed()
    {
        TreatmentSystem system = MakeTreatment();
        SurvivorState worker = MakeState(_work: 5);
        float healthy = system.SecondsFor(worker);
        worker.ApplyDamage(50);
        Assert.That(system.SecondsFor(worker), Is.EqualTo(healthy / 0.75f).Within(0.001f));
        worker.ApplyDamage(40);
        Assert.That(float.IsPositiveInfinity(system.SecondsFor(worker)), Is.True, "중상은 치료 속도가 없다.");
    }

    [Test]
    public void ProgressIsPreservedAcrossPauseAndHpRisesOnlyOnCompletion()
    {
        TreatmentSystem system = MakeTreatment();
        SurvivorState patient = Wounded();
        float hp = patient.Hp;
        Assert.That(system.TryStart(SessionPhase.Prep, patient, MakeState(_work: 3, _id: "w"), new ResourceEconomy(10, 1), out _), Is.True);
        Assert.That(system.Tick(5, _workerArrived: false), Is.Null);
        Assert.That(system.Current.Progress, Is.Zero, "담당자가 도착하기 전에는 진행하지 않는다.");
        Assert.That(system.Tick(10, true), Is.Null);
        Assert.That(system.Current.Progress, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(patient.Hp, Is.EqualTo(hp), "완료 전에는 HP가 오르지 않는다.");
        system.Pause();
        Assert.That(system.Tick(100, true), Is.Null);
        Assert.That(system.Current.Progress, Is.EqualTo(0.5f).Within(0.0001f), "일시정지 중에는 진행하지 않는다.");
        system.ResumeAtPrep();
        Assert.That(system.Current.Progress, Is.EqualTo(0.5f).Within(0.0001f), "재개해도 진행률이 이어진다.");
        Assert.That(system.RemainingSeconds, Is.EqualTo(10f).Within(0.001f));
        TreatmentJob done = system.Tick(10, true);
        Assert.That(done, Is.Not.Null);
        Assert.That(system.Current, Is.Null);
        Assert.That(patient.Hp, Is.EqualTo(hp + patient.MaxHp * Table.treatment.healFraction).Within(0.001f));
    }

    [Test]
    public void ProgressCarriesOverToNewWorkerAtNewSpeed()
    {
        TreatmentSystem system = MakeTreatment();
        SurvivorState worker = MakeState(_work: 3, _id: "w");
        Assert.That(system.TryStart(SessionPhase.Prep, Wounded(), worker, new ResourceEconomy(10, 1), out _), Is.True);
        system.Tick(10, true);
        system.Pause();
        worker.ApplyDamage(90);
        Assert.That(system.ResumeAtPrep(), Is.False, "담당자가 중상이면 재개하지 않는다.");
        Assert.That(system.Current.NeedsWorker, Is.True);
        Assert.That(system.Current.IsPaused, Is.True);
        Assert.That(system.Tick(100, true), Is.Null);
        SurvivorState next = MakeState(_work: 5, _id: "w2");
        Assert.That(system.TryAssignWorker(SessionPhase.Prep, next, out _), Is.True);
        Assert.That(system.Current.Progress, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(system.RemainingSeconds, Is.EqualTo(0.5f * 20f / 1.3f).Within(0.001f));
    }

    [Test]
    public void AssignWorkerRejectsPatientUnavailableAndOutsidePrep()
    {
        TreatmentSystem system = MakeTreatment();
        SurvivorState patient = Wounded();
        SurvivorState worker = MakeState(_id: "w");
        Assert.That(system.TryAssignWorker(SessionPhase.Prep, worker, out string none), Is.False);
        Assert.That(none, Is.EqualTo(TreatmentSystem.REASON_NO_JOB));
        Assert.That(system.TryStart(SessionPhase.Prep, patient, worker, new ResourceEconomy(10, 1), out _), Is.True);
        Assert.That(system.TryAssignWorker(SessionPhase.Prep, MakeState(_id: "w2"), out string busy), Is.False);
        Assert.That(busy, Is.EqualTo(TreatmentSystem.REASON_HAS_WORKER), "담당자가 있으면 교체하지 않는다.");
        system.Pause();
        worker.Kill();
        system.ResumeAtPrep();
        Assert.That(system.TryAssignWorker(SessionPhase.Prep, patient, out string self), Is.False);
        Assert.That(self, Is.EqualTo(TreatmentSystem.REASON_SELF));
        Assert.That(system.TryAssignWorker(SessionPhase.Alert, MakeState(_id: "w2"), out string phase), Is.False);
        Assert.That(phase, Is.EqualTo(TreatmentSystem.REASON_NOT_PREP));
        Assert.That(system.Current.NeedsWorker, Is.True);
    }

    [Test]
    public void HealIsCappedAtMaxHp()
    {
        TreatmentSystem system = MakeTreatment();
        SurvivorState patient = MakeState();
        patient.ApplyDamage(10);
        Assert.That(system.TryStart(SessionPhase.Prep, patient, MakeState(_id: "w"), new ResourceEconomy(10, 1), out _), Is.True);
        Assert.That(system.Tick(20, true), Is.Not.Null);
        Assert.That(patient.Hp, Is.EqualTo(patient.MaxHp));
    }

    [Test]
    public void LosingWorkerDuringPrepPausesAndWaitsForNewWorker()
    {
        TreatmentSystem system = MakeTreatment();
        SurvivorState worker = MakeState(_id: "w");
        Assert.That(system.DropUnavailableWorker(), Is.False);
        Assert.That(system.TryStart(SessionPhase.Prep, Wounded(), worker, new ResourceEconomy(10, 1), out _), Is.True);
        Assert.That(system.DropUnavailableWorker(), Is.False, "행동 가능한 담당자는 그대로다.");
        worker.ApplyDamage(90);
        Assert.That(system.DropUnavailableWorker(), Is.True);
        Assert.That(system.DropUnavailableWorker(), Is.False, "한 번만 멈춘다.");
        Assert.That(system.Current.IsPaused, Is.True);
        Assert.That(system.Current.NeedsWorker, Is.True);
    }

    // ── 표적 우선순위(V2-IMPL-02) ──
    [Test]
    public void ActableSurvivorOutranksDownedRegardlessOfDistance()
    {
        Assert.That(InjuryRules.IsBetterTarget(false, 100, false, false, float.MaxValue), Is.True, "없으면 쓰러진 인력도 표적이다.");
        Assert.That(InjuryRules.IsBetterTarget(true, 100, true, false, 1), Is.True, "먼 행동 가능 인력이 가까운 쓰러진 인력보다 먼저다.");
        Assert.That(InjuryRules.IsBetterTarget(false, 1, true, true, 100), Is.False, "가까워도 쓰러진 인력은 뒤로 밀린다.");
        Assert.That(InjuryRules.IsBetterTarget(true, 1, true, true, 2), Is.True);
        Assert.That(InjuryRules.IsBetterTarget(true, 3, true, true, 2), Is.False);
        Assert.That(InjuryRules.IsBetterTarget(false, 1, true, false, 2), Is.True);
    }

    [Test]
    public void DownedStaysTargetableButAwayAndDeadDoNot()
    {
        BuildSession();
        SurvivorUnit unit = session.FindUnit("s1");
        Assert.That(unit.IsTargetable, Is.True);
        session.DebugLowerHp("s1", 10);
        Assert.That(unit.State.CanAct, Is.False);
        Assert.That(unit.IsTargetable, Is.True, "완전 면역이면 전투 사망이 사라진다.");
        session.FindUnit("s2").State.BeginAway();
        Assert.That(session.FindUnit("s2").IsTargetable, Is.False);
        session.DebugKillSurvivor("s1");
        Assert.That(unit.IsTargetable, Is.False);
    }

    // ── 세션 경로 ──
    GameObject host;
    GameSession session;
    ProductionFacility production;
    InfirmaryFacility infirmary;
    SessionFlow flow;
    readonly List<GameEvents.SurvivorInjuredInfo> injured = new List<GameEvents.SurvivorInjuredInfo>();
    readonly List<GameEvents.TreatmentStartedInfo> started = new List<GameEvents.TreatmentStartedInfo>();
    readonly List<string> paused = new List<string>();
    readonly List<GameEvents.TreatmentResumedInfo> resumed = new List<GameEvents.TreatmentResumedInfo>();
    readonly List<GameEvents.TreatmentCompletedInfo> completed = new List<GameEvents.TreatmentCompletedInfo>();
    readonly List<GameEvents.TreatmentCancelledInfo> cancelled = new List<GameEvents.TreatmentCancelledInfo>();
    readonly List<string> rejected = new List<string>();
    Action<GameEvents.SurvivorInjuredInfo> onInjured;
    Action<GameEvents.TreatmentStartedInfo> onStarted;
    Action<GameEvents.TreatmentPausedInfo> onPaused;
    Action<GameEvents.TreatmentResumedInfo> onResumed;
    Action<GameEvents.TreatmentCompletedInfo> onCompleted;
    Action<GameEvents.TreatmentCancelledInfo> onCancelled;
    Action<GameEvents.TreatmentRejectedInfo> onRejected;

    // 초기 인원으로 세션 픽스처를 만든다. 실제 Survivor.prefab으로 Bind까지 하고 세션과 같은 부상 테이블을 넘긴다.
    void BuildSession(int _scrap = 20, int _count = 7)
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
        Set(session, "expeditions", new ExpeditionSystem(table.expeditions, new System.Random(1)));
        var core = host.AddComponent<CoreFacility>();
        core.Initialize(table.core.maxHp);
        Set(session, "core", core);
        Set(session, "waveDirector", host.AddComponent<WaveDirector>());

        production = host.AddComponent<ProductionFacility>();
        Set(production, "workSlots", new[] { Point("WorkA", 1, 0), Point("WorkB", 2, 0) });
        production.Initialize();
        Set(session, "production", production);
        infirmary = host.AddComponent<InfirmaryFacility>();
        Set(infirmary, "workerSlot", Point("TreatSlot", 50, 50));
        var seats = new Transform[table.recruitment.capacity];
        for (int index = 0; index < seats.Length; index++)
        {
            seats[index] = Point("Seat" + index, 60 + index * 2, 60);
        }
        Set(infirmary, "waitSlots", seats);
        infirmary.Initialize();
        Set(session, "infirmary", infirmary);

        var prefab = AssetDatabase.LoadAssetAtPath<SurvivorUnit>("Assets/Prefabs/Survivor.prefab");
        var markerPrefab = AssetDatabase.LoadAssetAtPath<DefenseMarkerView>("Assets/Prefabs/DefenseMarker.prefab");
        var units = new List<SurvivorUnit>();
        var map = (Dictionary<string, SurvivorUnit>)Get(session, "unitsById");
        var survivors = (List<SurvivorState>)Get(session, "survivors");
        for (int index = 0; index < _count; index++)
        {
            SurvivorUnit unit = UnityEngine.Object.Instantiate(prefab, host.transform);
            unit.SetDefenseMarker(UnityEngine.Object.Instantiate(markerPrefab, host.transform));
            var state = new SurvivorState(table.survivors[index], _injury: table.injury);
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

        onInjured = _info => injured.Add(_info);
        onStarted = _info => started.Add(_info);
        onPaused = _info => paused.Add(_info.patientId);
        onResumed = _info => resumed.Add(_info);
        onCompleted = _info => completed.Add(_info);
        onCancelled = _info => cancelled.Add(_info);
        onRejected = _info => rejected.Add(_info.reason);
        injured.Clear();
        started.Clear();
        paused.Clear();
        resumed.Clear();
        completed.Clear();
        cancelled.Clear();
        rejected.Clear();
        GameEvents.SurvivorInjured += onInjured;
        GameEvents.TreatmentStarted += onStarted;
        GameEvents.TreatmentPaused += onPaused;
        GameEvents.TreatmentResumed += onResumed;
        GameEvents.TreatmentCompleted += onCompleted;
        GameEvents.TreatmentCancelled += onCancelled;
        GameEvents.TreatmentRejected += onRejected;
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

    void Arrive(string _id) => Set(session.FindUnit(_id), "arrived", true);

    // 담당자가 도착한 뒤 시간을 흘린다.
    void TickTreatment(string _workerId, float _seconds)
    {
        Arrive(_workerId);
        Invoke(session, "TickTreatment", _seconds);
    }

    // 경보·전투·결과를 건너 다음 준비로 간다. 웨이브 스폰 없이 단계 함수만 부른다.
    void GoToNextPrep()
    {
        while (flow.Phase != SessionPhase.Prep)
        {
            flow.Advance();
        }
        Invoke(session, "EnterPrep");
    }

    [TearDown]
    public void Teardown()
    {
        if (onInjured != null)
        {
            GameEvents.SurvivorInjured -= onInjured;
            GameEvents.TreatmentStarted -= onStarted;
            GameEvents.TreatmentPaused -= onPaused;
            GameEvents.TreatmentResumed -= onResumed;
            GameEvents.TreatmentCompleted -= onCompleted;
            GameEvents.TreatmentCancelled -= onCancelled;
            GameEvents.TreatmentRejected -= onRejected;
            onInjured = null;
        }
        if (session != null)
        {
            Invoke(session, "OnDisable");
        }
        if (host != null)
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    [Test]
    public void ResultPhaseDoesNotRestoreHpAndHpCarriesToNextPrep()
    {
        BuildSession();
        session.DebugLowerHp("s1", 60);
        session.DebugLowerHp("s2", 10);
        flow.Advance();
        flow.Advance();
        Invoke(session, "EnterResult", 0, 0f);
        Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Result));
        Assert.That(session.FindUnit("s1").State.Hp, Is.EqualTo(60f), "DEC-09 오버라이드: 결과에서 회복하지 않는다.");
        Assert.That(session.FindUnit("s2").State.Hp, Is.EqualTo(10f));
        GoToNextPrep();
        Assert.That(session.FindUnit("s1").State.Hp, Is.EqualTo(60f));
        Assert.That(session.FindUnit("s2").State.Hp, Is.EqualTo(10f));
    }

    [Test]
    public void InjuredEventFiresOnlyWhenStatusWorsens()
    {
        BuildSession();
        SurvivorUnit unit = session.FindUnit("s6");
        unit.ApplyDamage(10, "t");
        Assert.That(injured, Is.Empty, "90%는 여전히 정상이다.");
        unit.ApplyDamage(30, "t");
        Assert.That(injured.Count, Is.EqualTo(1));
        Assert.That(injured[0].id, Is.EqualTo("s6"));
        Assert.That(injured[0].hp, Is.EqualTo(60f));
        Assert.That(injured[0].status, Is.EqualTo("Light"));
        unit.ApplyDamage(10, "t");
        Assert.That(injured.Count, Is.EqualTo(1), "같은 상태 안의 피해는 기록하지 않는다.");
        unit.ApplyDamage(30, "t");
        Assert.That(injured.Count, Is.EqualTo(2));
        Assert.That(injured[1].status, Is.EqualTo("Critical"));
        unit.ApplyDamage(5, "t");
        Assert.That(injured.Count, Is.EqualTo(2));
        unit.ApplyDamage(100, "t");
        Assert.That(injured.Count, Is.EqualTo(2), "사망은 SurvivorDied로만 기록한다.");
        Assert.That(unit.State.IsAlive, Is.False);
    }

    [Test]
    public void OneHitFromNormalToCriticalRaisesOneEvent()
    {
        BuildSession();
        session.FindUnit("s6").ApplyDamage(80, "t");
        Assert.That(injured.Count, Is.EqualTo(1));
        Assert.That(injured[0].status, Is.EqualTo("Critical"));
    }

    [Test]
    public void SessionStartRejectionsChangeNothingAndAreLogged()
    {
        BuildSession(_scrap: 1);
        session.DebugLowerHp("s1", 60);
        Assert.That(session.TryStartTreatment("s1", "s6", out string scrapError), Is.False);
        Assert.That(scrapError, Is.EqualTo("스크랩이 부족합니다."));
        Assert.That(session.Scrap, Is.EqualTo(1));
        Assert.That(session.Treatment.Current, Is.Null);
        Assert.That(session.FindUnit("s6").State.Activity, Is.Not.EqualTo(SurvivorActivity.Treating));
        Assert.That(session.TryStartTreatment("s1", "s1", out string selfError), Is.False);
        Assert.That(selfError, Is.EqualTo("담당자는 자기 자신을 치료할 수 없습니다."));
        Assert.That(session.TryStartTreatment("s1", "nobody", out _), Is.False);
        Assert.That(session.TryStartTreatment(null, "s6", out _), Is.False);
        Assert.That(rejected, Is.EqualTo(new[] { TreatmentSystem.REASON_NO_SCRAP, TreatmentSystem.REASON_SELF, TreatmentSystem.REASON_NO_WORKER, TreatmentSystem.REASON_NO_PATIENT }));
        Assert.That(started, Is.Empty);
    }

    [Test]
    public void SessionStartChargesOnceAndLogsPayloadAndCompletes()
    {
        BuildSession(_scrap: 5);
        session.DebugLowerHp("s1", 24);
        Assert.That(session.FindUnit("s1").State.Injury, Is.EqualTo(InjuryStatus.Critical));
        Assert.That(session.TryStartTreatment("s1", "s6", out string error), Is.True, error);
        Assert.That(session.Scrap, Is.EqualTo(3), "스크랩 2 차감");
        Assert.That(started.Count, Is.EqualTo(1));
        Assert.That(started[0].patientId, Is.EqualTo("s1"));
        Assert.That(started[0].workerId, Is.EqualTo("s6"));
        Assert.That(started[0].cost, Is.EqualTo(2));
        Assert.That(session.FindUnit("s6").State.Activity, Is.EqualTo(SurvivorActivity.Treating));
        Assert.That(session.IsTreating("s1"), Is.True);
        Assert.That(session.IsTreating("s6"), Is.True);
        Assert.That(session.TryStartTreatment("s2", "s3", out _), Is.False, "동시에 1건만");
        Assert.That(rejected, Is.EqualTo(new[] { TreatmentSystem.REASON_BUSY }));
        Invoke(session, "TickTreatment", 100f);
        Assert.That(session.FindUnit("s1").State.Hp, Is.EqualTo(24f), "담당자가 도착하기 전에는 진행하지 않는다.");
        TickTreatment("s6", 10);
        Assert.That(session.FindUnit("s1").State.Hp, Is.EqualTo(24f), "완료 전에는 HP가 오르지 않는다.");
        TickTreatment("s6", 10);
        SurvivorState patient = session.FindUnit("s1").State;
        Assert.That(patient.Hp, Is.EqualTo(72f).Within(0.001f), "최대 HP 120의 40%p");
        Assert.That(patient.Injury, Is.EqualTo(InjuryStatus.Light));
        Assert.That(patient.CanAct, Is.True);
        Assert.That(completed.Count, Is.EqualTo(1));
        Assert.That(completed[0].patientId, Is.EqualTo("s1"));
        Assert.That(completed[0].hp, Is.EqualTo(72f).Within(0.001f));
        Assert.That(session.Treatment.Current, Is.Null);
        Assert.That(session.Scrap, Is.EqualTo(3));
        Assert.That(session.FindUnit("s6").State.Activity, Is.EqualTo(SurvivorActivity.Guarding), "방어 대기 배정으로 돌아간다.");
    }

    [Test]
    public void AlertPausesWorkerAndLightPatientDefendCriticalPatientStaysThenResumesFree()
    {
        BuildSession(_scrap: 5);
        session.DebugLowerHp("s1", 60);
        Assert.That(session.TryStartTreatment("s1", "s6", out _), Is.True);
        TickTreatment("s6", 5);
        float progress = session.Treatment.Current.Progress;
        Assert.That(progress, Is.GreaterThan(0));
        session.DebugAdvancePhase();
        Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Alert));
        Assert.That(paused, Is.EqualTo(new[] { "s1" }));
        Assert.That(session.FindUnit("s6").State.Activity, Is.EqualTo(SurvivorActivity.Guarding), "담당자는 방어로 간다.");
        Assert.That(session.FindUnit("s1").State.Activity, Is.EqualTo(SurvivorActivity.Guarding), "경상 환자도 방어로 간다.");
        Assert.That(session.IsTreating("s6"), Is.False);
        Assert.That(session.Treatment.Involves("s6"), Is.True, "일시정지 중에도 치료에 묶여 있다.");
        Assert.That(session.Treatment.Current.Progress, Is.EqualTo(progress));
        GoToNextPrep();
        Assert.That(resumed.Count, Is.EqualTo(1));
        Assert.That(resumed[0].workerId, Is.EqualTo("s6"));
        Assert.That(session.Scrap, Is.EqualTo(3), "재개에 추가 차감이 없다.");
        Assert.That(session.Treatment.Current.IsPaused, Is.False);
        Assert.That(session.Treatment.Current.Progress, Is.EqualTo(progress));
        Assert.That(session.FindUnit("s6").State.Activity, Is.EqualTo(SurvivorActivity.Treating));
        Assert.That(started.Count, Is.EqualTo(1));
    }

    [Test]
    public void CriticalPatientDoesNotMoveOnAlert()
    {
        BuildSession();
        session.DebugLowerHp("s1", 10);
        SurvivorUnit patient = session.FindUnit("s1");
        Assert.That(session.TryStartTreatment("s1", "s6", out _), Is.True);
        Vector3 position = patient.transform.position;
        SurvivorActivity activity = patient.State.Activity;
        session.DebugAdvancePhase();
        Assert.That(patient.State.Activity, Is.EqualTo(activity), "중상은 경보 명령을 받지 않는다.");
        Assert.That(patient.transform.position, Is.EqualTo(position));
    }

    [Test]
    public void CriticalMoveToDistinctWardSeatsAtPrepStartAndFreeProductionSlots()
    {
        BuildSession();
        Assert.That(session.TrySetWork("s3", WorkAssignment.Production, out _), Is.True);
        foreach (SurvivorUnit unit in session.Units)
        {
            session.DebugLowerHp(unit.State.Id, 5);
        }
        var positions = new HashSet<Vector3>();
        flow.Advance();
        GoToNextPrep();
        foreach (SurvivorUnit unit in session.Units)
        {
            Assert.That(positions.Add(unit.transform.position), Is.True, "대기 자리가 겹치면 안 된다.");
            Assert.That(unit.transform.position.z, Is.EqualTo(60).Within(0.001f), "치료소 대기 자리로 옮겨졌다.");
        }
        Assert.That(production.TryClaim("other", out _), Is.True);
        Assert.That(production.TryClaim("other2", out _), Is.True, "중상자의 생산 자리가 비어 있다.");
    }

    [Test]
    public void PatientDeathEndsTreatmentWithoutRefundAndReleasesWorker()
    {
        BuildSession(_scrap: 5);
        session.DebugLowerHp("s1", 10);
        Assert.That(session.TryStartTreatment("s1", "s6", out _), Is.True);
        session.DebugKillSurvivor("s1");
        Assert.That(session.Treatment.Current, Is.Null);
        Assert.That(cancelled.Count, Is.EqualTo(1));
        Assert.That(cancelled[0].patientId, Is.EqualTo("s1"));
        Assert.That(cancelled[0].reason, Is.EqualTo(TreatmentSystem.END_PATIENT_DIED));
        Assert.That(session.Scrap, Is.EqualTo(3), "환불하지 않는다.");
        Assert.That(session.FindUnit("s6").State.Activity, Is.EqualTo(SurvivorActivity.Guarding));
        Assert.That(completed, Is.Empty);
    }

    [Test]
    public void PlayerCancelEndsWithoutRefund()
    {
        BuildSession(_scrap: 5);
        session.DebugLowerHp("s1", 60);
        Assert.That(session.TryCancelTreatment(out string none), Is.False);
        Assert.That(none, Is.EqualTo("진행 중인 치료가 없습니다."));
        Assert.That(session.TryStartTreatment("s1", "s6", out _), Is.True);
        Assert.That(session.TryCancelTreatment(out _), Is.True);
        Assert.That(session.Treatment.Current, Is.Null);
        Assert.That(session.Scrap, Is.EqualTo(3));
        Assert.That(cancelled[0].reason, Is.EqualTo(TreatmentSystem.END_PLAYER));
        Assert.That(session.FindUnit("s1").State.Hp, Is.EqualTo(60f), "취소하면 회복하지 않는다.");
        Assert.That(session.FindUnit("s6").State.Activity, Is.EqualTo(SurvivorActivity.Guarding));
    }

    [Test]
    public void WorkerDeathDuringPauseWaitsForNewWorkerAtFreeCost()
    {
        BuildSession(_scrap: 5);
        session.DebugLowerHp("s1", 10);
        Assert.That(session.TryStartTreatment("s1", "s6", out _), Is.True);
        TickTreatment("s6", 10);
        session.DebugAdvancePhase();
        session.DebugKillSurvivor("s6");
        GoToNextPrep();
        Assert.That(session.Treatment.Current.NeedsWorker, Is.True);
        Assert.That(resumed, Is.Empty, "담당자가 없으면 재개되지 않는다.");
        TickTreatment("s3", 100);
        Assert.That(session.FindUnit("s1").State.Hp, Is.EqualTo(10f));
        Assert.That(session.TryAssignTreatmentWorker("s1", out _), Is.False, "중상 환자는 담당자가 될 수 없다.");
        Assert.That(rejected, Is.EqualTo(new[] { TreatmentSystem.REASON_WORKER_UNAVAILABLE }));
        Assert.That(session.TryAssignTreatmentWorker("s3", out string error), Is.True, error);
        Assert.That(session.Scrap, Is.EqualTo(3));
        Assert.That(resumed.Count, Is.EqualTo(1));
        Assert.That(resumed[0].workerId, Is.EqualTo("s3"));
        Assert.That(session.Treatment.Current.Progress, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(session.FindUnit("s3").State.Activity, Is.EqualTo(SurvivorActivity.Treating));
    }

    [Test]
    public void WorkerLosingAbilityDuringPrepRaisesPauseOnce()
    {
        BuildSession();
        session.DebugLowerHp("s1", 60);
        Assert.That(session.TryStartTreatment("s1", "s6", out _), Is.True);
        session.DebugLowerHp("s6", 5);
        Invoke(session, "TickTreatment", 1f);
        Invoke(session, "TickTreatment", 1f);
        Assert.That(paused, Is.EqualTo(new[] { "s1" }));
        Assert.That(session.Treatment.Current.NeedsWorker, Is.True);
        Assert.That(session.Treatment.Involves("s6"), Is.False, "새 담당자를 고를 수 있게 놓아준다.");
    }

    [Test]
    public void TreatedPeopleLeaveProductionSeatAndOverflowReturnsToStandby()
    {
        BuildSession();
        AssignmentBoard board = session.Assignments;
        session.DebugLowerHp("s1", 60);
        Assert.That(session.TrySetWork("s3", WorkAssignment.Production, out _), Is.True);
        Assert.That(session.TryStartTreatment("s1", "s3", out _), Is.True);
        Assert.That(board.WorkOf("s3"), Is.EqualTo(WorkAssignment.Production), "저장된 작업 배정을 지우지 않는다.");
        Assert.That(board.ActiveWorkerCount, Is.Zero, "치료 중 담당자는 정원에서 빠진다.");
        Assert.That(session.TrySetWork("s4", WorkAssignment.Production, out _), Is.True);
        Assert.That(session.TrySetWork("s5", WorkAssignment.Production, out string fillError), Is.True, fillError);
        TickTreatment("s3", 100);
        Assert.That(completed.Count, Is.EqualTo(1));
        Assert.That(board.WorkOf("s3"), Is.EqualTo(WorkAssignment.DefenseStandby), "그동안 정원이 찼으면 방어 대기로 돌아간다.");
        Assert.That(board.ActiveWorkerCount, Is.EqualTo(Table.production.maxWorkers));
    }

    [Test]
    public void WorkerReturnsToProductionWhenSeatIsFree()
    {
        BuildSession();
        session.DebugLowerHp("s1", 60);
        Assert.That(session.TrySetWork("s3", WorkAssignment.Production, out _), Is.True);
        Assert.That(session.TryStartTreatment("s1", "s3", out _), Is.True);
        TickTreatment("s3", 100);
        Assert.That(session.Assignments.WorkOf("s3"), Is.EqualTo(WorkAssignment.Production));
        Assert.That(session.FindUnit("s3").State.Activity, Is.EqualTo(SurvivorActivity.Working), "치료 후 저장된 배정으로 돌아간다.");
    }

    [Test]
    public void RecoveredCriticalKeepsAssignmentOrFallsToStandbyWhenFull()
    {
        BuildSession();
        AssignmentBoard board = session.Assignments;
        Assert.That(session.TrySetWork("s1", WorkAssignment.Production, out _), Is.True);
        Assert.That(session.TrySetWork("s3", WorkAssignment.Production, out _), Is.True);
        session.DebugLowerHp("s1", 20);
        Assert.That(board.ActiveWorkerCount, Is.EqualTo(1), "중상은 정원에서 빠진다.");
        Assert.That(board.WorkOf("s1"), Is.EqualTo(WorkAssignment.Production), "중상자의 배정은 보존된다.");
        Assert.That(session.TrySetWork("s4", WorkAssignment.Production, out _), Is.True, "빈 자리를 다른 사람이 맡을 수 있다.");
        Assert.That(session.TryStartTreatment("s1", "s6", out _), Is.True);
        TickTreatment("s6", 100);
        Assert.That(session.FindUnit("s1").State.CanAct, Is.True);
        Assert.That(board.WorkOf("s1"), Is.EqualTo(WorkAssignment.DefenseStandby), "자리가 찼으면 방어 대기(V2-IMPL-04).");
        Assert.That(board.ActiveWorkerCount, Is.EqualTo(Table.production.maxWorkers));
    }

    [Test]
    public void RecoveredCriticalFollowsNormalOrders()
    {
        BuildSession();
        Assert.That(session.TrySetWork("s1", WorkAssignment.Production, out _), Is.True);
        session.DebugLowerHp("s1", 20);
        GoToNextPrep();
        Assert.That(session.TryStartTreatment("s1", "s6", out _), Is.True);
        TickTreatment("s6", 100);
        SurvivorUnit unit = session.FindUnit("s1");
        Assert.That(session.Assignments.WorkOf("s1"), Is.EqualTo(WorkAssignment.Production), "배정이 그대로다(엣지 4).");
        Assert.That(unit.State.Activity, Is.EqualTo(SurvivorActivity.Working), "저장된 배정을 수행한다.");
        Assert.That(session.CommandMove("s1", Vector3.zero), Is.True, "다시 명령을 받는다.");
        Assert.That(session.TrySetDefensePoint("s1", new Vector3(3, 0, 3), out _), Is.True);
        // 다음 Update에서 쓰러짐 표시가 풀린다.
        Invoke(unit, "SyncDown");
        Assert.That((bool)Get(unit, "isDown"), Is.False);
    }

    [Test]
    public void CriticalAssignmentsArePreservedAndChangesRejected()
    {
        BuildSession();
        AssignmentBoard board = session.Assignments;
        Assert.That(session.TrySetWork("s1", WorkAssignment.Production, out _), Is.True);
        DefenseAssignment defense = board.DefenseOf("s1");
        session.DebugLowerHp("s1", 10);
        Assert.That(session.TrySetWork("s1", WorkAssignment.DefenseStandby, out string workError), Is.False);
        Assert.That(workError, Is.EqualTo("중상인 생존자는 배정을 바꿀 수 없습니다."));
        Assert.That(session.TrySetDefensePoint("s1", new Vector3(9, 0, 9), out string defenseError), Is.False);
        Assert.That(defenseError, Is.EqualTo("중상인 생존자는 배정을 바꿀 수 없습니다."));
        Assert.That(board.TrySetDefenseTower("s1", "t1", 1, 1, out _), Is.False);
        Assert.That(board.WorkOf("s1"), Is.EqualTo(WorkAssignment.Production));
        Assert.That(board.DefenseOf("s1").x, Is.EqualTo(defense.x));
        Assert.That(board.DefenseOf("s1").z, Is.EqualTo(defense.z));
        Assert.That(session.CommandMove("s1", Vector3.one), Is.False);
        Assert.That(session.ReturnToAssignment("s1"), Is.False);
    }

    [Test]
    public void DirectOrdersDoNotPullPeopleOffTreatmentInPrep()
    {
        BuildSession();
        session.DebugLowerHp("s1", 60);
        Assert.That(session.TryStartTreatment("s1", "s6", out _), Is.True);
        SurvivorUnit worker = session.FindUnit("s6");
        Assert.That(session.TrySetWork("s6", WorkAssignment.Production, out _), Is.True, "배정 데이터는 바꿀 수 있다(TD-73).");
        Assert.That(worker.State.Activity, Is.EqualTo(SurvivorActivity.Treating));
        Assert.That(session.TrySetDefensePoint("s6", new Vector3(4, 0, 4), out string notice), Is.True);
        Assert.That(notice, Does.Contain("치료 중"));
        Assert.That(worker.State.Activity, Is.EqualTo(SurvivorActivity.Treating));
        Assert.That(session.Assignments.DefenseOf("s6").x, Is.EqualTo(4f));
        Assert.That(session.CommandMove("s6", Vector3.one), Is.False);
        session.ReturnToAssignment("s6");
        Assert.That(worker.State.Activity, Is.EqualTo(SurvivorActivity.Treating));
    }

    [Test]
    public void DispatchRejectsCriticalAndTreatedPeopleWithoutChange()
    {
        BuildSession();
        string region = Table.expeditions[0].id;
        session.DebugLowerHp("s1", 10);
        Assert.That(session.TryStartExpedition(region, new[] { "s1" }, out string criticalError), Is.False);
        Assert.That(criticalError, Is.EqualTo("중상인 인원은 파견할 수 없습니다."));
        Assert.That(session.TryStartTreatment("s1", "s6", out _), Is.True);
        Assert.That(session.TryStartExpedition(region, new[] { "s6" }, out string workerError), Is.False);
        Assert.That(workerError, Is.EqualTo("치료 중인 인원은 치료를 취소해야 파견할 수 있습니다."));
        Assert.That(session.TryStartExpedition(region, new[] { "s2", "s1" }, out _), Is.False);
        Assert.That(session.Expeditions.Active, Is.Empty);
        Assert.That(session.FindUnit("s2").State.IsAway, Is.False, "한 명이라도 거부되면 아무도 떠나지 않는다.");
        Assert.That(session.TryCancelTreatment(out _), Is.True);
        Assert.That(session.TryStartExpedition(region, new[] { "s6" }, out string error), Is.True, error);
    }

    [Test]
    public void DownedSurvivorLeavesTowerAndKeepsSeatAssignment()
    {
        BuildSession();
        var towerPrefab = AssetDatabase.LoadAssetAtPath<TowerBuilding>("Assets/Prefabs/Tower.prefab");
        TowerBuilding tower = UnityEngine.Object.Instantiate(towerPrefab, host.transform);
        tower.Initialize(Table.buildings.tower);
        tower.SetStationId("t1");
        SurvivorUnit unit = session.FindUnit("s1");
        Assert.That(session.Assignments.TrySetDefenseTower("s1", "t1", 0, 0, out _), Is.True);
        unit.transform.position = tower.StationPosition;
        Set(unit, "station", tower);
        Set(unit, "arrived", true);
        Assert.That(unit.OccupiedTower, Is.EqualTo(tower));
        Assert.That(unit.IsProtected, Is.True);
        // 탑 위는 피해를 받지 않으므로 규칙 상태를 직접 낮춰 쓰러짐 경로만 본다.
        unit.State.ApplyDamage(unit.State.MaxHp - 1);
        Invoke(unit, "Update");
        Assert.That(unit.OccupiedTower, Is.Null, "탑에서 내려온다.");
        Assert.That(unit.IsProtected, Is.False);
        Assert.That(Get(unit, "station"), Is.Null);
        Assert.That(unit.IsFiring, Is.False);
        Assert.That(session.Assignments.DefenseOf("s1").towerId, Is.EqualTo("t1"), "배정은 보존한다(TD-75).");
        Assert.That(unit.IsTargetable, Is.True);
    }

    [Test]
    public void AllCriticalIsNotWipedAndSessionKeepsRunning()
    {
        BuildSession();
        foreach (SurvivorUnit unit in session.Units)
        {
            session.DebugLowerHp(unit.State.Id, 5);
        }
        Assert.That(session.AliveCount, Is.EqualTo(7));
        Assert.That(SessionFlow.IsWiped((List<SurvivorState>)Get(session, "survivors")), Is.False, "전원 중상은 전멸이 아니다.");
        Assert.That((bool)Invoke(session, "CheckDefeat"), Is.False);
        Assert.That(session.Phase, Is.EqualTo(SessionPhase.Prep));
        Assert.DoesNotThrow(() => Invoke(session, "TickTreatment", 1f));
        Assert.DoesNotThrow(() => session.DebugAdvancePhase());
        Assert.DoesNotThrow(() => GoToNextPrep());
        Assert.That(session.TryStartTreatment("s1", "s2", out _), Is.False, "행동 가능한 담당자가 없다.");
        foreach (SurvivorUnit unit in session.Units)
        {
            session.DebugKillSurvivor(unit.State.Id);
        }
        Assert.That((bool)Invoke(session, "CheckDefeat"), Is.True, "전원 사망은 패배다.");
    }

    [Test]
    public void JoinedSurvivorFollowsSameInjuryAndTreatmentRules()
    {
        BuildSession();
        Assert.That(session.DebugJoinSurvivor(out string error), Is.True, error);
        SurvivorUnit joined = session.Units[session.Units.Count - 1];
        string id = joined.State.Id;
        Assert.That(joined.State.IsNewJoin, Is.True);
        Assert.That(joined.State.Injury, Is.EqualTo(InjuryStatus.Normal));
        session.DebugLowerHp(id, joined.State.MaxHp * 0.5f);
        Assert.That(joined.State.Injury, Is.EqualTo(InjuryStatus.Light), "합류 경로도 부상 테이블을 받는다.");
        Assert.That(session.TryStartTreatment(id, "s6", out string patientError), Is.True, patientError);
        Assert.That(session.TryCancelTreatment(out _), Is.True);
        session.DebugLowerHp(id, 1);
        Assert.That(joined.State.CanAct, Is.False);
        Assert.That(session.TryStartTreatment("s1", id, out _), Is.False, "중상은 담당자가 될 수 없다.");
    }

    [Test]
    public void InitialRosterPathGetsInjuryTable()
    {
        host = new GameObject("Fixture");
        host.SetActive(false);
        session = host.AddComponent<GameSession>();
        GameplayTableData table = Table;
        Set(session, "table", table);
        Set(session, "board", new AssignmentBoard(table.production.maxWorkers));
        var prefab = AssetDatabase.LoadAssetAtPath<SurvivorUnit>("Assets/Prefabs/Survivor.prefab");
        var markerPrefab = AssetDatabase.LoadAssetAtPath<DefenseMarkerView>("Assets/Prefabs/DefenseMarker.prefab");
        var placed = new List<SurvivorUnit>();
        var points = new Transform[table.survivors.Length];
        for (int index = 0; index < table.survivors.Length; index++)
        {
            SurvivorUnit unit = UnityEngine.Object.Instantiate(prefab, host.transform);
            unit.SetDefenseMarker(UnityEngine.Object.Instantiate(markerPrefab, host.transform));
            placed.Add(unit);
            points[index] = Point("P" + index, index, 0);
        }
        Set(session, "units", placed);
        Set(session, "defaultDefensePoints", points);
        Invoke(session, "BindSurvivors");
        SurvivorState state = session.Units[0].State;
        state.ApplyDamage(state.MaxHp * 0.9f);
        Assert.That(state.Injury, Is.EqualTo(InjuryStatus.Critical), "초기 인원 경로도 부상 테이블을 받는다.");
        Assert.That(state.CanAct, Is.False);
    }

    // ── 표시와 권위 경로 ──
    GameObject hudInstance;

    T HudPart<T>() where T : Component
    {
        if (hudInstance == null)
        {
            hudInstance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Hud.prefab"), host.transform);
        }
        return hudInstance.GetComponentInChildren<T>(true);
    }

    static void Click(object _view, string _event, params object[] _args)
    {
        var callback = (Delegate)_view.GetType().GetField(_event, PRIVATE).GetValue(_view);
        callback.DynamicInvoke(_args);
    }

    static string TextOf(object _view, string _field) => ((Text)Get(_view, _field)).text;

    [Test]
    public void TreatmentPanelStartIsNotBlockedAndRejectionReachesAuthorityPath()
    {
        BuildSession(_scrap: 0);
        session.DebugLowerHp("s1", 60);
        var view = HudPart<TreatmentPanelView>();
        var presenter = new TreatmentPanelPresenter(view, session);
        presenter.OpenPanel();
        Assert.That(view.IsOpen, Is.True);
        Click(view, "PatientClicked", 0);
        Click(view, "WorkerClicked", 5);
        presenter.Refresh();
        Assert.That(TextOf(view, "statusText"), Is.EqualTo("스크랩이 부족합니다."));
        var start = (Button)Get(view, "startButton");
        Assert.That(start.interactable, Is.True, "사유를 보여 주되 시작 버튼을 막지 않는다.");
        Click(view, "StartClicked");
        Assert.That(rejected, Is.EqualTo(new[] { TreatmentSystem.REASON_NO_SCRAP }), "권위 경로까지 가서 거부됐다.");
        Assert.That(session.Treatment.Current, Is.Null);
        presenter.Refresh();
        Assert.That(TextOf(view, "statusText"), Is.EqualTo("스크랩이 부족합니다."));
        session.Economy.Add(5);
        Click(view, "WorkerClicked", 0);
        Click(view, "StartClicked");
        Assert.That(rejected[rejected.Count - 1], Is.EqualTo(TreatmentSystem.REASON_SELF));
        presenter.Refresh();
        Assert.That(TextOf(view, "statusText"), Is.EqualTo("담당자는 자기 자신을 치료할 수 없습니다."));
        Assert.That(session.Scrap, Is.EqualTo(5));
        Click(view, "WorkerClicked", 5);
        Click(view, "StartClicked");
        Assert.That(session.Treatment.Current, Is.Not.Null);
        Assert.That(session.Scrap, Is.EqualTo(3));
        presenter.Refresh();
        Assert.That(TextOf(view, "progressText"), Does.Contain("치료 중"));
        Click(view, "PatientClicked", 2);
        Click(view, "WorkerClicked", 3);
        Click(view, "StartClicked");
        Assert.That(rejected[rejected.Count - 1], Is.EqualTo(TreatmentSystem.REASON_BUSY), "중복 치료도 권위 경로가 거부한다.");
        Assert.That(session.Scrap, Is.EqualTo(3));
        Click(view, "CancelClicked");
        Assert.That(session.Treatment.Current, Is.Null);
        Assert.That(session.Scrap, Is.EqualTo(3));
    }

    [Test]
    public void TreatmentPanelShowsNineSeatsIncludingEmptyOnes()
    {
        BuildSession();
        var view = HudPart<TreatmentPanelView>();
        var presenter = new TreatmentPanelPresenter(view, session);
        presenter.OpenPanel();
        presenter.Refresh();
        Assert.That(view.RowCount, Is.EqualTo(Table.recruitment.capacity));
        var labels = (Text[])Get(view, "workerLabels");
        var buttons = (Button[])Get(view, "workerButtons");
        Assert.That(labels[0].text, Does.Contain("작업 1"));
        Assert.That(labels[0].text, Does.Contain("예상 28.6초"));
        Assert.That(labels[2].text, Does.Contain("작업 5"));
        Assert.That(labels[2].text, Does.Contain("예상 15.4초"));
        Assert.That(labels[7].text, Is.EqualTo("빈 자리"));
        Assert.That(labels[8].text, Is.EqualTo("빈 자리"));
        Assert.That(buttons[8].interactable, Is.False);
        session.DebugLowerHp("s1", 10);
        presenter.Refresh();
        Assert.That(labels[0].text, Does.Contain("불가(중상)"));
        Assert.That(buttons[0].interactable, Is.True, "눌러서 거부 사유를 볼 수 있다.");
    }

    RTSInput MakeInput()
    {
        var input = host.AddComponent<RTSInput>();
        Set(input, "session", session);
        Invoke(input, "Awake");
        return input;
    }

    [Test]
    public void SelectionPanelShowsConditionAndDisablesOrdersForCritical()
    {
        BuildSession();
        RTSInput input = MakeInput();
        var view = HudPart<SelectionPanelView>();
        var presenter = new SelectionPanelPresenter(view, session, input);
        session.DebugLowerHp("s6", 50);
        input.SelectFromList("s6");
        presenter.Refresh();
        Assert.That(TextOf(view, "titleText"), Does.Contain("경상 · 효율 75%"));
        Assert.That(((Button)Get(view, "productionButton")).interactable, Is.True);
        session.DebugLowerHp("s1", 10);
        input.SelectFromList("s1");
        Assert.That(input.Selection.Contains("s1"), Is.True, "중상자도 목록에서 선택해 내용을 볼 수 있다.");
        presenter.Refresh();
        Assert.That(TextOf(view, "titleText"), Does.Contain("중상 · 치료 대기"));
        Assert.That(TextOf(view, "messageText"), Does.Contain("명령 불가"));
        Assert.That(TextOf(view, "actionText"), Does.Contain("쓰러짐"));
        foreach (string button in new[] { "productionButton", "standbyButton", "defenseButton", "returnButton" })
        {
            Assert.That(((Button)Get(view, button)).interactable, Is.False, button);
        }
        input.Selection.Prune();
        Assert.That(input.Selection.Contains("s1"), Is.True, "쓰러져도 선택에서 빠지지 않는다.");
    }

    [Test]
    public void SurvivorListShowsInjuryAndTreatmentAndKeepsCriticalClickable()
    {
        BuildSession();
        RTSInput input = MakeInput();
        var view = HudPart<SurvivorListView>();
        var presenter = new SurvivorListPresenter(view, session, input);
        session.DebugLowerHp("s6", 50);
        session.DebugLowerHp("s1", 10);
        presenter.Refresh();
        Text Status(int _row) => (Text)Get(view.Rows[_row], "statusText");
        Button RowButton(int _row) => (Button)Get(view.Rows[_row], "button");
        Assert.That(Status(5).text, Does.StartWith("경상"));
        Assert.That(Status(0).text, Is.EqualTo("중상"));
        Assert.That(RowButton(0).interactable, Is.True, "중상자 행은 눌린다.");
        Assert.That(Status(5).color, Is.Not.EqualTo(Status(1).color), "색으로 구별한다.");
        Assert.That(Status(0).color, Is.Not.EqualTo(Status(5).color));
        Assert.That(session.TryStartTreatment("s1", "s3", out _), Is.True);
        presenter.Refresh();
        Assert.That(Status(0).text, Is.EqualTo("치료 중"));
        Assert.That(Status(2).text, Is.EqualTo("치료 중"));
        Assert.That(Status(2).color, Is.Not.EqualTo(Status(5).color));
    }

    [Test]
    public void ExpeditionRowsBlockCriticalAndTreatedAndShowEffectiveValue()
    {
        BuildSession();
        var view = HudPart<ExpeditionPanelView>();
        var presenter = new ExpeditionPanelPresenter(view, session);
        session.DebugLowerHp("s1", 10);
        session.DebugLowerHp("s5", 45);
        Assert.That(session.TryStartTreatment("s1", "s6", out _), Is.True);
        Click(view, "MemberClicked", 0);
        Click(view, "MemberClicked", 5);
        Click(view, "MemberClicked", 4);
        presenter.Refresh();
        var labels = (Text[])Get(view, "memberLabels");
        var buttons = (Button[])Get(view, "memberButtons");
        Assert.That(labels[0].text, Does.Contain("중상 · 파견 불가"));
        Assert.That(buttons[0].interactable, Is.False);
        Assert.That(labels[5].text, Does.Contain("치료 중"));
        Assert.That(buttons[5].interactable, Is.False);
        Assert.That(labels[4].text, Does.Contain("탐사 5→3.75"));
        Assert.That(labels[4].text, Does.StartWith("[선택]"));
        Assert.That(TextOf(view, "previewText"), Does.Contain("1/"), "고를 수 없는 인원은 파티에 들어가지 않는다.");
        Assert.That(TextOf(view, "previewText"), Does.Contain("탐사 합 4"), "3.75는 반올림해 4다.");
    }

    [Test]
    public void DebugRestartCancelsTreatmentAndRestoresForRetest()
    {
        BuildSession();
        session.DebugLowerHp("s1", 10);
        Assert.That(session.TryStartTreatment("s1", "s6", out _), Is.True);
        session.DebugRestartWave();
        Assert.That(session.Treatment.Current, Is.Null);
        Assert.That(cancelled[0].reason, Is.EqualTo(TreatmentSystem.END_DEBUG));
        Assert.That(session.FindUnit("s1").State.Hp, Is.EqualTo(session.FindUnit("s1").State.MaxHp));
    }

    // ── 에셋과 테이블 ──
    [Test]
    public void InfirmaryPrefabHasDistinctSeatsForFullCapacity()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<InfirmaryFacility>("Assets/Prefabs/Infirmary.prefab");
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.WaitSlotCount, Is.GreaterThanOrEqualTo(Table.recruitment.capacity));
        var seats = (Transform[])Get(prefab, "waitSlots");
        var seen = new HashSet<Vector3>();
        var radius = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshAgent>("Assets/Prefabs/Survivor.prefab").radius;
        for (int index = 0; index < seats.Length; index++)
        {
            Assert.That(seen.Add(seats[index].position), Is.True);
            for (int other = 0; other < index; other++)
            {
                Assert.That(Vector3.Distance(seats[index].position, seats[other].position), Is.GreaterThan(radius * 2), "대기 자리가 겹치면 안 된다.");
            }
        }
    }

    [Test]
    public void HudPrefabWiresTreatmentPanel()
    {
        var hud = AssetDatabase.LoadAssetAtPath<GameHud>("Assets/Prefabs/Hud.prefab");
        var view = (TreatmentPanelView)Get(hud, "treatmentView");
        Assert.That(view, Is.Not.Null);
        Assert.That(view.RowCount, Is.EqualTo(Table.recruitment.capacity));
        foreach (string field in new[] { "patientLabels", "workerLabels", "patientButtons", "workerButtons" })
        {
            foreach (object entry in (Array)Get(view, field))
            {
                Assert.That((UnityEngine.Object)entry, Is.Not.Null, field);
            }
        }
        var hudView = hud.GetComponentInChildren<HudView>(true);
        Assert.That(Get(hudView, "treatmentButton"), Is.Not.Null);
        Assert.That(Get(hudView, "treatmentLabel"), Is.Not.Null);
    }

    [Test]
    public void ActualTableHasInitialValues()
    {
        GameplayTableData table = Table;
        Assert.That(table.injury.normalMinHpFraction, Is.EqualTo(0.7f));
        Assert.That(table.injury.lightMinHpFraction, Is.EqualTo(0.3f));
        Assert.That(table.injury.lightEfficiency, Is.EqualTo(0.75f));
        Assert.That(table.treatment.cost, Is.EqualTo(2));
        Assert.That(table.treatment.healFraction, Is.EqualTo(0.4f));
        Assert.That(table.treatment.baseSeconds, Is.EqualTo(20f));
    }

    void AssertTableRejected(TableManager _tables, InjuryData _injury, TreatmentData _treatment, string _pattern)
    {
        GameplayTableData table = _tables.Gameplay;
        table.injury = _injury;
        table.treatment = _treatment;
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(_pattern));
        var rules = new GameTableRules();
        rules.Validate(_tables);
        Assert.That(rules.IsValid, Is.False, _pattern);
    }

    [Test]
    public void TableRulesRejectBadInjuryAndTreatmentValues()
    {
        TableManager tables = TableManager.Instance.RequireValid();
        GameplayTableData table = tables.Gameplay;
        InjuryData injury = table.injury;
        TreatmentData treatment = table.treatment;
        var goodInjury = new InjuryData { normalMinHpFraction = 0.7f, lightMinHpFraction = 0.3f, lightEfficiency = 0.75f };
        var goodTreatment = new TreatmentData { cost = 2, healFraction = 0.4f, baseSeconds = 20, referenceWork = 3, speedPerWork = 0.15f };
        try
        {
            AssertTableRejected(tables, new InjuryData { normalMinHpFraction = 0.3f, lightMinHpFraction = 0.7f, lightEfficiency = 0.75f }, goodTreatment, "Gameplay.injury");
            AssertTableRejected(tables, new InjuryData { normalMinHpFraction = 0.7f, lightMinHpFraction = 0.3f, lightEfficiency = 0 }, goodTreatment, "Gameplay.injury.lightEfficiency");
            AssertTableRejected(tables, goodInjury, new TreatmentData { cost = -1, healFraction = 0.4f, baseSeconds = 20, referenceWork = 3, speedPerWork = 0.15f }, "Gameplay.treatment.cost");
            AssertTableRejected(tables, goodInjury, new TreatmentData { cost = 2, healFraction = 0, baseSeconds = 20, referenceWork = 3, speedPerWork = 0.15f }, "Gameplay.treatment.healFraction");
            AssertTableRejected(tables, goodInjury, new TreatmentData { cost = 2, healFraction = 0.4f, baseSeconds = 0, referenceWork = 3, speedPerWork = 0.15f }, "Gameplay.treatment.baseSeconds");
            AssertTableRejected(tables, goodInjury, new TreatmentData { cost = 2, healFraction = 0.4f, baseSeconds = 20, referenceWork = 3, speedPerWork = 1 }, "Gameplay.treatment.speedPerWork");
        }
        finally
        {
            table.injury = injury;
            table.treatment = treatment;
        }
    }
}
