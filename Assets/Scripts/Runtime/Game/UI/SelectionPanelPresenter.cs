using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 선택 패널. 작업 배정과 방어 위치를 별도 버튼으로 바꾼다. 여러 명이면 선택 인원 전체에 적용한다.
// 중상자도 내용을 볼 수 있지만 명령 버튼은 동작하지 않고, 권위 경로도 거부한다.
public sealed class SelectionPanelPresenter
{
    readonly SelectionPanelView view;
    readonly GameSession session;
    readonly RTSInput input;
    string message = string.Empty;
    SessionPhase shownPhase;

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
        input.CommandFeedback += _message => message = _message;
    }

    public void Refresh()
    {
        if (shownPhase != session.Phase)
        {
            shownPhase = session.Phase;
            message = string.Empty;
        }
        IReadOnlyList<string> ids = input.Selection.Ids;
        view.SetVisible(ids.Count > 0);
        if (ids.Count == 0)
        {
            return;
        }
        string shown = input.IsPickingDefense ? "지점 또는 감시탑을 클릭하세요. 탑 정원 1명. 우클릭 취소." : string.IsNullOrEmpty(message) ? DefaultHint() : message;
        foreach (string id in ids)
        {
            string blocked = session.FindUnit(id).MovementIssue;
            if (!string.IsNullOrEmpty(blocked))
            {
                shown = blocked + "\n" + shown;
                break;
            }
        }
        if (ids.Count == 1)
        {
            ShowSingle(session.FindUnit(ids[0]), shown);
        }
        else
        {
            ShowMultiple(ids, shown);
        }
        bool actable = false;
        foreach (string id in ids)
        {
            actable |= session.FindUnit(id).State.CanAct;
        }
        view.SetInteractable(session.CanChangeWork && actable, session.CanChangeDefense && actable, session.CanCommand && actable);
    }

    string DefaultHint() => session.Phase == SessionPhase.Prep
        ? "우클릭: 방어 위치 지정 · 감시탑 우클릭: 입장"
        : session.CanCommand ? "우클릭: 이동 · 적 우클릭: 공격(방어 배정 유지)"
        : "결과·종료 단계에서는 배정과 명령을 바꿀 수 없습니다.";

    void ShowSingle(SurvivorUnit _unit, string _message)
    {
        SurvivorState state = _unit.State;
        bool treating = session.IsTreating(state.Id);
        string combatProg = session.Experience != null ? session.Experience.FormatStatProgress("전투", state.Combat, StatType.Combat, state.Id) : $"전투 {state.Combat}";
        string workProg = session.Experience != null ? session.Experience.FormatStatProgress("작업", state.Work, StatType.Work, state.Id) : $"작업 {state.Work}";
        string expProg = session.Experience != null ? session.Experience.FormatStatProgress("탐사", state.Exploration, StatType.Exploration, state.Id) : $"탐사 {state.Exploration}";
        string traitDesc = SurvivorLabels.TraitDescription(state.Trait);
        view.Show(
            $"{state.DisplayName} · {SurvivorLabels.Specialty(state.Specialty)}  HP {Mathf.CeilToInt(state.Hp)}/{Mathf.CeilToInt(state.MaxHp)} · {SurvivorLabels.Condition(state, treating)}",
            $"{combatProg}   {workProg}   {expProg}\n특성: {traitDesc}\n사거리 {_unit.AttackRange:0.0}m · 초당 피해 {_unit.DamagePerSecond:0.#}",
            $"작업 배정: {SurvivorLabels.Work(session.Assignments.WorkOf(state.Id))}",
            $"방어 배정: {SurvivorLabels.Defense(session.Assignments.DefenseOf(state.Id), session.Assignments.IsDefenseAssigned(state.Id))}",
            $"현재 행동: {SurvivorLabels.Action(_unit)}",
            state.IsCritical ? "명령 불가 · 중상은 작업·탐사·방어를 할 수 없습니다. 치료소에서 경상 이상이 되면 다시 명령할 수 있습니다."
            : treating ? "치료 중 · 생산과 파견에 쓸 수 없습니다. 경보가 울리면 방어 위치로 이동합니다.\n" + _message
            : _message);
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
        input.AssignDefense(_point, input.PickedDefenseTower);
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
