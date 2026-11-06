using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 선택과 명령 입력. 좌클릭 단일, 드래그 다중, Shift 추가, 우클릭 바닥=이동/적=공격.
// UI 위에서 누른 클릭은 월드 명령으로 넘기지 않는다.
[DisallowMultipleComponent]
public sealed class RTSInput : MonoBehaviour
{
    [SerializeField] GameSession session;
    [SerializeField] Camera worldCamera;
    [SerializeField] RectTransform selectionBox;
    // 조작 보정값이다. 씬에서 조정한다.
    [SerializeField] float dragThreshold = 8f;
    [SerializeField] float pickRadius = 0.4f;
    [SerializeField] float rayDistance = 300f;
    [SerializeField] float navSampleRadius = 8f;
    [SerializeField] float moveSpread = 1.5f;

    readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    readonly List<string> selectionBuffer = new List<string>();
    PointerEventData pointerData;
    Action<Vector3> pendingDefensePick;
    bool leftPressedInWorld;
    Vector2 pressPosition;
    bool isDragging;
    bool wasLeftHeld;
    bool wasRightHeld;

    bool worldInputBlocked;
    int unblockedFrame = -1;
    public bool WorldInputBlocked
    {
        get => worldInputBlocked;
        set
        {
            if (worldInputBlocked && !value)
            {
                unblockedFrame = Time.frameCount;
            }
            worldInputBlocked = value;
        }
    }
    public event Action<string> CommandFeedback;
    public event Action InfirmaryClicked;
    public TowerBuilding PickedDefenseTower { get; private set; }
    public SelectionModel Selection { get; private set; }
    public bool IsPickingDefense => pendingDefensePick != null;

    void Awake()
    {
        Selection = new SelectionModel(CanSelect);
        Selection.Changed += ApplySelectionVisuals;
    }

    void OnEnable()
    {
        GameEvents.SurvivorDied += OnSurvivorDied;
    }

    void OnDisable()
    {
        GameEvents.SurvivorDied -= OnSurvivorDied;
    }

    void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }
        Selection.Prune();
        if (IsPickingDefense && (Selection.Count == 0 || !session.CanChangeDefense))
        {
            CancelDefensePick();
        }
        Vector2 position = mouse.position.ReadValue();
        // 프레임 밖 InputSystem.Update로 들어온 입력은 wasPressedThisFrame에 잡히지 않아 누름 상태 변화도 함께 본다.
        bool leftHeld = mouse.leftButton.isPressed;
        bool rightHeld = mouse.rightButton.isPressed;
        bool leftPressed = mouse.leftButton.wasPressedThisFrame || (leftHeld && !wasLeftHeld);
        bool leftReleased = mouse.leftButton.wasReleasedThisFrame || (!leftHeld && wasLeftHeld);
        bool rightPressed = mouse.rightButton.wasPressedThisFrame || (rightHeld && !wasRightHeld);
        wasLeftHeld = leftHeld;
        wasRightHeld = rightHeld;
        if (WorldInputBlocked || unblockedFrame == Time.frameCount)
        {
            leftPressedInWorld = false;
            isDragging = false;
            selectionBox.gameObject.SetActive(false);
            return;
        }
        if (leftPressed)
        {
            leftPressedInWorld = !IsOverUi(position);
            pressPosition = position;
            isDragging = false;
        }
        if (leftPressedInWorld && leftHeld && !IsPickingDefense)
        {
            UpdateDrag(position);
        }
        if (leftReleased)
        {
            OnLeftReleased(position);
        }
        if (rightPressed && !IsOverUi(position))
        {
            OnRightPressed(position);
        }
    }

    // ── 공개 API(패널이 호출) ──
    public void SelectFromList(string _id)
    {
        if (WorldInputBlocked || unblockedFrame == Time.frameCount)
        {
            return;
        }
        if (IsShiftHeld())
        {
            Selection.Add(_id);
            return;
        }
        Selection.Replace(new[] { _id });
    }

    // 다음 바닥 클릭 위치를 넘겨준다. 우클릭이나 단계 변경으로 취소된다.
    public void BeginDefensePick(Action<Vector3> _onPicked)
    {
        FindFirstObjectByType<BuildController>()?.Cancel();
        PickedDefenseTower = null;
        pendingDefensePick = _onPicked;
    }

    public void CancelDefensePick()
    {
        pendingDefensePick = null;
    }

    // ── 입력 처리 ──
    // 쓰러진 인력도 선택해 상태를 볼 수 있다. 명령은 GameSession이 거부한다.
    bool CanSelect(string _id)
    {
        SurvivorUnit unit = session.FindUnit(_id);
        return unit != null && unit.State.IsAlive && !unit.State.IsAway;
    }

    void ApplySelectionVisuals()
    {
        if (Selection.Count == 0)
        {
            CancelDefensePick();
        }
        foreach (SurvivorUnit unit in session.Units)
        {
            if (CanSelect(unit.State.Id))
            {
                unit.SetSelected(Selection.Contains(unit.State.Id));
            }
        }
    }

    void OnSurvivorDied(GameEvents.SurvivorDeathInfo _info)
    {
        Selection.Prune();
    }

    static bool IsShiftHeld()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.shiftKey.isPressed;
    }

    // 모듈의 프레임 상태에 기대지 않고 이번 위치의 UI를 직접 조사한다.
    bool IsOverUi(Vector2 _position)
    {
        EventSystem system = EventSystem.current;
        if (!system)
        {
            return false;
        }
        pointerData ??= new PointerEventData(system);
        pointerData.position = _position;
        uiHits.Clear();
        system.RaycastAll(pointerData, uiHits);
        return uiHits.Count > 0;
    }

    void UpdateDrag(Vector2 _position)
    {
        if (!isDragging && (_position - pressPosition).magnitude >= dragThreshold)
        {
            isDragging = true;
            selectionBox.gameObject.SetActive(true);
        }
        if (isDragging)
        {
            Vector2 min = Vector2.Min(pressPosition, _position);
            Vector2 max = Vector2.Max(pressPosition, _position);
            var canvas = selectionBox.parent as RectTransform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, min, null, out Vector2 localMin);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, max, null, out Vector2 localMax);
            selectionBox.localPosition = localMin;
            selectionBox.sizeDelta = localMax - localMin;
        }
    }

    void OnLeftReleased(Vector2 _position)
    {
        if (!leftPressedInWorld)
        {
            return;
        }
        leftPressedInWorld = false;
        if (IsPickingDefense)
        {
            PickedDefenseTower = PickComponent<TowerBuilding>(_position, GameLayers.OBSTACLE_MASK);
            Vector3 point = PickedDefenseTower ? PickedDefenseTower.StationPosition : Vector3.zero;
            if (PickedDefenseTower || TryGetGroundPoint(_position, out point))
            {
                Action<Vector3> onPicked = pendingDefensePick;
                pendingDefensePick = null;
                onPicked(point);
            }
            return;
        }
        if (isDragging)
        {
            isDragging = false;
            selectionBox.gameObject.SetActive(false);
            SelectInScreenRect(Vector2.Min(pressPosition, _position), Vector2.Max(pressPosition, _position));
            return;
        }
        SelectAtPoint(_position);
    }

    void SelectAtPoint(Vector2 _position)
    {
        SurvivorUnit unit = PickComponent<SurvivorUnit>(_position, GameLayers.PLAYER_MASK);
        bool additive = IsShiftHeld();
        if (unit && CanSelect(unit.State.Id))
        {
            if (additive)
            {
                Selection.Add(unit.State.Id);
            }
            else
            {
                Selection.Replace(new[] { unit.State.Id });
            }
            return;
        }
        // 시설은 생존자에 가려지지 않을 때만 눌린다.
        if (PickComponent<InfirmaryFacility>(_position, GameLayers.OBSTACLE_MASK))
        {
            InfirmaryClicked?.Invoke();
            return;
        }
        if (!additive)
        {
            Selection.Clear();
        }
    }

    void SelectInScreenRect(Vector2 _min, Vector2 _max)
    {
        selectionBuffer.Clear();
        if (IsShiftHeld())
        {
            selectionBuffer.AddRange(Selection.Ids);
        }
        foreach (SurvivorUnit unit in session.Units)
        {
            if (!unit.State.CanAct)
            {
                continue;
            }
            Vector3 screen = worldCamera.WorldToScreenPoint(unit.transform.position);
            if (screen.z > 0 && screen.x >= _min.x && screen.x <= _max.x && screen.y >= _min.y && screen.y <= _max.y)
            {
                selectionBuffer.Add(unit.State.Id);
            }
        }
        Selection.Replace(selectionBuffer);
    }

    void OnRightPressed(Vector2 _position)
    {
        if (IsPickingDefense)
        {
            CancelDefensePick();
            return;
        }
        if (Selection.Count == 0)
        {
            return;
        }
        // 탑에 붙은 적도 공격할 수 있게 광선에서 더 가까운 쪽을 고른다.
        TowerBuilding tower = PickComponent<TowerBuilding>(_position, GameLayers.OBSTACLE_MASK, out float towerDistance);
        EnemyUnit enemy = PickComponent<EnemyUnit>(_position, GameLayers.ENEMY_MASK, out float enemyDistance);
        if (tower && (!enemy || towerDistance < enemyDistance))
        {
            AssignDefense(tower.StationPosition, tower);
            return;
        }
        if (enemy)
        {
            bool commanded = false;
            foreach (string id in Selection.Ids)
            {
                commanded |= session.CommandAttack(id, enemy);
            }
            if (!commanded)
            {
                CommandFeedback?.Invoke("명령할 수 없는 인원입니다.");
            }
            return;
        }
        if (TryGetGroundPoint(_position, out Vector3 point))
        {
            if (session.Phase == SessionPhase.Prep)
            {
                AssignDefense(point, null);
            }
            else if (session.CanCommand)
            {
                CommandFeedback?.Invoke(IssueMove(point) ? "직접 이동 · 방어 배정 유지" : "명령할 수 없는 인원입니다.");
            }
            else
            {
                CommandFeedback?.Invoke("결과·종료 단계에서는 명령할 수 없습니다.");
            }
        }
    }

    public void AssignDefense(Vector3 _point, TowerBuilding _tower)
    {
        var notices = new List<string>();
        IReadOnlyList<string> ids = Selection.Ids;
        for (int index = 0; index < ids.Count; index++)
        {
            // 다중 선택의 두 번째 이후 인원은 기존 배정을 그대로 둔다.
            if (_tower && index > 0)
            {
                if (!notices.Contains("감시탑 정원 1명 · 나머지 인원 배정 유지"))
                {
                    notices.Add("감시탑 정원 1명 · 나머지 인원 배정 유지");
                }
                continue;
            }
            string error;
            bool ok = _tower ? session.TrySetDefenseStation(ids[index], _tower, out error)
                : session.TrySetDefensePoint(ids[index], SpreadPoint(_point, index, ids.Count), out error);
            string notice = error ?? (ok ? (_tower ? "탑 입장 지시 · 방어 배정 저장" : "방어 위치 저장 · 바로 이동") : "배정할 수 없습니다.");
            if (!notices.Contains(notice))
            {
                notices.Add(notice);
            }
        }
        CommandFeedback?.Invoke(string.Join("\n", notices));
    }

    // 여러 명은 지점 주위 원으로 펼쳐 같은 점을 두고 다투지 않게 한다.
    bool IssueMove(Vector3 _point)
    {
        bool commanded = false;
        IReadOnlyList<string> ids = Selection.Ids;
        for (int index = 0; index < ids.Count; index++)
        {
            commanded |= session.CommandMove(ids[index], SpreadPoint(_point, index, ids.Count));
        }
        return commanded;
    }

    public Vector3 SpreadPoint(Vector3 _point, int _index, int _count)
    {
        if (_count <= 1)
        {
            return _point;
        }
        float angle = Mathf.PI * 2 * _index / _count;
        Vector3 offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * moveSpread;
        return NavMesh.SamplePosition(_point + offset, out NavMeshHit hit, navSampleRadius, NavMesh.AllAreas) ? hit.position : _point;
    }

    T PickComponent<T>(Vector2 _position, int _mask) where T : Component
    {
        return PickComponent<T>(_position, _mask, out _);
    }

    T PickComponent<T>(Vector2 _position, int _mask, out float _distance) where T : Component
    {
        _distance = float.MaxValue;
        Ray ray = worldCamera.ScreenPointToRay(_position);
        RaycastHit[] hits = Physics.SphereCastAll(ray, pickRadius, rayDistance, _mask, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (_left, _right) => _left.distance.CompareTo(_right.distance));
        foreach (RaycastHit hit in hits)
        {
            T component = hit.collider.GetComponentInParent<T>();
            if (component)
            {
                _distance = hit.distance;
                return component;
            }
        }
        return null;
    }

    // 벽을 눌러도 가까운 걸을 수 있는 지점으로 옮긴다.
    bool TryGetGroundPoint(Vector2 _position, out Vector3 _point)
    {
        _point = default;
        Ray ray = worldCamera.ScreenPointToRay(_position);
        if (!Physics.Raycast(ray, out RaycastHit hit, rayDistance, GameLayers.WORLD_MASK, QueryTriggerInteraction.Ignore))
        {
            return false;
        }
        if (!NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, navSampleRadius, NavMesh.AllAreas))
        {
            return false;
        }
        _point = navHit.position;
        return true;
    }
}
