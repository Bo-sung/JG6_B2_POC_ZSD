using System;
using System.Collections.Generic;

public readonly struct ExpeditionMember
{
    public readonly string id;
    // 부상·특성 효율을 반영한 실효값이다. 파티 합은 TotalExploration으로만 구한다.
    public readonly float exploration;

    public ExpeditionMember(string _id, float _exploration)
    {
        id = _id;
        exploration = _exploration;
    }
}

public sealed class ActiveExpedition
{
    public string regionId { get; }
    public IReadOnlyList<string> party { get; }
    public float remainingSeconds { get; internal set; }
    internal int Exploration { get; }

    internal ActiveExpedition(ExpeditionRegionData _region, ExpeditionMember[] _members)
    {
        regionId = _region.id;
        remainingSeconds = _region.durationSeconds;
        var ids = new string[_members.Length];
        for (int index = 0; index < _members.Length; index++)
        {
            ids[index] = _members[index].id;
        }
        Exploration = ExpeditionSystem.TotalExploration(_members);
        party = Array.AsReadOnly(ids);
    }
}

public readonly struct ExpeditionOutcome
{
    public readonly string regionId;
    public readonly int reward;
    public readonly string[] casualties;
    public readonly string[] returned;

    public ExpeditionOutcome(string _regionId, int _reward, string[] _casualties, string[] _returned)
    {
        regionId = _regionId;
        reward = _reward;
        casualties = _casualties;
        returned = _returned;
    }
}

public sealed class ExpeditionSystem
{
    readonly Dictionary<string, ExpeditionRegionData> regions = new Dictionary<string, ExpeditionRegionData>(StringComparer.Ordinal);
    readonly List<ActiveExpedition> active = new List<ActiveExpedition>();
    readonly Random random;
    public IReadOnlyList<ActiveExpedition> Active { get; }

    public ExpeditionSystem(ExpeditionRegionData[] _regions, Random _random)
    {
        if (_regions == null)
        {
            throw new ArgumentNullException(nameof(_regions));
        }
        random = _random ?? throw new ArgumentNullException(nameof(_random));
        foreach (ExpeditionRegionData region in _regions)
        {
            regions.Add(region.id, region);
        }
        Active = active.AsReadOnly();
    }

    // 실효 탐사의 합을 반올림한 파티 탐사 합(.5는 올림). 미리보기와 실제 판정이 이 값 하나를 쓴다.
    public static int TotalExploration(IReadOnlyList<ExpeditionMember> _members)
    {
        double total = 0;
        foreach (ExpeditionMember member in _members)
        {
            total += member.exploration;
        }
        return checked((int)Math.Round(total, MidpointRounding.AwayFromZero));
    }

    public bool TryStart(string _regionId, ExpeditionMember[] _members, out string _error)
    {
        if (_regionId == null || !regions.TryGetValue(_regionId, out ExpeditionRegionData region))
        {
            _error = "알 수 없는 탐사 지역입니다.";
            return false;
        }
        if (_members == null || _members.Length < region.minParty || _members.Length > region.maxParty)
        {
            _error = "파견 인원 범위를 벗어났습니다.";
            return false;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (ExpeditionMember member in _members)
        {
            if (string.IsNullOrEmpty(member.id) || member.exploration < 0 || !ids.Add(member.id))
            {
                _error = "유효하지 않거나 중복된 인원입니다.";
                return false;
            }
            foreach (ActiveExpedition expedition in active)
            {
                foreach (string id in expedition.party)
                {
                    if (id == member.id)
                    {
                        _error = "이미 파견 중인 인원입니다.";
                        return false;
                    }
                }
            }
        }
        active.Add(new ActiveExpedition(region, _members));
        _error = null;
        return true;
    }

    public List<ExpeditionOutcome> Tick(float _deltaSeconds)
    {
        if (_deltaSeconds < 0 || float.IsNaN(_deltaSeconds) || float.IsInfinity(_deltaSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(_deltaSeconds));
        }
        var outcomes = new List<ExpeditionOutcome>();
        // 시작 순서를 유지해 같은 틱에 끝나는 파견의 난수 소비도 재현한다.
        for (int index = 0; index < active.Count;)
        {
            ActiveExpedition expedition = active[index];
            expedition.remainingSeconds = Math.Max(0, expedition.remainingSeconds - _deltaSeconds);
            if (expedition.remainingSeconds > 0)
            {
                index++;
                continue;
            }
            ExpeditionRegionData region = regions[expedition.regionId];
            bool rewarded = random.NextDouble() < RewardChance(region.id, expedition.Exploration);
            bool died = random.NextDouble() < DeathChance(region.id, expedition.Exploration, expedition.party.Count);
            // 사망 여부와 무관하게 난수를 3회 소비한다. 같은 시드 비교에서 뒤따르는 파견의 결과가 갈리지 않게 한다.
            int victim = random.Next(expedition.party.Count);
            int casualty = died ? victim : -1;
            var returned = new List<string>();
            var casualties = new List<string>();
            for (int memberIndex = 0; memberIndex < expedition.party.Count; memberIndex++)
            {
                if (memberIndex == casualty)
                {
                    casualties.Add(expedition.party[memberIndex]);
                }
                else
                {
                    returned.Add(expedition.party[memberIndex]);
                }
            }
            int reward = rewarded && returned.Count > 0 ? ExpectedReward(region.id, expedition.Exploration) : 0;
            outcomes.Add(new ExpeditionOutcome(region.id, reward, casualties.ToArray(), returned.ToArray()));
            active.RemoveAt(index);
        }
        return outcomes;
    }

    public float RewardChance(string _regionId, int _totalExploration)
    {
        ExpeditionRegionData region = regions[_regionId];
        return ClampChance(region.rewardChanceBase + region.rewardChancePerExploration * _totalExploration);
    }

    public float DeathChance(string _regionId, int _totalExploration, int _partyCount)
    {
        ExpeditionRegionData region = regions[_regionId];
        return ClampChance(region.deathChanceBase + region.deathChancePerExploration * _totalExploration + region.deathChancePerExtraMember * (_partyCount - 1));
    }

    // UI에는 보상 성공 시 지급량을 표시한다. 중간값은 양수에서 올림한다.
    public int ExpectedReward(string _regionId, int _totalExploration)
    {
        ExpeditionRegionData region = regions[_regionId];
        return Math.Max(0, checked((int)Math.Round(region.rewardBase + region.rewardPerExploration * _totalExploration, MidpointRounding.AwayFromZero)));
    }

    static float ClampChance(float _chance)
    {
        return Math.Max(0, Math.Min(1, _chance));
    }
}
