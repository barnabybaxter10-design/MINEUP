using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BoxCutter
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovement : MonoBehaviour
    {
        [Header("Movement Speeds")] public float walkSpeed = 5f;
        public float sprintSpeed = 10f;

        [Header("Look Settings")]
        public float mouseSensitivity = 2f;

        [Header("Jump / Gravity")] public float gravity = -9.81f;
        public float jumpHeight = 1.5f;

        [Tooltip("Drag your Camera here")] public Transform cameraTransform;
        public Transform camLeanObj;

        [Header("Lean Settings")] 
        [Tooltip("Max roll angle when strafing")] public float maxLeanAngle = 15f;

        [Tooltip("How long it takes to settle into the lean (smaller = snappier)")]
        public float leanSmoothTime = 0.2f;

        private float currentLeanAngle = 0f;
        private float leanVelocity = 0f;

        [Header("Weapon Sway Settings")] public float swayIntensity = 0.02f;
        public float maxSwayMagnitude = 0.05f;

        [Tooltip("How quickly the raw input is smoothed (smaller = tighter)")]
        public float inputSmoothTime = 0.05f;

        [Tooltip("How quickly the weapon poses catch up (smaller = snappier)")]
        public float swaySmoothTime = 0.1f;

        [Tooltip("Drag your Weapon here")]
        public Transform weapon;

        private CharacterController controller;
        private float verticalVelocity;
        private float xRotation;

        public CameraBob camBob;
        public CameraBob.CamBobData idleBobData;
        public CameraBob.CamBobData walkBobData;
        public CameraBob.CamBobData sprintBobData;

        private Vector3 weaponRestPos;
        private Quaternion weaponRestRot;

        // Smoothing state
        private Vector2 currentSwayInput;
        private Vector2 swayInputVelocity;
        private Vector3 weaponPosVelocity;
        
#if ENABLE_INPUT_SYSTEM
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction sprintAction;
        private InputAction jumpAction;
#endif

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            Cursor.lockState = CursorLockMode.Locked;

            if (weapon != null)
            {
                weaponRestPos = weapon.localPosition;
                weaponRestRot = weapon.localRotation;
            }

            InitializeInputSystem();
        }

        private void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            moveAction.Enable();
            lookAction.Enable();
            sprintAction.Enable();
            jumpAction.Enable();
#endif
        }

        private void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            moveAction.Disable();
            lookAction.Disable();
            sprintAction.Disable();
            jumpAction.Disable();
#endif
        }

        void InitializeInputSystem()
        {
#if ENABLE_INPUT_SYSTEM
            moveAction = new InputAction("Move", binding: "<Gamepad>/leftStick");
            moveAction.AddCompositeBinding("Dpad")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            lookAction = new InputAction("Look", binding: "<Mouse>/delta");
            lookAction.AddBinding("<Gamepad>/rightStick");

            sprintAction = new InputAction("Sprint", binding: "<Keyboard>/leftShift");
            sprintAction.AddBinding("<Gamepad>/leftStickPress");

            jumpAction = new InputAction("Jump", binding: "<Keyboard>/space");
            jumpAction.AddBinding("<Gamepad>/buttonSouth");
#endif
        }

        private Vector2 GetLookInput()
        {
#if ENABLE_INPUT_SYSTEM
            return lookAction.ReadValue<Vector2>(); 
#else
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
        }

        private Vector2 GetMovementInput()
        {
#if ENABLE_INPUT_SYSTEM
            return moveAction.ReadValue<Vector2>();
#else
            return new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
#endif
        }
        
        private Vector2 GetMovementInputRaw()
        {
#if ENABLE_INPUT_SYSTEM
            return moveAction.ReadValue<Vector2>();
#else
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
        }

        private bool GetSprintInput()
        {
#if ENABLE_INPUT_SYSTEM
            return sprintAction.IsPressed();
#else
            return Input.GetKey(KeyCode.LeftShift);
#endif
        }

        private bool GetJumpInputDown()
        {
#if ENABLE_INPUT_SYSTEM
            return jumpAction.WasPerformedThisFrame();
#else
            return Input.GetButtonDown("Jump");
#endif
        }
        
        void Update()
        {
            if (Time.timeScale != 1f) return;
            HandleMouseLook();
            HandleMovement();
            CameraLean();
        }

        void LateUpdate()
        {
            if (Time.timeScale != 1f) return;
            ApplyWeaponSway();
        }

        void HandleMouseLook()
        {
            Vector2 mouseInput = GetLookInput();

            float sensMul = 1;
            #if ENABLE_INPUT_SYSTEM
                sensMul = 0.06f;
            #endif
            
            float mouseX = mouseInput.x * mouseSensitivity * sensMul;
            float mouseY = mouseInput.y * mouseSensitivity * sensMul;

            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -90f, 90f);

            cameraTransform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
            transform.Rotate(Vector3.up * mouseX);
        }

        void HandleMovement()
        {
            bool isSprinting = GetSprintInput();
            float currentSpeed = isSprinting ? sprintSpeed : walkSpeed;

            Vector2 inputDir = GetMovementInput();
            Vector3 input = new Vector3(inputDir.x, 0f, inputDir.y);
            
            // Using Raw for boolean check to ensure threshold consistency
            Vector2 inputRaw = GetMovementInputRaw();
            bool noMovement = Mathf.Approximately(inputRaw.x, 0f) && Mathf.Approximately(inputRaw.y, 0f);
            
            Vector3 move = (transform.right * input.x + transform.forward * input.z) * currentSpeed;

            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;

            if (isSprinting && !noMovement)
            {
                camBob.bobData = sprintBobData;
            }
            else if (!noMovement)
            {
                camBob.bobData = walkBobData;
            }
            else
            {
                camBob.bobData = idleBobData;
            }

            if (GetJumpInputDown() && controller.isGrounded)
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }

            verticalVelocity += gravity * Time.deltaTime;
            move.y = verticalVelocity;

            controller.Move(move * Time.deltaTime);
        }

        void ApplyWeaponSway()
        {
            if (weapon == null) return;

            Vector2 lookInput = GetLookInput();
            
            float rawX = -lookInput.x * swayIntensity;
            float rawY = -lookInput.y * swayIntensity;

            currentSwayInput.x = Mathf.SmoothDamp(
                currentSwayInput.x, rawX,
                ref swayInputVelocity.x,
                inputSmoothTime
            );
            currentSwayInput.y = Mathf.SmoothDamp(
                currentSwayInput.y, rawY,
                ref swayInputVelocity.y,
                inputSmoothTime
            );

            Vector3 targetPos = new Vector3(
                currentSwayInput.x,
                currentSwayInput.y,
                0f
            );
            targetPos = Vector3.ClampMagnitude(targetPos, maxSwayMagnitude) + weaponRestPos;

            weapon.localPosition = Vector3.SmoothDamp(
                weapon.localPosition,
                targetPos,
                ref weaponPosVelocity,
                swaySmoothTime
            );

            Quaternion targetRot = weaponRestRot * Quaternion.Euler(
                currentSwayInput.y * 50f,
                currentSwayInput.x * 50f,
                currentSwayInput.x * 50f
            );
            
            weapon.localRotation = Quaternion.Slerp(
                weapon.localRotation,
                targetRot,
                Time.deltaTime * (1f / swaySmoothTime)
            );
        }

        private void CameraLean()
        {
            Vector2 inputDir = GetMovementInput();
            float xInput = inputDir.x;

            float targetLean = -xInput * maxLeanAngle;

            currentLeanAngle = Mathf.SmoothDamp(
                currentLeanAngle,
                targetLean,
                ref leanVelocity,
                leanSmoothTime
            );

            Vector3 e = camLeanObj.localEulerAngles;
            camLeanObj.localRotation = Quaternion.Euler(e.x, e.y, currentLeanAngle);
        }
    }
}