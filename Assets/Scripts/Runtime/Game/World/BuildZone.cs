using UnityEngine;

// 허용 영역과 표시 메시 모두 씬에 배치한다. 트리거는 이동과 설치 충돌에서 제외한다.
public sealed class BuildZone : MonoBehaviour
{
    [SerializeField] BoxCollider area;
    [SerializeField] Renderer indicator;

    public bool Contains(Vector3 _point)
    {
        Vector3 local = area.transform.InverseTransformPoint(_point) - area.center;
        return Mathf.Abs(local.x) <= area.size.x * 0.5f && Mathf.Abs(local.z) <= area.size.z * 0.5f;
    }

    public void SetVisible(bool _visible)
    {
        indicator.enabled = _visible;
    }
}
