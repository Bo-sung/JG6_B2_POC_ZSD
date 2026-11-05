using UnityEngine;
using UnityEngine.AI;

// 방벽. 프리팹의 NavMeshObstacle(carve)이 길을 막고, 적은 막힌 경로에서 이 시설을 부순다.
[RequireComponent(typeof(NavMeshObstacle))]
public sealed class WallBuilding : BuildingBase
{
    public override BuildingType Type => BuildingType.Wall;
}
