using UnityEngine;

// 경보 카운트다운과 사망 알림.
public sealed class AlertPresenter
{
    readonly AlertBannerView view;
    readonly GameSession session;
    float noticeEndTime;
    string notice = string.Empty;

    public AlertPresenter(AlertBannerView _view, GameSession _session)
    {
        view = _view;
        session = _session;
    }

    public void OnSurvivorDied(GameEvents.SurvivorDeathInfo _info)
    {
        SurvivorUnit unit = session.FindUnit(_info.survivorId);
        notice = unit != null ? $"{unit.State.DisplayName} 사망" : "생존자 사망";
        noticeEndTime = Time.time + view.NoticeSeconds;
    }

    public void Refresh()
    {
        bool alert = session.Phase == SessionPhase.Alert;
        view.ShowAlert(alert, alert ? $"공습 경보! 방어 위치로 이동 — {Mathf.CeilToInt(session.PhaseRemaining)}초" : string.Empty);
        bool hasNotice = Time.time < noticeEndTime;
        view.ShowNotice(hasNotice, hasNotice ? notice : string.Empty);
    }
}
