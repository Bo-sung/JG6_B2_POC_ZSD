using System;
using UnityEngine;
using UnityEngine.UI;

// 프리팹에 저장된 목적지·인원 지정 UI. 게임 규칙은 Presenter와 세션이 담당한다.
public sealed class ExpeditionPanelView : MonoBehaviour
{
    [SerializeField] Button nearButton;
    [SerializeField] Button farButton;
    [SerializeField] Button dispatchButton;
    [SerializeField] Button[] memberButtons;
    [SerializeField] Text[] memberLabels;
    [SerializeField] Text previewText;
    [SerializeField] Text statusText;
    [SerializeField] Text activeText;
    [SerializeField] Text historyText;
    public event Action<int> RegionClicked;
    public event Action<int> MemberClicked;
    public event Action DispatchClicked;

    void Awake()
    {
        nearButton.onClick.AddListener(() => RegionClicked?.Invoke(0));
        farButton.onClick.AddListener(() => RegionClicked?.Invoke(1));
        dispatchButton.onClick.AddListener(() => DispatchClicked?.Invoke());
        for (int index = 0; index < memberButtons.Length; index++)
        {
            int captured = index;
            memberButtons[index].onClick.AddListener(() => MemberClicked?.Invoke(captured));
        }
    }

    public void ShowMember(int _index, string _label, bool _available)
    {
        memberLabels[_index].text = _label;
        memberButtons[_index].interactable = _available;
    }

    public void Show(string _preview, string _status, bool _canDispatch, string _active, string _history)
    {
        previewText.text = _preview;
        statusText.text = _status;
        dispatchButton.interactable = _canDispatch;
        activeText.text = _active;
        historyText.text = _history;
    }
}
