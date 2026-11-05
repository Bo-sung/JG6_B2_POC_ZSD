using System;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class FileLogStore
{
    readonly string baseFolder;

    public FileLogStore(string _baseFolder)
    {
        if (string.IsNullOrWhiteSpace(_baseFolder))
        {
            throw new ArgumentException("로그 기준 폴더가 필요합니다.", nameof(_baseFolder));
        }
        baseFolder = _baseFolder;
    }

    // 테스트의 파일 경로는 Unity 실행 환경에 의존하지 않게 한다.
    public static FileLogStore CreateDefault()
    {
        return new FileLogStore(Application.persistentDataPath);
    }

    public string PathFor(string _sessionId)
    {
        if (string.IsNullOrWhiteSpace(_sessionId) || _sessionId == "." || _sessionId == ".." || _sessionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || _sessionId.Contains("/") || _sessionId.Contains("\\"))
        {
            throw new ArgumentException("세션 ID는 파일 이름이어야 합니다.", nameof(_sessionId));
        }
        return Path.Combine(baseFolder, "Telemetry", _sessionId + ".jsonl");
    }

    internal StreamWriter Open(string _sessionId)
    {
        string path = PathFor(_sessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        // 같은 ID를 다시 열어 이전 기록을 덮거나 sequence를 섞지 않는다.
        var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
        writer.AutoFlush = true;
        return writer;
    }
}

