using UnityEngine;

// 상단 HUD. 웨이브·카운트다운·스크랩·핵심 시설 HP·생존자 수.
[DisallowMultipleComponent]
public sealed class HudView : MonoBehaviour
{
    [SerializeField] UnityEngine.UI.Text waveText;
    [SerializeField] UnityEngine.UI.Text countdownText;
    [SerializeField] UnityEngine.UI.Text scrapText;
    [SerializeField] UnityEngine.UI.Text coreText;
    [SerializeField] UnityEngine.UI.Text survivorText;
    // 방문 대기 중에만 켜지는 '방문자 도착' 항목.
    [SerializeField] UnityEngine.UI.Button visitorButton;
    // 대기 중인 탐사 사건이 있을 때만 켜지는 경고. 탐사 패널은 항상 보이므로 누를 것이 없다(TD-35).
    [SerializeField] GameObject expeditionEventNotice;
    [SerializeField] UnityEngine.UI.Text expeditionEventLabel;
    // 치료소 패널을 여는 항목. 진행 상태를 함께 보여 준다.
    [SerializeField] UnityEngine.UI.Button treatmentButton;
    [SerializeField] UnityEngine.UI.Text treatmentLabel;

    [SerializeField] UnityEngine.UI.Button[] speedButtons;
    public event System.Action<int> SpeedClicked;
    public event System.Action VisitorClicked;
    public event System.Action TreatmentClicked;

    void Awake()
    {
        for (int index = 0; index < speedButtons.Length; index++)
        {
            int option = index;
            speedButtons[index].onClick.AddListener(() => SpeedClicked?.Invoke(option));
        }
        visitorButton.onClick.AddListener(() => VisitorClicked?.Invoke());
        treatmentButton.onClick.AddListener(() => TreatmentClicked?.Invoke());
    }

    public void ShowSpeed(System.Collections.Generic.IReadOnlyList<float> _options, float _speed)
    {
        for (int index = 0; index < speedButtons.Length; index++)
        {
            bool available = index < _options.Count;
            speedButtons[index].gameObject.SetActive(available);
            if (available)
            {
                speedButtons[index].GetComponentInChildren<UnityEngine.UI.Text>().text = $"{_options[index]:0.#}배";
                speedButtons[index].interactable = !Mathf.Approximately(_speed, _options[index]);
            }
        }
    }

    public void Show(string _wave, string _countdown, string _scrap, string _core, string _survivors)
    {
        waveText.text = _wave;
        countdownText.text = _countdown;
        scrapText.text = _scrap;
        coreText.text = _core;
        survivorText.text = _survivors;
    }

    public void ShowTreatment(string _label)
    {
        treatmentLabel.text = _label;
    }

    public void ShowExpeditionEventNotice(int _count, string _label)
    {
        expeditionEventNotice.SetActive(_count > 0);
        expeditionEventLabel.text = _label;
    }

    // 방문 대기 중에만 '방문자 도착' 항목을 켠다.
    public void ShowVisitorNotice(bool _visible)
    {
        visitorButton.gameObject.SetActive(_visible);
    }
}
