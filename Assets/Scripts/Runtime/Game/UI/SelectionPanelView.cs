using System;
using UnityEngine;

// 선택 패널. 능력치, 작업 배정, 방어 위치 지정, 현재 행동, 배정 복귀.
[DisallowMultipleComponent]
public sealed class SelectionPanelView : MonoBehaviour
{
    [SerializeField] GameObject root;
    [SerializeField] UnityEngine.UI.Text titleText;
    [SerializeField] UnityEngine.UI.Text statsText;
    [SerializeField] UnityEngine.UI.Text workText;
    [SerializeField] UnityEngine.UI.Text defenseText;
    [SerializeField] UnityEngine.UI.Text actionText;
    [SerializeField] UnityEngine.UI.Text messageText;
    [SerializeField] UnityEngine.UI.Button productionButton;
    [SerializeField] UnityEngine.UI.Button standbyButton;
    [SerializeField] UnityEngine.UI.Button defenseButton;
    [SerializeField] UnityEngine.UI.Button returnButton;

    public event Action ProductionClicked;
    public event Action StandbyClicked;
    public event Action DefenseClicked;
    public event Action ReturnClicked;

    void Awake()
    {
        productionButton.onClick.AddListener(() => ProductionClicked?.Invoke());
        standbyButton.onClick.AddListener(() => StandbyClicked?.Invoke());
        defenseButton.onClick.AddListener(() => DefenseClicked?.Invoke());
        returnButton.onClick.AddListener(() => ReturnClicked?.Invoke());
    }

    public void SetVisible(bool _visible)
    {
        root.SetActive(_visible);
    }

    public void Show(string _title, string _stats, string _work, string _defense, string _action, string _message)
    {
        titleText.text = _title;
        statsText.text = _stats;
        workText.text = _work;
        defenseText.text = _defense;
        actionText.text = _action;
        messageText.text = _message;
    }

    public void SetInteractable(bool _work, bool _defense, bool _return)
    {
        productionButton.interactable = _work;
        standbyButton.interactable = _work;
        defenseButton.interactable = _defense;
        returnButton.interactable = _return;
    }
}
