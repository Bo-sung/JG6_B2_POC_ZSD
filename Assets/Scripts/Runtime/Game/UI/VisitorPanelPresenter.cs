// 방문자 패널 표시. 판정과 차감은 GameSession.TryRecruitVisitor 하나만 한다.
// 영입 버튼을 막지 않는다. 눌러야 권위 경로를 거쳐 no_scrap·full 거부가 기록된다.
public sealed class VisitorPanelPresenter
{
    readonly VisitorPanelView view;
    readonly GameSession session;

    public VisitorPanelPresenter(VisitorPanelView _view, GameSession _session)
    {
        view = _view;
        session = _session;
        view.AcceptClicked += Accept;
        view.DeclineClicked += Decline;
        view.CloseClicked += view.Close;
    }

    // HUD의 '방문자 도착' 항목이 호출한다. 대기 중인 방문자가 있을 때만 연다.
    public void OpenPanel()
    {
        if (session.Recruitment.PendingVisitor == null)
        {
            return;
        }
        view.Open();
    }

    void Accept()
    {
        if (session.TryRecruitVisitor(out _))
        {
            view.Close();
        }
    }

    void Decline()
    {
        session.DeclineVisitor();
        view.Close();
    }

    public void Refresh()
    {
        VisitorRecord visitor = session.Recruitment.PendingVisitor;
        if (visitor == null)
        {
            // 영입·거절·만료로 방문자가 사라지면 열려 있던 패널도 닫는다.
            if (view.IsOpen)
            {
                view.Close();
            }
            return;
        }
        if (!view.IsOpen)
        {
            return;
        }
        SurvivorData candidate = visitor.Candidate;
        string title = $"방문자 도착 · 웨이브 {visitor.Wave}";
        SurvivorSpecialty specialty = SurvivorState.SpecialtyOf(candidate.combat, candidate.work, candidate.exploration);
        string info = $"{candidate.displayName} · {SurvivorLabels.Specialty(specialty)} · 신규\n"
            + $"전투 {candidate.combat} · 작업 {candidate.work} · 탐사 {candidate.exploration}\n"
            + $"특성: {SurvivorLabels.Trait(candidate.trait)}\n"
            + $"영입 비용: 스크랩 {session.Recruitment.VisitorCost}\n"
            + $"현재 {ExpeditionLabels.Headcount(session.AliveCount, session.Recruitment.Capacity, 0, session.Recruitment.ReservedSlots)}";
        view.Show(title, info, StatusOf());
    }

    // 지금 상태의 사유를 보여 준다. 스크랩이 모이면 문구도 바로 바뀐다. 판정 자체는 TryRecruitVisitor가 한다.
    string StatusOf()
    {
        if (!session.Recruitment.HasCapacity(session.AliveCount))
        {
            return "수용 정원이 찼습니다. 준비가 끝나면 만료됩니다.";
        }
        if (!session.Economy.CanAfford(session.Recruitment.VisitorCost))
        {
            return "스크랩이 부족합니다.";
        }
        return "영입하면 합류 지점에서 방어 대기로 시작합니다.";
    }
}
