using System.Globalization;

// 생존자 표시 문구의 단일 출처. 목록과 선택 패널이 같은 표현을 쓴다.
public static class SurvivorLabels
{
    public static string Specialty(SurvivorSpecialty _specialty) => _specialty switch
    {
        SurvivorSpecialty.Combat => "전투 특화",
        SurvivorSpecialty.Work => "작업 특화",
        SurvivorSpecialty.Exploration => "탐사 특화",
        _ => "범용",
    };

    public static string Status(SurvivorState _state) => _state.Activity switch
    {
        SurvivorActivity.Working => "작업",
        SurvivorActivity.Guarding => "경비",
        SurvivorActivity.ManualMove => "명령",
        SurvivorActivity.ManualAttack => "명령",
        SurvivorActivity.Away => "탐사",
        _ => "사망",
    };

    public static string Work(WorkAssignment _work) => _work == WorkAssignment.Production ? "생산" : "방어 대기";

    // 특성 키의 표시 이름. 키 집합은 GameTableRules.TRAIT_KEYS와 같다. 효과는 G4에서 붙는다.
    public static string Trait(string _trait) => _trait switch
    {
        "marksman" => "사격 전문",
        "foreman" => "숙련 작업자",
        "scout" => "정찰 전문가",
        _ => "없음",
    };

    public static string Defense(DefenseAssignment _defense, bool _assigned = true)
    {
        if (!_assigned)
        {
            return "방어 위치 미지정";
        }
        string point = string.Format(CultureInfo.InvariantCulture, "({0:0}, {1:0})", _defense.x, _defense.z);
        return _defense.IsTower ? "감시탑 " + point : "지점 " + point;
    }

    public static string Action(SurvivorUnit _unit)
    {
        switch (_unit.State.Activity)
        {
            case SurvivorActivity.Working:
                return _unit.HasArrived ? "생산 작업 중" : "작업장으로 이동 중";
            case SurvivorActivity.Guarding:
                if (_unit.IsHeadingToTower)
                {
                    return _unit.OccupiedTower ? (_unit.IsFiring ? "탑 위 교전 중" : "탑 위 대기") : "탑으로 이동 중";
                }
                if (_unit.IsFiring)
                {
                    return "교전 중";
                }
                return _unit.HasArrived ? "방어 위치 대기" : "방어 위치로 이동 중";
            case SurvivorActivity.ManualMove:
                if (_unit.IsFiring)
                {
                    return "교전 중(직접 명령)";
                }
                return _unit.HasArrived ? "지정 위치 대기(직접 명령)" : "지정 위치로 이동 중(직접 명령)";
            case SurvivorActivity.ManualAttack:
                return "지정 대상 공격 중(직접 명령)";
            case SurvivorActivity.Away:
                return "탐사 중";
            default:
                return "사망";
        }
    }
}
