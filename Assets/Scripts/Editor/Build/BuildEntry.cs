using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// CI·로컬 공통 빌드 진입점. 씬 이름을 코드에 하드코딩하지 않고 EditorBuildSettings의 활성 씬을 순서대로 쓴다.
public static class BuildEntry
{
    public const string OUTPUT_MENU = "Tools/MyJG/Build Windows (Development)";

    [MenuItem(OUTPUT_MENU)]
    public static string BuildConfiguredTarget()
    {
        string project = Directory.GetParent(Application.dataPath).FullName;
        string destination = Path.Combine(project, "Artifacts", "Windows");
        Directory.CreateDirectory(destination);

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EnabledScenes(),
            locationPathName = Path.Combine(destination, PlayerSettings.productName + ".exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development | BuildOptions.DetailedBuildReport
        });

        string summary = $"Result: {report.summary.result}\nErrors: {report.summary.totalErrors}\nWarnings: {report.summary.totalWarnings}\nBytes: {report.summary.totalSize}\nDuration: {report.summary.totalTime}\nOutput: {report.summary.outputPath}\n";
        File.WriteAllText(Path.Combine(destination, "BuildReport.txt"), summary);
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(summary);
        }

        Debug.Log(summary);
        return summary;
    }

    // 첫 활성 씬이 시작 씬이며, 꺼진 샌드박스 씬은 제외된다.
    static string[] EnabledScenes()
    {
        var scenes = new List<string>();
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled && !string.IsNullOrEmpty(scene.path))
            {
                scenes.Add(scene.path);
            }
        }

        if (scenes.Count == 0)
        {
            throw new InvalidOperationException("EditorBuildSettings에 활성 씬이 없습니다.");
        }

        return scenes.ToArray();
    }
}
