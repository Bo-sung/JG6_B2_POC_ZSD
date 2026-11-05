using UnityEngine;

// 기본 생산 시설의 작업 자리. 정원은 AssignmentBoard가 판단하고 여기는 자리만 나눠 준다.
[DisallowMultipleComponent]
public sealed class ProductionFacility : MonoBehaviour
{
    [SerializeField] Transform[] workSlots;

    string[] occupants;

    public int SlotCount => workSlots.Length;

    public void Initialize()
    {
        occupants = new string[workSlots.Length];
    }

    // 이미 자리가 있으면 그 자리를 돌려준다. 빈 자리가 없으면 false.
    public bool TryClaim(string _id, out Vector3 _position)
    {
        int free = -1;
        for (int index = 0; index < occupants.Length; index++)
        {
            if (occupants[index] == _id)
            {
                _position = workSlots[index].position;
                return true;
            }
            if (free < 0 && occupants[index] == null)
            {
                free = index;
            }
        }
        if (free < 0)
        {
            _position = default;
            return false;
        }
        occupants[free] = _id;
        _position = workSlots[free].position;
        return true;
    }

    public void Release(string _id)
    {
        for (int index = 0; index < occupants.Length; index++)
        {
            if (occupants[index] == _id)
            {
                occupants[index] = null;
            }
        }
    }
}
