using System;
using System.Collections.Generic;

// 전시 방어 배정. 맵의 지점 또는 감시탑 자리다. 탑 자리도 좌표를 함께 들고 있어 탑이 사라지면 그 지점으로 남는다.
public readonly struct DefenseAssignment
{
    public readonly float x;
    public readonly float z;
    public readonly string towerId;

    public DefenseAssignment(float _x, float _z, string _towerId = null)
    {
        x = _x;
        z = _z;
        towerId = _towerId;
    }

    public bool IsTower => !string.IsNullOrEmpty(towerId);
}

// 작업 배정과 방어 배정을 독립으로 저장한다. 한쪽을 바꿔도 다른 쪽은 그대로다(POC_CORE 3.1).
// 사망자와 파견 중인 인원의 배정 변경은 거부한다. 단계별 허용 여부는 SessionFlow가 판단한다.
public sealed class AssignmentBoard
{
    sealed class Entry
    {
        public SurvivorState survivor;
        public WorkAssignment work;
        public DefenseAssignment defense;
        // 합류 인력은 합류 지점을 방어 위치로 가지지만 플레이어가 정한 게 아니므로 '미지정'으로 표시한다(V2-IMPL-08).
        public bool defenseAssigned;
    }

    readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
    readonly List<Entry> ordered = new List<Entry>();
    readonly int maxWorkers;

    public AssignmentBoard(int _maxWorkers)
    {
        if (_maxWorkers < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(_maxWorkers));
        }
        maxWorkers = _maxWorkers;
    }

    public int MaxWorkers => maxWorkers;

    public void Register(SurvivorState _survivor, DefenseAssignment _defense, bool _defenseAssigned = true)
    {
        if (_survivor == null)
        {
            throw new ArgumentNullException(nameof(_survivor));
        }
        var entry = new Entry { survivor = _survivor, work = WorkAssignment.DefenseStandby, defense = _defense, defenseAssigned = _defenseAssigned };
        entries.Add(_survivor.Id, entry);
        ordered.Add(entry);
    }

    public WorkAssignment WorkOf(string _id) => entries[_id].work;
    public DefenseAssignment DefenseOf(string _id) => entries[_id].defense;
    public bool IsDefenseAssigned(string _id) => entries[_id].defenseAssigned;

    // 정원은 지금 거점에서 일할 수 있는 인원만 센다. 파견 중인 작업자의 자리는 다른 인원이 맡을 수 있다.
    public int ActiveWorkerCount
    {
        get
        {
            int count = 0;
            foreach (Entry entry in ordered)
            {
                if (entry.work == WorkAssignment.Production && entry.survivor.CanAct)
                {
                    count++;
                }
            }
            return count;
        }
    }

    public bool TrySetWork(string _id, WorkAssignment _work, out string _error)
    {
        if (!TryGetActable(_id, out Entry entry, out _error))
        {
            return false;
        }
        if (entry.work == _work)
        {
            return true;
        }
        if (_work == WorkAssignment.Production && ActiveWorkerCount >= maxWorkers)
        {
            _error = "생산 시설 정원이 찼습니다.";
            return false;
        }
        entry.work = _work;
        return true;
    }

    public bool TrySetDefensePoint(string _id, float _x, float _z, out string _error)
    {
        if (!TryGetActable(_id, out Entry entry, out _error))
        {
            return false;
        }
        entry.defense = new DefenseAssignment(_x, _z);
        entry.defenseAssigned = true;
        return true;
    }

    // 탑 자리는 정원 1명이다. 파견 중인 인원의 자리는 유지하고 사망자의 자리는 비운다.
    public bool TrySetDefenseTower(string _id, string _towerId, float _x, float _z, out string _error)
    {
        if (string.IsNullOrEmpty(_towerId))
        {
            _error = "감시탑이 지정되지 않았습니다.";
            return false;
        }
        if (!TryGetActable(_id, out Entry entry, out _error))
        {
            return false;
        }
        foreach (Entry other in ordered)
        {
            if (other != entry && other.survivor.IsAlive && other.defense.towerId == _towerId)
            {
                _error = "이미 다른 인원이 배치된 감시탑입니다.";
                return false;
            }
        }
        entry.defense = new DefenseAssignment(_x, _z, _towerId);
        entry.defenseAssigned = true;
        return true;
    }

    // 탑이 사라지면 배치 인원은 같은 좌표의 지점 배정으로 남는다.
    public void VacateTower(string _towerId)
    {
        foreach (Entry entry in ordered)
        {
            if (entry.defense.towerId == _towerId)
            {
                entry.defense = new DefenseAssignment(entry.defense.x, entry.defense.z);
            }
        }
    }

    // 귀환 시 호출한다. 파견 중 자리가 채워졌으면 방어 대기로 돌리고 true를 반환한다.
    public bool ResolveReturn(string _id)
    {
        Entry entry = entries[_id];
        if (entry.work != WorkAssignment.Production || ActiveWorkerCount <= maxWorkers)
        {
            return false;
        }
        entry.work = WorkAssignment.DefenseStandby;
        return true;
    }

    bool TryGetActable(string _id, out Entry _entry, out string _error)
    {
        if (_id == null || !entries.TryGetValue(_id, out _entry))
        {
            _entry = null;
            _error = "알 수 없는 생존자입니다.";
            return false;
        }
        if (!_entry.survivor.IsAlive)
        {
            _error = "사망한 생존자는 배정할 수 없습니다.";
            return false;
        }
        if (_entry.survivor.IsAway)
        {
            _error = "파견 중인 생존자는 배정할 수 없습니다.";
            return false;
        }
        _error = null;
        return true;
    }
}
