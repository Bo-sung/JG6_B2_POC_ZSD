using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 선택 패널. 작업 배정과 방어 위치를 별도 버튼으로 바꾼다. 여러 명이면 선택 인원 전체에 적용한다.
public sealed class SelectionPanelPresenter
{
    readonly SelectionPanelView view;
    readonly GameSession session;
    readonly RTSInput input;
    string message = string.Empty;

    public SelectionPanelPresenter(SelectionPanelView _view, GameSession _session, RTSInput _input)
    {
        view = _view;
        session = _session;
        input = _input;
        view.ProductionClicked += () => SetWork(WorkAssignment.Production);
        view.StandbyClicked += () => SetWork(WorkAssignment.DefenseStandby);
        view.DefenseClicked += BeginDefensePick;
        view.ReturnClicked += ReturnToAssignment;
        input.Selection.Changed += () => message = string.Empty;
    }

    public void Refresh()
    {
        IReadOnlyList<string> ids = input.Selection.Ids;
        view.SetVisible(ids.Count > 0);
        if (ids.Count == 0)
        {
            return;
        }
        string shown = input.IsPickingDefense ? "맵을 클릭해 방어 위치를 지정하세요. 우클릭으로 취소합니다." : message;
        if (ids.Count == 1)
        {
            ShowSingle(session.FindUnit(ids[0]), shown);
        }
        else
        {
            ShowMultiple(ids, shown);
        }
        view.SetInteractable(session.CanChangeWork, session.CanChangeDefense, session.CanCommand);
    }

    void ShowSingle(SurvivorUnit _unit, string _message)
    {
        SurvivorState state = _unit.State;
        view.Show(
            $"{state.DisplayName}  HP {Mathf.CeilToInt(state.Hp)}/{Mathf.CeilToInt(state.MaxHp)}",
            $"전투 {state.Combat}   작업 {state.Work}   탐사 {state.Exploration}",
            $"작업 배정: {SurvivorLabels.Work(session.Assignments.WorkOf(state.Id))}",
            $"방어 배정: {SurvivorLabels.Defense(session.Assignments.DefenseOf(state.Id))}",
            $"현재 행동: {SurvivorLabels.Action(_unit)}",
            _message);
    }

    void ShowMultiple(IReadOnlyList<string> _ids, string _message)
    {
        var names = new StringBuilder();
        foreach (string id in _ids)
        {
            if (names.Length > 0)
            {
                names.Append(", ");
            }
            names.Append(session.FindUnit(id).State.DisplayName);
        }
        view.Show($"{_ids.Count}명 선택", names.ToString(), "작업 배정: 선택 인원 전체에 적용", "방어 배정: 지점 주위로 펼쳐 적용", "현재 행동: 인원별로 다름", _message);
    }

    void SetWork(WorkAssignment _work)
    {
        message = string.Empty;
        foreach (string id in new List<string>(input.Selection.Ids))
        {
            if (!session.TrySetWork(id, _work, out string error))
            {
                message = error;
            }
        }
    }

    void BeginDefensePick()
    {
        message = string.Empty;
        input.BeginDefensePick(ApplyDefensePoint);
    }

    void ApplyDefensePoint(Vector3 _point)
    {
        var ids = new List<string>(input.Selection.Ids);
        for (int index = 0; index < ids.Count; index++)
        {
            if (!session.TrySetDefensePoint(ids[index], input.SpreadPoint(_point, index, ids.Count), out string error))
            {
                message = error;
            }
        }
    }

    void ReturnToAssignment()
    {
        message = string.Empty;
        foreach (string id in input.Selection.Ids)
        {
            session.ReturnToAssignment(id);
        }
    }
}
