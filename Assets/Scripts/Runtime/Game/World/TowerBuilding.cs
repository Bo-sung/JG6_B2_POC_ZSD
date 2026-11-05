using UnityEngine;

// 사격은 생존자만 한다. 지상 접근 자리와 탑 위 표시 자리를 분리한다.
public sealed class TowerBuilding : BuildingBase, ICombatStation
{
    [SerializeField] Transform approach;
    [SerializeField] Transform seat;
    GameSession session;
    SurvivorCombatData combat;

    public override BuildingType Type => BuildingType.Tower;
    public string StationId => GetInstanceID().ToString();
    public Vector3 StationPosition => approach.position;
    public Vector3 SeatPosition => seat.position;
    public float RangeBonus => combat.towerRangeBonus;
    public float DamageMultiplier => combat.towerDamageMultiplier;
    public bool ProtectsOccupant => Hp > 0;
    public bool IsOccupied
    {
        get
        {
            if (!session || Hp <= 0)
            {
                return false;
            }
            foreach (SurvivorUnit unit in session.Units)
            {
                if (unit && unit.OccupiedTower == this)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public override void Initialize(BuildingStatData _stat)
    {
        base.Initialize(_stat);
        session = FindFirstObjectByType<GameSession>();
        combat = TableManager.Instance.RequireValid().Gameplay.survivorCombat;
    }

    protected override void OnDestroyed()
    {
        session.VacateTower(this);
    }
}
