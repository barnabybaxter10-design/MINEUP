using UnityEngine;
using System;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BoxCutter
{
    public class CameraBob : MonoBehaviour
    {
        public CamBobData bobData;
        public CamBobData finalBobData;

        private float timer = 0.0f;
        private float halfTimer = 0.0f;

        private Vector3 startLocalPos;

        [Serializable]
        public class CamBobData
        {
            [Range(0, 0.2f)] public float speed;
            [Range(0, 0.1f)] public float bobX;
            [Range(0, 0.1f)] public float bobY;
            [Range(0, 2f)] public float rotation;
        }

        private void Start()
        {
            startLocalPos = transform.parent.InverseTransformPoint(transform.position);
        }

        void Update()
        {
            //─────────────────────────────────────────────────────────────────────────────────────
            float lerpSpeed = Time.deltaTime * 7;

            finalBobData.speed = Mathf.Lerp(finalBobData.speed, bobData.speed, lerpSpeed);
            finalBobData.bobX = Mathf.Lerp(finalBobData.bobX, bobData.bobX, lerpSpeed);
            finalBobData.bobY = Mathf.Lerp(finalBobData.bobY, bobData.bobY, lerpSpeed);
            finalBobData.rotation = Mathf.Lerp(finalBobData.rotation, bobData.rotation, lerpSpeed);

            //─────────────────────────────────────────────────────────────────────────────────────
            Vector2 inputVector = GetInputVector();
            
            // Use small epsilon for float comparison to determine idle state
            bool idle = inputVector.sqrMagnitude < 0.01f; 
            //─────────────────────────────────────────────────────────────────────────────────────
            float timeEnd = Mathf.PI * 2;

            float waveslice = Mathf.Sin(timer);
            timer += finalBobData.speed;

            if (timer > timeEnd)
            {
                timer -= timeEnd;
            }

            float halfWaveslice = Mathf.Sin(halfTimer * 0.5f);
            halfTimer += finalBobData.speed;
            if (halfTimer > timeEnd * 2)
            {
                halfTimer -= timeEnd * 2;
            }

            //─────────────────────────────────────────────────────────────────────────────────────
            Transform trans = transform;

            float xBob = halfWaveslice * finalBobData.bobX;
            float yBob = waveslice * finalBobData.bobY;
            float rotateChange = waveslice * finalBobData.rotation;

            trans.position = trans.parent.TransformPoint(startLocalPos) + new Vector3(xBob, yBob, 0);

            if (!idle)
            {
                trans.localRotation = Quaternion.Euler(trans.localRotation.eulerAngles.x, trans.localRotation.eulerAngles.y, rotateChange);
            }
            else
            {
                trans.localRotation = Quaternion.Slerp(trans.localRotation, Quaternion.Euler(trans.localRotation.eulerAngles.x, trans.localRotation.eulerAngles.y, 0), Time.deltaTime * 7);
            }
            //─────────────────────────────────────────────────────────────────────────────────────
        }

        /// <summary>
        /// Checks both Legacy and New Input Systems to determine movement intent.
        /// </summary>
        private Vector2 GetInputVector()
        {
            Vector2 input = Vector2.zero;

#if ENABLE_LEGACY_INPUT_MANAGER
            input.x += Input.GetAxis("Horizontal");
            input.y += Input.GetAxis("Vertical");
#endif

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) input.y += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) input.y -= 1f;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1f;
            }

            if (Gamepad.current != null)
            {
                input += Gamepad.current.leftStick.ReadValue();
            }
#endif
            return input;
        }
    }
}