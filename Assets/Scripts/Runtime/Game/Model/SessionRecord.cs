using System.Collections.Generic;

// 세션 누적 기록(기준서 4.5). 결과 화면이 보여 줄 추가·사망 인원과 성장·방문·탐사 사건 기록을 한 곳에 모은다.
// 남은 HP·스크랩·최종 인원처럼 지금 상태에서 읽는 값은 여기 두지 않는다.
public sealed class SessionRecord
{
    sealed class StatGrowth
    {
        public string stat;
        public int first;
        public int last;
    }

    sealed class SurvivorGrowth
    {
        public string name;
        public readonly List<StatGrowth> stats = new List<StatGrowth>();
    }

    sealed class OutcomeCount
    {
        public string label;
        public int count;
    }

    readonly List<string> joined = new List<string>();
    readonly List<string> deaths = new List<string>();
    readonly List<string> visitorLog = new List<string>();
    readonly List<SurvivorGrowth> growthOrder = new List<SurvivorGrowth>();
    readonly Dictionary<string, SurvivorGrowth> growthById = new Dictionary<string, SurvivorGrowth>();
    readonly List<OutcomeCount> expeditionOutcomes = new List<OutcomeCount>();

    public SessionRecord(int _initialCount)
    {
        InitialCount = _initialCount;
    }

    public int InitialCount { get; }
    public IReadOnlyList<string> Joined => joined;
    public IReadOnlyList<string> Deaths => deaths;
    public IReadOnlyList<string> VisitorLog => visitorLog;

    public int JoinedCount => joined.Count;
    public int DeathCount => deaths.Count;

    public void RecordJoin(string _displayName, string _source)
    {
        joined.Add($"{_displayName} ({SourceLabel(_source)})");
    }

    public void RecordDeath(string _displayName)
    {
        deaths.Add(_displayName);
    }

    // 같은 인력·능력치는 처음 값→마지막 값으로 합친다(TD-96). 긴 세션에도 인원 수만큼만 늘어난다.
    public void RecordGrowth(string _id, string _displayName, string _statName, int _old, int _new)
    {
        if (!growthById.TryGetValue(_id, out SurvivorGrowth survivor))
        {
            survivor = new SurvivorGrowth { name = _displayName };
            growthById.Add(_id, survivor);
            growthOrder.Add(survivor);
        }
        StatGrowth stat = survivor.stats.Find(_entry => _entry.stat == _statName);
        if (stat == null)
        {
            stat = new StatGrowth { stat = _statName, first = _old };
            survivor.stats.Add(stat);
        }
        stat.last = _new;
    }

    // 인력별 한 항목. 예: "서준 전투 1→3·작업 2→3"
    public List<string> GrowthSummary()
    {
        var lines = new List<string>();
        foreach (SurvivorGrowth survivor in growthOrder)
        {
            var parts = new List<string>();
            foreach (StatGrowth stat in survivor.stats)
            {
                parts.Add($"{stat.stat} {stat.first}→{stat.last}");
            }
            lines.Add($"{survivor.name} {string.Join("·", parts)}");
        }
        return lines;
    }

    // 방문 후보의 최종 결과. 영입한 후보는 합류 기록에도 남는다.
    public void RecordVisitorOutcome(int _wave, string _displayName, string _outcome)
    {
        visitorLog.Add($"W{_wave} {_displayName} {_outcome}");
    }

    // 결과별 건수만 남긴다(TD-97). 시각과 지역은 JSONL의 ExpeditionEventResolved에 있다.
    public void RecordExpeditionOutcome(string _outcomeLabel)
    {
        OutcomeCount outcome = expeditionOutcomes.Find(_entry => _entry.label == _outcomeLabel);
        if (outcome == null)
        {
            outcome = new OutcomeCount { label = _outcomeLabel };
            expeditionOutcomes.Add(outcome);
        }
        outcome.count++;
    }

    // 예: "구조 성공 2건"
    public List<string> ExpeditionSummary()
    {
        var lines = new List<string>();
        foreach (OutcomeCount outcome in expeditionOutcomes)
        {
            lines.Add($"{outcome.label} {outcome.count}건");
        }
        return lines;
    }

    static string SourceLabel(string _source) => _source switch
    {
        RecruitmentSystem.SOURCE_VISITOR => "방문",
        RecruitmentSystem.SOURCE_RESCUE => "구조",
        _ => _source,
    };
}
