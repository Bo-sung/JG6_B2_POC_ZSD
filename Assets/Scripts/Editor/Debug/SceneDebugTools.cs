using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// 특정 씬을 바로 듣는 디버그 진입.
//   편집 중: 저장 확인 후 해당 씬을 열고, 그 상태로 Play에 진입한다(playModeStartScene을 임시로 바꿨다가 되돌린다).
//   플레이 중: 현재 실행을 끊지 않고 해당 씬으로 새로 진입한다. 진입용 상태는 일회용이며 빌드에 넣지 않는다.
// 목록은 FlowTableData(씬 진행 경로)와 EditorBuildSettings의 활성 씬을 합쳐 만든다.
[InitializeOnLoad]
public static class SceneDebugTools
{
    const string PENDING_KEY = "MyJG.Debug.PendingScene";
    const string PREVIOUS_START_KEY = "MyJG.Debug.PreviousStartScene";
    const string RESTORE_START_KEY = "MyJG.Debug.RestoreStartScene";
    public const string WINDOW_MENU = "Tools/MyJG/Debug/Jump To Scene";

    [Serializable]
    sealed class Entry
    {
        public string scene;
    }

    static SceneDebugTools() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

    [MenuItem(WINDOW_MENU)]
    static void OpenWindow() => EditorWindow.GetWindow<JumpWindow>(true, "씬 직행");

    sealed class JumpWindow : EditorWindow
    {
        Vector2 scroll;
        string search = string.Empty;
        bool buildOnly;

        void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("검색", GUILayout.Width(34));
            search = GUILayout.TextField(search, EditorStyles.toolbarSearchField);
            if (GUILayout.Toggle(buildOnly, "빌드 씬만", EditorStyles.toolbarButton, GUILayout.Width(70)) != buildOnly)
            {
                buildOnly = !buildOnly;
            }

            EditorGUILayout.EndHorizontal();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var option in Options())
            {
                if (!string.IsNullOrEmpty(search) && option.scene.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (buildOnly && !option.buildScene)
                {
                    continue;
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(option.scene, GUILayout.MinWidth(220));
                GUILayout.Label(option.note, EditorStyles.miniLabel, GUILayout.MinWidth(180));
                GUI.enabled = !EditorApplication.isCompiling;
                if (GUILayout.Button("이동", GUILayout.Width(56)))
                {
                    Jump(option.scene);
                }

                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.HelpBox(
                EditorApplication.isPlaying ? "플레이 중: 선택한 씬으로 새로 진입합니다. 진입 상태는 일회용입니다."
                                           : "편집 중: 선택한 씬을 연 뒤 Play를 누르면 그 상태로 진입합니다.",
                MessageType.Info);
        }

        // 씬 표의 경로를 따라 순서를 매기고, 표에 없는 빌드 씬은 뒤에 덧붙인다.
        static List<(string scene, string note, bool buildScene)> Options()
        {
            var result = new List<(string, string, bool)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var tables = TableManager.Instance;
            foreach (var link in tables.Scenes)
            {
                if (link == null || string.IsNullOrEmpty(link.scene) || !seen.Add(link.scene))
                {
                    continue;
                }

                string next = string.IsNullOrEmpty(link.nextScene) ? "최종 씬" : "다음: " + link.nextScene;
                result.Add((link.scene, next, true));
            }
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (!scene.enabled || string.IsNullOrEmpty(scene.path))
                {
                    continue;
                }

                string name = System.IO.Path.GetFileNameWithoutExtension(scene.path);
                if (!seen.Add(name))
                {
                    continue;
                }

                result.Add((name, "빌드 목록의 씬(테이블 미등록)", true));
            }
            return result;
        }
    }

    public static void Jump(string _target)
    {
        if (string.IsNullOrEmpty(_target))
        {
            return;
        }

        if (EditorApplication.isCompiling || (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying))
        {
            return;
        }

        var sceneAsset = FindScene(_target);
        if (!sceneAsset)
        {
            throw new InvalidOperationException($"씬 '{_target}'을(를) 찾지 못했습니다. 씬 이름과 프로젝트의 씬 에셋을 확인하세요.");
        }

        if (!EditorApplication.isPlaying)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(AssetDatabase.GetAssetPath(sceneAsset), OpenSceneMode.Single);
        }
        TableManager.ReloadAfterTableEdit();

        // 테이블이 있으면 검증 후 진입(잘못된 데이터로 조용히 동작하지 않는다).
        var tables = TableManager.Instance;
        if (!tables.IsValid)
        {
            Debug.LogWarning("테이블 검증이 실패했습니다. 진입은 하지만 오류를 먼저 고치는 것이 좋습니다.");
        }

        if (EditorApplication.isPlaying)
        {
            SetPending(_target);
            EditorApplication.isPaused = false;
            Time.timeScale = 1;
            SceneManager.LoadSceneAsync(_target);
            return;
        }

        SetPending(_target);
        SessionState.SetString(PREVIOUS_START_KEY, EditorSceneManager.playModeStartScene ? AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) : string.Empty);
        SessionState.SetBool(RESTORE_START_KEY, true);
        EditorSceneManager.playModeStartScene = sceneAsset;
        EditorApplication.EnterPlaymode();
    }

    // 편집 중 진입 예정 정보(일회용). 편집기 쪽 정리에만 쓴다.
    static void SetPending(string _target) => SessionState.SetString(PENDING_KEY, JsonUtility.ToJson(new Entry { scene = _target }));

    static SceneAsset FindScene(string _name)
    {
        foreach (string id in AssetDatabase.FindAssets(_name + " t:Scene"))
        {
            string path = AssetDatabase.GUIDToAssetPath(id);
            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            if (asset && asset.name == _name)
            {
                return asset;
            }
        }
        return null;
    }

    static void OnPlayModeChanged(PlayModeStateChange _state)
    {
        if (_state != PlayModeStateChange.EnteredPlayMode && _state != PlayModeStateChange.EnteredEditMode)
        {
            return;
        }

        if (SessionState.GetBool(RESTORE_START_KEY, false))
        {
            string path = SessionState.GetString(PREVIOUS_START_KEY, string.Empty);
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            SessionState.EraseBool(RESTORE_START_KEY);
            SessionState.EraseString(PREVIOUS_START_KEY);
        }
        if (_state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.EraseString(PENDING_KEY);
        }
    }
}
