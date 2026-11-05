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
