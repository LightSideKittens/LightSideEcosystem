using UnityEngine;
using UnityEngine.UI;

namespace LightSide.Showcase
{
    /// <summary>Shows and hides the letters of a password field.</summary>
    [RequireComponent(typeof(Button))]
    public sealed class PasswordToggle : MonoBehaviour
    {
        [SerializeField] private UniTextEditable field;
        [SerializeField] private UniTextBase icon;
        [SerializeField] private string hiddenIcon = "👁";
        [SerializeField] private string shownIcon = "🙈";

        private PasswordBehavior password;

        private void Awake() => GetComponent<Button>().onClick.AddListener(Toggle);

        private void OnEnable()
        {
            password ??= field.GetBehavior<PasswordBehavior>();
            password.RevealedChanged += Show;
            Show(password.Revealed);
        }

        private void OnDisable() => password.RevealedChanged -= Show;

        private void Toggle() => password.Revealed = !password.Revealed;

        private void Show(bool revealed) => icon.Text = revealed ? shownIcon : hiddenIcon;
    }
}
