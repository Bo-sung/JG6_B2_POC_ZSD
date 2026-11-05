using System;
using UnityEngine;

// 웨이브 결과, 패배, 승리 패널. 다음과 재시작 버튼을 가진다.
public sealed class ResultPanelView : PanelBase
{
    [SerializeField] UnityEngine.UI.Text titleText;
    [SerializeField] UnityEngine.UI.Text bodyText;
    [SerializeField] UnityEngine.UI.Button nextButton;
    [SerializeField] UnityEngine.UI.Text nextLabel;
    [SerializeField] UnityEngine.UI.Button restartButton;

    public event Action NextClicked;
    public event Action RestartClicked;

    protected override void Awake()
    {
        nextButton.onClick.AddListener(() => NextClicked?.Invoke());
        restartButton.onClick.AddListener(() => RestartClicked?.Invoke());
        base.Awake();
    }

    // 다음 문구가 비어 있으면 다음 버튼을 숨긴다.
    public void ShowResult(string _title, string _body, string _nextLabel)
    {
        titleText.text = _title;
        bodyText.text = _body;
        bool hasNext = !string.IsNullOrEmpty(_nextLabel);
        nextButton.gameObject.SetActive(hasNext);
        nextLabel.text = hasNext ? _nextLabel : string.Empty;
        Open();
    }
}
