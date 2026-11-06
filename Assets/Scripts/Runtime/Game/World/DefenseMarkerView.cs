using UnityEngine;

// 프리팹에 배치된 마커·연결선·사거리 원의 점과 선택 상태만 갱신한다.
[DisallowMultipleComponent]
public sealed class DefenseMarkerView : MonoBehaviour
{
    [SerializeField] WorldLabelView label;
    [SerializeField] GameObject highlight;
    [SerializeField] Transform ring;
    [SerializeField] LineRenderer rangeRing;
    [SerializeField] float ringHeightScale = 0.01f;
    [SerializeField] float normalScale = 0.55f;
    [SerializeField] float selectedScale = 1.4f;
    [SerializeField] float rangeHeight = 0.1f;
    float range;
    Vector3 rangeCenter;

    public void SetLabel(string _text)
    {
        label.SetText(_text + " 방어 위치");
    }

    public void Place(Vector3 _position)
    {
        transform.position = _position;
    }

    public void SetRange(float _range, Vector3 _center)
    {
        range = _range;
        rangeCenter = _center;
    }

    void LateUpdate()
    {
        if (rangeRing.enabled)
        {
            DrawCircle(rangeRing, rangeCenter, range, rangeHeight);
        }
    }

    public void SetHighlighted(bool _highlighted)
    {
        highlight.SetActive(_highlighted);
        label.gameObject.SetActive(_highlighted);
        float size = _highlighted ? selectedScale : normalScale;
        ring.localScale = new Vector3(size, ringHeightScale, size);
        rangeRing.enabled = _highlighted;
    }

    public static void DrawCircle(LineRenderer _line, Vector3 _center, float _radius, float _height)
    {
        for (int index = 0; index < _line.positionCount; index++)
        {
            float angle = 2 * Mathf.PI * index / _line.positionCount;
            _line.SetPosition(index, new Vector3(_center.x + Mathf.Cos(angle) * _radius, _height, _center.z + Mathf.Sin(angle) * _radius));
        }
    }
}
