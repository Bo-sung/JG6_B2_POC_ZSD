using UnityEngine;
using System.Collections.Generic;

// 경보 카운트다운과 사망 알림.
public sealed class AlertPresenter
{
    readonly AlertBannerView view;
    readonly GameSession session;
    readonly Queue<string> notices = new Queue<string>();
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
        notices.Enqueue(unit != null ? $"{unit.State.DisplayName} {(_info.cause == GameSession.CAUSE_EXPEDITION ? "탐사 중 " : "")}사망" : "생존자 사망");
    }

    public void OnExpeditionResolved(GameEvents.ExpeditionResultInfo _info)
    {
        string prefix = _info.returned.Length == 0 ? "탐사 종료 · " : session.Phase == SessionPhase.Combat ? "전투 중 귀환! " : "탐사 귀환 · ";
        notices.Enqueue(prefix + session.DescribeExpedition(new ExpeditionOutcome(_info.regionId, _info.reward, _info.casualties, _info.returned)));
    }

    public void Refresh()
    {
        bool alert = session.Phase == SessionPhase.Alert;
        view.ShowAlert(alert, alert ? $"공습 경보! 방어 위치로 이동 — {Mathf.CeilToInt(session.PhaseRemaining)}초" : string.Empty);
        if (Time.unscaledTime >= noticeEndTime && notices.Count > 0)
        {
            notice = notices.Dequeue();
            noticeEndTime = Time.unscaledTime + view.NoticeSeconds;
        }
        bool hasNotice = Time.unscaledTime < noticeEndTime;
        view.ShowNotice(hasNotice, hasNotice ? notice : string.Empty);
    }
}
