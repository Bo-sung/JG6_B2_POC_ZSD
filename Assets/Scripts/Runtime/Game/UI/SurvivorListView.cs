using System.Collections.Generic;
using UnityEngine;

// 생존자 목록. 행은 프리팹에 7개 배치되어 있고 코드로 만들지 않는다.
[DisallowMultipleComponent]
public sealed class SurvivorListView : MonoBehaviour
{
    [SerializeField] SurvivorRowView[] rows;

    public IReadOnlyList<SurvivorRowView> Rows => rows;
}
