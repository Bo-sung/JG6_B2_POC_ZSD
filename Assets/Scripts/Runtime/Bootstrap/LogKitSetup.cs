using System;
using LogKit;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// LogKit 자동 부트스트랩(AfterSceneLoad) 이전에 서버 주소·키를 코드로 넣는다.
// Debug.Log 계열 로그는 LogKit이 자동 수집해 전송하므로 개별 호출이 필요 없다.
// 분석용 이벤트는 GameLogger.Data(category, message, data)로 보내고 Docs/PLAY_LOG.md 규칙을 따른다.
public static class LogKitSetup
{
    // LogKit이 큐를 닫기 전에 반드시 한 번 실행해야 하는 작업(판 종료 로그 등)을 여기 등록한다.
    public static event Action BeforeShutdown;

    // 서버 주소와 업로드 키는 코드에 두지 않는다.
    //   빌드: LogKit 빌드 단계가 CI 환경변수 LOGKIT_SERVER_URL, LOGKIT_UPLOAD_KEY 값을 넣는다.
    //   에디터: 같은 환경변수를 PC에 설정하면 여기서 적용하고, 없으면 GameLogger Inspector 값을 쓴다.
    const string ServerUrlVariable = "LOGKIT_SERVER_URL";
    const string UploadKeyVariable = "LOGKIT_UPLOAD_KEY";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetShutdownHooks()
    {
        RemoveShutdownHooks();
        BeforeShutdown = null;
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
        string serverUrl = Environment.GetEnvironmentVariable(ServerUrlVariable);
        string apiKey = Environment.GetEnvironmentVariable(UploadKeyVariable);
        if (!string.IsNullOrWhiteSpace(serverUrl) || !string.IsNullOrWhiteSpace(apiKey))
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
    static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.ExitingPlayMode) return;
        PrepareQuit();
        RemoveShutdownHooks();
    }
#endif
}
