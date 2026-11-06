using System;

// 진행 중인 치료 1건. 진행률은 비율로 들고 있어 담당자가 바뀌어도 이어진다.
public sealed class TreatmentJob
{
    public SurvivorState Patient { get; }
    // 새 담당자를 기다리는 동안 null이다.
    public SurvivorState Worker { get; internal set; }
    public float Progress { get; internal set; }
    public bool IsPaused { get; internal set; }
    public bool CostPaid { get; internal set; }
    public bool NeedsWorker => Worker == null;

    internal TreatmentJob(SurvivorState _patient, SurvivorState _worker)
    {
        Patient = _patient;
        Worker = _worker;
    }
}

// 치료소의 규칙(V2-IMPL-05, 06). Unity에 의존하지 않으며 GameSession이 소유한다. 시간은 호출 측이 넘기는 게임 시간이다.
// 비용은 시작 때 한 번만 차감하고, 일시정지·재개·담당자 교체에서는 다시 차감하지 않는다.
public sealed class TreatmentSystem
{
    public const string REASON_NOT_PREP = "not_prep";
    public const string REASON_BUSY = "busy";
    public const string REASON_NO_WORKER = "no_worker";
    public const string REASON_WORKER_UNAVAILABLE = "worker_unavailable";
    public const string REASON_NO_PATIENT = "no_patient";
    public const string REASON_PATIENT_DEAD = "patient_dead";
    public const string REASON_PATIENT_AWAY = "patient_away";
    public const string REASON_PATIENT_FULL_HP = "patient_full_hp";
    public const string REASON_SELF = "self";
    public const string REASON_NO_SCRAP = "no_scrap";
    public const string REASON_NO_JOB = "no_job";
    public const string REASON_HAS_WORKER = "has_worker";
    public const string END_PLAYER = "player";
    public const string END_PATIENT_DIED = "patient_died";
    public const string END_DEBUG = "debug";

    readonly TreatmentData data;

    public TreatmentJob Current { get; private set; }
    public int Cost => data.cost;
    public float HealFraction => data.healFraction;

    public TreatmentSystem(TreatmentData _data)
    {
        data = _data;
    }

    // 치료에 묶인 인원인지. 일시정지 중에도 끝나거나 취소될 때까지는 묶여 있다.
    public bool Involves(string _id)
    {
        return Current != null && (Current.Patient.Id == _id || (Current.Worker != null && Current.Worker.Id == _id));
    }

    // TD-29b. 작업 능력 보정에 담당자 효율을 곱한다. 행동할 수 없는 담당자는 효율이 0이라 끝나지 않는다.
    public float SecondsFor(SurvivorState _worker)
    {
        float speed = (1 + data.speedPerWork * (_worker.Work - data.referenceWork)) * _worker.WorkEfficiency;
        return speed > 0 ? data.baseSeconds / speed : float.PositiveInfinity;
    }

    public float RemainingSeconds => Current == null || Current.Worker == null ? 0 : (1 - Current.Progress) * SecondsFor(Current.Worker);

    // 시작 조건 전부. 문제가 없으면 null이고, 아니면 사유 키를 돌려준다. 아무것도 바꾸지 않으므로 패널의 사유 표시도 이 함수를 쓴다.
    public string CheckStart(SessionPhase _phase, SurvivorState _patient, SurvivorState _worker, int _scrap)
    {
        if (_phase != SessionPhase.Prep)
        {
            return REASON_NOT_PREP;
        }
        if (Current != null)
        {
            return REASON_BUSY;
        }
        if (_worker == null)
        {
            return REASON_NO_WORKER;
        }
        if (!_worker.CanAct)
        {
            return REASON_WORKER_UNAVAILABLE;
        }
        if (_patient == null)
        {
            return REASON_NO_PATIENT;
        }
        if (!_patient.IsAlive)
        {
            return REASON_PATIENT_DEAD;
        }
        if (_patient.IsAway)
        {
            return REASON_PATIENT_AWAY;
        }
        if (_patient.Hp >= _patient.MaxHp)
        {
            return REASON_PATIENT_FULL_HP;
        }
        if (_patient == _worker)
        {
            return REASON_SELF;
        }
        return _scrap < data.cost ? REASON_NO_SCRAP : null;
    }

    public bool TryStart(SessionPhase _phase, SurvivorState _patient, SurvivorState _worker, ResourceEconomy _economy, out string _reason)
    {
        _reason = CheckStart(_phase, _patient, _worker, _economy.Scrap);
        if (_reason != null)
        {
            return false;
        }
        if (!_economy.TrySpend(data.cost))
        {
            _reason = REASON_NO_SCRAP;
            return false;
        }
        Current = new TreatmentJob(_patient, _worker) { CostPaid = true };
        return true;
    }

    public string CheckAssign(SessionPhase _phase, SurvivorState _worker)
    {
        if (_phase != SessionPhase.Prep)
        {
            return REASON_NOT_PREP;
        }
        if (Current == null)
        {
            return REASON_NO_JOB;
        }
        if (!Current.NeedsWorker)
        {
            return REASON_HAS_WORKER;
        }
        if (_worker == null)
        {
            return REASON_NO_WORKER;
        }
        if (!_worker.CanAct)
        {
            return REASON_WORKER_UNAVAILABLE;
        }
        return _worker == Current.Patient ? REASON_SELF : null;
    }

    // 재개 대기 중인 치료에 새 담당자를 지정한다. 재개는 무료다(V2-IMPL-06).
    public bool TryAssignWorker(SessionPhase _phase, SurvivorState _worker, out string _reason)
    {
        _reason = CheckAssign(_phase, _worker);
        if (_reason != null)
        {
            return false;
        }
        Current.Worker = _worker;
        Current.IsPaused = false;
        return true;
    }

    // 경보. 진행률을 보존하고 멈춘다. 이번 호출로 멈췄으면 true.
    public bool Pause()
    {
        if (Current == null || Current.IsPaused)
        {
            return false;
        }
        Current.IsPaused = true;
        return true;
    }

    // 준비 시작. 같은 담당자가 행동 가능하면 재개하고 true, 아니면 새 담당자를 기다린다.
    public bool ResumeAtPrep()
    {
        if (Current == null || !Current.IsPaused)
        {
            return false;
        }
        if (Current.Worker == null || !Current.Worker.CanAct)
        {
            Current.Worker = null;
            return false;
        }
        Current.IsPaused = false;
        return true;
    }

    // 진행 중 담당자가 행동 불가가 되면 멈추고 새 담당자를 기다린다. 이번 호출로 멈췄으면 true.
    // 일시정지 중의 담당자는 건드리지 않는다. 다음 준비 시작의 ResumeAtPrep이 판단한다.
    public bool DropUnavailableWorker()
    {
        if (Current == null || Current.IsPaused || Current.Worker == null || Current.Worker.CanAct)
        {
            return false;
        }
        Current.Worker = null;
        Current.IsPaused = true;
        return true;
    }

    // 환불하지 않는다(V2-IMPL-06). 끝낸 치료를 돌려주고 없으면 null이다.
    public TreatmentJob End()
    {
        TreatmentJob ended = Current;
        Current = null;
        return ended;
    }

    // 담당자가 도착한 동안만 진행한다. 완료 순간에만 HP를 올리고 끝난 치료를 돌려준다.
    public TreatmentJob Tick(float _deltaSeconds, bool _workerArrived)
    {
        if (_deltaSeconds < 0 || float.IsNaN(_deltaSeconds) || float.IsInfinity(_deltaSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(_deltaSeconds));
        }
        if (Current == null || Current.IsPaused || Current.Worker == null || !_workerArrived)
        {
            return null;
        }
        Current.Progress = Math.Min(1, Current.Progress + _deltaSeconds / SecondsFor(Current.Worker));
        if (Current.Progress < 1)
        {
            return null;
        }
        TreatmentJob done = End();
        done.Patient.Heal(done.Patient.MaxHp * data.healFraction);
        return done;
    }
}
