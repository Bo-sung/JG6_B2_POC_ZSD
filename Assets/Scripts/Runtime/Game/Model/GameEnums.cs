// POC 전체가 공유하는 상태 열거형.

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
