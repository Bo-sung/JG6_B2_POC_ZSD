using System;
using System.Collections.Generic;

// 특성 키 비교의 한 곳(TD-44, V2-IMPL-07). 배율 값은 테이블에 있고 호출 측이 넘긴다.
public static class TraitRules
{
    public const string MARKSMAN = "marksman";
    public const string FOREMAN = "foreman";
    public const string SCOUT = "scout";

    public static bool HasTrait(string _trait, string _targetTrait)
    {
        return !string.IsNullOrEmpty(_trait) && string.Equals(_trait, _targetTrait, StringComparison.Ordinal);
    }

    public static float Multiplier(string _trait, string _targetTrait, float _multiplier)
    {
        return HasTrait(_trait, _targetTrait) ? _multiplier : 1f;
    }

    public static bool PartyHasTrait(IEnumerable<string> _traits, string _targetTrait)
    {
        if (_traits == null)
        {
            return false;
        }
        foreach (string trait in _traits)
        {
            if (HasTrait(trait, _targetTrait))
            {
                return true;
            }
        }
        return false;
    }

    public static StatType ParseStat(string _name)
    {
        if (Enum.TryParse<StatType>(_name, true, out var result))
        {
            return result;
        }
        throw new ArgumentException($"알 수 없는 능력치 이름: {_name}");
    }
}
