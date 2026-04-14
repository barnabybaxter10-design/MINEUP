using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BoxCutter
{
    public class SceneReload : MonoBehaviour
    {
        void Update()
        {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current.rKey.wasPressedThisFrame) ReloadScene();
#else
            if (Input.GetKeyDown(KeyCode.R)) ReloadScene();
#endif
        }

        public void ReloadScene()
        {
            Screen.fullScreen = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            Time.timeScale = 1;
        }
    }
}