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
    }

    public void Refresh()
    {
        string wave = $"웨이브 {session.WaveNumber}/{session.WaveCount} · {PhaseName(session.Phase)}";
        string countdown = HasTimer(session.Phase) ? $"{Mathf.CeilToInt(session.PhaseRemaining)}초" : "-";
        string scrap = $"스크랩 {session.Scrap}";
        string core = $"핵심 시설 {Mathf.CeilToInt(session.Core.Hp)}/{Mathf.CeilToInt(session.Core.MaxHp)}";
        int away = session.AwayCount;
        string survivors = away > 0
            ? $"생존자 {session.AliveCount}/{session.Units.Count} (파견 {away})"
            : $"생존자 {session.AliveCount}/{session.Units.Count}";
        view.Show(wave, countdown, scrap, core, survivors);
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
