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

    [SerializeField] UnityEngine.UI.Button[] speedButtons;
    public event System.Action<int> SpeedClicked;

    void Awake()
    {
        for (int index = 0; index < speedButtons.Length; index++)
        {
            int option = index;
            speedButtons[index].onClick.AddListener(() => SpeedClicked?.Invoke(option));
        }
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
}
