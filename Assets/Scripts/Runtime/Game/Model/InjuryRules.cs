// 부상 상태와 효율의 순수 규칙. 상태는 HP에서 파생하고, 수치는 InjuryData에서 읽는다.
public static class InjuryRules
{
    // 부동소수 오차로 정확히 70%·30%가 아래 구간으로 떨어지지 않게 하는 비교 여유. 밸런스 수치가 아니다.
    const double BOUNDARY_EPSILON = 1e-5;

    // 경계값은 위 구간에 속한다. 정확히 70%는 정상, 정확히 30%는 경상이다.
    public static InjuryStatus StatusOf(float _hp, float _maxHp, InjuryData _data)
    {
        if (_hp <= 0)
        {
            return InjuryStatus.Dead;
        }
        if (_data == null)
        {
            return InjuryStatus.Normal;
        }
        double ratio = (double)_hp / _maxHp;
        if (ratio >= _data.normalMinHpFraction - BOUNDARY_EPSILON)
        {
            return InjuryStatus.Normal;
        }
        return ratio >= _data.lightMinHpFraction - BOUNDARY_EPSILON ? InjuryStatus.Light : InjuryStatus.Critical;
    }

    // 중상·사망은 행동하지 않으므로 효율이 없다. 부상 구간이 없으면(_data가 null) 정상만 나온다.
    public static float Efficiency(InjuryStatus _status, InjuryData _data)
    {
        switch (_status)
        {
            case InjuryStatus.Normal:
                return 1;
            case InjuryStatus.Light:
                return _data.lightEfficiency;
            default:
                return 0;
        }
    }

    public static bool IsWorse(InjuryStatus _before, InjuryStatus _after) => _after > _before;

    // 적의 표적 우선순위(V2-IMPL-02). 행동 가능한 인력이 쓰러진 인력보다 먼저이고, 같은 등급이면 가까운 쪽이다.
    public static bool IsBetterTarget(bool _canAct, float _distance, bool _hasBest, bool _bestCanAct, float _bestDistance)
    {
        if (!_hasBest)
        {
            return true;
        }
        if (_canAct != _bestCanAct)
        {
            return _canAct;
        }
        return _distance < _bestDistance;
    }
}
