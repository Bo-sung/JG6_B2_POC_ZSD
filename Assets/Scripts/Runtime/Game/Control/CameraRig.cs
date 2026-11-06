using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 쿼터뷰 카메라. 리그는 바닥에서 팬하고, 경사각과 방향은 씬에 배치한 값으로 고정한다.
[DisallowMultipleComponent]
public sealed class CameraRig : MonoBehaviour
{
    [SerializeField] Camera rigCamera;
    // 조작감 값이다. 씬에서 조정한다.
    [SerializeField] float panSpeed = 25f;
    [SerializeField] float zoomStep = 0.1f;
    [SerializeField] float minDistance = 25f;
    [SerializeField] float maxDistance = 90f;
    [SerializeField] Vector2 panMin = new Vector2(-40f, -40f);
    [SerializeField] Vector2 panMax = new Vector2(40f, 40f);

    Vector3 viewDirection;
    float distance;
    readonly List<RaycastResult> uiHits = new List<RaycastResult>();

    void Awake()
    {
        Vector3 offset = rigCamera.transform.position - transform.position;
        distance = offset.magnitude;
        viewDirection = offset.normalized;
    }

    void Update()
    {
        UpdatePan();
        UpdateZoom();
    }

    void UpdatePan()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }
        var input = Vector2.zero;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
        {
            input.y += 1;
        }
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
        {
            input.y -= 1;
        }
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
        {
            input.x += 1;
        }
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
        {
            input.x -= 1;
        }
        if (input == Vector2.zero)
        {
            return;
        }
        Vector3 forward = Vector3.ProjectOnPlane(rigCamera.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(rigCamera.transform.right, Vector3.up).normalized;
        Vector3 next = transform.position + (forward * input.y + right * input.x) * (panSpeed * Time.unscaledDeltaTime);
        next.x = Mathf.Clamp(next.x, panMin.x, panMax.x);
        next.z = Mathf.Clamp(next.z, panMin.y, panMax.y);
        transform.position = next;
    }

    void UpdateZoom()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }
        EventSystem system = EventSystem.current;
        if (system)
        {
            var pointer = new PointerEventData(system) { position = mouse.position.ReadValue() };
            uiHits.Clear();
            system.RaycastAll(pointer, uiHits);
            if (uiHits.Count > 0)
            {
                return;
            }
        }
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0))
        {
            return;
        }
        // 스크롤 단위는 플랫폼과 입력 설정마다 달라 방향만 쓴다.
        distance = Mathf.Clamp(distance * (1 - Mathf.Sign(scroll) * zoomStep), minDistance, maxDistance);
        rigCamera.transform.position = transform.position + viewDirection * distance;
    }
}
