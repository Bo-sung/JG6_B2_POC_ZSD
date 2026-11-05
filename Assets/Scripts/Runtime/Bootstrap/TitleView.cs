using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class TitleView : MonoBehaviour
{
    [SerializeField] InputField seedInput;
    [SerializeField] Button startButton;
    [SerializeField] Text statusText;
    public string SeedText => seedInput.text;
    public event Action StartClicked;
    void Awake() => startButton.onClick.AddListener(() => StartClicked?.Invoke());
    public void Show(string _message) => statusText.text = _message;
}
