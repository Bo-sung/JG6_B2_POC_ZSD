using UnityEngine;

// 생존자 목록 9행. 행을 누르면 선택하고, 사망·파견 인원은 구별해 표시한다.
// 행과 인원의 대응은 세션이 늘어난 뒤에도 유지되므로 클릭 시점의 인원을 본다.
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
        for (int index = 0; index < view.Rows.Count; index++)
        {
            int captured = index;
            view.Rows[index].Clicked += () =>
            {
                if (captured < session.Units.Count)
                {
                    input.SelectFromList(session.Units[captured].State.Id);
                }
            };
        }
    }

    public void Refresh()
    {
        for (int index = 0; index < view.Rows.Count; index++)
        {
            // 합류로 인원이 늘어도 행이 모자라지 않게 하고, 빈 행은 숨긴다.
            if (index >= session.Units.Count)
            {
                view.Rows[index].gameObject.SetActive(false);
                continue;
            }
            view.Rows[index].gameObject.SetActive(true);
            SurvivorState state = session.Units[index].State;
            string name = state.DisplayName + (state.IsNewJoin ? " · 신규" : "") + " · " + SurvivorLabels.Specialty(state.Specialty).Replace(" 특화", "");
            string hp = state.IsAlive ? $"HP {Mathf.CeilToInt(state.Hp)}/{Mathf.CeilToInt(state.MaxHp)}" : "HP 0";
            view.Rows[index].Show(name, hp, (state.IsAway ? $"탐사 {Mathf.CeilToInt(session.ExpeditionRemaining(state.Id))}초" : SurvivorLabels.Status(state)), LookOf(state));
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
