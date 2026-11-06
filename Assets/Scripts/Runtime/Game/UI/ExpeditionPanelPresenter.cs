using System.Collections.Generic;
using System.Text;
using UnityEngine;

public sealed class ExpeditionPanelPresenter
{
    readonly ExpeditionPanelView view;
    readonly GameSession session;
    readonly ExpeditionRegionData[] regions;
    readonly List<string> selected = new List<string>();
    int regionIndex;
    string error;
    int shownEventId;
    string eventError;

    public ExpeditionPanelPresenter(ExpeditionPanelView _view, GameSession _session)
    {
        view = _view;
        session = _session;
        regions = TableManager.Instance.Gameplay.expeditions;
        view.RegionClicked += SelectRegion;
        view.MemberClicked += Toggle;
        view.DispatchClicked += Dispatch;
        view.EventActionClicked += DecideEvent;
    }

    // 버튼은 막지 않는다. 눌러야 권위 경로까지 가고 거부 사유가 표시·기록된다(V2-IMPL-10). 판정은 세션만 한다.
    void DecideEvent(int _index)
    {
        ExpeditionDecision decision = _index == 0 ? ExpeditionDecision.Act : _index == 1 ? ExpeditionDecision.Continue : ExpeditionDecision.Withdraw;
        session.TryResolveExpeditionEvent(shownEventId, decision, out eventError);
    }

    void RefreshEvent()
    {
        ActiveExpedition next = session.Expeditions.NextPendingEvent;
        if (next == null)
        {
            shownEventId = 0;
            eventError = null;
            view.HideEvent();
            return;
        }
        if (shownEventId != next.expeditionId)
        {
            shownEventId = next.expeditionId;
            eventError = null;
        }
        ExpeditionEvent current = next.Event;
        var text = new StringBuilder($"{ExpeditionLabels.EventTitle(current.Type)} · {session.RegionName(next.regionId)} · 남은 {Mathf.CeilToInt(current.TimeoutRemaining)}초");
        int count = session.Expeditions.PendingEventCount;
        if (count > 1)
        {
            text.Append($" · 대기 사건 {count}건");
        }
        text.Append('\n').Append(ExpeditionLabels.EventReason(current.Type, session.RegionName(next.regionId)));
        text.Append("\n파티: ").Append(PartyLine(next));
        text.Append('\n').Append(EventOptions(next));
        view.ShowEvent(text.ToString(), eventError ?? "답이 없으면 임무를 계속합니다.", ActionLabel(current));
    }

    static string ActionLabel(ExpeditionEvent _event)
    {
        if (_event.IsWithdrawOffered)
        {
            return string.Empty;
        }
        return _event.Type == ExpeditionEventType.Rescue ? "구조 시도" : "추가 수색";
    }

    string PartyLine(ActiveExpedition _expedition)
    {
        var parts = new List<string>();
        foreach (string id in session.LivingParty(_expedition))
        {
            SurvivorState state = session.FindUnit(id).State;
            parts.Add($"{state.DisplayName} {Mathf.CeilToInt(state.Hp)}/{Mathf.CeilToInt(state.MaxHp)}({SurvivorLabels.Injury(state.Injury)})");
        }
        return string.Join(", ", parts);
    }

    // 위험·시간·기대 보상. 표시 값은 판정과 같은 규칙 함수에서 읽는다.
    string EventOptions(ActiveExpedition _expedition)
    {
        ExpeditionEventData data = TableManager.Instance.Gameplay.expeditionEvents;
        ExpeditionEvent current = _expedition.Event;
        int remaining = Mathf.CeilToInt(_expedition.remainingSeconds);
        int withdraw = Mathf.CeilToInt(Mathf.Min(_expedition.remainingSeconds, data.withdrawSeconds));
        string keepGoing = $"임무 계속: 귀환 {remaining}초 · 보상 성공 시 {session.BaseRewardOf(_expedition)} 스크랩 · 귀환 사망 {session.ReturnDeathChanceOf(_expedition):P0}";
        string withdrawLine = $"철수: 귀환 {withdraw}초 · 보상과 귀환 사망 판정 없음";
        string act;
        if (current.IsWithdrawOffered)
        {
            act = ExpeditionLabels.EventResult(_expedition, VictimName(current));
        }
        else if (current.Type == ExpeditionEventType.Rescue)
        {
            act = $"구조 시도: 성공 {session.RescueChanceOf(_expedition):P0} · 귀환 +{data.rescue.extraSeconds:0}초 · 실패 시 1명 HP -{data.rescue.failureDamageFraction:P0}";
        }
        else
        {
            act = $"추가 수색: 스크랩 +{session.SearchBonusOf(_expedition)} · 귀환 +{data.supply.extraSeconds:0}초 · {data.supply.damageChance:P0} 확률로 1명 HP -{data.supply.damageFraction:P0}";
        }
        return $"{act}\n{keepGoing}\n{withdrawLine}";
    }

    // 목록은 휠로 넘겨야 하므로 귀환 예정 인력은 제목 줄에도 보여 준다.
    string ActiveHeader()
    {
        var names = new List<string>();
        foreach (ActiveExpedition expedition in session.Expeditions.Active)
        {
            if (expedition.RescuedCandidate != null)
            {
                names.Add(expedition.RescuedCandidate.displayName);
            }
        }
        return names.Count > 0 ? $"진행 중 탐사 · 귀환 예정 인력 {string.Join(", ", names)} · 휠로 이동" : "진행 중 탐사 · 휠로 목록 이동";
    }

    string VictimName(ExpeditionEvent _event)
    {
        SurvivorUnit victim = session.FindUnit(_event.VictimId);
        return victim != null ? victim.State.DisplayName : null;
    }

    void SelectRegion(int _index)
    {
        regionIndex = _index;
        error = null;
    }

    void Toggle(int _index)
    {
        if (_index >= session.Units.Count)
        {
            return;
        }
        string id = session.Units[_index].State.Id;
        if (!CanSelect(session.FindUnit(id).State))
        {
            return;
        }
        error = null;
        if (selected.Remove(id))
        {
            return;
        }
        if (selected.Count >= regions[regionIndex].maxParty)
        {
            error = $"최대 {regions[regionIndex].maxParty}명까지 선택하세요.";
            return;
        }
        selected.Add(id);
    }

    void Dispatch()
    {
        if (session.TryStartExpedition(regions[regionIndex].id, selected, out error))
        {
            selected.Clear();
        }
    }

    // 중상과 치료 중인 인원은 고를 수 없다. 사유는 행에 표시하고 판정은 TryStartExpedition이 한다.
    bool CanSelect(SurvivorState _state) => _state.CanAct && !session.Treatment.Involves(_state.Id);

    string DutyOf(SurvivorState _state)
    {
        if (!_state.IsAlive)
        {
            return "사망";
        }
        if (_state.IsAway)
        {
            return $"탐사 {Mathf.CeilToInt(session.ExpeditionRemaining(_state.Id))}초";
        }
        if (_state.IsCritical)
        {
            return "중상 · 파견 불가";
        }
        return session.Treatment.Involves(_state.Id) ? "치료 중 · 취소 후 파견" : SurvivorLabels.Work(session.Assignments.WorkOf(_state.Id));
    }

    public void Refresh()
    {
        selected.RemoveAll(_id => !CanSelect(session.FindUnit(_id).State));
        ExpeditionRegionData region = regions[regionIndex];
        int exploration = session.PartyExploration(selected);
        for (int index = 0; index < view.MemberCount; index++)
        {
            // 합류로 인원이 늘어도 9행 안에서 표시된다. 빈 행은 숨긴다.
            if (index >= session.Units.Count)
            {
                view.SetMemberVisible(index, false);
                continue;
            }
            view.SetMemberVisible(index, true);
            SurvivorState state = session.Units[index].State;
            bool chosen = selected.Contains(state.Id);
            string stat = state.Injury == InjuryStatus.Light && !state.IsAway ? $"탐사 {state.Exploration}→{state.EffectiveExploration:0.##}(경상)" : $"탐사 {state.Exploration}";
            view.ShowMember(index, $"{(chosen ? "[선택]" : "[  ]")} {state.DisplayName}{(state.IsNewJoin ? " · 신규" : "")} · {stat} · {DutyOf(state)}", CanSelect(state));
        }
        float rewardChance = session.Expeditions.RewardChance(region.id, exploration);
        if (selected.Count == 1)
        {
            rewardChance *= 1 - session.Expeditions.DeathChance(region.id, exploration, selected.Count);
        }
        string prediction = selected.Count > 0
            ? $"위험 {region.riskLabel} · 최대 1명 사망 {session.Expeditions.DeathChance(region.id, exploration, selected.Count):P1}\n보상 성공 시 {session.Expeditions.ExpectedReward(region.id, exploration)} 스크랩 · 지급 확률 {rewardChance:P1}"
            : $"위험 {region.riskLabel} · 인원을 선택하면 확률·보상을 표시합니다.";
        var timing = TableManager.Instance.Gameplay.session;
        string preview = $"{region.displayName} · {selected.Count}/{region.maxParty}명 · 탐사 합 {exploration}\n소요 {region.durationSeconds:0}초 · {ExpeditionLabels.ReturnTiming(region.durationSeconds, session.Phase, session.PhaseRemaining, timing.alertSeconds, timing.combatSeconds)}\n{prediction}";
        bool valid = selected.Count >= region.minParty && selected.Count <= region.maxParty;
        string status = !session.CanDispatch ? "준비 단계에만 파견할 수 있습니다." : !valid ? $"파견 인원 {region.minParty}~{region.maxParty}명을 선택하세요." : error ?? "생산·방어에서 제외 · *조기 종료 시 이월";
        var active = new StringBuilder(ActiveHeader());
        foreach (ActiveExpedition expedition in session.Expeditions.Active)
        {
            string eventNote = expedition.Event != null ? ExpeditionLabels.EventResult(expedition, VictimName(expedition.Event)) : string.Empty;
            active.Append($"\n{session.RegionName(expedition.regionId)} · {session.LivingParty(expedition).Count}명 · {Mathf.CeilToInt(expedition.remainingSeconds)}초{(eventNote.Length > 0 ? " · " + eventNote : "")}");
        }
        if (session.Expeditions.Active.Count == 0)
        {
            active.Append(" · 없음");
        }
        string history = "최근 결과 · 없음";
        if (session.ExpeditionHistory.Count > 0)
        {
            history = "최근 결과\n" + session.DescribeExpedition(session.ExpeditionHistory[session.ExpeditionHistory.Count - 1]);
        }
        view.Show(preview, status, session.CanDispatch && valid, active.ToString(), history);
        RefreshEvent();
    }
}
