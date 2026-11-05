using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 씬에 배치된 미리보기와 구역 표시를 제어한다. 생성은 설치 확정 시 세션만 수행한다.
[DefaultExecutionOrder(-20)]
public sealed class BuildController : MonoBehaviour
{
    public enum Mode { None, Wall, Tower, Repair }

    [SerializeField] GameSession session;
    [SerializeField] RTSInput input;
    [SerializeField] Camera worldCamera;
    [SerializeField] GameObject wallPreview;
    [SerializeField] GameObject towerPreview;
    [SerializeField] BuildZone[] zones;
    // 입력 및 표시 보정값. 게임 밸런스 수치가 아니다.
    [SerializeField] float rayDistance = 300f;
    [SerializeField] Color validColor = new Color(0.2f, 1f, 0.45f);
    [SerializeField] Color invalidColor = new Color(1f, 0.15f, 0.15f);
    [SerializeField] Key rotateKey = Key.R;
    [SerializeField] float quarterTurn = 90f;

    readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    static readonly int BASE_COLOR = Shader.PropertyToID("_BaseColor");
    PointerEventData pointer;
    MaterialPropertyBlock tint;
    bool leftWasHeld;
    bool rightWasHeld;
    bool rotateWasHeld;
    bool escapeWasHeld;
    bool leftBeganInWorld;
    float yaw;
    string notice;
    public Mode CurrentMode { get; private set; }
    public bool IsActive => CurrentMode != Mode.None;
    public bool IsValid { get; private set; }
    public Vector3 PreviewPosition { get; private set; }
    public string Message { get; private set; } = "방벽·감시탑 설치 또는 수리를 선택하세요.";
    public BuildingBase RepairTarget { get; private set; }

    public void Begin(Mode _mode)
    {
        if (!session.CanBuild)
        {
            Message = "준비 단계에만 건설·수리할 수 있습니다.";
            return;
        }
        input.CancelDefensePick();
        CurrentMode = _mode;
        input.WorldInputBlocked = true;
        leftBeganInWorld = false;
        notice = null;
    }

    public void Cancel()
    {
        CurrentMode = Mode.None;
        input.WorldInputBlocked = false;
        leftBeganInWorld = false;
        notice = null;
        HidePreviews();
        foreach (BuildZone zone in zones)
        {
            zone.SetVisible(false);
        }
        Message = "방벽·감시탑 설치 또는 수리를 선택하세요.";
    }

    void OnDisable()
    {
        Cancel();
    }

    void HidePreviews()
    {
        wallPreview.SetActive(false);
        towerPreview.SetActive(false);
    }

    bool IsOverUi(Vector2 _position)
    {
        if (!EventSystem.current)
        {
            return false;
        }
        pointer ??= new PointerEventData(EventSystem.current);
        pointer.position = _position;
        uiHits.Clear();
        EventSystem.current.RaycastAll(pointer, uiHits);
        return uiHits.Count > 0;
    }

    void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }
        bool left = mouse.leftButton.isPressed;
        bool right = mouse.rightButton.isPressed;
        bool pressed = mouse.leftButton.wasPressedThisFrame || (left && !leftWasHeld);
        bool released = mouse.leftButton.wasReleasedThisFrame || (!left && leftWasHeld);
        Keyboard keyboard = Keyboard.current;
        bool rotateHeld = keyboard != null && keyboard[rotateKey].isPressed;
        bool escapeHeld = keyboard != null && keyboard.escapeKey.isPressed;
        bool rotatePressed = keyboard != null && (keyboard[rotateKey].wasPressedThisFrame || (rotateHeld && !rotateWasHeld));
        bool cancel = mouse.rightButton.wasPressedThisFrame || (right && !rightWasHeld)
            || (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || (escapeHeld && !escapeWasHeld)));
        rotateWasHeld = rotateHeld;
        escapeWasHeld = escapeHeld;
        leftWasHeld = left;
        rightWasHeld = right;
        if (!IsActive)
        {
            return;
        }
        if (cancel)
        {
            Cancel();
            return;
        }
        bool overUi = IsOverUi(mouse.position.ReadValue());
        if (pressed)
        {
            leftBeganInWorld = !overUi;
            notice = null;
        }
        foreach (BuildZone zone in zones)
        {
            zone.SetVisible(session.CanBuild);
        }
        if (rotatePressed && CurrentMode == Mode.Wall)
        {
            yaw += quarterTurn;
        }
        HidePreviews();
        Ray ray = worldCamera.ScreenPointToRay(mouse.position.ReadValue());
        IsValid = false;
        RepairTarget = null;
        if (CurrentMode == Mode.Repair)
        {
            if (!overUi && Physics.Raycast(ray, out RaycastHit targetHit, rayDistance, GameLayers.OBSTACLE_MASK))
            {
                RepairTarget = targetHit.collider.GetComponentInParent<BuildingBase>();
            }
            IsValid = session.CanBuild && RepairTarget && RepairTarget.IsDamaged && session.Scrap >= RepairTarget.RepairCost;
            Message = !session.CanBuild ? "수리는 준비 단계에만 가능합니다."
                : RepairTarget ? $"수리 비용 {RepairTarget.RepairCost} 스크랩 · HP {RepairTarget.Hp:0}/{RepairTarget.MaxHp:0}"
                : "손상된 시설을 클릭하세요. 파괴된 시설은 재설치하세요.";
            if (released && leftBeganInWorld && !overUi)
            {
                int cost = RepairTarget ? RepairTarget.RepairCost : 0;
                notice = session.TryRepairBuilding(RepairTarget, out string error) ? $"수리 완료 · 스크랩 {cost} 사용" : error;
            }
        }
        else if (!overUi && Physics.Raycast(ray, out RaycastHit ground, rayDistance, GameLayers.GROUND_MASK, QueryTriggerInteraction.Ignore))
        {
            BuildingType type = CurrentMode == Mode.Wall ? BuildingType.Wall : BuildingType.Tower;
            Quaternion rotation = Quaternion.Euler(0, CurrentMode == Mode.Wall ? yaw : 0, 0);
            PreviewPosition = session.SnapToGrid(ground.point);
            IsValid = session.CanPlaceBuilding(type, PreviewPosition, rotation, out string error);
            GameObject preview = CurrentMode == Mode.Wall ? wallPreview : towerPreview;
            preview.transform.SetPositionAndRotation(PreviewPosition, rotation);
            preview.SetActive(true);
            tint ??= new MaterialPropertyBlock();
            tint.SetColor(BASE_COLOR, IsValid ? validColor : invalidColor);
            foreach (Renderer renderer in preview.GetComponentsInChildren<Renderer>())
            {
                renderer.SetPropertyBlock(tint);
            }
            Message = IsValid ? "설치 가능 · 좌클릭 설치 · R 방벽 회전 · 우클릭/Esc 취소" : error;
            if (released && leftBeganInWorld && !overUi)
            {
                notice = session.TryPlaceBuilding(type, PreviewPosition, rotation, out _, out error) ? "설치 완료 · 연속 설치 가능" : error;
            }
        }
        else
        {
            Message = session.CanBuild ? "맵에서 위치를 고르세요. 우클릭/Esc 취소" : "준비 단계에만 건설할 수 있습니다.";
        }
        if (!string.IsNullOrEmpty(notice) && CurrentMode == Mode.Repair)
        {
            // 호버 비용을 유지하면서 마지막 수리 결과도 보여준다. 설치 사유는 현재 후보 위치를 우선한다.
            Message = RepairTarget && session.CanBuild ? Message + "\n" + notice : notice;
        }
        if (released)
        {
            leftBeganInWorld = false;
        }
    }
}
