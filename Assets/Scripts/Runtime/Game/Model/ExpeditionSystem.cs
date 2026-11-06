using System;
using System.Collections.Generic;

public readonly struct ExpeditionMember
{
    public readonly string id;
    // 부상·특성 효율을 반영한 실효값이다. 파티 합은 TotalExploration으로만 구한다.
    public readonly float exploration;
    public readonly bool isScout;

    public ExpeditionMember(string _id, float _exploration, bool _isScout = false)
    {
        id = _id;
        exploration = _exploration;
        isScout = _isScout;
    }
}

// 탐사 하나의 사건. 파견 시작 때 정해지고(TD-31, V2-IMPL-10) 대기 → 해결 상태를 가진다.
// 판정에 쓸 난수도 그때 뽑아 두므로 플레이어의 결정이 다른 탐사의 사건을 바꾸지 않는다.
public sealed class ExpeditionEvent
{
    public const string OUTCOME_RESCUE_SUCCESS = "rescue_success";
    public const string OUTCOME_RESCUE_FAILED = "rescue_failed";
    public const string OUTCOME_SEARCH_SAFE = "search_safe";
    public const string OUTCOME_SEARCH_HURT = "search_hurt";
    public const string OUTCOME_ENCOUNTER = "encounter";
    public const string OUTCOME_CONTINUED = "continued";
    public const string OUTCOME_WITHDRAWN = "withdrawn";

    public ExpeditionEventType Type { get; internal set; }
    public ExpeditionEventState State { get; internal set; } = ExpeditionEventState.Scheduled;
    public float TriggerSeconds { get; internal set; }
    public float TimeoutRemaining { get; internal set; }
    // 강제 조우가 끝난 뒤 '철수'를 고를 수 있는 동안 true다(TD-34). 사건 자체는 이미 해결됐다.
    public bool IsWithdrawOffered { get; internal set; }
    public string Outcome { get; internal set; }
    public string VictimId { get; internal set; }
    public float Damage { get; internal set; }
    public float AddedSeconds { get; internal set; }
    internal double OutcomeRoll { get; set; }
    internal double VictimRoll { get; set; }

    // 플레이어의 응답을 기다리는 상태. HUD 경고와 대기 사건 수가 이 값을 본다.
    public bool IsAwaitingDecision => State == ExpeditionEventState.Pending || IsWithdrawOffered;
}

public sealed class ActiveExpedition
{
    public int expeditionId { get; }
    public string regionId { get; }
    public IReadOnlyList<string> party { get; }
    public float remainingSeconds { get; internal set; }
    public float elapsedSeconds { get; internal set; }
    // 최대 1건. 발생하지 않으면 null이다.
    public ExpeditionEvent Event { get; internal set; }
    // 정산 완료 플래그. 정산은 탐사마다 정확히 한 번이다(V2-IMPL-13).
    public bool IsSettled { get; internal set; }
    public bool IsWithdrawn { get; internal set; }
    public int BonusReward { get; internal set; }
    // 구조 성공으로 확보한 후보. 자리 예약과 짝이고 귀환 정산 때만 합류한다(V2-IMPL-11).
    public SurvivorData RescuedCandidate { get; internal set; }
    public bool HadEncounter { get; internal set; }
    public bool RespondedToEvent { get; internal set; }
    internal int Exploration { get; }
    readonly ExpeditionMember[] members;
    public IReadOnlyList<ExpeditionMember> Members => members;

    internal ActiveExpedition(int _expeditionId, ExpeditionRegionData _region, ExpeditionMember[] _members)
    {
        expeditionId = _expeditionId;
        regionId = _region.id;
        remainingSeconds = _region.durationSeconds;
        var ids = new string[_members.Length];
        for (int index = 0; index < _members.Length; index++)
        {
            ids[index] = _members[index].id;
        }
        members = (ExpeditionMember[])_members.Clone();
        Exploration = ExpeditionSystem.TotalExploration(_members);
        party = Array.AsReadOnly(ids);
    }

    // 파견 때 값으로 살아 있는 파티원의 탐사 합을 낸다. 전원 생존이면 Exploration과 같다.
    internal int LivingExploration(Func<string, bool> _isAlive)
    {
        var living = new List<ExpeditionMember>();
        foreach (ExpeditionMember member in members)
        {
            if (_isAlive == null || _isAlive(member.id))
            {
                living.Add(member);
            }
        }
        return ExpeditionSystem.TotalExploration(living);
    }
}

public readonly struct ExpeditionOutcome
{
    public readonly string regionId;
    public readonly int reward;
    public readonly string[] casualties;
    public readonly string[] returned;
    public readonly int expeditionId;
    public readonly bool withdrawn;
    public readonly SurvivorData rescued;
    public readonly bool hadEncounter;
    public readonly bool respondedToEvent;
    public readonly string[] growths;

    public ExpeditionOutcome(string _regionId, int _reward, string[] _casualties, string[] _returned, int _expeditionId = 0, bool _withdrawn = false, SurvivorData _rescued = null, bool _hadEncounter = false, bool _respondedToEvent = false, string[] _growths = null)
    {
        regionId = _regionId;
        reward = _reward;
        casualties = _casualties;
        returned = _returned;
        expeditionId = _expeditionId;
        withdrawn = _withdrawn;
        rescued = _rescued;
        hadEncounter = _hadEncounter;
        respondedToEvent = _respondedToEvent;
        growths = _growths;
    }
}

public sealed class ExpeditionSystem
{
    readonly Dictionary<string, ExpeditionRegionData> regions = new Dictionary<string, ExpeditionRegionData>(StringComparer.Ordinal);
    readonly List<ActiveExpedition> active = new List<ActiveExpedition>();
    readonly Random random;
    readonly ExpeditionEventData events;
    readonly Random eventRandom;
    int expeditionSequence;
    public IReadOnlyList<ActiveExpedition> Active { get; }

    // 사건 수치와 전용 스트림은 함께 넘긴다. 없으면 사건이 일어나지 않는다. 탐사 결과 스트림은 사건과 섞지 않는다(V2-IMPL-10).
    public ExpeditionSystem(ExpeditionRegionData[] _regions, Random _random, ExpeditionEventData _events = null, Random _eventRandom = null)
    {
        if (_regions == null)
        {
            throw new ArgumentNullException(nameof(_regions));
        }
        random = _random ?? throw new ArgumentNullException(nameof(_random));
        events = _events;
        eventRandom = _eventRandom;
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
        var started = new ActiveExpedition(++expeditionSequence, region, _members);
        started.Event = PlanEvent(region);
        active.Add(started);
        _error = null;
        return true;
    }

    // 파견 시작 때 사건을 정한다(TD-31). 발생 여부와 무관하게 난수를 5회 소비해 뒤따르는 파견의 사건이 갈리지 않게 한다.
    ExpeditionEvent PlanEvent(ExpeditionRegionData _region)
    {
        if (events == null || eventRandom == null)
        {
            return null;
        }
        double occurrence = eventRandom.NextDouble();
        double type = eventRandom.NextDouble();
        double trigger = eventRandom.NextDouble();
        double outcome = eventRandom.NextDouble();
        double victim = eventRandom.NextDouble();
        if (occurrence >= _region.eventChance)
        {
            return null;
        }
        return new ExpeditionEvent
        {
            Type = ExpeditionEventRules.PickType(_region, type),
            TriggerSeconds = ExpeditionEventRules.TriggerSeconds(events, _region.durationSeconds, trigger),
            OutcomeRoll = outcome,
            VictimRoll = victim,
        };
    }

    // 디버그 전용: 아직 도착하지 않은 탐사에 지정한 사건을 바로 일으킨다. _outcomeRoll이 음수면 뽑아 둔 값을 쓴다.
    public bool ForceEvent(ActiveExpedition _expedition, ExpeditionEventType _type, double _outcomeRoll)
    {
        ExpeditionEvent planned = _expedition.Event;
        if (events == null || (planned != null && planned.State != ExpeditionEventState.Scheduled))
        {
            return false;
        }
        _expedition.Event = new ExpeditionEvent
        {
            Type = _type,
            TriggerSeconds = _expedition.elapsedSeconds,
            OutcomeRoll = _outcomeRoll >= 0 ? _outcomeRoll : planned != null ? planned.OutcomeRoll : 0,
            VictimRoll = planned != null ? planned.VictimRoll : 0,
        };
        return true;
    }

    public ActiveExpedition Find(int _expeditionId)
    {
        foreach (ActiveExpedition expedition in active)
        {
            if (expedition.expeditionId == _expeditionId)
            {
                return expedition;
            }
        }
        return null;
    }

    public int PendingEventCount
    {
        get
        {
            int count = 0;
            foreach (ActiveExpedition expedition in active)
            {
                if (expedition.Event != null && expedition.Event.IsAwaitingDecision)
                {
                    count++;
                }
            }
            return count;
        }
    }

    // 먼저 만료되는 대기 사건의 탐사. 없으면 null이다.
    public ActiveExpedition NextPendingEvent
    {
        get
        {
            ActiveExpedition next = null;
            foreach (ActiveExpedition expedition in active)
            {
                if (expedition.Event != null && expedition.Event.IsAwaitingDecision
                    && (next == null || expedition.Event.TimeoutRemaining < next.Event.TimeoutRemaining))
                {
                    next = expedition;
                }
            }
            return next;
        }
    }

    // 시간을 흘리고, 이번 틱에 도착한 사건의 탐사를 시작 순서로 돌려준다. 사건 처리는 GameSession이 하고 그 뒤 CollectFinished를 부른다.
    public List<ActiveExpedition> Advance(float _deltaSeconds)
    {
        if (_deltaSeconds < 0 || float.IsNaN(_deltaSeconds) || float.IsInfinity(_deltaSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(_deltaSeconds));
        }
        var arrived = new List<ActiveExpedition>();
        foreach (ActiveExpedition expedition in active)
        {
            expedition.remainingSeconds = Math.Max(0, expedition.remainingSeconds - _deltaSeconds);
            expedition.elapsedSeconds += _deltaSeconds;
            ExpeditionEvent current = expedition.Event;
            if (current == null)
            {
                continue;
            }
            if (current.State == ExpeditionEventState.Scheduled)
            {
                if (expedition.elapsedSeconds >= current.TriggerSeconds)
                {
                    current.State = ExpeditionEventState.Pending;
                    current.TimeoutRemaining = events.timeoutSeconds;
                    arrived.Add(expedition);
                }
            }
            else if (current.IsAwaitingDecision)
            {
                current.TimeoutRemaining = Math.Max(0, current.TimeoutRemaining - _deltaSeconds);
            }
        }
        return arrived;
    }

    // 미응답 시간이 끝났거나 귀환 시간이 된 대기 사건의 탐사. 귀환까지 답이 없으면 '임무 계속'으로 끝낸 뒤 정산한다.
    public List<ActiveExpedition> ExpiredEvents()
    {
        var expired = new List<ActiveExpedition>();
        foreach (ActiveExpedition expedition in active)
        {
            if (expedition.Event != null && expedition.Event.IsAwaitingDecision
                && (expedition.Event.TimeoutRemaining <= 0 || expedition.remainingSeconds <= 0))
            {
                expired.Add(expedition);
            }
        }
        return expired;
    }

    // 후보가 바닥나면 대기 중인 생존자 발견을 추가 보급품으로 바꾼다. 후보를 새로 만들지 않는다.
    public void ReplacePendingRescues()
    {
        foreach (ActiveExpedition expedition in active)
        {
            if (expedition.Event != null && expedition.Event.Type == ExpeditionEventType.Rescue && expedition.Event.State == ExpeditionEventState.Pending)
            {
                expedition.Event.Type = ExpeditionEventType.Supply;
            }
        }
    }

    // 철수는 남은 시간을 철수 시간 이하로 줄이고 보상과 기존 사망 판정을 없앤다(V2-IMPL-12).
    public void Withdraw(ActiveExpedition _expedition)
    {
        _expedition.IsWithdrawn = true;
        _expedition.remainingSeconds = Math.Min(_expedition.remainingSeconds, events.withdrawSeconds);
    }

    public List<ExpeditionOutcome> Tick(float _deltaSeconds, Func<string, bool> _isAlive = null)
    {
        Advance(_deltaSeconds);
        return CollectFinished(_isAlive);
    }

    // 시간이 끝났거나 파티가 전멸한 탐사를 정산해 목록에서 뺀다. 목록에서 빠지므로 탐사마다 한 번만 정산된다.
    public List<ExpeditionOutcome> CollectFinished(Func<string, bool> _isAlive = null)
    {
        var outcomes = new List<ExpeditionOutcome>();
        // 시작 순서를 유지해 같은 틱에 끝나는 파견의 난수 소비도 재현한다.
        for (int index = 0; index < active.Count;)
        {
            ActiveExpedition expedition = active[index];
            var alive = new List<string>();
            var casualties = new List<string>();
            foreach (string id in expedition.party)
            {
                if (_isAlive == null || _isAlive(id))
                {
                    alive.Add(id);
                }
                else
                {
                    casualties.Add(id);
                }
            }
            // 살아 있는 파티의 대기 사건은 먼저 끝내야 정산한다(ExpiredEvents가 같은 틱에 끝낸다).
            bool awaiting = expedition.Event != null && expedition.Event.IsAwaitingDecision;
            if (alive.Count > 0 && (expedition.remainingSeconds > 0 || awaiting))
            {
                index++;
                continue;
            }
            ExpeditionRegionData region = regions[expedition.regionId];
            int exploration = expedition.LivingExploration(_isAlive);
            bool hasScout = false;
            foreach (ExpeditionMember m in expedition.Members)
            {
                if (m.isScout && alive.Contains(m.id))
                {
                    hasScout = true;
                    break;
                }
            }
            bool rewarded = random.NextDouble() < RewardChance(region.id, exploration);
            bool died = random.NextDouble() < DeathChance(region.id, exploration, alive.Count, hasScout);
            // 사망 여부와 무관하게 난수를 3회 소비한다. 철수·전멸도 같아서 뒤따르는 파견의 결과가 갈리지 않는다.
            int victim = random.Next(Math.Max(1, alive.Count));
            // 철수는 기존 판정을 없애고, 사건에서 이미 죽은 파티원은 판정 대상이 아니다(V2-IMPL-12, 13).
            int casualty = died && !expedition.IsWithdrawn ? victim : -1;
            var returned = new List<string>();
            for (int memberIndex = 0; memberIndex < alive.Count; memberIndex++)
            {
                if (memberIndex == casualty)
                {
                    casualties.Add(alive[memberIndex]);
                }
                else
                {
                    returned.Add(alive[memberIndex]);
                }
            }
            int reward = 0;
            if (returned.Count > 0 && !expedition.IsWithdrawn)
            {
                reward = (rewarded ? ExpectedReward(region.id, exploration) : 0) + expedition.BonusReward;
            }
            expedition.IsSettled = true;
            outcomes.Add(new ExpeditionOutcome(region.id, reward, casualties.ToArray(), returned.ToArray(), expedition.expeditionId, expedition.IsWithdrawn, expedition.RescuedCandidate, expedition.HadEncounter, expedition.RespondedToEvent, null));
            active.RemoveAt(index);
        }
        return outcomes;
    }

    public float RewardChance(string _regionId, int _totalExploration)
    {
        ExpeditionRegionData region = regions[_regionId];
        return ClampChance(region.rewardChanceBase + region.rewardChancePerExploration * _totalExploration);
    }

    public float DeathChance(string _regionId, int _totalExploration, int _partyCount, bool _hasScout = false)
    {
        ExpeditionRegionData region = regions[_regionId];
        float chance = ClampChance(region.deathChanceBase + region.deathChancePerExploration * _totalExploration + region.deathChancePerExtraMember * (_partyCount - 1));
        return _hasScout && events != null ? ClampChance(chance * events.scoutRiskMultiplier) : chance;
    }

    // UI에는 보상 성공 시 지급량을 표시한다. 중간값은 양수에서 올림한다.
    public int ExpectedReward(string _regionId, int _totalExploration)
    {
        ExpeditionRegionData region = regions[_regionId];
        return Math.Max(0, checked((int)Math.Round(region.rewardBase + region.rewardPerExploration * _totalExploration, MidpointRounding.AwayFromZero)));
    }

    // 추가 수색 보상의 기준이 되는 기본 보상. 살아 있는 파티원의 탐사 합으로 정산 때와 같게 계산한다.
    public int BaseRewardOf(ActiveExpedition _expedition, Func<string, bool> _isAlive)
    {
        return ExpectedReward(_expedition.regionId, _expedition.LivingExploration(_isAlive));
    }

    // 귀환 때 적용될 사망 판정 확률. 정산과 같이 살아 있는 파티원 기준이다.
    public float DeathChanceOf(ActiveExpedition _expedition, Func<string, bool> _isAlive, int _aliveCount, bool _hasScout = false)
    {
        return DeathChance(_expedition.regionId, _expedition.LivingExploration(_isAlive), _aliveCount, _hasScout);
    }

    static float ClampChance(float _chance)
    {
        return Math.Max(0, Math.Min(1, _chance));
    }
}
