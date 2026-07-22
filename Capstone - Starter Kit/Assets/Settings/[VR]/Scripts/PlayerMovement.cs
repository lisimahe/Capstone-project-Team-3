using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit;

namespace MissionBit
{
    /// <summary>
    /// Applies walk/sprint speed to both:
    ///   • Desktop: <see cref="XRDeviceSimulator.keyboardBodyTranslateMultiplier"/>
    ///   • Headset: <see cref="ActionBasedContinuousMoveProvider.moveSpeed"/>
    ///
    /// Sprint sources (any one is enough):
    ///   1. PlayerInput UnityEvents → <see cref="OnSprintAction"/> (Shift / thumbstick click)
    ///   2. Direct polling of the InputManager "Player/Run" action (fallback if events are unwired)
    ///   3. Optional extra <see cref="InputActionReference"/> for a dedicated XR sprint binding
    ///
    /// NOTE: Class name MUST stay "PlayerMovement" — it is serialized on the XR Rig Player child.
    /// </summary>
    public class PlayerMovement : MonoBehaviour
    {
        [Header("Locomotion Targets")]
        [SerializeField] private XRDeviceSimulator desktopMove;
        [SerializeField] private ActionBasedContinuousMoveProvider xrMove;

        [Header("Speeds")]
        public float BASE_SPEED_XR = 3f;
        public float BASE_SPEED_DESKTOP = 15f;
        [SerializeField] private float sprintMultiplier = 2f;

        [Header("Sprint Input (optional extras)")]
        [Tooltip("Optional dedicated sprint action. Leave empty to use InputManager Player/Run.")]
        [SerializeField] private InputActionReference sprintActionReference;
        [Tooltip("Also treat left-stick click / primary button as sprint when held (OpenXR).")]
        [SerializeField] private bool enableBuiltInXrSprintFallback = true;

        public bool wasInteracting = false;

        private bool isSprinting;
        private bool sprintFromCallback;
        private Vector2 inputMove;

        private InputAction _runAction;
        private InputAction _xrThumbstickClick;
        private InputAction _xrPrimaryButton;
        private bool _ownsFallbackActions;

        public static PlayerMovement Instance;

        private void Awake()
        {
            Instance = this;
            inputMove = Vector2.zero;
            AutoResolveTargets();
            BindInputFallback();
        }

        private void OnEnable()
        {
            if (_runAction != null) _runAction.Enable();
            if (_xrThumbstickClick != null) _xrThumbstickClick.Enable();
            if (_xrPrimaryButton != null) _xrPrimaryButton.Enable();
            if (sprintActionReference != null && sprintActionReference.action != null)
                sprintActionReference.action.Enable();
        }

        private void OnDisable()
        {
            if (_runAction != null) _runAction.Disable();
            if (_xrThumbstickClick != null) _xrThumbstickClick.Disable();
            if (_xrPrimaryButton != null) _xrPrimaryButton.Disable();
            if (sprintActionReference != null && sprintActionReference.action != null)
                sprintActionReference.action.Disable();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            if (_ownsFallbackActions)
            {
                _xrThumbstickClick?.Dispose();
                _xrPrimaryButton?.Dispose();
                _ownsFallbackActions = false;
            }
        }

        private void AutoResolveTargets()
        {
            if (xrMove == null)
                xrMove = GetComponentInParent<ActionBasedContinuousMoveProvider>()
                         ?? FindObjectOfType<ActionBasedContinuousMoveProvider>();

            if (desktopMove == null)
                desktopMove = FindObjectOfType<XRDeviceSimulator>();
        }

        private void BindInputFallback()
        {
            // Prefer the project's InputManager asset (same one PlayerInput uses).
            var playerInput = FindObjectOfType<PlayerInput>();
            if (playerInput != null && playerInput.actions != null)
            {
                _runAction = playerInput.actions.FindAction("Player/Run", throwIfNotFound: false)
                             ?? playerInput.actions.FindAction("Run", throwIfNotFound: false);
            }

            if (_runAction == null && sprintActionReference != null)
                _runAction = sprintActionReference.action;

            if (!enableBuiltInXrSprintFallback)
                return;

            // Extra OpenXR-friendly bindings so sprint works even if InputManager
            // events were disconnected from this component in the scene.
            _xrThumbstickClick = new InputAction("XRSprintThumbstick", InputActionType.Button);
            _xrThumbstickClick.AddBinding("<XRController>{LeftHand}/thumbstickClicked");
            _xrThumbstickClick.AddBinding("<XRController>{RightHand}/thumbstickClicked");
            _xrThumbstickClick.AddBinding("<XRController>/thumbstickClicked");

            _xrPrimaryButton = new InputAction("XRSprintPrimary", InputActionType.Button);
            // Left primary is a common "sprint" affordance when thumbstick click is awkward.
            _xrPrimaryButton.AddBinding("<XRController>{LeftHand}/primaryButton");

            _ownsFallbackActions = true;
        }

        public void OnMove(InputAction.CallbackContext context)
        {
            inputMove = context.ReadValue<Vector2>();
        }

        public void OnInteract(InputAction.CallbackContext context)
        {
            wasInteracting = context.performed || context.action.triggered;
            if (context.canceled)
                wasInteracting = false;
        }

        public void OnSprintAction(InputAction.CallbackContext context)
        {
            if (context.performed)
                sprintFromCallback = true;
            else if (context.canceled)
                sprintFromCallback = false;
            else if (context.action != null && context.action.type == InputActionType.Button)
                sprintFromCallback = context.ReadValueAsButton();
        }

        private void Update()
        {
            AutoResolveTargets();
            isSprinting = EvaluateSprintHeld();
            ApplySpeeds();
        }

        private bool EvaluateSprintHeld()
        {
            if (sprintFromCallback)
                return true;

            if (_runAction != null && _runAction.enabled && _runAction.IsPressed())
                return true;

            if (sprintActionReference != null
                && sprintActionReference.action != null
                && sprintActionReference.action.enabled
                && sprintActionReference.action.IsPressed())
                return true;

            if (_xrThumbstickClick != null && _xrThumbstickClick.IsPressed())
                return true;

            if (_xrPrimaryButton != null && _xrPrimaryButton.IsPressed())
                return true;

            return false;
        }

        private void ApplySpeeds()
        {
            float mult = isSprinting ? sprintMultiplier : 1f;

            // Desktop simulator path — only scale while there is keyboard/move intent
            // OR always keep the base multiplier ready (simulator reads keys itself).
            if (desktopMove != null)
                desktopMove.keyboardBodyTranslateMultiplier = BASE_SPEED_DESKTOP * mult;

            // XR Continuous Move Provider reads its OWN stick actions. Never zero
            // moveSpeed based on PlayerInput OnMove — that desync is what made
            // sprint (and sometimes all locomotion) feel broken on the headset.
            if (xrMove != null)
                xrMove.moveSpeed = BASE_SPEED_XR * mult;
        }

        /// <summary>Legacy name kept for any external callers / UnityEvents.</summary>
        public void CheckSprinting()
        {
            isSprinting = EvaluateSprintHeld();
            ApplySpeeds();
        }
    }
}
