// POC 전체가 공유하는 상태 열거형.

// 능력치 3종. 기준서 4.2를 따른다.
public enum StatType
{
    Combat,
    Work,
    Exploration,
}

public enum SessionPhase
{
    Prep,
    Alert,
    Combat,
    Result,
    Ended,
}

public enum SessionResult
{
    None,
    Victory,
    Defeat,
}

// 평시 작업 배정. 방어 위치와 독립으로 저장한다.
public enum WorkAssignment
{
    DefenseStandby,
    Production,
}

public enum BuildingType
{
    Wall,
    Tower,
}

// HP에서 파생하는 부상 상태. 나빠지는 순서로 둔다. 저장하지 않는다(기준서 4.3).
public enum InjuryStatus
{
    Normal,
    Light,
    Critical,
    Dead,
}

// 탐사 사건 3종(기준서 4.4). 구조와 보급은 선택, 조우는 강제다.
public enum ExpeditionEventType
{
    Rescue,
    Supply,
    Encounter,
}

public enum ExpeditionEventState
{
    Scheduled,
    Pending,
    Resolved,
}

// 사건에 대한 대응. Act는 맥락 행동(구조 시도·추가 수색)이고 Timeout은 미응답으로 끝난 '임무 계속'이다.
public enum ExpeditionDecision
{
    Act,
    Continue,
    Withdraw,
    Timeout,
}
