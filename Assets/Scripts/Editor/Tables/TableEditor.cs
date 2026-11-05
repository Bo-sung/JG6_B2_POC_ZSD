using System;
using UnityEditor;
using UnityEngine;

// 테이블 편집 지원. JSON을 저장하면 캐시를 무효화해 다음 조회가 최신 원본을 읽고,
// 메뉴에서 수동으로 다시 읽을 수도 있다(플레이 중 재로드는 씬 실행 상태를 재구성하지 못해 막는다).
public sealed class TableEditor : AssetPostprocessor
{
    const string TableFolder = "Assets/Resources/Tables/";
    public const string ReloadMenu = "Tools/MyJG/Tables/Reload Tables";

    [MenuItem(ReloadMenu)]
    static void ReloadTables()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("테이블 다시 읽기는 플레이 종료 후 사용하세요. 씬의 실행 중 상태는 자동 재구성하지 않습니다.");
            return;
        }
        TableManager.ReloadAfterTableEdit();
        if (TableManager.Instance.IsValid) Debug.Log("게임 테이블을 다시 읽었습니다.");
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach (var paths in new[] { imported, deleted, moved, movedFrom })
            foreach (string path in paths)
                if (path.StartsWith(TableFolder, StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.Ordinal))
                {
                    // 다음 조회 시 최신 원본을 한 번 읽는다. 추가 임포트나 씬 수정은 하지 않는다(도메인 리로드 끊김 방지).
                    TableManager.Reload();
                    return;
                }
    }
}
