using UnityEngine;

// 핵심 시설. HP가 0이 되면 GameSession이 패배로 판정한다.
[DisallowMultipleComponent]
public sealed class CoreFacility : MonoBehaviour
{
    [SerializeField] Collider body;

    public float Hp { get; private set; }
    public float MaxHp { get; private set; }
    public bool IsDestroyed => Hp <= 0;

    public void Initialize(float _maxHp)
    {
        MaxHp = _maxHp;
        Hp = _maxHp;
    }

    public void ApplyDamage(float _amount)
    {
        if (_amount > 0)
        {
            Hp = Mathf.Max(0, Hp - _amount);
        }
    }

    public Vector3 ClosestPoint(Vector3 _from) => body.ClosestPoint(_from);
}
