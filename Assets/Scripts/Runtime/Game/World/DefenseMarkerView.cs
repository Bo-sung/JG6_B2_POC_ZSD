using UnityEngine;

// 맵 위의 방어 위치 표시. 씬에 생존자마다 하나씩 배치한다.
[DisallowMultipleComponent]
public sealed class DefenseMarkerView : MonoBehaviour
{
    [SerializeField] WorldLabelView label;
    [SerializeField] GameObject highlight;

    public void SetLabel(string _text)
    {
        label.SetText(_text);
    }

    public void Place(Vector3 _position)
    {
        transform.position = _position;
    }

    public void SetHighlighted(bool _highlighted)
    {
        highlight.SetActive(_highlighted);
    }
}
