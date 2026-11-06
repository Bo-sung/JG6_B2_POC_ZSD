using System;

// 탐사 사건의 판정 공식 한 곳. Unity에 의존하지 않고 난수 값은 호출 측이 넘긴다.
// 피해·위험 계산이 모두 여기를 거치므로 G4 특성(scout)의 위험 감소는 이 함수들에 곱하면 된다(TD-44).
public static class ExpeditionEventRules
{
    // 살아 있는 파티원 중 피해자를 고른다. 파견 때 뽑은 값이므로 결정 시점과 무관하게 재현된다.
    public static int PickVictim(double _roll, int _aliveCount)
    {
        return Math.Min(_aliveCount - 1, (int)(_roll * _aliveCount));
    }

    public static ExpeditionEventType PickType(ExpeditionRegionData _region, double _roll)
    {
        double point = _roll * (_region.rescueWeight + _region.supplyWeight + _region.encounterWeight);
        if (point < _region.rescueWeight)
        {
            return ExpeditionEventType.Rescue;
        }
        return point < _region.rescueWeight + _region.supplyWeight ? ExpeditionEventType.Supply : ExpeditionEventType.Encounter;
    }

    public static float TriggerSeconds(ExpeditionEventData _data, float _duration, double _roll)
    {
        return _duration * (float)(_data.triggerMinFraction + (_data.triggerMaxFraction - _data.triggerMinFraction) * _roll);
    }

    public static float RescueChance(ExpeditionEventData _data, int _totalExploration)
    {
        return Math.Min(_data.rescue.chanceMax, _data.rescue.chanceBase + _data.rescue.chancePerExploration * _totalExploration);
    }

    // 피해는 모두 최대 HP 대비 비율이다.
    public static float RescueFailureFraction(ExpeditionEventData _data) => _data.rescue.failureDamageFraction;

    public static int SearchBonus(ExpeditionEventData _data, int _baseReward)
    {
        return (int)Math.Ceiling(_baseReward * _data.supply.bonusRewardFraction);
    }

    public static float SearchDamageChance(ExpeditionEventData _data) => _data.supply.damageChance;

    public static float SearchDamageFraction(ExpeditionEventData _data) => _data.supply.damageFraction;

    // 파티 실효 전투 합이 높을수록 피해 비율이 줄고 최소 비율 아래로는 내려가지 않는다(TD-33).
    public static float EncounterDamageFraction(ExpeditionEventData _data, float _totalCombat)
    {
        float fraction = _data.encounter.damageMaxFraction - _data.encounter.damageReductionPerCombat * _totalCombat;
        return Math.Max(_data.encounter.damageMinFraction, Math.Min(_data.encounter.damageMaxFraction, fraction));
    }
}
