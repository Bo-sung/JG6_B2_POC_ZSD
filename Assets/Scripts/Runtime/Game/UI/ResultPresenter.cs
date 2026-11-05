using UnityEngine;

// 결과·패배·승리 패널. 결과에서는 다음, 어디서든 재시작할 수 있다.
public sealed class ResultPresenter
{
    readonly ResultPanelView view;
    readonly GameSession session;
    SessionPhase shownPhase = SessionPhase.Prep;

    public ResultPresenter(ResultPanelView _view, GameSession _session)
    {
        view = _view;
        session = _session;
        view.NextClicked += session.ConfirmResult;
        view.RestartClicked += session.Restart;
    }

    public void Refresh()
    {
        SessionPhase phase = session.Phase;
        if (phase == shownPhase)
        {
            return;
        }
        shownPhase = phase;
        switch (phase)
        {
            case SessionPhase.Result:
                view.ShowResult($"웨이브 {session.WaveNumber} 방어 성공", SummaryText(), session.IsLastWave ? "결과 확인" : "다음 웨이브");
                break;
            case SessionPhase.Ended:
                bool victory = session.Result == SessionResult.Victory;
                view.ShowResult(victory ? "승리" : "패배", victory ? VictoryText() : DefeatText(), null);
                break;
            default:
                view.Close();
                break;
        }
    }

    string SummaryText()
    {
        WaveSummary wave = session.LastWave;
        string deaths = wave.deaths.Length > 0 ? string.Join(", ", wave.deaths) : "없음";
        string body = $"핵심 시설 {Mathf.CeilToInt(wave.coreHp)}/{Mathf.CeilToInt(session.Core.MaxHp)}\n생존자 {wave.alive}명 · 스크랩 {wave.scrap}\n이번 웨이브 사망: {deaths}\n생존자 HP가 전량 회복되었습니다.";
        if (wave.timedOutEnemies > 0)
        {
            body += $"\n시간 초과: 남은 적 {wave.timedOutEnemies}기로 핵심 시설 피해 {Mathf.CeilToInt(wave.timeoutDamage)}";
        }
        return body;
    }

    string VictoryText() => $"{session.WaveCount}개 웨이브를 모두 막아 냈습니다.\n핵심 시설 {Mathf.CeilToInt(session.Core.Hp)}/{Mathf.CeilToInt(session.Core.MaxHp)} · 생존자 {session.AliveCount}명 · 스크랩 {session.Scrap}";

    string DefeatText()
    {
        string reason = session.Core.IsDestroyed ? "핵심 시설이 파괴되었습니다." : "파견 인력까지 전원 사망했습니다.";
        return $"웨이브 {session.WaveNumber}에서 패배했습니다.\n{reason}";
    }
}
