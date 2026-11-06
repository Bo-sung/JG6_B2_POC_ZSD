using System;

// 생존자의 현재 행동. ARCHITECTURE.md 생존자 행동표와 같다.
public enum SurvivorActivity
{
    Working,
    Guarding,
    ManualMove,
    ManualAttack,
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
// 사망은 되돌릴 수 없고(INV-02), 파견 중에는 거점 행동을 할 수 없다.
public sealed class SurvivorState
{
    public string Id { get; }
    public string DisplayName { get; }
    public int Combat { get; }
    public int Work { get; }
    public int Exploration { get; }
    public SurvivorSpecialty Specialty { get; }
    public string Trait { get; }
    // 합류 인원은 목록에서 '· 신규'로 구별한다. 초기 인원은 false다.
    public bool IsNewJoin { get; }
    public float MaxHp { get; }
    public float Hp { get; private set; }
    public SurvivorActivity Activity { get; private set; }

    public bool IsAlive => Activity != SurvivorActivity.Dead;
    public bool IsAway => Activity == SurvivorActivity.Away;
    public bool CanAct => IsAlive && !IsAway;

    public SurvivorState(SurvivorData _data, bool _newJoin = false)
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
        Specialty = SpecialtyOf(Combat, Work, Exploration);
        Trait = _data.trait ?? string.Empty;
        IsNewJoin = _newJoin;
        MaxHp = _data.maxHp;
        Hp = _data.maxHp;
        Activity = SurvivorActivity.Guarding;
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

    // 사망자는 회복되지 않는다(DEC-09).
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
