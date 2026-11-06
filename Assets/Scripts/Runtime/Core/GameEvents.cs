using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// 게임 도메인 이벤트의 단일 발행 지점.
// 규칙:
//   1) 이벤트는 이 파일에 struct 페이로드와 함께 추가한다. 개별 컴포넌트가 서로를 직접 참조해 알리지 않는다.
//   2) 생산자는 Raise*만 호출한다. 구독자 예외가 게임 흐름을 멈추지 않도록 여기서 삼킨다.
//   3) 구독은 OnEnable/OnDisable에서 대칭으로 한다(OnDisable에서 해제하지 않으면 정적 이벤트에 갇힌다).
//   4) 페이로드는 struct로 불변 data only로 두고, 시간·좌표처럼 분석에 쓰는 값은 여기서 함께 보낸다.
public static class GameEvents
{
    // ── 페이로드 ──
    public readonly struct RunInfo
    {
        public readonly string scene;
        public readonly double time;
        public readonly string reason;
        public RunInfo(string _scene, double _time, string _reason = null)
        {
            this.scene = _scene;
            this.time = _time;
            this.reason = _reason;
        }
    }
    public readonly struct SceneInfo
    {
        public readonly string scene;
        public readonly bool loaded;
        public SceneInfo(string _scene, bool _loaded)
        {
            this.scene = _scene;
            this.loaded = _loaded;
        }
    }
    public readonly struct UpgradeInfo
    {
        public readonly string id, displayName;
        public UpgradeInfo(string _id, string _displayName)
        {
            this.id = _id;
            this.displayName = _displayName;
        }
    }
    public readonly struct PanelInfo
    {
        public readonly string panel;
        public readonly bool open;
        public PanelInfo(string _panel, bool _open)
        {
            this.panel = _panel;
            this.open = _open;
        }
    }

    public readonly struct SessionStartInfo
    {
        public readonly int seed;
        public SessionStartInfo(int _seed)
        {
            seed = _seed;
        }
    }
    public readonly struct SessionEndInfo
    {
        public readonly SessionResult result;
        public readonly string reason;
        // 기준서 7.4의 SessionEnded(result, seed). 재시작 재현에 쓴다.
        public readonly int seed;
        public SessionEndInfo(SessionResult _result, string _reason, int _seed = 0)
        {
            result = _result;
            reason = _reason;
            seed = _seed;
        }
    }
    public readonly struct PhaseInfo
    {
        public readonly SessionPhase phase;
        public readonly int waveIndex;
        public PhaseInfo(SessionPhase _phase, int _waveIndex)
        {
            phase = _phase;
            waveIndex = _waveIndex;
        }
    }
    // work와 defense는 로그와 표시용 문자열이다. 실제 배정은 AssignmentBoard가 가진다.
    public readonly struct AssignmentInfo
    {
        public readonly string survivorId, work, defense;
        public AssignmentInfo(string _survivorId, string _work, string _defense)
        {
            survivorId = _survivorId;
            work = _work;
            defense = _defense;
        }
    }
    public readonly struct ExpeditionStartInfo
    {
        public readonly string regionId;
        public readonly string[] party;
        public readonly int expeditionId;
        public ExpeditionStartInfo(string _regionId, string[] _party, int _expeditionId = 0)
        {
            regionId = _regionId;
            party = _party;
            expeditionId = _expeditionId;
        }
    }
    // rescuedId는 이 정산에서 합류한 구조 인력의 새 ID다. 없으면 null이다.
    public readonly struct ExpeditionResultInfo
    {
        public readonly string regionId, rescuedId;
        public readonly int reward, expeditionId;
        public readonly string[] casualties, returned;
        public readonly bool withdrawn;
        public ExpeditionResultInfo(string _regionId, int _reward, string[] _casualties, string[] _returned, int _expeditionId = 0, bool _withdrawn = false, string _rescuedId = null)
        {
            regionId = _regionId;
            reward = _reward;
            casualties = _casualties;
            returned = _returned;
            expeditionId = _expeditionId;
            withdrawn = _withdrawn;
            rescuedId = _rescuedId;
        }
    }
    // 필드 이름은 기준서 7.4를 따른다. decision의 값은 rescue·search·continue·withdraw·timeout·forced다.
    public readonly struct ExpeditionEventRaisedInfo
    {
        public readonly int expeditionId;
        public readonly string type;
        public ExpeditionEventRaisedInfo(int _expeditionId, string _type)
        {
            expeditionId = _expeditionId;
            type = _type;
        }
    }
    public readonly struct ExpeditionEventDecisionInfo
    {
        public readonly int expeditionId;
        public readonly string decision;
        public ExpeditionEventDecisionInfo(int _expeditionId, string _decision)
        {
            expeditionId = _expeditionId;
            decision = _decision;
        }
    }
    public readonly struct ExpeditionEventResolvedInfo
    {
        public readonly int expeditionId;
        public readonly string outcome;
        public ExpeditionEventResolvedInfo(int _expeditionId, string _outcome)
        {
            expeditionId = _expeditionId;
            outcome = _outcome;
        }
    }
    public readonly struct BuildingInfo
    {
        public readonly BuildingType type;
        public readonly Vector3 position;
        public readonly int cost;
        public readonly string id;
        public BuildingInfo(BuildingType _type, Vector3 _position, int _cost, string _id = null)
        {
            type = _type;
            position = _position;
            cost = _cost;
            id = _id;
        }
    }
    public readonly struct WaveStartInfo
    {
        public readonly int waveIndex;
        public WaveStartInfo(int _waveIndex)
        {
            waveIndex = _waveIndex;
        }
    }
    public readonly struct WaveEndInfo
    {
        public readonly int waveIndex, alive, scrap;
        public readonly float coreHp;
        // 기준서 7.4의 WaveEnded(index, alive, injured, recruited, scrap). injured는 경상+중상 수(TD-52).
        public readonly int injured, recruited;
        public WaveEndInfo(int _waveIndex, float _coreHp, int _alive, int _scrap, int _injured, int _recruited)
        {
            waveIndex = _waveIndex;
            coreHp = _coreHp;
            alive = _alive;
            scrap = _scrap;
            injured = _injured;
            recruited = _recruited;
        }
    }
    public readonly struct SurvivorDeathInfo
    {
        public readonly string survivorId, cause;
        public SurvivorDeathInfo(string _survivorId, string _cause)
        {
            survivorId = _survivorId;
            cause = _cause;
        }
    }
    // 필드 이름은 기준서 7.4의 로그 항목을 따른다.
    public readonly struct VisitorArrivedInfo
    {
        public readonly string candidateId;
        public readonly int wave;
        public VisitorArrivedInfo(string _candidateId, int _wave)
        {
            candidateId = _candidateId;
            wave = _wave;
        }
    }
    public readonly struct RecruitmentAcceptedInfo
    {
        public readonly string candidateId;
        public readonly int cost, aliveCount;
        public RecruitmentAcceptedInfo(string _candidateId, int _cost, int _aliveCount)
        {
            candidateId = _candidateId;
            cost = _cost;
            aliveCount = _aliveCount;
        }
    }
    public readonly struct RecruitmentRejectedInfo
    {
        public readonly string reason;
        public RecruitmentRejectedInfo(string _reason)
        {
            reason = _reason;
        }
    }
    public readonly struct SurvivorJoinedInfo
    {
        public readonly string newId, source;
        public SurvivorJoinedInfo(string _newId, string _source)
        {
            newId = _newId;
            source = _source;
        }
    }

    public readonly struct SurvivorInjuredInfo
    {
        public readonly string id, status;
        public readonly float hp;
        public SurvivorInjuredInfo(string _id, float _hp, string _status)
        {
            id = _id;
            hp = _hp;
            status = _status;
        }
    }
    public readonly struct TreatmentStartedInfo
    {
        public readonly string patientId, workerId;
        public readonly int cost;
        public TreatmentStartedInfo(string _patientId, string _workerId, int _cost)
        {
            patientId = _patientId;
            workerId = _workerId;
            cost = _cost;
        }
    }
    public readonly struct TreatmentPausedInfo
    {
        public readonly string patientId;
        public TreatmentPausedInfo(string _patientId)
        {
            patientId = _patientId;
        }
    }
    public readonly struct TreatmentResumedInfo
    {
        public readonly string patientId, workerId;
        public TreatmentResumedInfo(string _patientId, string _workerId)
        {
            patientId = _patientId;
            workerId = _workerId;
        }
    }
    public readonly struct TreatmentCompletedInfo
    {
        public readonly string patientId;
        public readonly float hp;
        public TreatmentCompletedInfo(string _patientId, float _hp)
        {
            patientId = _patientId;
            hp = _hp;
        }
    }
    public readonly struct TreatmentCancelledInfo
    {
        public readonly string patientId, reason;
        public TreatmentCancelledInfo(string _patientId, string _reason)
        {
            patientId = _patientId;
            reason = _reason;
        }
    }
    public readonly struct TreatmentRejectedInfo
    {
        public readonly string reason;
        public TreatmentRejectedInfo(string _reason)
        {
            reason = _reason;
        }
    }

    // 기준서 7.4. stat은 Combat, Work, Exploration이다.
    public readonly struct ExperienceGainedInfo
    {
        public readonly string id, stat;
        public readonly int amount;
        public ExperienceGainedInfo(string _id, string _stat, int _amount)
        {
            id = _id;
            stat = _stat;
            amount = _amount;
        }
    }

    public readonly struct StatImprovedInfo
    {
        public readonly string id, stat;
        public readonly int old, @new;
        public StatImprovedInfo(string _id, string _stat, int _old, int _new)
        {
            id = _id;
            stat = _stat;
            old = _old;
            @new = _new;
        }
    }

    // ── POC 이벤트 ──
    public static event Action<SessionStartInfo> SessionStarted;
    public static event Action<SessionEndInfo> SessionEnded;
    public static event Action<PhaseInfo> PhaseChanged;
    public static event Action<AssignmentInfo> AssignmentChanged;
    public static event Action<ExpeditionStartInfo> ExpeditionStarted;
    public static event Action<ExpeditionResultInfo> ExpeditionResolved;
    public static event Action<ExpeditionEventRaisedInfo> ExpeditionEventRaised;
    public static event Action<ExpeditionEventDecisionInfo> ExpeditionEventDecided;
    public static event Action<ExpeditionEventResolvedInfo> ExpeditionEventResolved;
    public static event Action<BuildingInfo> BuildingPlaced;
    public static event Action<WaveStartInfo> WaveStarted;
    public static event Action<WaveEndInfo> WaveEnded;
    public static event Action<SurvivorDeathInfo> SurvivorDied;
    public static event Action<VisitorArrivedInfo> VisitorArrived;
    public static event Action<RecruitmentAcceptedInfo> RecruitmentAccepted;
    public static event Action<RecruitmentRejectedInfo> RecruitmentRejected;
    public static event Action<SurvivorJoinedInfo> SurvivorJoined;
    public static event Action<SurvivorInjuredInfo> SurvivorInjured;
    public static event Action<TreatmentStartedInfo> TreatmentStarted;
    public static event Action<TreatmentPausedInfo> TreatmentPaused;
    public static event Action<TreatmentResumedInfo> TreatmentResumed;
    public static event Action<TreatmentCompletedInfo> TreatmentCompleted;
    public static event Action<TreatmentCancelledInfo> TreatmentCancelled;
    public static event Action<TreatmentRejectedInfo> TreatmentRejected;
    public static event Action<ExperienceGainedInfo> ExperienceGained;
    public static event Action<StatImprovedInfo> StatImproved;

    public static void RaiseSessionStarted(int _seed) => Emit(SessionStarted, new SessionStartInfo(_seed));
    public static void RaiseSessionEnded(SessionResult _result, string _reason, int _seed) => Emit(SessionEnded, new SessionEndInfo(_result, _reason, _seed));
    public static void RaisePhaseChanged(SessionPhase _phase, int _waveIndex) => Emit(PhaseChanged, new PhaseInfo(_phase, _waveIndex));
    public static void RaiseAssignmentChanged(string _survivorId, string _work, string _defense) => Emit(AssignmentChanged, new AssignmentInfo(_survivorId, _work, _defense));
    public static void RaiseExpeditionStarted(string _regionId, string[] _party, int _expeditionId = 0) => Emit(ExpeditionStarted, new ExpeditionStartInfo(_regionId, _party, _expeditionId));
    public static void RaiseExpeditionResolved(string _regionId, int _reward, string[] _casualties, string[] _returned, int _expeditionId = 0, bool _withdrawn = false, string _rescuedId = null) => Emit(ExpeditionResolved, new ExpeditionResultInfo(_regionId, _reward, _casualties, _returned, _expeditionId, _withdrawn, _rescuedId));
    public static void RaiseExpeditionEventRaised(int _expeditionId, string _type) => Emit(ExpeditionEventRaised, new ExpeditionEventRaisedInfo(_expeditionId, _type));
    public static void RaiseExpeditionEventDecided(int _expeditionId, string _decision) => Emit(ExpeditionEventDecided, new ExpeditionEventDecisionInfo(_expeditionId, _decision));
    public static void RaiseExpeditionEventResolved(int _expeditionId, string _outcome) => Emit(ExpeditionEventResolved, new ExpeditionEventResolvedInfo(_expeditionId, _outcome));
    public static void RaiseBuildingPlaced(BuildingType _type, Vector3 _position, int _cost, string _id = null) => Emit(BuildingPlaced, new BuildingInfo(_type, _position, _cost, _id));
    public static void RaiseWaveStarted(int _waveIndex) => Emit(WaveStarted, new WaveStartInfo(_waveIndex));
    public static void RaiseWaveEnded(int _waveIndex, float _coreHp, int _alive, int _scrap, int _injured, int _recruited) => Emit(WaveEnded, new WaveEndInfo(_waveIndex, _coreHp, _alive, _scrap, _injured, _recruited));
    public static void RaiseSurvivorDied(string _survivorId, string _cause) => Emit(SurvivorDied, new SurvivorDeathInfo(_survivorId, _cause));
    public static void RaiseVisitorArrived(string _candidateId, int _wave) => Emit(VisitorArrived, new VisitorArrivedInfo(_candidateId, _wave));
    public static void RaiseRecruitmentAccepted(string _candidateId, int _cost, int _aliveCount) => Emit(RecruitmentAccepted, new RecruitmentAcceptedInfo(_candidateId, _cost, _aliveCount));
    public static void RaiseRecruitmentRejected(string _reason) => Emit(RecruitmentRejected, new RecruitmentRejectedInfo(_reason));
    public static void RaiseSurvivorJoined(string _newId, string _source) => Emit(SurvivorJoined, new SurvivorJoinedInfo(_newId, _source));
    public static void RaiseSurvivorInjured(string _id, float _hp, string _status) => Emit(SurvivorInjured, new SurvivorInjuredInfo(_id, _hp, _status));
    public static void RaiseTreatmentStarted(string _patientId, string _workerId, int _cost) => Emit(TreatmentStarted, new TreatmentStartedInfo(_patientId, _workerId, _cost));
    public static void RaiseTreatmentPaused(string _patientId) => Emit(TreatmentPaused, new TreatmentPausedInfo(_patientId));
    public static void RaiseTreatmentResumed(string _patientId, string _workerId) => Emit(TreatmentResumed, new TreatmentResumedInfo(_patientId, _workerId));
    public static void RaiseTreatmentCompleted(string _patientId, float _hp) => Emit(TreatmentCompleted, new TreatmentCompletedInfo(_patientId, _hp));
    public static void RaiseTreatmentCancelled(string _patientId, string _reason) => Emit(TreatmentCancelled, new TreatmentCancelledInfo(_patientId, _reason));
    public static void RaiseTreatmentRejected(string _reason) => Emit(TreatmentRejected, new TreatmentRejectedInfo(_reason));
    public static void RaiseExperienceGained(string _id, string _stat, int _amount) => Emit(ExperienceGained, new ExperienceGainedInfo(_id, _stat, _amount));
    public static void RaiseStatImproved(string _id, string _stat, int _old, int _new) => Emit(StatImproved, new StatImprovedInfo(_id, _stat, _old, _new));

    // ── 이벤트 ──
    public static event Action<RunInfo> RunStarted;
    public static event Action<RunInfo> RunEnded;
    public static event Action<SceneInfo> SceneChanged;
    public static event Action<UpgradeInfo> UpgradeGranted;
    public static event Action<PanelInfo> PanelVisibility;

    // ── 발행 ──
    public static void RaiseRunStarted(string _scene, double _time) => Emit(RunStarted, new RunInfo(_scene, _time));
    public static void RaiseRunEnded(string _scene, double _time, string _reason) => Emit(RunEnded, new RunInfo(_scene, _time, _reason));
    public static void RaiseUpgradeGranted(UpgradeData _upgrade)
    {
        if (_upgrade == null)
        {
            return;
        }

        Emit(UpgradeGranted, new UpgradeInfo(_upgrade.id, string.IsNullOrEmpty(_upgrade.displayName) ? _upgrade.id : _upgrade.displayName));
    }
    public static void RaisePanelVisibility(string _panelName, bool _open) => Emit(PanelVisibility, new PanelInfo(_panelName, _open));

    // 구독자 하나가 예외를 던져도 나머지 구독자와 게임 진행은 멈추지 않는다.
    static void Emit<T>(Action<T> _handlers, T _payload)
    {
        if (_handlers == null)
        {
            return;
        }

        foreach (var handler in _handlers.GetInvocationList())
        {
            try
            {
                ((Action<T>)handler).Invoke(_payload);
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogError($"이벤트 구독자 예외({handler.Method.Name}): {error.Message}");
            }
        }
    }

    // ── 진단용 순번 ──
    // 로그에서 같은 공격·판정을 대조하기 위한 증가 번호. 배열 순서나 Object instance ID에 의존하지 않는다.
    static int SEQUENCE;
    public static int NextId() => ++SEQUENCE;

    // ── 씬 진입/진행을 자동으로 흘린다(별도 설치 불필요) ──
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState()
    {
        RunStarted = null;
        RunEnded = null;
        SceneChanged = null;
        UpgradeGranted = null;
        PanelVisibility = null;
        SessionStarted = null;
        SessionEnded = null;
        PhaseChanged = null;
        AssignmentChanged = null;
        ExpeditionStarted = null;
        ExpeditionResolved = null;
        ExpeditionEventRaised = null;
        ExpeditionEventDecided = null;
        ExpeditionEventResolved = null;
        BuildingPlaced = null;
        WaveStarted = null;
        WaveEnded = null;
        SurvivorDied = null;
        VisitorArrived = null;
        RecruitmentAccepted = null;
        RecruitmentRejected = null;
        SurvivorJoined = null;
        SurvivorInjured = null;
        TreatmentStarted = null;
        TreatmentPaused = null;
        TreatmentResumed = null;
        TreatmentCompleted = null;
        TreatmentCancelled = null;
        TreatmentRejected = null;
        SEQUENCE = 0;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        HOOKED = false;
    }

    static bool HOOKED;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void InstallSceneHook()
    {
        if (HOOKED)
        {
            return;
        }

        HOOKED = true;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Emit(SceneChanged, new SceneInfo(SceneManager.GetActiveScene().name, true));
    }

    static void OnSceneLoaded(Scene _scene, LoadSceneMode _mode) => Emit(SceneChanged, new SceneInfo(_scene.name, _mode != LoadSceneMode.Additive));
}
