using UnityEngine;

// 사전 배치된 치료소 1개(기준서 3.1). 담당자 자리 하나와 환자 대기 자리를 나눠 준다. 규칙은 TreatmentSystem이 판단한다.
[DisallowMultipleComponent]
public sealed class InfirmaryFacility : MonoBehaviour
{
    [SerializeField] Transform workerSlot;
    [SerializeField] Transform[] waitSlots;

    string[] occupants;

    public Vector3 WorkerPosition => workerSlot.position;
    public int WaitSlotCount => waitSlots.Length;

    public void Initialize()
    {
        occupants = new string[waitSlots.Length];
    }

    // 이미 자리가 있으면 그 자리를 돌려준다. 빈 자리가 없으면 false.
    public bool TryClaimWait(string _id, out Vector3 _position)
    {
        int free = -1;
        for (int index = 0; index < occupants.Length; index++)
        {
            if (occupants[index] == _id)
            {
                _position = waitSlots[index].position;
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
        _position = waitSlots[free].position;
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
