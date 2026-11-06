using UnityEngine;

// 방문 대기 중에만 켜지는 거점 입구 표식. 콜라이더가 없어 클릭 판정에 끼지 않는다(V2-IMPL-15).
// 자기 자신을 껐다 켜면 OnEnable 구독이 유실되므로 visual 자식만 온·오프한다.
// 영입·거절·만료로 결정되면 사라지고, 스크랩 부족·정원 초과로는 계속 대기한다.
[DisallowMultipleComponent]
public sealed class VisitorMarkerView : MonoBehaviour
{
    [SerializeField] GameObject visual;

    void Awake()
    {
        // 씬에 저장된 활성 상태와 무관하게 꺼진 상태로 시작한다.
        SetVisual(false);
    }

    void OnEnable()
    {
        GameEvents.VisitorArrived += OnVisitorArrived;
        GameEvents.RecruitmentAccepted += OnResolved;
        GameEvents.RecruitmentRejected += OnRejected;
        GameEvents.SessionEnded += OnSessionEnded;
    }

    void OnDisable()
    {
        GameEvents.VisitorArrived -= OnVisitorArrived;
        GameEvents.RecruitmentAccepted -= OnResolved;
        GameEvents.RecruitmentRejected -= OnRejected;
        GameEvents.SessionEnded -= OnSessionEnded;
    }

    void OnVisitorArrived(GameEvents.VisitorArrivedInfo _info)
    {
        SetVisual(true);
    }

    void OnResolved(GameEvents.RecruitmentAcceptedInfo _info)
    {
        SetVisual(false);
    }

    void OnRejected(GameEvents.RecruitmentRejectedInfo _info)
    {
        // no_scrap·full은 결정이 아니므로 방문자는 대기 상태를 유지한다.
        if (_info.reason == RecruitmentSystem.REASON_DECLINED || _info.reason == RecruitmentSystem.REASON_EXPIRED)
        {
            SetVisual(false);
        }
    }

    void OnSessionEnded(GameEvents.SessionEndInfo _info)
    {
        SetVisual(false);
    }

    void SetVisual(bool _visible)
    {
        if (visual != null)
        {
            visual.SetActive(_visible);
        }
    }
}
