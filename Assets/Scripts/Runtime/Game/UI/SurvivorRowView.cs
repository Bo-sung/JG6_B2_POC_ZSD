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
        Critical,
    }

    public enum Condition
    {
        None,
        Light,
        Critical,
        Treating,
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
    [SerializeField] Color criticalColor = new Color(0.56f, 0.2f, 0.1f, 0.95f);
    [SerializeField] Color lightStatusColor = new Color(1f, 0.82f, 0.32f);
    [SerializeField] Color criticalStatusColor = new Color(1f, 0.55f, 0.42f);
    [SerializeField] Color treatingStatusColor = new Color(0.45f, 0.92f, 1f);

    public event Action Clicked;

    void Awake()
    {
        button.onClick.AddListener(() => Clicked?.Invoke());
    }

    public void Show(string _name, string _hp, string _status, Look _look, Condition _condition = Condition.None)
    {
        nameText.text = _name;
        hpText.text = _hp;
        statusText.text = _status;
        statusText.color = _condition switch
        {
            Condition.Light => lightStatusColor,
            Condition.Critical => criticalStatusColor,
            Condition.Treating => treatingStatusColor,
            _ => Color.white,
        };
        background.color = _look switch
        {
            Look.Selected => selectedColor,
            Look.Away => awayColor,
            Look.Dead => deadColor,
            Look.Critical => criticalColor,
            _ => normalColor,
        };
        button.interactable = _look != Look.Dead && _look != Look.Away;
    }
}
