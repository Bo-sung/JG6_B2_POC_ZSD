using UnityEngine;

public sealed class PreloadView : MonoBehaviour
{
    [SerializeField] UnityEngine.UI.Text statusText;
    public void Show(string _message) => statusText.text = _message;
}
