using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// 프로젝트 자기 점검. 사람 눈으로 확인하기 쉬운 설정 어긋남을 먼저 잡는다.
// 항목: 빈 스크립트 참조 / 테이블 검증 / 빌드 씬 / 레이어-TagManager 동기화 / 입력 설정 / Play Mode 옵션
public static class ProjectValidation
{
    public const string MENU = "Tools/MyJG/Validate Project";

    [MenuItem(MENU)]
    static void ValidateMenuItem()
    {
        string report = Validate();
        Debug.Log(report);
        EditorUtility.DisplayDialog("프로젝트 검증", report, "확인");
    }

    public static string Validate()
    {
        var issues = new List<string>();
        var info = new List<string>();

        // 1) 열려 있는 씬의 빈 스크립트 참조
        int missing = 0, objects = 0;
        foreach (var transform in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            objects++;
            foreach (var component in transform.GetComponents<MonoBehaviour>())
            {
                if (component == null)
                {
                    missing++;
                }
            }
        }
        if (missing != 0)
        {
            issues.Add($"스크립트 참조 누락 {missing}개(씬에서 각 컴포넌트를 확인하세요).");
        }

        // 2) 테이블
        var tables = TableManager.Instance;
        if (!tables.IsValid)
        {
            issues.Add("게임 테이블 검증 실패: 콘솔에 출력된 오류 항목을 먼저 수정하세요.");
        }
        else
        {
            info.Add($"테이블 정상: 해금 {tables.Upgrades.Count}건 / 씬 경로 {tables.Scenes.Count}건");
        }

        // 3) 빌드 씬 목록
        int enabledScenes = 0;
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled && !string.IsNullOrEmpty(scene.path))
            {
                enabledScenes++;
            }
        }

        if (enabledScenes == 0)
        {
            issues.Add("EditorBuildSettings에 활성 씬이 없습니다. BuildEntry가 시작 씬을 찾지 못합니다.");
        }
        else
        {
            info.Add($"빌드 활성 씬 {enabledScenes}개");
        }

        // 4) GameLayers와 TagManager.asset의 레이어 이름 동기화
        foreach (var (number, name) in GameLayers.TAG_MANAGER_NAMES)
        {
            string actual = LayerMask.LayerToName(number);
            if (string.IsNullOrEmpty(actual))
            {
                issues.Add($"레이어 {number}가 TagManager에 비어 있습니다. GameLayers는 '{name}'을(를) 기대합니다.");
            }
            else if (actual != name)
            {
                issues.Add($"레이어 {number} 불일치: TagManager='{actual}' / GameLayers='{name}'");
            }
        }

        // 5) 입력: 새 Input System만 사용(activeInputHandler 1 = Input System Package.New only)
        bool newInput = System.Type.GetType("UnityEngine.InputSystem.InputSystem, Unity.InputSystem") != null;
        if (!newInput)
        {
            issues.Add("새 Input System 패키지가 없습니다(구 Input API 사용 금지 규칙을 강제할 수 없습니다).");
        }

        string handler = ActiveInputHandler();
        if (handler == "0")
        {
            issues.Add("Player Settings의 Active Input Handling이 Input Manager(Old)입니다. 새 Input System만 쓰려면 'Input System Package (New)'로 바꿉니다.");
        }
        else if (handler == "2")
        {
            info.Add("Active Input Handling = Both. 구 Input API를 쓰지 않으므로 'Input System Package (New)'로 조이면 실수를 막을 수 있습니다.");
        }
        else if (handler == "1")
        {
            info.Add("Active Input Handling = Input System Package (New)");
        }

        // 6) Enter Play Mode Options: 도메인 리로드를 꺼도 정적 상태는 ResetStaticState로 초기화된다
        info.Add(EditorSettings.enterPlayModeOptionsEnabled
            ? $"Enter Play Mode Options 켜짐(옵션 {EditorSettings.enterPlayModeOptions}). 정적 초기화: TableManager/GameEvents/LogKitSetup"
            : "Enter Play Mode Options 꺼짐(표준 동작)");

        var report = new StringBuilder();
        report.Append(issues.Count == 0 ? "검증 통과\n" : "문제 발견\n");
        foreach (string line in info)
        {
            report.Append("  · ").Append(line).Append('\n');
        }

        foreach (string line in issues)
        {
            report.Append("  ! ").Append(line).Append('\n');
        }

        if (issues.Count == 0)
        {
            report.Append("  · 정적 상태 초기화는 RuntimeInitializeOnLoadMethod(SubsystemRegistration) 규약을 따릅니다.");
        }

        return report.ToString().TrimEnd();
    }

    // ProjectSettings.asset에서 Active Input Handling 값을 읽는다(공개 API가 없는 항목이라 본문을 파싱한다).
    static string ActiveInputHandler()
    {
        string path = System.IO.Path.Combine(Application.dataPath, "..", "ProjectSettings", "ProjectSettings.asset");
        if (!System.IO.File.Exists(path))
        {
            return string.Empty;
        }

        foreach (string line in File.ReadAllLines(path))
        {
            if (line.TrimStart().StartsWith("activeInputHandler:", System.StringComparison.Ordinal))
            {
                return line.Split(':')[1].Trim();
            }
        }

        return string.Empty;
    }

    // 열려 있는 씬 이름(참고 정보용).
    public static string OpenSceneNames()
    {
        var names = new List<string>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            names.Add(SceneManager.GetSceneAt(i).name);
        }

        return string.Join(", ", names);
    }
}
