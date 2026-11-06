using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class ReviewRegressionTests
{
    GameObject host;
    string folder;
    const BindingFlags PRIVATE_INSTANCE = BindingFlags.NonPublic | BindingFlags.Instance;

    static void Set(object _target, string _field, object _value)
    {
        _target.GetType().GetField(_field, PRIVATE_INSTANCE).SetValue(_target, _value);
    }

    static object Invoke(object _target, string _method, params object[] _args)
    {
        return _target.GetType().GetMethod(_method, PRIVATE_INSTANCE).Invoke(_target, _args);
    }

    // LogKitSetup이 이름으로 되돌리는 필드다. 패키지를 올려 이름이 바뀌면 조용히 실패하지 않게 여기서 잡는다.
    [Test]
    public void LogKitQuittingFieldStillExists()
    {
        FieldInfo field = typeof(LogKit.GameLogger).GetField(LogKitSetup.LOGKIT_QUITTING_FIELD, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(field, Is.Not.Null);
        Assert.That(field.FieldType, Is.EqualTo(typeof(bool)));
    }

    [SetUp]
    public void Setup()
    {
        host = new GameObject("Review fixture");
        host.SetActive(false);
        folder = Path.Combine(Path.GetTempPath(), "ReviewRegressionTests", Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void Cleanup()
    {
        UnityEngine.Object.DestroyImmediate(host);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ShutdownWritesExactlyOneEndAndPreservesNormalResult(bool _normalEnd)
    {
        var logger = host.AddComponent<LogGameLifecycle>();
        var store = new FileLogStore(folder);
        var log = new LogSession(store, "shutdown");
        Set(logger, "session", log);
        if (_normalEnd)
        {
            Invoke(logger, "OnSessionEnded", new GameEvents.SessionEndInfo(SessionResult.Victory, "all_waves_cleared"));
        }
        Invoke(logger, "EndForQuit");
        Invoke(logger, "OnDisable");
        string[] lines = File.ReadAllLines(store.PathFor("shutdown"));
        Assert.That(lines.Length, Is.EqualTo(1));
        JObject record = JObject.Parse(lines[0]);
        Assert.That((string)record["type"], Is.EqualTo("SessionEnded"));
        Assert.That((string)record["payload"]["result"], Is.EqualTo(_normalEnd ? "Victory" : "None"));
        Assert.That((string)record["payload"]["reason"], Is.EqualTo(_normalEnd ? "all_waves_cleared" : "quit"));
    }

    [Test]
    public void AssignmentLogOnlyEmitsWhenStoredValuesChange()
    {
        var session = host.AddComponent<GameSession>();
        var board = new AssignmentBoard(2);
        var state = new SurvivorState(TableManager.Instance.RequireValid().Gameplay.survivors[0]);
        board.Register(state, new DefenseAssignment(0, 0));
        Set(session, "board", board);
        int changes = 0;
        void Changed(GameEvents.AssignmentInfo _info)
        {
            changes++;
        }
        GameEvents.AssignmentChanged += Changed;
        try
        {
            Invoke(session, "RaiseAssignment", state.Id);
            board.TrySetWork(state.Id, WorkAssignment.DefenseStandby, out _);
            Invoke(session, "RaiseAssignment", state.Id);
            Assert.That(changes, Is.EqualTo(1));
            board.TrySetWork(state.Id, WorkAssignment.Production, out _);
            Invoke(session, "RaiseAssignment", state.Id);
            board.TrySetDefensePoint(state.Id, 0.01f, 0, out _);
            Invoke(session, "RaiseAssignment", state.Id);
            Invoke(session, "RaiseAssignment", state.Id);
            Assert.That(changes, Is.EqualTo(3));
        }
        finally
        {
            GameEvents.AssignmentChanged -= Changed;
        }
    }

    [Test]
    public void ProductionRejectsFewerSlotsThanFinalTableCapacity()
    {
        var session = host.AddComponent<GameSession>();
        var production = host.AddComponent<ProductionFacility>();
        var table = TableManager.Instance.RequireValid().Gameplay;
        Set(session, "table", table);
        Set(session, "production", production);
        Set(production, "workSlots", new Transform[Math.Max(0, table.production.maxWorkers - 1)]);
        var exception = Assert.Throws<TargetInvocationException>(() => Invoke(session, "BindProduction"));
        Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
        Set(production, "workSlots", new Transform[table.production.maxWorkers]);
        Assert.DoesNotThrow(() => Invoke(session, "BindProduction"));
    }

    [Test]
    public void DeadExpeditionMemberCannotReachReturnOrClaimSlot()
    {
        var session = host.AddComponent<GameSession>();
        var unit = host.AddComponent<SurvivorUnit>();
        var state = new SurvivorState(TableManager.Instance.RequireValid().Gameplay.survivors[0]);
        state.BeginAway();
        state.ApplyDamage(state.MaxHp);
        Set(unit, "<State>k__BackingField", state);
        var map = (Dictionary<string, SurvivorUnit>)typeof(GameSession).GetField("unitsById", PRIVATE_INSTANCE).GetValue(session);
        map.Add(state.Id, unit);
        // No return point or production facility exists: either side effect would fail.
        Assert.DoesNotThrow(() => Invoke(session, "ReturnFromExpedition", state.Id));
        Assert.That(state.IsAlive, Is.False);
    }

    [Test]
    public void DeadEnemySkipsUninitializedNavigationAndAttack()
    {
        var enemy = host.AddComponent<EnemyUnit>();
        Set(enemy, "data", TableManager.Instance.RequireValid().Gameplay.enemies[0]);
        Set(enemy, "hp", 0f);
        Assert.DoesNotThrow(() => Invoke(enemy, "Update"));
    }

    [Test]
    public void HaltedSurvivorSkipsCombatAndClearsTargets()
    {
        var unit = host.AddComponent<SurvivorUnit>();
        var tracer = host.AddComponent<LineRenderer>();
        Set(unit, "tracer", tracer);
        Set(unit, "halted", true);
        Set(unit, "<State>k__BackingField", new SurvivorState(TableManager.Instance.RequireValid().Gameplay.survivors[0]));
        // No combat data or agent is bound, so attempting combat would fail.
        Assert.DoesNotThrow(() => Invoke(unit, "Update"));
    }

    [Test]
    public void BuildingIdentifierIsSharedWithPlacementEvent()
    {
        var tower = host.AddComponent<TowerBuilding>();
        tower.SetStationId("3");
        var info = new GameEvents.BuildingInfo(BuildingType.Tower, Vector3.zero, 8, tower.StationId);
        Assert.That(info.id, Is.EqualTo("3"));
        Assert.That(tower.StationId, Is.EqualTo(info.id));
    }

    SurvivorUnit CreateSurvivor()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Survivor.prefab");
        var unit = UnityEngine.Object.Instantiate(prefab, host.transform).GetComponent<SurvivorUnit>();
        var marker = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/DefenseMarker.prefab"), host.transform).GetComponent<DefenseMarkerView>();
        Set(unit, "defenseMarker", marker);
        var table = TableManager.Instance.RequireValid().Gameplay;
        unit.Bind(new SurvivorState(table.survivors[0]), table.survivorCombat);
        return unit;
    }

    [Test]
    public void LosingLastSelectedMemberCancelsDefensePick()
    {
        var session = host.AddComponent<GameSession>();
        var unit = CreateSurvivor();
        Set(session, "units", new[] { unit });
        var map = (Dictionary<string, SurvivorUnit>)typeof(GameSession).GetField("unitsById", PRIVATE_INSTANCE).GetValue(session);
        map.Add(unit.State.Id, unit);
        var input = host.AddComponent<RTSInput>();
        Set(input, "session", session);
        Invoke(input, "Awake");
        input.Selection.Replace(new[] { unit.State.Id });
        Set(input, "pendingDefensePick", new Action<Vector3>(_point => Assert.Fail("Canceled pick must not execute")));
        unit.State.BeginAway();
        input.Selection.Prune();
        Assert.That(input.Selection.Count, Is.Zero);
        Assert.That(input.IsPickingDefense, Is.False);
    }

    [Test]
    public void WaveResultCanResumeButSessionEndPermanentlyStopsCombat()
    {
        var session = host.AddComponent<GameSession>();
        var unit = CreateSurvivor();
        var core = host.AddComponent<CoreFacility>();
        var director = host.AddComponent<WaveDirector>();
        var table = TableManager.Instance.RequireValid().Gameplay;
        var flow = new SessionFlow(table.session.prepSeconds, table.session.alertSeconds, table.session.combatSeconds,
            table.session.timeoutCoreDamagePerEnemy, table.waves.Length);
        flow.Advance();
        flow.Advance();
        core.Initialize(table.core.maxHp);
        Set(session, "units", new[] { unit });
        Set(session, "core", core);
        Set(session, "flow", flow);
        Set(session, "economy", new ResourceEconomy(table.session.startScrap, table.production.scrapPerSecondPerWork));
        Set(session, "waveDirector", director);
        Invoke(session, "EnterResult", 0, 0f);
        Assert.That((bool)typeof(SurvivorUnit).GetField("halted", PRIVATE_INSTANCE).GetValue(unit), Is.False);
        Invoke(session, "EndSession", SessionResult.Defeat, "test");
        Assert.That((bool)typeof(SurvivorUnit).GetField("halted", PRIVATE_INSTANCE).GetValue(unit), Is.True);
        Assert.That(unit.IsFiring, Is.False);
    }
}
