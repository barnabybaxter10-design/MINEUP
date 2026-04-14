using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BoxCutter
{
    public class BoxCutterFreeCam : MonoBehaviour
    {
        [Header("Movement")] 
        public float moveSpeed = 6f;
        public float slowSpeed = 2f;
        public float fastSpeed = 18f;

        [Tooltip("Adjust base moveSpeed with the mouse wheel.")]
        public float scrollSpeedStep = 1f;

        public float minMoveSpeed = 0.5f;
        public float maxMoveSpeed = 100f;

        [Header("Look")] 
        public bool holdRightMouseToLook = true;
        public float lookSensitivity = 2f;
        public float maxPitch = 89f;

        [Header("Cursor")] 
        public bool lockCursorOnStart = true;

        float _yaw;
        float _pitch;

        void Start()
        {
            Vector3 e = transform.eulerAngles;
            _yaw = e.y;
            _pitch = e.x;

            if (!holdRightMouseToLook && lockCursorOnStart)
                LockCursor(true);
        }

        void OnEnable()
        {
            if (!holdRightMouseToLook && lockCursorOnStart)
                LockCursor(true);
        }

        void OnDisable()
        {
            LockCursor(false);
        }

        void Update()
        {
            HandleLook();
            HandleMove();
            HandleSpeedScroll();
            HandleCursorLocking();
        }

        void HandleCursorLocking()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                LockCursor(false);
#else
            if (Input.GetKeyDown(KeyCode.Escape))
                 LockCursor(false);
#endif

            if (!holdRightMouseToLook && Cursor.lockState == CursorLockMode.None)
            {
#if ENABLE_INPUT_SYSTEM
                if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                    LockCursor(true);
#else
                if (Input.GetMouseButtonDown(0))
                    LockCursor(true);
#endif
            }
        }

        void HandleLook()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null) return;

            bool looking = true;

            if (holdRightMouseToLook)
            {
                // Check if RMB is held
                bool isRightHeld = Mouse.current.rightButton.isPressed;
                
                // Handle state changes
                if (Mouse.current.rightButton.wasPressedThisFrame) LockCursor(true);
                if (Mouse.current.rightButton.wasReleasedThisFrame) LockCursor(false);
                
                looking = isRightHeld;
            }

            if (!looking) return;

            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                _yaw += mouseDelta.x * lookSensitivity * 0.035f; 
                _pitch -= mouseDelta.y * lookSensitivity * 0.035f;
            }
#else
            bool looking = true;

            if (holdRightMouseToLook)
            {
                if (Input.GetMouseButtonDown(1)) LockCursor(true);
                if (Input.GetMouseButtonUp(1)) LockCursor(false);
                looking = Input.GetMouseButton(1);
            }

            if (!looking) return;

            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            _yaw += mouseX * lookSensitivity;
            _pitch -= mouseY * lookSensitivity;
#endif
            
            // Apply rotation
            _pitch = Mathf.Clamp(_pitch, -maxPitch, maxPitch);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        void HandleMove()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null) return;

            float speed = moveSpeed;
            if (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed)
                speed = fastSpeed;
            else if (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed)
                speed = slowSpeed;

            float h = 0f;
            float v = 0f;
            
            if (Keyboard.current.aKey.isPressed) h -= 1f;
            if (Keyboard.current.dKey.isPressed) h += 1f;
            if (Keyboard.current.wKey.isPressed) v += 1f;
            if (Keyboard.current.sKey.isPressed) v -= 1f;

            Vector3 move = transform.right * h + transform.forward * v;

            if (Keyboard.current.eKey.isPressed) move += Vector3.up;
            if (Keyboard.current.qKey.isPressed) move += Vector3.down;

            if (move.sqrMagnitude > 1f) move.Normalize();
            transform.position += move * speed * Time.deltaTime;
#else
            float speed = moveSpeed;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                speed = fastSpeed;
            else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                speed = slowSpeed;

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            Vector3 move = transform.right * h + transform.forward * v;

            if (Input.GetKey(KeyCode.E)) move += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) move += Vector3.down;

            if (move.sqrMagnitude > 1f) move.Normalize();
            transform.position += speed * Time.deltaTime * move;
#endif
        }

        void HandleSpeedScroll()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null) return;
            float scroll = Mouse.current.scroll.ReadValue().y / 120f; 
#else
            float scroll = Input.mouseScrollDelta.y;
#endif
            if (Mathf.Abs(scroll) > 0.01f)
            {
                moveSpeed = Mathf.Clamp(moveSpeed + scroll * scrollSpeedStep, minMoveSpeed, maxMoveSpeed);
            }
        }

        void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}