using System;
using System.Collections.Generic;
using System.Globalization;

// 방문자의 결정 상태. 도착 전에는 웨이브 몫만 정해져 있고, 준비 시작에 도착한다.
public enum VisitorStatus
{
    Waiting,
    Accepted,
    Declined,
    Expired,
}

// 웨이브별 방문 기록. 후보는 세션 시작 몫으로 정해지고 상태만 바뀐다.
public sealed class VisitorRecord
{
    public SurvivorData Candidate { get; }
    public int Wave { get; }
    public bool Arrived { get; private set; }
    public VisitorStatus Status { get; internal set; }
    // 도착해서 아직 영입도 보내기도 하지 않은 상태. 이때만 영입할 수 있다.
    public bool IsWaiting => Arrived && Status == VisitorStatus.Waiting;

    public VisitorRecord(SurvivorData _candidate, int _wave)
    {
        Candidate = _candidate;
        Wave = _wave;
        Status = VisitorStatus.Waiting;
    }

    internal void MarkArrived()
    {
        Arrived = true;
    }
}

// 인력 수급 규칙의 한 곳: 수용 상한 판정, 방문 후보 추첨, 신규 ID 발급, 방문 상태(V2-IMPL-09).
// Unity에 의존하지 않는다. 상한은 '살아 있는 보유 인원(파견·중상 포함) + 귀환 예정 예약 < capacity'다.
public sealed class RecruitmentSystem
{
    // RecruitmentRejected의 사유 값. 기준서 7.4의 4종이다.
    public const string REASON_DECLINED = "declined";
    public const string REASON_EXPIRED = "expired";
    public const string REASON_NO_SCRAP = "no_scrap";
    public const string REASON_FULL = "full";
    // SurvivorJoined의 출처.
    public const string SOURCE_VISITOR = "visitor";
    public const string SOURCE_RESCUE = "rescue";

    readonly int capacity;
    readonly int visitorCost;
    readonly VisitorRecord[] visitors;
    readonly Queue<SurvivorData> rescueShare = new Queue<SurvivorData>();
    int reservedSlots;
    int joinedCount;

    public RecruitmentSystem(RecruitmentData _data, Random _random)
    {
        if (_data == null)
        {
            throw new ArgumentNullException(nameof(_data));
        }
        if (_random == null)
        {
            throw new ArgumentNullException(nameof(_random));
        }
        capacity = _data.capacity;
        visitorCost = _data.visitorCost;
        int[] waves = _data.visitorWaves ?? Array.Empty<int>();
        var pool = new List<SurvivorData>(_data.candidates ?? Array.Empty<SurvivorData>());
        // 세션 시작에 후보를 섞어 방문 몫을 먼저 정하고 나머지를 구조 몫 큐로 둔다(V2-IMPL-09).
        // 같은 시드면 같은 몫이 나오고, 탐사 결과 스트림과는 무관하다.
        for (int index = pool.Count - 1; index > 0; index--)
        {
            int swap = _random.Next(index + 1);
            (pool[index], pool[swap]) = (pool[swap], pool[index]);
        }
        visitors = new VisitorRecord[Math.Min(waves.Length, pool.Count)];
        for (int index = 0; index < visitors.Length; index++)
        {
            visitors[index] = new VisitorRecord(pool[index], waves[index]);
        }
        for (int index = visitors.Length; index < pool.Count; index++)
        {
            rescueShare.Enqueue(pool[index]);
        }
    }

    public int Capacity => capacity;
    public int VisitorCost => visitorCost;
    public IReadOnlyList<VisitorRecord> Visitors => visitors;
    public int RescueShareCount => rescueShare.Count;
    public int ReservedSlots => reservedSlots;

    // 수용 상한의 유일한 판정. aliveCount는 파견·중상 인원을 포함한 살아 있는 수다.
    public bool HasCapacity(int _aliveCount) => _aliveCount + reservedSlots < capacity;

    // 귀환 예정 구조자의 자리 예약. 예약은 상한 판정에 포함되고 같은 술어로 막는다.
    public bool ReserveSlot(int _aliveCount)
    {
        if (!HasCapacity(_aliveCount))
        {
            return false;
        }
        reservedSlots++;
        return true;
    }

    // 탐사팀 전멸 등으로 예약이 무산되면 푼다.
    public void ReleaseSlot()
    {
        reservedSlots = Math.Max(0, reservedSlots - 1);
    }

    // 도착해서 대기 중인 방문자. 이 상태에서만 영입·보내기가 가능하다.
    public VisitorRecord PendingVisitor
    {
        get
        {
            foreach (VisitorRecord record in visitors)
            {
                if (record.IsWaiting)
                {
                    return record;
                }
            }
            return null;
        }
    }

    // 해당 웨이브의 준비가 시작하면 방문자가 도착한다. 새로 도착했으면 기록을 돌려준다.
    public VisitorRecord ArriveVisitor(int _wave)
    {
        VisitorRecord record = VisitorOf(_wave);
        if (record == null || record.Arrived || record.Status != VisitorStatus.Waiting)
        {
            return null;
        }
        record.MarkArrived();
        return record;
    }

    // 준비 단계가 끝날 때(경보 진입) 미결정 방문자는 만료된다. 만료됐으면 기록을 돌려준다.
    public VisitorRecord ExpireVisitor(int _wave)
    {
        VisitorRecord record = VisitorOf(_wave);
        if (record == null || !record.IsWaiting)
        {
            return null;
        }
        record.Status = VisitorStatus.Expired;
        return record;
    }

    // 플레이어가 '보내기'로 거절했다. 대기 중일 때만 true.
    public bool DeclineVisitor()
    {
        VisitorRecord record = PendingVisitor;
        if (record == null)
        {
            return false;
        }
        record.Status = VisitorStatus.Declined;
        return true;
    }

    // 합류 인력의 새 ID. 세션 순번으로 만들어 후보 ID와 무관하고 재사용이 없다(V2-IMPL-09).
    public string IssueNewId() => "n" + (++joinedCount).ToString(CultureInfo.InvariantCulture);

    // 구조 몫의 다음 후보를 꺼낸다. 없으면 null.
    public SurvivorData TakeRescueCandidate() => rescueShare.Count > 0 ? rescueShare.Dequeue() : null;

    VisitorRecord VisitorOf(int _wave)
    {
        foreach (VisitorRecord record in visitors)
        {
            if (record.Wave == _wave)
            {
                return record;
            }
        }
        return null;
    }
}
