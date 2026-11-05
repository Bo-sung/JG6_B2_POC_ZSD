using UnityEngine;

// 월드 공간 이름표. 유닛이 돌아도 항상 카메라를 향한다.
[DisallowMultipleComponent]
public sealed class WorldLabelView : MonoBehaviour
{
    [SerializeField] UnityEngine.UI.Text text;

    Transform cameraTransform;

    public void SetText(string _text)
    {
        text.text = _text;
    }

    void LateUpdate()
    {
        if (!cameraTransform)
        {
            Camera main = Camera.main;
            if (!main)
            {
                return;
            }
            cameraTransform = main.transform;
        }
        transform.rotation = cameraTransform.rotation;
    }
}
