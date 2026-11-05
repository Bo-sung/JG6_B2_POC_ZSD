using System;
using UnityEngine;

// 생존자 목록의 한 행. 7행 모두 프리팹에 배치되어 있다.
[DisallowMultipleComponent]
public sealed class SurvivorRowView : MonoBehaviour
{
    public enum Look
    {
        Normal,
        Selected,
        Away,
        Dead,
    }

    [SerializeField] UnityEngine.UI.Button button;
    [SerializeField] UnityEngine.UI.Image background;
    [SerializeField] UnityEngine.UI.Text nameText;
    [SerializeField] UnityEngine.UI.Text hpText;
    [SerializeField] UnityEngine.UI.Text statusText;
    [SerializeField] Color normalColor = Color.gray;
    [SerializeField] Color selectedColor = Color.yellow;
    [SerializeField] Color awayColor = Color.blue;
    [SerializeField] Color deadColor = Color.black;

    public event Action Clicked;

    void Awake()
    {
        button.onClick.AddListener(() => Clicked?.Invoke());
    }

    public void Show(string _name, string _hp, string _status, Look _look)
    {
        nameText.text = _name;
        hpText.text = _hp;
        statusText.text = _status;
        background.color = _look switch
        {
            Look.Selected => selectedColor,
            Look.Away => awayColor,
            Look.Dead => deadColor,
            _ => normalColor,
        };
        button.interactable = _look != Look.Dead && _look != Look.Away;
    }
}
