using Template.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace NightCourier.UI
{
    /// <summary>
    /// Appears where the thumb lands and reads the drag from there: <see cref="Value"/> points the way to steer,
    /// its length (0–1) how hard. Presses that start on a button are left to the button. Arrow keys or WASD
    /// work too, for the editor.
    /// </summary>
    public sealed class FloatingJoystick : MonoBehaviour
    {
        private const float BaseRadius = 150f; // canvas units (1080 × 1920 reference)
        private float _radius = BaseRadius;

        private RectTransform _area;
        private RectTransform _visual;
        private RectTransform _knob;
        private bool _dragging;
        private Vector2 _origin;

        public Vector2 Value { get; private set; }
        public bool Touched { get; private set; }

        public static FloatingJoystick Create(RectTransform area, float scale = 1f, float opacity = 1f)
        {
            var joystick = area.gameObject.AddComponent<FloatingJoystick>();
            joystick._area = area;
            joystick._radius = BaseRadius * scale;
            joystick._visual = UiFactory.CreateRect("Joystick", area);
            joystick._visual.anchorMin = joystick._visual.anchorMax = new Vector2(0.5f, 0.5f);
            UiFactory.CreateRounded(joystick._visual, Vector2.zero, Vector2.one * joystick._radius * 2f, new Color(1f, 1f, 1f, 0.1f), (int)joystick._radius);
            joystick._visual.gameObject.AddComponent<CanvasGroup>().alpha = opacity;
            joystick._knob = UiFactory.CreateRounded(joystick._visual, Vector2.zero, Vector2.one * 120f * scale, new Color(0.25f, 0.95f, 1f, 0.55f), (int)(60 * scale))
                .rectTransform;
            joystick._visual.gameObject.SetActive(false);
            return joystick;
        }

        private void Update()
        {
            var pointer = Pointer.current;
            if (pointer != null)
            {
                if (pointer.press.wasPressedThisFrame && !OverButton())
                {
                    _dragging = true;
                    Touched = true;
                    _origin = ToLocal(pointer.position.ReadValue());
                    _visual.anchoredPosition = _origin;
                    _visual.gameObject.SetActive(true);
                }

                if (_dragging && pointer.press.isPressed)
                {
                    Vector2 drag = ToLocal(pointer.position.ReadValue()) - _origin;
                    Value = Vector2.ClampMagnitude(drag / _radius, 1f);
                    _knob.anchoredPosition = Value * _radius;
                    return;
                }

                if (_dragging)
                {
                    Release();
                }
            }

            Value = KeyboardValue();
            Touched |= Value != Vector2.zero;
        }

        /// <summary>Lets go of the stick, e.g. when the game pauses mid-drag.</summary>
        public void Release()
        {
            _dragging = false;
            Value = Vector2.zero;
            _knob.anchoredPosition = Vector2.zero;
            _visual.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            _dragging = false;
            Value = Vector2.zero;
        }

        private Vector2 ToLocal(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_area, screen, null, out var local);
            return local;
        }

        private static bool OverButton() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        private static Vector2 KeyboardValue()
        {
            var k = Keyboard.current;
            if (k == null)
            {
                return Vector2.zero;
            }

            float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
            float y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }
    }
}
