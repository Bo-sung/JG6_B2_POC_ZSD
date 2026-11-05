using UnityEngine;

// 공습 경보 표시와 짧은 알림(사망 등).
[DisallowMultipleComponent]
public sealed class AlertBannerView : MonoBehaviour
{
    [SerializeField] GameObject alertRoot;
    [SerializeField] UnityEngine.UI.Text alertText;
    [SerializeField] GameObject noticeRoot;
    [SerializeField] UnityEngine.UI.Text noticeText;
    [SerializeField] float noticeSeconds = 4f;

    public float NoticeSeconds => noticeSeconds;

    public void ShowAlert(bool _visible, string _text)
    {
        alertRoot.SetActive(_visible);
        alertText.text = _text;
    }

    public void ShowNotice(bool _visible, string _text)
    {
        noticeRoot.SetActive(_visible);
        noticeText.text = _text;
    }
}
