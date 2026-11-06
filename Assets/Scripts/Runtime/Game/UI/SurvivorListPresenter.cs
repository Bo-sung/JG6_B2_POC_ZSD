using UnityEngine;

// 생존자 목록 7행. 행을 누르면 선택하고, 사망·파견 인원은 구분해 표시한다.
public sealed class SurvivorListPresenter
{
    readonly SurvivorListView view;
    readonly GameSession session;
    readonly RTSInput input;

    public SurvivorListPresenter(SurvivorListView _view, GameSession _session, RTSInput _input)
    {
        view = _view;
        session = _session;
        input = _input;
        for (int index = 0; index < view.Rows.Count && index < session.Units.Count; index++)
        {
            string id = session.Units[index].State.Id;
            view.Rows[index].Clicked += () => input.SelectFromList(id);
        }
    }

    public void Refresh()
    {
        for (int index = 0; index < view.Rows.Count && index < session.Units.Count; index++)
        {
            SurvivorState state = session.Units[index].State;
            string hp = state.IsAlive ? $"HP {Mathf.CeilToInt(state.Hp)}/{Mathf.CeilToInt(state.MaxHp)}" : "HP 0";
            view.Rows[index].Show(state.DisplayName + " · " + SurvivorLabels.Specialty(state.Specialty).Replace(" 특화", ""), hp, (state.IsAway ? $"탐사 {Mathf.CeilToInt(session.ExpeditionRemaining(state.Id))}초" : SurvivorLabels.Status(state)), LookOf(state));
        }
    }

    SurvivorRowView.Look LookOf(SurvivorState _state)
    {
        if (!_state.IsAlive)
        {
            return SurvivorRowView.Look.Dead;
        }
        if (_state.IsAway)
        {
            return SurvivorRowView.Look.Away;
        }
        return input.Selection.Contains(_state.Id) ? SurvivorRowView.Look.Selected : SurvivorRowView.Look.Normal;
    }
}
