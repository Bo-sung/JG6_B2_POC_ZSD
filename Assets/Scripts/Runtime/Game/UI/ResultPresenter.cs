using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 결과·패배·승리 패널. 결과에서는 다음, 어디서든 재시작할 수 있다.
public sealed class ResultPresenter
{
    const string NONE = "없음";

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
                view.ShowResult(victory ? "승리" : "패배", SessionEndText(victory), null);
                break;
            default:
                view.Close();
                break;
        }
    }

    static string JoinOrNone(IReadOnlyCollection<string> _items) => _items.Count > 0 ? string.Join(", ", _items) : NONE;

    string SummaryText()
    {
        WaveSummary wave = session.LastWave;
        var text = new StringBuilder();
        text.AppendLine($"핵심 시설 {Mathf.CeilToInt(wave.coreHp)}/{Mathf.CeilToInt(session.Core.MaxHp)} · 생존자 {wave.alive}명 · 스크랩 {wave.scrap}");
        text.AppendLine($"부상: 경상 {wave.injuredLight}명 · 중상 {wave.injuredCritical}명");
        text.AppendLine($"사망: {JoinOrNone(wave.deaths)}");
        text.AppendLine($"충원: {JoinOrNone(wave.joined)}");
        text.AppendLine($"성장: {JoinOrNone(wave.growths)}");
        text.AppendLine($"시설 피해: 파괴 {wave.destroyedBuildings}개 · 손상 {wave.damagedBuildings}개");
        if (wave.timedOutEnemies > 0)
        {
            text.AppendLine($"시간 초과: 남은 적 {wave.timedOutEnemies}기로 핵심 시설 피해 {Mathf.CeilToInt(wave.timeoutDamage)}");
        }
        text.AppendLine($"다음 운영 공백: {string.Join(" · ", OperationGaps(wave))}");
        if (wave.expeditions.Length > 0)
        {
            text.AppendLine($"이번 주기 탐사 {wave.expeditions.Length}건");
            foreach (ExpeditionOutcome outcome in wave.expeditions)
            {
                text.AppendLine(session.DescribeExpedition(outcome));
            }
        }
        return text.ToString().TrimEnd();
    }

    // 지금 값으로 말할 수 있는 공백만 적는다. 없으면 '없음' 한 항목이다.
    List<string> OperationGaps(WaveSummary _wave)
    {
        var gaps = new List<string>();
        if (_wave.injuredCritical > 0)
        {
            gaps.Add($"중상 {_wave.injuredCritical}명 대기");
        }
        TreatmentJob job = session.Treatment.Current;
        if (job != null)
        {
            gaps.Add(job.NeedsWorker ? "치료소 담당자 필요" : $"치료 일시정지 {Mathf.FloorToInt(job.Progress * 100)}%");
        }
        int emptyProduction = session.Assignments.MaxWorkers - session.Assignments.ActiveWorkerCount;
        if (emptyProduction > 0)
        {
            gaps.Add($"생산 공백 {emptyProduction}자리");
        }
        if (gaps.Count == 0)
        {
            gaps.Add(NONE);
        }
        return gaps;
    }

    // 승리·패배 공통 세션 결과(기준서 4.5). 누적은 SessionRecord, 남은 값은 지금 상태에서 읽는다.
    string SessionEndText(bool _victory)
    {
        SessionRecord record = session.Record;
        var text = new StringBuilder();
        if (_victory)
        {
            text.AppendLine($"{session.WaveCount}개 웨이브를 모두 막아 냈습니다.");
        }
        else
        {
            string reason = session.Core.IsDestroyed ? "핵심 시설이 파괴되었습니다." : "파견 인력까지 전원 사망했습니다.";
            text.AppendLine($"웨이브 {session.WaveNumber}에서 패배했습니다. {reason}");
        }
        text.AppendLine($"인원: 초기 {record.InitialCount}명 + 추가 {record.JoinedCount}명 − 사망 {record.DeathCount}명 = 최종 {session.AliveCount}명");
        text.AppendLine($"사망자: {JoinOrNone(record.Deaths)}");
        text.AppendLine($"추가 인원: {JoinOrNone(record.Joined)}");
        text.AppendLine($"남은 상태: 핵심 시설 {Mathf.CeilToInt(session.Core.Hp)}/{Mathf.CeilToInt(session.Core.MaxHp)} · 스크랩 {session.Scrap} · 경상 {session.CountInjury(InjuryStatus.Light)}명 · 중상 {session.CountInjury(InjuryStatus.Critical)}명");
        text.AppendLine($"성장 결과: {JoinOrNone(record.GrowthSummary())}");
        text.AppendLine($"방문 기록: {JoinOrNone(record.VisitorLog)}");
        text.AppendLine($"탐사 사건: {JoinOrNone(record.ExpeditionSummary())}");
        return text.ToString().TrimEnd();
    }
}
