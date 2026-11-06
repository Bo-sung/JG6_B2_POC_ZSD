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

    public ExpeditionPanelPresenter(ExpeditionPanelView _view, GameSession _session)
    {
        view = _view;
        session = _session;
        regions = TableManager.Instance.Gameplay.expeditions;
        view.RegionClicked += SelectRegion;
        view.MemberClicked += Toggle;
        view.DispatchClicked += Dispatch;
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
        if (!session.FindUnit(id).State.CanAct)
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

    public void Refresh()
    {
        selected.RemoveAll(_id => !session.FindUnit(_id).State.CanAct);
        ExpeditionRegionData region = regions[regionIndex];
        int exploration = 0;
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
            if (chosen)
            {
                exploration += state.Exploration;
            }
            string duty = !state.IsAlive ? "사망" : state.IsAway ? $"탐사 {Mathf.CeilToInt(session.ExpeditionRemaining(state.Id))}초" : SurvivorLabels.Work(session.Assignments.WorkOf(state.Id));
            view.ShowMember(index, $"{(chosen ? "[선택]" : "[  ]")} {state.DisplayName}{(state.IsNewJoin ? " · 신규" : "")} · 탐사 {state.Exploration} · {duty}", state.CanAct);
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
        var active = new StringBuilder("진행 중 탐사 · 휠로 목록 이동");
        foreach (ActiveExpedition expedition in session.Expeditions.Active)
        {
            active.Append($"\n{session.RegionName(expedition.regionId)} · {expedition.party.Count}명 · {Mathf.CeilToInt(expedition.remainingSeconds)}초");
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
    }
}
