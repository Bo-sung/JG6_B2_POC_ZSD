using System.Collections.Generic;
using NUnit.Framework;

// 작은 고정 수치는 경계 검증용 입력이며 게임 밸런스를 정하지 않는다.
public sealed class PocSliceRuleTests
{
    const int MAX_WORKERS = 2;

    [Test]
    public void WorkAndDefenseAreStoredIndependently()
    {
        AssignmentBoard board = Board(out SurvivorState[] survivors);
        string id = survivors[0].Id;
        Assert.That(board.TrySetDefensePoint(id, 3, 4, out _), Is.True);
        Assert.That(board.TrySetWork(id, WorkAssignment.Production, out _), Is.True);
        Assert.That(board.DefenseOf(id).x, Is.EqualTo(3));
        Assert.That(board.DefenseOf(id).z, Is.EqualTo(4));
        Assert.That(board.TrySetDefensePoint(id, 7, 8, out _), Is.True);
        Assert.That(board.WorkOf(id), Is.EqualTo(WorkAssignment.Production));
        Assert.That(board.TrySetWork(id, WorkAssignment.DefenseStandby, out _), Is.True);
        Assert.That(board.DefenseOf(id).x, Is.EqualTo(7));
    }

    [Test]
    public void ProductionCapacityIsEnforced()
    {
        AssignmentBoard board = Board(out SurvivorState[] survivors);
        Assert.That(board.TrySetWork(survivors[0].Id, WorkAssignment.Production, out _), Is.True);
        Assert.That(board.TrySetWork(survivors[1].Id, WorkAssignment.Production, out _), Is.True);
        Assert.That(board.TrySetWork(survivors[2].Id, WorkAssignment.Production, out string error), Is.False);
        Assert.That(error, Is.Not.Empty);
        Assert.That(board.WorkOf(survivors[2].Id), Is.EqualTo(WorkAssignment.DefenseStandby));
        Assert.That(board.TrySetWork(survivors[0].Id, WorkAssignment.Production, out _), Is.True, "같은 배정 재지정은 정원과 무관하다.");
    }

    [Test]
    public void DeadSurvivorIsRejectedAndFreesCapacity()
    {
        AssignmentBoard board = Board(out SurvivorState[] survivors);
        board.TrySetWork(survivors[0].Id, WorkAssignment.Production, out _);
        board.TrySetWork(survivors[1].Id, WorkAssignment.Production, out _);
        survivors[0].Kill();
        Assert.That(board.TrySetWork(survivors[0].Id, WorkAssignment.DefenseStandby, out _), Is.False);
        Assert.That(board.TrySetDefensePoint(survivors[0].Id, 1, 1, out _), Is.False);
        Assert.That(board.TrySetDefenseTower(survivors[0].Id, "t", 1, 1, out _), Is.False);
        Assert.That(board.ActiveWorkerCount, Is.EqualTo(1));
        Assert.That(board.TrySetWork(survivors[2].Id, WorkAssignment.Production, out _), Is.True);
    }

    [Test]
    public void AwaySurvivorIsRejectedAndDoesNotCountAsWorker()
    {
        AssignmentBoard board = Board(out SurvivorState[] survivors);
        board.TrySetWork(survivors[0].Id, WorkAssignment.Production, out _);
        survivors[0].BeginAway();
        Assert.That(board.TrySetWork(survivors[0].Id, WorkAssignment.DefenseStandby, out _), Is.False);
        Assert.That(board.TrySetDefensePoint(survivors[0].Id, 1, 1, out _), Is.False);
        Assert.That(board.ActiveWorkerCount, Is.Zero);
    }

    [Test]
    public void ReturningWorkerFallsBackToStandbyWhenSlotsWereFilled()
    {
        AssignmentBoard board = Board(out SurvivorState[] survivors);
        board.TrySetWork(survivors[0].Id, WorkAssignment.Production, out _);
        survivors[0].BeginAway();
        board.TrySetWork(survivors[1].Id, WorkAssignment.Production, out _);
        board.TrySetWork(survivors[2].Id, WorkAssignment.Production, out _);
        survivors[0].EndAway();
        Assert.That(board.ResolveReturn(survivors[0].Id), Is.True);
        Assert.That(board.WorkOf(survivors[0].Id), Is.EqualTo(WorkAssignment.DefenseStandby));
        Assert.That(board.ActiveWorkerCount, Is.EqualTo(MAX_WORKERS));
    }

    [Test]
    public void ReturningWorkerKeepsSlotWhenFree()
    {
        AssignmentBoard board = Board(out SurvivorState[] survivors);
        board.TrySetWork(survivors[0].Id, WorkAssignment.Production, out _);
        survivors[0].BeginAway();
        survivors[0].EndAway();
        Assert.That(board.ResolveReturn(survivors[0].Id), Is.False);
        Assert.That(board.WorkOf(survivors[0].Id), Is.EqualTo(WorkAssignment.Production));
    }

    [Test]
    public void TowerSeatHoldsOnePersonAndFallsBackToPoint()
    {
        AssignmentBoard board = Board(out SurvivorState[] survivors);
        Assert.That(board.TrySetDefenseTower(survivors[0].Id, "t", 5, 6, out _), Is.True);
        Assert.That(board.TrySetDefenseTower(survivors[1].Id, "t", 5, 6, out _), Is.False);
        Assert.That(board.TrySetDefenseTower(survivors[0].Id, "t", 5, 6, out _), Is.True);
        survivors[0].Kill();
        Assert.That(board.TrySetDefenseTower(survivors[1].Id, "t", 5, 6, out _), Is.True);
        board.VacateTower("t");
        Assert.That(board.DefenseOf(survivors[1].Id).IsTower, Is.False);
        Assert.That(board.DefenseOf(survivors[1].Id).x, Is.EqualTo(5));
    }

    [Test]
    public void DeathIsPermanent()
    {
        var survivor = new SurvivorState(Data("a"));
        Assert.That(survivor.ApplyDamage(survivor.MaxHp - 1), Is.False);
        Assert.That(survivor.IsAlive, Is.True);
        survivor.RestoreHp();
        Assert.That(survivor.Hp, Is.EqualTo(survivor.MaxHp));
        Assert.That(survivor.ApplyDamage(survivor.MaxHp + 1), Is.True);
        Assert.That(survivor.ApplyDamage(1), Is.False, "사망 판정은 한 번만 난다.");
        Assert.That(survivor.Hp, Is.Zero);
        survivor.RestoreHp();
        Assert.That(survivor.IsAlive, Is.False);
        Assert.That(survivor.Hp, Is.Zero);
        Assert.That(survivor.SetActivity(SurvivorActivity.Guarding), Is.False);
        Assert.That(survivor.BeginAway(), Is.False);
        Assert.That(survivor.Kill(), Is.False);
    }

    [Test]
    public void AwaySurvivorIgnoresOrdersUntilReturned()
    {
        var survivor = new SurvivorState(Data("a"));
        Assert.That(survivor.BeginAway(), Is.True);
        Assert.That(survivor.CanAct, Is.False);
        Assert.That(survivor.SetActivity(SurvivorActivity.Working), Is.False);
        Assert.That(survivor.BeginAway(), Is.False);
        Assert.That(survivor.EndAway(), Is.True);
        Assert.That(survivor.CanAct, Is.True);
        Assert.That(survivor.EndAway(), Is.False);
    }

    [Test]
    public void FlowRunsThreeWavesThenVictory()
    {
        SessionFlow flow = Flow();
        for (int wave = 1; wave <= 3; wave++)
        {
            Assert.That(flow.WaveNumber, Is.EqualTo(wave));
            Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Prep));
            Assert.That(flow.Remaining, Is.EqualTo(10));
            Assert.That(flow.Tick(9), Is.False);
            Assert.That(flow.Tick(1), Is.True);
            flow.Advance();
            Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Alert));
            Assert.That(flow.Remaining, Is.EqualTo(2));
            flow.Advance();
            Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Combat));
            Assert.That(flow.Remaining, Is.EqualTo(5));
            flow.Advance();
            Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Result));
            Assert.That(flow.Tick(100), Is.False, "결과 단계에서는 시간이 흐르지 않는다.");
            Assert.That(flow.Result, Is.EqualTo(SessionResult.None));
            flow.Advance();
        }
        Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Ended));
        Assert.That(flow.Result, Is.EqualTo(SessionResult.Victory));
        Assert.That(flow.WaveNumber, Is.EqualTo(3));
    }

    [Test]
    public void DefeatEndsImmediatelyFromAnyPhaseAndStaysDefeat()
    {
        SessionFlow flow = Flow();
        flow.Advance();
        flow.EndDefeat();
        Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Ended));
        Assert.That(flow.Result, Is.EqualTo(SessionResult.Defeat));
        Assert.That(flow.Tick(1), Is.False);
        flow.RestartWave();
        Assert.That(flow.Phase, Is.EqualTo(SessionPhase.Ended));
    }

    [Test]
    public void TimeoutDamageScalesWithRemainingEnemies()
    {
        SessionFlow flow = Flow();
        Assert.That(flow.TimeoutCoreDamage(4), Is.EqualTo(12));
        Assert.That(flow.TimeoutCoreDamage(0), Is.Zero);
        Assert.That(flow.TimeoutCoreDamage(-1), Is.Zero);
    }

    [Test]
    public void WipeRequiresEveryoneDeadIncludingAway()
    {
        var home = new SurvivorState(Data("home"));
        var away = new SurvivorState(Data("away"));
        var list = new List<SurvivorState> { home, away };
        away.BeginAway();
        home.Kill();
        Assert.That(SessionFlow.IsWiped(list), Is.False, "파견 인력이 살아 있으면 전멸이 아니다(DEC-14).");
        away.Kill();
        Assert.That(SessionFlow.IsWiped(list), Is.True);
    }

    [Test]
    public void PhaseGatesMatchDesignTable()
    {
        Assert.That(SessionFlow.CanChangeWork(SessionPhase.Prep), Is.True);
        Assert.That(SessionFlow.CanChangeWork(SessionPhase.Alert), Is.False);
        Assert.That(SessionFlow.CanChangeWork(SessionPhase.Combat), Is.False);
        Assert.That(SessionFlow.CanChangeWork(SessionPhase.Result), Is.False);
        Assert.That(SessionFlow.CanChangeDefense(SessionPhase.Alert), Is.True);
        Assert.That(SessionFlow.CanChangeDefense(SessionPhase.Combat), Is.True);
        Assert.That(SessionFlow.CanChangeDefense(SessionPhase.Result), Is.False);
        Assert.That(SessionFlow.ProducesScrap(SessionPhase.Prep), Is.True);
        Assert.That(SessionFlow.ProducesScrap(SessionPhase.Alert), Is.False, "경보 후 생산 금지.");
        Assert.That(SessionFlow.ProducesScrap(SessionPhase.Combat), Is.False);
        Assert.That(SessionFlow.RunsExpeditions(SessionPhase.Combat), Is.True);
        Assert.That(SessionFlow.RunsExpeditions(SessionPhase.Result), Is.False);
        Assert.That(SessionFlow.CanBuild(SessionPhase.Combat), Is.False);
        Assert.That(SessionFlow.CanDispatch(SessionPhase.Alert), Is.False);
    }

    [Test]
    public void SelectionSkipsUnselectableAndPrunesDead()
    {
        var blocked = new HashSet<string> { "dead" };
        var selection = new SelectionModel(_id => !blocked.Contains(_id));
        int changes = 0;
        selection.Changed += () => changes++;
        selection.Replace(new[] { "a", "dead", "a", "b" });
        Assert.That(selection.Ids, Is.EqualTo(new[] { "a", "b" }));
        selection.Add("dead");
        selection.Add("c");
        Assert.That(selection.Ids, Is.EqualTo(new[] { "a", "b", "c" }));
        blocked.Add("b");
        selection.Prune();
        Assert.That(selection.Ids, Is.EqualTo(new[] { "a", "c" }));
        Assert.That(changes, Is.EqualTo(3));
        selection.Clear();
        selection.Clear();
        Assert.That(selection.Count, Is.Zero);
        Assert.That(changes, Is.EqualTo(4));
    }

    static SessionFlow Flow() => new SessionFlow(10, 2, 5, 3, 3);

    static AssignmentBoard Board(out SurvivorState[] _survivors)
    {
        var board = new AssignmentBoard(MAX_WORKERS);
        _survivors = new[] { new SurvivorState(Data("a")), new SurvivorState(Data("b")), new SurvivorState(Data("c")) };
        foreach (SurvivorState survivor in _survivors)
        {
            board.Register(survivor, new DefenseAssignment(0, 0));
        }
        return board;
    }

    static SurvivorData Data(string _id)
    {
        return new SurvivorData { id = _id, displayName = _id, combat = 1, work = 1, exploration = 1, maxHp = 10 };
    }
}
