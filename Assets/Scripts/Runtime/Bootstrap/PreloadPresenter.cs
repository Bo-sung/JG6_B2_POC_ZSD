using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 씬에 배치한 View를 사용하며 실패하면 씬을 이동하지 않는다.
public sealed class PreloadPresenter : MonoBehaviour
{
    [SerializeField] PreloadView view;
    // 첫 오류의 원인을 읽을 수 있게 제한하는 화면 표시 값이며 검증 규칙에는 영향을 주지 않는다.
    [SerializeField, Min(1)] int maxDisplayedErrors = 4;
    readonly List<string> errors = new List<string>();

    void OnEnable() => Application.logMessageReceived += CaptureError;
    void OnDisable() => Application.logMessageReceived -= CaptureError;

    void CaptureError(string _message, string _stack, LogType _type)
    {
        if ((_type == LogType.Error || _type == LogType.Exception || _type == LogType.Assert) && errors.Count < maxDisplayedErrors)
        {
            errors.Add(_message);
        }
    }

    IEnumerator Start()
    {
        // LogKit AfterSceneLoad 초기화 및 첫 화면 렌더 뒤에 검증한다.
        yield return null;
        try
        {
            TableManager tables = TableManager.Instance.RequireValid();
            string next = tables.NextSceneOf(SceneManager.GetActiveScene().name);
            if (string.IsNullOrEmpty(next) || !Application.CanStreamedLevelBeLoaded(next))
            {
                throw new InvalidOperationException("Flow 다음 씬을 빌드 목록에서 찾을 수 없습니다: " + next);
            }
            SceneManager.LoadScene(next);
        }
        catch (Exception exception)
        {
            view.Show("테이블·씬 검증 실패\n" + exception.Message + "\n" + string.Join("\n", errors));
            Debug.LogError("Preload 중단: " + exception);
        }
    }
}
