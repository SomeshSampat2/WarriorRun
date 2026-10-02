using UnityEngine;
using UnityEngine.InputSystem;

namespace WarriorRun.Input
{
    /// <summary>
    /// Unified swipe + keyboard input for the runner.
    /// Touch: drag beyond threshold fires one directional swipe. Short tap fires Tap.
    /// Keyboard (arrows/WASD) and mouse drag work in the editor.
    /// </summary>
    public class RunnerInput : MonoBehaviour
    {
        public event System.Action SwipeLeft, SwipeRight, SwipeUp, SwipeDown, Tap;

        [SerializeField] float swipeThresholdPx = 60f;
        [SerializeField] float tapMaxDuration = 0.25f;
        [SerializeField] float tapMaxMovePx = 24f;

        Vector2 pressPos;
        float pressTime;
        bool pointerDown;
        bool swipeConsumed;

        void Update()
        {
            PollPointer();
            PollKeyboard();
        }

        void PollPointer()
        {
            Vector2 pos;
            bool down, up;

            var touch = Touchscreen.current?.primaryTouch;
            if (touch != null && (touch.press.isPressed || touch.press.wasReleasedThisFrame || pointerDown))
            {
                pos = touch.position.ReadValue();
                down = touch.press.wasPressedThisFrame;
                up = touch.press.wasReleasedThisFrame;
                if (!down && !up && !pointerDown) return;
            }
            else
            {
                var mouse = Mouse.current;
                if (mouse == null) return;
                pos = mouse.position.ReadValue();
                down = mouse.leftButton.wasPressedThisFrame;
                up = mouse.leftButton.wasReleasedThisFrame;
                if (!down && !up && !pointerDown) return;
            }

            if (down)
            {
                pointerDown = true;
                swipeConsumed = false;
                pressPos = pos;
                pressTime = Time.unscaledTime;
            }

            if (!pointerDown) return;

            Vector2 delta = pos - pressPos;
            if (!swipeConsumed && delta.magnitude >= swipeThresholdPx)
            {
                swipeConsumed = true;
                FireDirection(delta);
                return;
            }

            if (up)
            {
                pointerDown = false;
                bool isTap = !swipeConsumed
                             && Time.unscaledTime - pressTime <= tapMaxDuration
                             && delta.magnitude <= tapMaxMovePx;
                if (isTap) Tap?.Invoke();
            }
        }

        void PollKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) SwipeLeft?.Invoke();
            if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) SwipeRight?.Invoke();
            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) SwipeUp?.Invoke();
            if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) SwipeDown?.Invoke();
            if (kb.enterKey.wasPressedThisFrame) Tap?.Invoke();
        }

        void FireDirection(Vector2 delta)
        {
            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            {
                if (delta.x > 0) SwipeRight?.Invoke(); else SwipeLeft?.Invoke();
            }
            else
            {
                if (delta.y > 0) SwipeUp?.Invoke(); else SwipeDown?.Invoke();
            }
        }
    }
}
