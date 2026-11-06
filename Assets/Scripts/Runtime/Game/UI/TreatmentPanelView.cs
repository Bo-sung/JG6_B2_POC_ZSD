using System;
using UnityEngine;
using UnityEngine.UI;

// 치료소 패널. 환자·담당자 후보 9행, 진행 표시, 시작·취소·닫기. 규칙은 Presenter와 세션이 담당한다.
public sealed class TreatmentPanelView : PanelBase
{
    [SerializeField] Text titleText;
    [SerializeField] Text progressText;
    [SerializeField] Text infoText;
    [SerializeField] Text statusText;
    [SerializeField] Button[] patientButtons;
    [SerializeField] Text[] patientLabels;
    [SerializeField] Button[] workerButtons;
    [SerializeField] Text[] workerLabels;
    [SerializeField] Button startButton;
    [SerializeField] Button cancelButton;
    [SerializeField] Button closeButton;

    public event Action<int> PatientClicked;
    public event Action<int> WorkerClicked;
    public event Action StartClicked;
    public event Action CancelClicked;
    public event Action CloseClicked;

    public int RowCount => patientButtons.Length;

    protected override void Awake()
    {
        for (int index = 0; index < patientButtons.Length; index++)
        {
            int captured = index;
            patientButtons[index].onClick.AddListener(() => PatientClicked?.Invoke(captured));
            workerButtons[index].onClick.AddListener(() => WorkerClicked?.Invoke(captured));
        }
        startButton.onClick.AddListener(() => StartClicked?.Invoke());
        cancelButton.onClick.AddListener(() => CancelClicked?.Invoke());
        closeButton.onClick.AddListener(() => CloseClicked?.Invoke());
        base.Awake();
    }

    public void ShowPatient(int _index, string _label, bool _available)
    {
        patientLabels[_index].text = _label;
        patientButtons[_index].interactable = _available;
    }

    public void ShowWorker(int _index, string _label, bool _available)
    {
        workerLabels[_index].text = _label;
        workerButtons[_index].interactable = _available;
    }

    public void Show(string _title, string _progress, string _info, string _status)
    {
        titleText.text = _title;
        progressText.text = _progress;
        infoText.text = _info;
        statusText.text = _status;
    }
}
