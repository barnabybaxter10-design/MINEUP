using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BoxCutter
{
    public class DebugTools : MonoBehaviour
    {
        [SerializeField] private float timeSlow = 0.05f;

        void Update()
        {
            bool tPressed = false;
            bool gPressed = false;
            bool rPressed = false;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                tPressed = Keyboard.current.tKey.wasPressedThisFrame;
                gPressed = Keyboard.current.gKey.wasPressedThisFrame;
                rPressed = Keyboard.current.rKey.wasPressedThisFrame;
            }
#else
            // Legacy Input Manager Logic
            tPressed = Input.GetKeyDown(KeyCode.T);
            gPressed = Input.GetKeyDown(KeyCode.G);
            rPressed = Input.GetKeyDown(KeyCode.R);
#endif

            if (tPressed)
            {
                Time.timeScale = math.abs(Time.timeScale - timeSlow) <= 0.001f ? 1 : timeSlow;
            }

            if (gPressed)
            {
                Time.timeScale = Time.timeScale == 0 ? 1 : 0;
            }

            if (rPressed)
            {
                ReloadScene();
            }
        }

        public void ReloadScene()
        {
            Screen.fullScreen = true;
            Cursor.visible = false;

            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

            Time.timeScale = 1;
        }
    }
}