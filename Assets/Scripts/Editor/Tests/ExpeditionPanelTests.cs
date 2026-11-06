using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public sealed class ExpeditionPanelTests
{
    GameObject host;
    GameSession session;
    ExpeditionPanelView view;
    ExpeditionPanelPresenter presenter;
    Text preview;
    Button dispatch;
    readonly List<SurvivorState> states = new List<SurvivorState>();

    static void Set(object _target, string _field, object _value)
    {
        _target.GetType().GetField(_field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_target, _value);
    }
    Text Label()
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(host.transform);
        return go.GetComponent<Text>();
    }
    Button Button()
    {
        var go = new GameObject("Button", typeof(RectTransform), typeof(Button));
        go.transform.SetParent(host.transform);
        return go.GetComponent<Button>();
    }
    void Click(string _event, params object[] _args)
    {
        var callback = (Delegate)typeof(ExpeditionPanelView).GetField(_event, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
        callback.DynamicInvoke(_args);
    }

    [SetUp]
    public void Setup()
    {
        host = new GameObject("Fixture");
        host.SetActive(false);
        session = host.AddComponent<GameSession>();
        view = host.AddComponent<ExpeditionPanelView>();
        var table = TableManager.Instance.RequireValid().Gameplay;
        Set(session, "table", table);
        Set(session, "flow", new SessionFlow(table.session.prepSeconds, table.session.alertSeconds, table.session.combatSeconds, table.session.timeoutCoreDamagePerEnemy, table.waves.Length));
        Set(session, "expeditions", new ExpeditionSystem(table.expeditions, new System.Random(1)));
        var board = new AssignmentBoard(table.production.maxWorkers);
        Set(session, "board", board);
        var units = new List<SurvivorUnit>();
        var map = (Dictionary<string, SurvivorUnit>)typeof(GameSession).GetField("unitsById", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
        states.Clear();
        for (int index = 0; index < table.survivors.Length; index++)
        {
            var go = new GameObject("Unit"); go.SetActive(false); go.transform.SetParent(host.transform);
            var unit = go.AddComponent<SurvivorUnit>();
            units.Add(unit);
            var state = new SurvivorState(table.survivors[index]);
            states.Add(state);
            Set(units[index], "<State>k__BackingField", state);
            board.Register(state, new DefenseAssignment(0, 0));
            map.Add(state.Id, units[index]);
        }
        Set(session, "units", units);
        var buttons = new Button[units.Count];
        var labels = new Text[units.Count];
        for (int index = 0; index < units.Count; index++)
        {
            buttons[index] = Button(); labels[index] = Label();
        }
        Set(view, "memberButtons", buttons); Set(view, "memberLabels", labels);
        preview = Label(); dispatch = Button();
        Set(view, "previewText", preview); Set(view, "statusText", Label());
        Set(view, "activeText", Label()); Set(view, "historyText", Label()); Set(view, "dispatchButton", dispatch);
        presenter = new ExpeditionPanelPresenter(view, session);
    }

    [TearDown]
    public void Teardown() => UnityEngine.Object.DestroyImmediate(host);

    [Test]
    public void SelectionUsesTableProbabilitiesAndCapacity()
    {
        var region = TableManager.Instance.Gameplay.expeditions[0];
        int total = 0;
        for (int index = 0; index < region.maxParty; index++)
        {
            Click("MemberClicked", index); total += states[index].Exploration;
        }
        Click("MemberClicked", region.maxParty);
        presenter.Refresh();
        Assert.That(preview.text, Does.Contain($"{region.maxParty}/{region.maxParty}명"));
        Assert.That(preview.text, Does.Contain($"탐사 합 {total}"));
        Assert.That(preview.text, Does.Contain($"확률 {session.Expeditions.RewardChance(region.id, total):P1}"));
        Assert.That(preview.text, Does.Contain($"최대 1명 사망 {session.Expeditions.DeathChance(region.id, total, region.maxParty):P1}"));
        Assert.That(preview.text, Does.Contain($"성공 시 {session.Expeditions.ExpectedReward(region.id, total)} 스크랩"));
        Assert.That(dispatch.interactable, Is.True);
    }

    [Test]
    public void DeadAndAwayMembersCannotRemainSelected()
    {
        Click("MemberClicked", 0);
        states[0].ApplyDamage(states[0].MaxHp);
        states[1].BeginAway();
        Click("MemberClicked", 1);
        presenter.Refresh();
        Assert.That(dispatch.interactable, Is.False);
        Assert.That(preview.text, Does.Contain("0/"));
    }

    [Test]
    public void RegionChangeAndCombatDisableDispatch()
    {
        Click("MemberClicked", 0);
        Click("RegionClicked", 1);
        presenter.Refresh();
        var region = TableManager.Instance.Gameplay.expeditions[1];
        Assert.That(preview.text, Does.Contain(region.displayName));
        Assert.That(preview.text, Does.Contain($"소요 {region.durationSeconds:0}초"));
        var flow = (SessionFlow)typeof(GameSession).GetField("flow", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
        flow.Advance(); flow.Advance();
        presenter.Refresh();
        Assert.That(dispatch.interactable, Is.False);
    }

    [Test]
    public void SimultaneousDeathAndReturnNoticesStayQueued()
    {
        var alertView = host.AddComponent<AlertBannerView>();
        var alertText = Label(); var noticeText = Label();
        Set(alertView, "alertRoot", alertText.gameObject); Set(alertView, "alertText", alertText);
        Set(alertView, "noticeRoot", noticeText.gameObject); Set(alertView, "noticeText", noticeText);
        var alert = new AlertPresenter(alertView, session);
        alert.OnSurvivorDied(new GameEvents.SurvivorDeathInfo(states[0].Id, GameSession.CAUSE_EXPEDITION));
        alert.OnExpeditionResolved(new GameEvents.ExpeditionResultInfo(TableManager.Instance.Gameplay.expeditions[0].id, 0, new[] { states[0].Id }, new[] { states[1].Id }));
        alert.Refresh();
        Assert.That(noticeText.text, Does.Contain(states[0].DisplayName + " 탐사 중 사망"));
        Set(alert, "noticeEndTime", -1f);
        alert.Refresh();
        Assert.That(noticeText.text, Does.Contain("빈손 귀환"));
        Assert.That(noticeText.text, Does.Contain(states[1].DisplayName));
    }

    [TestCase(70, 100, "이번 준비 중")]
    [TestCase(150, 100, "이번 경보·전투 중")]
    public void ReturnEstimateComparesRemainingPrep(float _duration, float _remaining, string _expected)
    {
        var timing = TableManager.Instance.Gameplay.session;
        Assert.That(ExpeditionLabels.ReturnTiming(_duration, SessionPhase.Prep, _remaining, timing.alertSeconds, timing.combatSeconds), Does.Contain(_expected));
    }

    [Test]
    public void SingleMemberShowsActualPayoutChance()
    {
        Click("MemberClicked", 0);
        presenter.Refresh();
        var region = TableManager.Instance.Gameplay.expeditions[0];
        float payoutChance = session.Expeditions.RewardChance(region.id, states[0].Exploration)
            * (1 - session.Expeditions.DeathChance(region.id, states[0].Exploration, 1));
        Assert.That(preview.text, Does.Contain($"지급 확률 {payoutChance:P1}"));
        Click("MemberClicked", 1);
        presenter.Refresh();
        float rewardChance = session.Expeditions.RewardChance(region.id, states[0].Exploration + states[1].Exploration);
        Assert.That(preview.text, Does.Contain($"지급 확률 {rewardChance:P1}"));
    }

    [Test]
    public void ReturnEstimateUsesFinalTableBoundaries()
    {
        var table = TableManager.Instance.Gameplay;
        foreach (var region in table.expeditions)
        {
            float remaining = region.durationSeconds - table.session.alertSeconds - table.session.combatSeconds;
            if (remaining < 0)
            {
                continue;
            }
            Assert.That(ExpeditionLabels.ReturnTiming(region.durationSeconds, SessionPhase.Prep, remaining,
                table.session.alertSeconds, table.session.combatSeconds), Does.Contain("이번 경보·전투 중"));
            Assert.That(ExpeditionLabels.ReturnTiming(region.durationSeconds, SessionPhase.Prep, remaining - 0.01f,
                table.session.alertSeconds, table.session.combatSeconds), Does.Contain("다음 준비 이후"));
            Assert.That(ExpeditionLabels.ReturnTiming(region.durationSeconds, SessionPhase.Prep, region.durationSeconds,
                table.session.alertSeconds, table.session.combatSeconds), Does.Contain("이번 준비 중"));
        }
    }
}
