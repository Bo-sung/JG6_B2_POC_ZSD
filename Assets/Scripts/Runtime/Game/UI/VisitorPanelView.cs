using System;
using UnityEngine;
using UnityEngine.UI;

// 방문자 영입 패널. 기존 패널 양식(제목·본문·버튼·상태 문구)을 그대로 쓴다.
public sealed class VisitorPanelView : PanelBase
{
    [SerializeField] Text titleText;
    [SerializeField] Text infoText;
    [SerializeField] Text statusText;
    [SerializeField] Button acceptButton;
    [SerializeField] Button declineButton;
    [SerializeField] Button closeButton;

    public event Action AcceptClicked;
    public event Action DeclineClicked;
    public event Action CloseClicked;

    protected override void Awake()
    {
        acceptButton.onClick.AddListener(() => AcceptClicked?.Invoke());
        declineButton.onClick.AddListener(() => DeclineClicked?.Invoke());
        closeButton.onClick.AddListener(() => CloseClicked?.Invoke());
        base.Awake();
    }

    public void Show(string _title, string _info, string _status)
    {
        titleText.text = _title;
        infoText.text = _info;
        statusText.text = _status;
    }
}
