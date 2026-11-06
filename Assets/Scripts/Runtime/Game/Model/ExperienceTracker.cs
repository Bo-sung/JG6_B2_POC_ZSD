using System;
using System.Collections.Generic;

// 생존자 한 명의 경험치 진행 상태. Unity에 의존하지 않는다.
public sealed class SurvivorExperienceProgress
{
    public int CombatXp { get; internal set; }
    public int WorkXp { get; internal set; }
    public int ExplorationXp { get; internal set; }
    public float WorkSeconds { get; internal set; }
    public int WaveCombatXp { get; internal set; }
    public int WaveWorkXp { get; internal set; }
    public bool DamagedInWave { get; internal set; }

    public int GetXp(StatType _stat) => _stat switch
    {
        StatType.Combat => CombatXp,
        StatType.Work => WorkXp,
        StatType.Exploration => ExplorationXp,
        _ => 0,
    };

    public void SetXp(StatType _stat, int _value)
    {
        switch (_stat)
        {
            case StatType.Combat:
                CombatXp = _value;
                break;
            case StatType.Work:
                WorkXp = _value;
                break;
            case StatType.Exploration:
                ExplorationXp = _value;
                break;
        }
    }
}

// 순수 모델: 능력치별 XP, 웨이브·탐사 건별 상한, 성장을 맡는다(TD-40~45, V2-IMPL-14).
public sealed class ExperienceTracker
{
    readonly ExperienceData data;
    readonly Func<string, SurvivorState> survivorResolver;
    readonly Dictionary<string, SurvivorExperienceProgress> progress = new Dictionary<string, SurvivorExperienceProgress>(StringComparer.Ordinal);

    public int MaxStat => data.maxStat;

    public ExperienceTracker(ExperienceData _data, Func<string, SurvivorState> _survivorResolver)
    {
        data = _data ?? throw new ArgumentNullException(nameof(_data));
        survivorResolver = _survivorResolver ?? throw new ArgumentNullException(nameof(_survivorResolver));
    }

    public SurvivorExperienceProgress GetProgress(string _survivorId)
    {
        if (string.IsNullOrEmpty(_survivorId))
        {
            return null;
        }
        if (!progress.TryGetValue(_survivorId, out var entry))
        {
            entry = new SurvivorExperienceProgress();
            progress[_survivorId] = entry;
        }
        return entry;
    }

    public int GetXp(string _survivorId, StatType _stat)
    {
        return GetProgress(_survivorId)?.GetXp(_stat) ?? 0;
    }

    public int NeededXp(int _currentStat)
    {
        return _currentStat * data.xpPerStat;
    }

    // 웨이브 시작에 카운터 초기화(TD-41, TD-42). 소수 작업 시간은 이어 간다.
    public void ResetWave()
    {
        foreach (var entry in progress.Values)
        {
            entry.WaveCombatXp = 0;
            entry.WaveWorkXp = 0;
            entry.DamagedInWave = false;
        }
    }

    // 전투 XP: 이번 웨이브 첫 타격(TD-41).
    public int RecordCombatHit(string _survivorId)
    {
        SurvivorState survivor = survivorResolver(_survivorId);
        if (survivor == null || !survivor.IsAlive)
        {
            return 0;
        }
        var entry = GetProgress(_survivorId);
        if (entry.DamagedInWave)
        {
            return 0;
        }
        entry.DamagedInWave = true;
        return AddXp(_survivorId, StatType.Combat, data.combatFirstHitXp, true, null);
    }

    // 전투 XP: 처치된 적에게 피해를 준 모든 인력(TD-41).
    public void RecordEnemyKilled(IEnumerable<string> _damagerIds)
    {
        if (_damagerIds == null)
        {
            return;
        }
        foreach (string id in _damagerIds)
        {
            SurvivorState survivor = survivorResolver(id);
            if (survivor != null && survivor.IsAlive)
            {
                AddXp(id, StatType.Combat, data.combatKillParticipationXp, true, null);
            }
        }
    }

    // 작업 XP: 생산 자리에서 일한 시간(TD-42). 소수 시간은 이어 가되, 받을 수 없는 동안의 시간은 쌓지 않는다.
    // 쌓아 두면 다음 웨이브에 한꺼번에 지급되어 웨이브 상한이 무의미해진다.
    public void TickWorkTime(string _survivorId, float _deltaSeconds)
    {
        SurvivorState survivor = survivorResolver(_survivorId);
        if (survivor == null || !survivor.IsAlive || _deltaSeconds <= 0)
        {
            return;
        }
        var entry = GetProgress(_survivorId);
        if (entry.WaveWorkXp >= data.workWaveCap || survivor.Work >= MaxStat)
        {
            return;
        }
        entry.WorkSeconds += _deltaSeconds;
        while (entry.WorkSeconds >= data.workSecondsPerXp && entry.WaveWorkXp < data.workWaveCap && survivor.Work < MaxStat)
        {
            entry.WorkSeconds -= data.workSecondsPerXp;
            AddXp(_survivorId, StatType.Work, 1, true, null);
        }
    }

    // 작업 XP: 치료 완료 1건(TD-42).
    public int RecordTreatmentComplete(string _workerId)
    {
        SurvivorState worker = survivorResolver(_workerId);
        if (worker == null || !worker.IsAlive)
        {
            return 0;
        }
        return AddXp(_workerId, StatType.Work, data.treatmentXp, true, null);
    }

    // 탐사 XP 수치 계산(TD-43). 귀환, 선택 사건 직접 대응, 조우 생존을 더하고 건당 상한을 둔다. 철수는 비율을 곱해 내림한다.
    public int CalculateExpeditionXp(bool _directEventResponse, bool _survivedEncounter, bool _withdrawn)
    {
        int xp = data.expeditionReturnXp;
        if (_directEventResponse)
        {
            xp += data.expeditionEventResponseBonus;
        }
        if (_survivedEncounter)
        {
            xp += data.expeditionEncounterSurvivalBonus;
        }
        xp = Math.Min(xp, data.expeditionCap);
        if (_withdrawn)
        {
            xp = (int)Math.Floor(xp * data.withdrawMultiplier);
        }
        return xp;
    }

    // 귀환 정산에서 살아 돌아온 인력에게 1회 지급(TD-43).
    public int SettleExpedition(IEnumerable<string> _returnedLivingIds, bool _directEventResponse, bool _survivedEncounter, bool _withdrawn, List<string> _growths = null)
    {
        if (_returnedLivingIds == null)
        {
            return 0;
        }
        int amount = CalculateExpeditionXp(_directEventResponse, _survivedEncounter, _withdrawn);
        if (amount <= 0)
        {
            return 0;
        }
        foreach (string id in _returnedLivingIds)
        {
            SurvivorState survivor = survivorResolver(id);
            if (survivor != null && survivor.IsAlive)
            {
                AddXp(id, StatType.Exploration, amount, false, _growths);
            }
        }
        return amount;
    }

    // 디버그 전용 지급. 정규 경로와 같은 성장 판정을 거친다.
    public int AddDebugExperience(string _survivorId, StatType _stat, int _amount, List<string> _growths = null)
    {
        return AddXp(_survivorId, _stat, _amount, false, _growths);
    }

    // 공통 XP 지급 및 성장 판정 경로.
    int AddXp(string _survivorId, StatType _stat, int _amount, bool _checkWaveCap, List<string> _growths)
    {
        if (_amount <= 0)
        {
            return 0;
        }
        SurvivorState survivor = survivorResolver(_survivorId);
        if (survivor == null || !survivor.IsAlive)
        {
            return 0;
        }
        if (survivor.StatOf(_stat) >= MaxStat)
        {
            return 0;
        }
        var entry = GetProgress(_survivorId);
        int granted = _amount;
        if (_checkWaveCap)
        {
            if (_stat == StatType.Combat)
            {
                granted = Math.Min(granted, Math.Max(0, data.combatWaveCap - entry.WaveCombatXp));
            }
            else if (_stat == StatType.Work)
            {
                granted = Math.Min(granted, Math.Max(0, data.workWaveCap - entry.WaveWorkXp));
            }
        }
        if (granted <= 0)
        {
            return 0;
        }
        if (_stat == StatType.Combat)
        {
            entry.WaveCombatXp += granted;
        }
        else if (_stat == StatType.Work)
        {
            entry.WaveWorkXp += granted;
        }
        entry.SetXp(_stat, entry.GetXp(_stat) + granted);
        GameEvents.RaiseExperienceGained(_survivorId, _stat.ToString(), granted);

        // 조건을 채운 즉시 성장한다(TD-45). 상한에 닿으면 남은 XP는 버린다(V2-IMPL-14).
        while (survivor.StatOf(_stat) < MaxStat && entry.GetXp(_stat) >= NeededXp(survivor.StatOf(_stat)))
        {
            int oldValue = survivor.StatOf(_stat);
            entry.SetXp(_stat, entry.GetXp(_stat) - NeededXp(oldValue));
            if (!survivor.ImproveStat(_stat, MaxStat))
            {
                break;
            }
            int newValue = survivor.StatOf(_stat);
            _growths?.Add($"{survivor.DisplayName} {StatDisplayName(_stat)} {oldValue}→{newValue}");
            GameEvents.RaiseStatImproved(_survivorId, _stat.ToString(), oldValue, newValue);
            if (newValue >= MaxStat)
            {
                entry.SetXp(_stat, 0);
            }
        }
        return granted;
    }

    public static string StatDisplayName(StatType _stat) => _stat switch
    {
        StatType.Combat => "전투",
        StatType.Work => "작업",
        StatType.Exploration => "탐사",
        _ => _stat.ToString(),
    };

    // UI 표시용 문구. 상한에 닿은 능력치는 '최대'다.
    public string FormatStatProgress(string _label, int _statValue, StatType _stat, string _survivorId)
    {
        if (_statValue >= MaxStat)
        {
            return $"{_label} {MaxStat} (최대)";
        }
        return $"{_label} {_statValue} (XP {GetXp(_survivorId, _stat)}/{NeededXp(_statValue)})";
    }
}
