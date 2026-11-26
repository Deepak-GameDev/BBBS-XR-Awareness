using UnityEngine;

namespace BBB.Awareness
{
    /// <summary>
    /// Keyboard shortcuts for testing in the Unity Editor only.
    /// Works with both the new Input System and the old Input Manager, whichever the project uses.
    /// Always returns false in device builds (the headset uses controllers).
    /// </summary>
    public static class EditorKeys
    {
        public static bool Down(KeyCode key)
        {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            var control = GetKey(key);
            return control != null && control.wasPressedThisFrame;
#elif UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(key);
#else
            return false;
#endif
        }

        public static bool Held(KeyCode key)
        {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            var control = GetKey(key);
            return control != null && control.isPressed;
#elif UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(key);
#else
            return false;
#endif
        }

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
        private static UnityEngine.InputSystem.Controls.KeyControl GetKey(KeyCode key)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null)
            {
                return null;
            }
            switch (key)
            {
                case KeyCode.P: return kb.pKey;
                case KeyCode.O: return kb.oKey;
                case KeyCode.Q: return kb.qKey;
                case KeyCode.E: return kb.eKey;
                case KeyCode.UpArrow: return kb.upArrowKey;
                case KeyCode.DownArrow: return kb.downArrowKey;
                default: return null;
            }
        }
#endif
    }
}
