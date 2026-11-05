using System;
using UnityEngine;
using UnityEngine.UI;

// View는 Hud 프리팹에 저장한다. 표시와 버튼 이벤트만 맡는다.
public sealed class BuildPanelView : MonoBehaviour
{
    [SerializeField] Button wallButton;
    [SerializeField] Button towerButton;
    [SerializeField] Button repairButton;
    [SerializeField] Button cancelButton;
    [SerializeField] Text wallLabel;
    [SerializeField] Text towerLabel;
    [SerializeField] Text statusText;
    public event Action WallClicked;
    public event Action TowerClicked;
    public event Action RepairClicked;
    public event Action CancelClicked;

    void Awake()
    {
        wallButton.onClick.AddListener(() => WallClicked?.Invoke());
        towerButton.onClick.AddListener(() => TowerClicked?.Invoke());
        repairButton.onClick.AddListener(() => RepairClicked?.Invoke());
        cancelButton.onClick.AddListener(() => CancelClicked?.Invoke());
    }

    public void Show(int _wallCost, int _towerCost, bool _canBuild, string _message)
    {
        wallLabel.text = $"방벽 · {_wallCost} 스크랩";
        towerLabel.text = $"감시탑 · {_towerCost} 스크랩";
        wallButton.interactable = _canBuild;
        towerButton.interactable = _canBuild;
        repairButton.interactable = _canBuild;
        statusText.text = _canBuild ? _message : "건설·수리 비활성 · 준비 단계 전용\n" + _message;
    }
}
