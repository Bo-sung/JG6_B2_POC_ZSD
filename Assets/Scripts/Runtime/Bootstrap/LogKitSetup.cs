using System;
using LogKit;
using UnityEngine;
#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
#endif

// LogKit 자동 부트스트랩(AfterSceneLoad) 이전에 서버 주소·키를 코드로 넣는다.
// Debug.Log 계열 로그는 LogKit이 자동 수집해 전송하므로 개별 호출이 필요 없다.
// 분석 이벤트는 LogGameLifecycle → LogSession → FileLogStore에서만 기록한다.
public static class LogKitSetup
{
    // LogKit이 큐를 닫기 전에 반드시 한 번 실행해야 하는 작업(판 종료 로그 등)을 여기 등록한다.
    public static event Action BeforeShutdown;

    // 서버 주소와 업로드 키는 코드에 두지 않는다.
    //   빌드: LogKit 빌드 단계가 CI 환경변수 LOGKIT_SERVER_URL, LOGKIT_UPLOAD_KEY 값을 넣는다.
    //   에디터: 같은 환경변수를 PC에 설정하면 여기서 적용하고, 없으면 GameLogger Inspector 값을 쓴다.
    const string SERVER_URL_VARIABLE = "LOGKIT_SERVER_URL";
    const string UPLOAD_KEY_VARIABLE = "LOGKIT_UPLOAD_KEY";
#if UNITY_EDITOR
    public const string LOGKIT_QUITTING_FIELD = "_quitting";
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetShutdownHooks()
    {
        RemoveShutdownHooks();
        BeforeShutdown = null;
#if UNITY_EDITOR
        // 도메인 리로드가 꺼져 있으면 LogKit 종료 플래그가 다음 Play까지 남아 인스턴스가 생기지 않는다.
        // 패키지를 고치지 않고 플래그만 되돌린다(DEC-28). 업스트림에 수정이 들어가면 지운다.
        typeof(GameLogger).GetField(LOGKIT_QUITTING_FIELD, BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, false);
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    static void Bootstrap()
    {
        // GameLogger의 AfterSceneLoad 부트스트랩보다 먼저 wantsToQuit에 붙는다.
        Application.wantsToQuit += PrepareQuit;
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.quitting += PrepareEditorQuit;
#endif
        // 기본은 로컬 콘솔·오류 수집만 한다. 업로드는 환경 설정을 둘 다 명시한 경우에만 켠다.
        string serverUrl = Environment.GetEnvironmentVariable(SERVER_URL_VARIABLE);
        string apiKey = Environment.GetEnvironmentVariable(UPLOAD_KEY_VARIABLE);
        bool upload = !string.IsNullOrWhiteSpace(serverUrl) && !string.IsNullOrWhiteSpace(apiKey);
        GameLogger.Configure(captureUnityLogs: true, uploadOnQuit: upload,
            retryPendingOnStartup: upload, uploadIntervalSeconds: upload ? 5f : 0f);
        // 5초는 LogKit 기본 전송 주기이며 게임 밸런스와 무관하다.
        if (upload)
        {
            // 비어 있는 항목은 기존 값(빌드 설정 또는 Inspector)을 유지한다.
            GameLogger.Configure(
                developmentServerUrl: serverUrl,
                releaseServerUrl: serverUrl,
                apiKey: apiKey);
        }
    }

    static bool PrepareQuit()
    {
        BeforeShutdown?.Invoke();
        return true;
    }

    static void RemoveShutdownHooks()
    {
        Application.wantsToQuit -= PrepareQuit;
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.quitting -= PrepareEditorQuit;
#endif
    }

#if UNITY_EDITOR
    static void PrepareEditorQuit() => PrepareQuit();
    static void OnPlayModeChanged(PlayModeStateChange _change)
    {
        if (_change != PlayModeStateChange.ExitingPlayMode)
        {
            return;
        }

        PrepareQuit();
        RemoveShutdownHooks();
    }
#endif
}
