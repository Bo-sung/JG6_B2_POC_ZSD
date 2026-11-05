using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class TitlePresenter : MonoBehaviour
{
    [SerializeField] TitleView view;
    bool launching;
    void OnEnable() => view.StartClicked += Launch;
    void OnDisable() => view.StartClicked -= Launch;

    void Launch()
    {
        if (launching)
        {
            return;
        }
        string text = view.SeedText.Trim();
        int seed;
        if (text.Length == 0)
        {
            seed = new System.Random().Next();
        }
        else if (!int.TryParse(text, out seed))
        {
            view.Show("시드는 정수로 입력하세요. 비우면 무작위입니다.");
            return;
        }
        try
        {
            TableManager tables = TableManager.Instance.RequireValid();
            string next = tables.NextSceneOf(SceneManager.GetActiveScene().name);
            if (string.IsNullOrEmpty(next) || !Application.CanStreamedLevelBeLoaded(next))
            {
                throw new InvalidOperationException("Flow 다음 씬을 찾을 수 없습니다: " + next);
            }
            SessionLaunch.SetSeed(seed);
            launching = true;
            SceneManager.LoadScene(next);
        }
        catch (Exception exception)
        {
            view.Show(exception.Message);
            Debug.LogError("시작 실패: " + exception);
        }
    }
}
