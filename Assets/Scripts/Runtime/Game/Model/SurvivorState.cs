using System;

// 생존자의 현재 행동. ARCHITECTURE.md 생존자 행동표와 같다.
public enum SurvivorActivity
{
    Working,
    Guarding,
    ManualMove,
    ManualAttack,
    Treating,
    Away,
    Dead,
}

public enum SurvivorSpecialty
{
    General,
    Combat,
    Work,
    Exploration,
}

// 생존자 한 명의 규칙 상태. Unity에 의존하지 않는다.
// 사망은 되돌릴 수 없고(INV-02), 파견 중이거나 중상이면 거점 행동을 할 수 없다.
// 부상 상태는 저장하지 않고 HP에서 파생한다(기준서 4.3).
public sealed class SurvivorState
{
    public string Id { get; }
    public string DisplayName { get; }
    public int Combat { get; private set; }
    public int Work { get; private set; }
    public int Exploration { get; private set; }
    public SurvivorSpecialty Specialty => SpecialtyOf(Combat, Work, Exploration);
    public string Trait { get; }
    // 합류 인원은 목록에서 '· 신규'로 구별한다. 초기 인원은 false다.
    public bool IsNewJoin { get; }
    public float MaxHp { get; }
    public float Hp { get; private set; }
    public SurvivorActivity Activity { get; private set; }
    readonly InjuryData injury;
    readonly float traitMultiplier;

    public bool IsAlive => Activity != SurvivorActivity.Dead;
    public bool IsAway => Activity == SurvivorActivity.Away;
    public InjuryStatus Injury => IsAlive ? InjuryRules.StatusOf(Hp, MaxHp, injury) : InjuryStatus.Dead;
    public bool IsCritical => Injury == InjuryStatus.Critical;
    public bool CanAct => IsAlive && !IsAway && !IsCritical;

    // 효율 곱의 한 곳(V2-IMPL-07). 표시 능력치·사거리·이동 속도에는 쓰지 않는다.
    public float CombatEfficiency => InjuryRules.Efficiency(Injury, injury) * TraitRules.Multiplier(Trait, TraitRules.MARKSMAN, traitMultiplier);
    public float WorkEfficiency => InjuryRules.Efficiency(Injury, injury) * TraitRules.Multiplier(Trait, TraitRules.FOREMAN, traitMultiplier);
    public float ExplorationEfficiency => InjuryRules.Efficiency(Injury, injury) * TraitRules.Multiplier(Trait, TraitRules.SCOUT, traitMultiplier);
    public float EffectiveWork => Work * WorkEfficiency;
    public float EffectiveExploration => Exploration * ExplorationEfficiency;

    // _injury가 없으면 부상 구간 없이 생사만 판정한다. 부상과 무관한 규칙 테스트가 쓰며 세션은 항상 테이블을 넘긴다.
    // _traitMultiplier의 기본값 1은 특성 효과를 끈다는 뜻이다. 세션은 항상 테이블 값을 넘긴다.
    public SurvivorState(SurvivorData _data, bool _newJoin = false, InjuryData _injury = null, float _traitMultiplier = 1)
    {
        if (_data == null)
        {
            throw new ArgumentNullException(nameof(_data));
        }
        Id = _data.id;
        DisplayName = _data.displayName;
        Combat = _data.combat;
        Work = _data.work;
        Exploration = _data.exploration;
        Trait = _data.trait ?? string.Empty;
        IsNewJoin = _newJoin;
        injury = _injury;
        traitMultiplier = _traitMultiplier;
        MaxHp = _data.maxHp;
        Hp = _data.maxHp;
        Activity = SurvivorActivity.Guarding;
    }

    // 능력치 조회의 단일 출처.
    public int StatOf(StatType _stat) => _stat switch
    {
        StatType.Combat => Combat,
        StatType.Work => Work,
        StatType.Exploration => Exploration,
        _ => 0,
    };

    // 능력치 성장의 유일한 진입점. 상한을 넘지 않는다(V2-IMPL-14, TD-40). 성공 시 true.
    public bool ImproveStat(StatType _stat, int _maxStat)
    {
        if (!IsAlive || StatOf(_stat) >= _maxStat)
        {
            return false;
        }
        switch (_stat)
        {
            case StatType.Combat:
                Combat++;
                return true;
            case StatType.Work:
                Work++;
                return true;
            case StatType.Exploration:
                Exploration++;
                return true;
            default:
                return false;
        }
    }

    // 최고 능력치가 하나면 그 분야 특화이고, 같은 최고값이 여러 개면 범용이다.
    public static SurvivorSpecialty SpecialtyOf(int _combat, int _work, int _exploration)
    {
        return _combat > _work && _combat > _exploration ? SurvivorSpecialty.Combat
            : _work > _combat && _work > _exploration ? SurvivorSpecialty.Work
            : _exploration > _combat && _exploration > _work ? SurvivorSpecialty.Exploration : SurvivorSpecialty.General;
    }

    // 이번 호출로 사망했으면 true.
    public bool ApplyDamage(float _amount)
    {
        if (!IsAlive || _amount <= 0)
        {
            return false;
        }
        Hp = Math.Max(0, Hp - _amount);
        if (Hp > 0)
        {
            return false;
        }
        Activity = SurvivorActivity.Dead;
        return true;
    }

    // 이번 호출로 사망했으면 true. 탐사 사망처럼 피해 없이 제외할 때 쓴다.
    public bool Kill()
    {
        if (!IsAlive)
        {
            return false;
        }
        Hp = 0;
        Activity = SurvivorActivity.Dead;
        return true;
    }

    // 치료 완료의 회복. 사망자는 회복되지 않고 최대 HP를 넘지 않는다.
    public void Heal(float _amount)
    {
        if (IsAlive && _amount > 0)
        {
            Hp = Math.Min(MaxHp, Hp + _amount);
        }
    }

    // 디버그 웨이브 재시작 전용. 정규 진행에는 HP 자동 회복이 없다(DEC-09 오버라이드).
    public void RestoreHp()
    {
        if (IsAlive)
        {
            Hp = MaxHp;
        }
    }

    // 사망 또는 파견 상태를 일반 행동으로 덮어쓰지 않는다. 적용되면 true.
    public bool SetActivity(SurvivorActivity _activity)
    {
        if (!CanAct || _activity == SurvivorActivity.Away || _activity == SurvivorActivity.Dead)
        {
            return false;
        }
        Activity = _activity;
        return true;
    }

    public bool BeginAway()
    {
        if (!CanAct)
        {
            return false;
        }
        Activity = SurvivorActivity.Away;
        return true;
    }

    // 귀환 직후 기본 행동은 호출 측이 단계에 맞게 다시 지시한다.
    public bool EndAway()
    {
        if (!IsAway)
        {
            return false;
        }
        Activity = SurvivorActivity.Guarding;
        return true;
    }
}
