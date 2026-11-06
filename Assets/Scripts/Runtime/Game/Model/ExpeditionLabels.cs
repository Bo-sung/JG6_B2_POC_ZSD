using System;

// 귀환 예측은 전투가 조기 종료될 수 있음을 명시한다. 밸런스 수치를 복제하지 않는다.
public static class ExpeditionLabels
{
    public static string ReturnTiming(float _duration, SessionPhase _phase, float _remaining, float _alertSeconds, float _combatSeconds)
    {
        if (_phase != SessionPhase.Prep)
        {
            return "준비 단계에서 파견 가능";
        }
        if (_duration <= _remaining)
        {
            return "이번 준비 중 복귀 예상";
        }
        if (_duration > _remaining + _alertSeconds + _combatSeconds)
        {
            return "다음 준비 이후 복귀 예상";
        }
        return "이번 경보·전투 중 복귀 예상*";
    }

    // HUD와 방문 패널의 인원 표기. 상한은 파견·중상·귀환 예정까지 포함한다.
    public static string Headcount(int _alive, int _capacity, int _away, int _reserved)
    {
        var notes = new System.Collections.Generic.List<string>();
        if (_away > 0)
        {
            notes.Add($"파견 {_away}");
        }
        if (_reserved > 0)
        {
            notes.Add($"귀환 예정 {_reserved}");
        }
        string count = $"인원 {_alive}/{_capacity}";
        return notes.Count > 0 ? $"{count} ({string.Join(", ", notes)})" : count;
    }

    public static string EventTitle(ExpeditionEventType _type) => _type switch
    {
        ExpeditionEventType.Rescue => "생존자 발견",
        ExpeditionEventType.Supply => "추가 보급품 발견",
        _ => "적대 생물 조우",
    };

    // 왜 일어났는지. 파견된 지역에서 벌어진 일로 설명한다(기준서 4.4).
    public static string EventReason(ExpeditionEventType _type, string _regionName) => _type switch
    {
        ExpeditionEventType.Rescue => $"{_regionName} 수색 중 고립된 사람을 찾았습니다.",
        ExpeditionEventType.Supply => $"{_regionName} 수색 중 보급품 흔적을 찾았습니다.",
        _ => $"{_regionName}에서 적대 생물과 마주쳐 교전했습니다.",
    };

    // 진행 중 탐사 행에 붙는 사건 결과와 귀환 시간 변화.
    public static string EventResult(ActiveExpedition _expedition, string _victimName)
    {
        ExpeditionEvent current = _expedition.Event;
        if (current == null || current.State != ExpeditionEventState.Resolved)
        {
            return string.Empty;
        }
        string hurt = _victimName != null ? $" · {_victimName} HP -{Math.Ceiling(current.Damage)}" : string.Empty;
        string added = current.AddedSeconds > 0 ? $" · +{current.AddedSeconds:0}초" : string.Empty;
        string withdrawn = _expedition.IsWithdrawn ? " · 철수" : string.Empty;
        switch (current.Outcome)
        {
            case ExpeditionEvent.OUTCOME_RESCUE_SUCCESS:
                return $"구조 성공{added} · 귀환 예정 인력 {_expedition.RescuedCandidate.displayName}";
            case ExpeditionEvent.OUTCOME_RESCUE_FAILED:
                return $"구조 실패{added}{hurt}";
            case ExpeditionEvent.OUTCOME_SEARCH_SAFE:
            case ExpeditionEvent.OUTCOME_SEARCH_HURT:
                return $"추가 수색{added} · 스크랩 +{_expedition.BonusReward}{hurt}";
            case ExpeditionEvent.OUTCOME_ENCOUNTER:
                return $"조우{added}{hurt}{withdrawn}";
            case ExpeditionEvent.OUTCOME_WITHDRAWN:
                return "철수 · 보상 없음";
            default:
                return "임무 계속";
        }
    }
}
