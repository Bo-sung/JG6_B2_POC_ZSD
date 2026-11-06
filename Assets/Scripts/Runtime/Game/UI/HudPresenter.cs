using UnityEngine;

// 세션 상태를 상단 HUD 문자열로 옮긴다.
public sealed class HudPresenter
{
    readonly HudView view;
    readonly GameSession session;

    public HudPresenter(HudView _view, GameSession _session)
    {
        view = _view;
        session = _session;
        view.SpeedClicked += _index => session.SetSpeed(session.SpeedOptions[_index]);
    }

    public void Refresh()
    {
        string wave = $"시드 {session.Seed} · 웨이브 {session.WaveNumber}/{session.WaveCount} · {PhaseName(session.Phase)}";
        string countdown = HasTimer(session.Phase) ? $"{Mathf.CeilToInt(session.PhaseRemaining)}초" : "-";
        string scrap = $"스크랩 {session.Scrap}";
        string core = $"핵심 시설 {Mathf.CeilToInt(session.Core.Hp)}/{Mathf.CeilToInt(session.Core.MaxHp)}";
        string headcount = ExpeditionLabels.Headcount(session.AliveCount, session.Capacity, session.AwayCount, session.Recruitment.ReservedSlots);
        string injury = $"부상 {session.CountInjury(InjuryStatus.Light)} · 중상 {session.CountInjury(InjuryStatus.Critical)}";
        // 인원 현재/최대는 파견·중상 인원을 포함한 살아 있는 수 기준이다.
        view.ShowSpeed(session.SpeedOptions, session.Speed);
        view.ShowVisitorNotice(session.Recruitment.PendingVisitor != null);
        view.ShowExpeditionEventNotice(session.Expeditions.PendingEventCount, EventNoticeLabel());
        view.ShowTreatment(TreatmentLabel(session.Treatment.Current));
        view.Show(wave, countdown, scrap, core, headcount, injury);
    }

    // 먼저 만료되는 사건의 남은 초를 함께 보여 준다. 패널을 자동으로 열지 않는다(TD-35).
    string EventNoticeLabel()
    {
        ActiveExpedition next = session.Expeditions.NextPendingEvent;
        return next != null ? $"탐사 사건 {session.Expeditions.PendingEventCount}건 · {Mathf.CeilToInt(next.Event.TimeoutRemaining)}초" : string.Empty;
    }

    static string TreatmentLabel(TreatmentJob _job)
    {
        if (_job == null)
        {
            return "치료소";
        }
        if (_job.NeedsWorker)
        {
            return "치료소 · 담당자 필요";
        }
        return $"치료소 · {(_job.IsPaused ? "일시정지" : "치료 중")} {Mathf.FloorToInt(_job.Progress * 100)}%";
    }

    static bool HasTimer(SessionPhase _phase) => _phase == SessionPhase.Prep || _phase == SessionPhase.Alert || _phase == SessionPhase.Combat;

    static string PhaseName(SessionPhase _phase) => _phase switch
    {
        SessionPhase.Prep => "준비",
        SessionPhase.Alert => "공습 경보",
        SessionPhase.Combat => "전투",
        SessionPhase.Result => "결과",
        _ => "종료",
    };
}
