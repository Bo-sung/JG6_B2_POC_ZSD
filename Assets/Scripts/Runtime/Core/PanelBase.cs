using UnityEngine;

// UI 패널 온·오프 규약의 base class.
// 규칙:
//   1) 시작 상태를 코드로 명시한다. 씬에 저장된 activeSelf에 의존하지 않기 위해 Awake에서 항상 SetOpen(startOpen)을 적용한다.
//   2) 비활성화되면 열려 있던 상태·대기 중 동작을 정리한다(OnDisable → Close). 재점성 시 꺼진 상태로 시작한다.
//   3) 표시/은폐는 root.SetActive 한 곳에서만 하고, 서브 오브젝트는 OnShow/OnHide에서 다룬다.
//   4) 열린 여부 변화는 GameEvents.PanelVisibility로 흘린다(입력·카메라·일시정지 쪽이 구독).
[DisallowMultipleComponent]
public abstract class PanelBase : MonoBehaviour
{
    [Tooltip("씬에 저장된 활성 상태와 무관하게 코드로 적용하는 시작 상태")]
    [SerializeField] bool startOpen;
    [SerializeField] GameObject root;          // 비어 있으면 이 gameObject를 사용

    public bool IsOpen { get; private set; }
    public string PanelName => name;
    protected GameObject Root => root ? root : gameObject;
    protected bool Active;                      // OnEnable 이후 표시 동작이 유효한 상태

    // 열 수 있는 조건(플레이 사망·다른 패널 열림 등). 조건이 false면 SetOpen(true)가 거부된다.
    protected virtual bool CanOpen => true;
    // 표시/은폐 직전 hook. 개별 UI는 여기에서 텍스트·풀·커서를 정리한다.
    protected virtual void OnShow()
    {
    }
    protected virtual void OnHide()
    {
    }

    // 시작 상태 명시. 씬 저장 상태가 열려 있어도 startOpen이 false면 껏다가 켠다(온·오프 기능이 시작 상태를 잃지 않게).
    protected virtual void Awake()
    {
        if (!root)
        {
            root = gameObject;
        }

        IsOpen = false;
        Root.SetActive(startOpen);
        IsOpen = startOpen;
        if (startOpen)
        {
            OnShow();
        }
        else
        {
            OnHide();
        }
    }

    protected virtual void OnEnable()
    {
        Active = true;
        SetOpen(IsOpen, _quiet: true);
    }

    // 꺼질 때 정리: 열린 상태·대기 타이머·구독을 남기지 않는다.
    protected virtual void OnDisable()
    {
        Active = false;
        if (IsOpen)
        {
            Close();
        }
    }

    // returns true when the requested state is now applied.
    public bool SetOpen(bool _open, bool _quiet = false)
    {
        if (_open && !CanOpen)
        {
            _open = false;
        }

        bool changed = IsOpen != _open;
        // IsOpen을 먼저 바꾼다. SetActive에 곧장 이어지는 OnEnable·OnDisable이 옛값으로 되돌리면
        // 첫 Open에서 꺼진 상태(IsOpen만 참)로 남는다.
        IsOpen = _open;
        Root.SetActive(_open);
        if (!changed && !_quiet)
        {
            return true;
        }

        if (_open)
        {
            OnShow();
        }
        else
        {
            OnHide();
        }

        if (!_quiet && changed)
        {
            GameEvents.RaisePanelVisibility(PanelName, _open);
        }

        return IsOpen == _open;
    }

    public bool Toggle() => SetOpen(!IsOpen);
    public void Open() => SetOpen(true);
    public void Close() => SetOpen(false);
}
