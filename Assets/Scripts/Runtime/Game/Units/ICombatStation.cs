using UnityEngine;

// 생존자가 배치되어 전투 보너스를 받는 자리. 감시탑(TowerBuilding)이 구현한다.
// SurvivorUnit은 구체 시설을 모르고 이 인터페이스만 본다.
public interface ICombatStation
{
    string StationId { get; }
    Vector3 StationPosition { get; }
    float RangeBonus { get; }
    float DamageMultiplier { get; }
    bool ProtectsOccupant { get; }
}
