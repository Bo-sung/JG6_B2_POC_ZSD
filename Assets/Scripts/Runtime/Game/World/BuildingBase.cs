using UnityEngine;

// 설치 시설의 공통 동작. HP, 피해, 수리, 파괴. 파괴되면 오브젝트가 사라져 NavMesh 구멍도 함께 닫힌다.
[DisallowMultipleComponent]
public abstract class BuildingBase : MonoBehaviour
{
    [SerializeField] Collider body;
    [SerializeField] Renderer bodyRenderer;
    [SerializeField] Color damagedColor = Color.black;

    static readonly int BASE_COLOR = Shader.PropertyToID("_BaseColor");

    MaterialPropertyBlock block;
    Color intactColor;
    int cost;

    public abstract BuildingType Type { get; }
    public float Hp { get; private set; }
    public float MaxHp { get; private set; }
    public int Cost => cost;
    public bool IsDamaged => Hp < MaxHp;

    public virtual void Initialize(BuildingStatData _stat)
    {
        cost = _stat.cost;
        MaxHp = _stat.maxHp;
        Hp = _stat.maxHp;
        intactColor = bodyRenderer.sharedMaterial.GetColor(BASE_COLOR);
        block = new MaterialPropertyBlock();
        RefreshTint();
    }

    public void ApplyDamage(float _amount)
    {
        if (_amount <= 0 || Hp <= 0)
        {
            return;
        }
        Hp = Mathf.Max(0, Hp - _amount);
        if (Hp <= 0)
        {
            OnDestroyed();
            Destroy(gameObject);
            return;
        }
        RefreshTint();
    }

    // 수리 비용은 손상 비율 × 설치비 올림(DECISIONS 에이전트 기본값).
    public int RepairCost => IsDamaged ? Mathf.CeilToInt((MaxHp - Hp) / MaxHp * cost) : 0;

    public void Repair()
    {
        Hp = MaxHp;
        RefreshTint();
    }

    public Vector3 ClosestPoint(Vector3 _from) => body.ClosestPoint(_from);

    protected virtual void OnDestroyed()
    {
    }

    void RefreshTint()
    {
        bodyRenderer.GetPropertyBlock(block);
        block.SetColor(BASE_COLOR, Color.Lerp(damagedColor, intactColor, Hp / MaxHp));
        bodyRenderer.SetPropertyBlock(block);
    }
}
