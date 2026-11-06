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

    public static string Status(SurvivorState _state)
    {
        if (_state.IsCritical && !_state.IsAway)
        {
            return "중상";
        }
        string activity = _state.Activity switch
        {
            SurvivorActivity.Working => "작업",
            SurvivorActivity.Guarding => "경비",
            SurvivorActivity.ManualMove => "명령",
            SurvivorActivity.ManualAttack => "명령",
            SurvivorActivity.Treating => "치료",
            SurvivorActivity.Away => "탐사",
            _ => "사망",
        };
        return _state.Injury == InjuryStatus.Light && !_state.IsAway ? "경상·" + activity : activity;
    }

    public static string Injury(InjuryStatus _status) => _status switch
    {
        InjuryStatus.Normal => "정상",
        InjuryStatus.Light => "경상",
        InjuryStatus.Critical => "중상",
        _ => "사망",
    };

    // 선택 패널의 상태 표기. 예: '경상 · 효율 75%', '중상 · 치료 대기'.
    public static string Condition(SurvivorState _state, bool _treating)
    {
        string injury = Injury(_state.Injury);
        switch (_state.Injury)
        {
            case InjuryStatus.Light:
                injury += $" · 효율 {(int)System.Math.Round(_state.WorkEfficiency * 100)}%";
                break;
            case InjuryStatus.Critical:
                return injury + (_treating ? " · 치료 중" : " · 치료 대기");
        }
        return _treating ? injury + " · 치료 중" : injury;
    }

    public static string TreatmentReason(string _reason) => _reason switch
    {
        TreatmentSystem.REASON_NOT_PREP => "치료는 준비 단계에만 시작할 수 있습니다.",
        TreatmentSystem.REASON_BUSY => "이미 진행 중이거나 일시정지된 치료가 있습니다.",
        TreatmentSystem.REASON_NO_WORKER => "담당자를 선택하세요.",
        TreatmentSystem.REASON_WORKER_UNAVAILABLE => "담당자가 행동할 수 없습니다(사망·파견·중상).",
        TreatmentSystem.REASON_NO_PATIENT => "환자를 선택하세요.",
        TreatmentSystem.REASON_PATIENT_DEAD => "사망자는 치료할 수 없습니다.",
        TreatmentSystem.REASON_PATIENT_AWAY => "파견 중인 인원은 치료할 수 없습니다.",
        TreatmentSystem.REASON_PATIENT_FULL_HP => "HP가 가득 찬 인원은 치료할 필요가 없습니다.",
        TreatmentSystem.REASON_SELF => "담당자는 자기 자신을 치료할 수 없습니다.",
        TreatmentSystem.REASON_NO_SCRAP => "스크랩이 부족합니다.",
        TreatmentSystem.REASON_NO_JOB => "진행 중인 치료가 없습니다.",
        TreatmentSystem.REASON_HAS_WORKER => "이미 담당자가 있습니다.",
        _ => _reason,
    };

    public static string Work(WorkAssignment _work) => _work == WorkAssignment.Production ? "생산" : "방어 대기";

    // 특성 키의 표시 이름. 키 집합은 GameTableRules.TRAIT_KEYS와 같다. 없으면 '없음'.
    public static string Trait(string _trait) => _trait switch
    {
        "marksman" => "사격 전문",
        "foreman" => "숙련 작업자",
        "scout" => "정찰 전문가",
        _ => "없음",
    };

    // 선택 패널 표시용: 특성 이름과 효과. 없으면 '없음'(V2-IMPL-07, TD-44).
    public static string TraitDescription(string _trait) => _trait switch
    {
        "marksman" => "사격 전문 (전투 효율 ×1.2)",
        "foreman" => "숙련 작업자 (작업 효율 ×1.2)",
        "scout" => "정찰 전문가 (탐사 효율 ×1.2 · 위험 ×0.8)",
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
        if (_unit.State.IsCritical && !_unit.State.IsAway)
        {
            return "쓰러짐 · 치료 대기";
        }
        switch (_unit.State.Activity)
        {
            case SurvivorActivity.Treating:
                return _unit.HasArrived ? "치료소에서 치료 중" : "치료소로 이동 중";
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
