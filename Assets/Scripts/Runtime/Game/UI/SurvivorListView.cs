using System.Collections.Generic;
using UnityEngine;

// 생존자 목록. 행은 프리팹에 배치되어 있고(9행) 코드로 만들지 않는다.
// 초기 7명보다 행이 많으므로 인원이 늘어도 같은 행을 쓴다.
[DisallowMultipleComponent]
public sealed class SurvivorListView : MonoBehaviour
{
    [SerializeField] SurvivorRowView[] rows;

    public IReadOnlyList<SurvivorRowView> Rows => rows;
}
