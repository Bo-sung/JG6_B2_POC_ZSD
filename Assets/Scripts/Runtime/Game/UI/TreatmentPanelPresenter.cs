using UnityEngine;

// 치료소 패널 표시. 판정과 차감은 GameSession의 권위 경로만 한다.
// 시작 버튼을 막지 않는다. 눌러야 권위 경로를 거쳐 거부 사유가 기록된다(방문자 패널과 같다).
public sealed class TreatmentPanelPresenter
{
    readonly TreatmentPanelView view;
    readonly GameSession session;
    string patientId;
    string workerId;
    string message;

    public TreatmentPanelPresenter(TreatmentPanelView _view, GameSession _session)
    {
        view = _view;
        session = _session;
        view.PatientClicked += SelectPatient;
        view.WorkerClicked += SelectWorker;
        view.StartClicked += Begin;
        view.CancelClicked += Cancel;
        view.CloseClicked += view.Close;
    }

    // HUD 버튼과 치료소 클릭이 호출한다.
    public void OpenPanel()
    {
        message = null;
        view.Open();
    }

    void SelectPatient(int _index)
    {
        if (_index < session.Units.Count)
        {
            patientId = session.Units[_index].State.Id;
            message = null;
        }
    }

    void SelectWorker(int _index)
    {
        if (_index < session.Units.Count)
        {
            workerId = session.Units[_index].State.Id;
            message = null;
        }
    }

    void Begin()
    {
        TreatmentJob job = session.Treatment.Current;
        string error;
        bool started = job != null && job.NeedsWorker
            ? session.TryAssignTreatmentWorker(workerId, out error)
            : session.TryStartTreatment(patientId, workerId, out error);
        message = started ? null : error;
    }

    void Cancel()
    {
        session.TryCancelTreatment(out message);
    }

    public void Refresh()
    {
        if (!view.IsOpen)
        {
            return;
        }
        TreatmentJob job = session.Treatment.Current;
        string shownPatient = job != null ? job.Patient.Id : patientId;
        string shownWorker = job != null && job.Worker != null ? job.Worker.Id : workerId;
        for (int index = 0; index < view.RowCount; index++)
        {
            // 수용 상한까지 자리를 보여 준다. 빈 자리는 누를 수 없다.
            if (index >= session.Units.Count)
            {
                view.ShowPatient(index, "빈 자리", false);
                view.ShowWorker(index, "빈 자리", false);
                continue;
            }
            SurvivorState state = session.Units[index].State;
            view.ShowPatient(index, PatientLabel(state, state.Id == shownPatient), true);
            view.ShowWorker(index, WorkerLabel(state, state.Id == shownWorker), true);
        }
        view.Show("치료소", ProgressText(job), InfoText(), string.IsNullOrEmpty(message) ? StatusText(job) : message);
    }

    string PatientLabel(SurvivorState _state, bool _chosen)
    {
        string condition = !_state.IsAlive ? "사망" : _state.IsAway ? "파견 중" : SurvivorLabels.Injury(_state.Injury);
        return $"{Mark(_chosen)} {_state.DisplayName}{(_state.IsNewJoin ? " · 신규" : "")} · HP {Mathf.CeilToInt(_state.Hp)}/{Mathf.CeilToInt(_state.MaxHp)} · {condition}";
    }

    string WorkerLabel(SurvivorState _state, bool _chosen)
    {
        string head = $"{Mark(_chosen)} {_state.DisplayName}{(_state.IsNewJoin ? " · 신규" : "")}";
        if (!_state.CanAct)
        {
            string reason = !_state.IsAlive ? "사망" : _state.IsAway ? "파견 중" : "중상";
            return $"{head} · 작업 {_state.Work} · 불가({reason})";
        }
        string work = _state.Injury == InjuryStatus.Light ? $"작업 {_state.Work}(실효 {_state.EffectiveWork:0.##})" : $"작업 {_state.Work}";
        string duty = session.Assignments.WorkOf(_state.Id) == WorkAssignment.Production ? " · 생산" : "";
        return $"{head} · {work} · 예상 {session.Treatment.SecondsFor(_state):0.0}초{duty}";
    }

    static string Mark(bool _chosen) => _chosen ? "[선택]" : "[  ]";

    string ProgressText(TreatmentJob _job)
    {
        if (_job == null)
        {
            return "진행 중인 치료 없음";
        }
        string progress = $"진행 {Mathf.FloorToInt(_job.Progress * 100)}%";
        if (_job.NeedsWorker)
        {
            return $"담당자 대기 · 환자 {_job.Patient.DisplayName} · {progress} · 새 담당자를 고르고 시작(추가 비용 없음)";
        }
        string pair = $"환자 {_job.Patient.DisplayName} · 담당 {_job.Worker.DisplayName}";
        if (_job.IsPaused)
        {
            return $"일시정지 · {pair} · {progress} · 다음 준비에 재개(추가 비용 없음)";
        }
        string arrival = session.FindUnit(_job.Worker.Id).AtTreatmentSlot ? $"남은 {session.Treatment.RemainingSeconds:0.0}초" : "담당자 이동 중";
        return $"치료 중 · {pair} · {progress} · {arrival}";
    }

    string InfoText()
    {
        TreatmentSystem treatment = session.Treatment;
        return $"비용 스크랩 {treatment.Cost} (보유 {session.Scrap}) · 완료 시 HP +{Mathf.RoundToInt(treatment.HealFraction * 100)}%p · 동시에 1건 · 준비 단계에만 시작\n담당자와 환자는 치료가 끝나거나 취소될 때까지 생산·파견에 쓸 수 없습니다.";
    }

    // 지금 상태의 사유를 보여 준다. 판정 자체는 버튼을 눌렀을 때 권위 경로가 한다.
    string StatusText(TreatmentJob _job)
    {
        TreatmentSystem treatment = session.Treatment;
        SurvivorState worker = StateOf(workerId);
        if (_job == null)
        {
            string reason = treatment.CheckStart(session.Phase, StateOf(patientId), worker, session.Scrap);
            return reason == null ? "시작할 수 있습니다." : SurvivorLabels.TreatmentReason(reason);
        }
        if (_job.NeedsWorker)
        {
            string reason = treatment.CheckAssign(session.Phase, worker);
            return reason == null ? "시작하면 추가 비용 없이 이어서 치료합니다." : SurvivorLabels.TreatmentReason(reason);
        }
        return "취소하면 스크랩을 돌려받지 못합니다.";
    }

    SurvivorState StateOf(string _id)
    {
        SurvivorUnit unit = session.FindUnit(_id);
        return unit != null ? unit.State : null;
    }
}
