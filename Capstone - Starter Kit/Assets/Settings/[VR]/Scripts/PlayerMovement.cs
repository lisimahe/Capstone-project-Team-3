using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit;

namespace MissionBit
{
    /// <summary>
    /// Drives the player's continuous movement speed for both the desktop
    /// (XR Device Simulator) and XR (ActionBasedContinuousMoveProvider) paths and
    /// handles sprinting.
    ///
    /// NOTE: The class name MUST stay "PlayerMovement" because this file is
    /// PlayerMovement.cs and the component is serialized on the Player object via
    /// this script's GUID. (A previous edit replaced the whole file with an
    /// unrelated "PlayerTeleportSync" class, which broke the component - Unity
    /// could no longer load a class named "PlayerMovement" from this file. The
    /// teleport-sync helper now lives in its own PlayerTeleportSync.cs.)
    /// </summary>
    public class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private XRDeviceSimulator desktopMove;
        [SerializeField] private ActionBasedContinuousMoveProvider xrMove;

        public float BASE_SPEED_XR = 3f;
        public float BASE_SPEED_DESKTOP = 15f;

        private bool isMoving;
        private bool isSprinting;

        public bool wasInteracting = false;

        private Vector2 inputMove;
        private Vector2 lastMove;

        public static PlayerMovement Instance;

        private void Awake()
        {
            Instance = this;
            inputMove = Vector2.zero;
            lastMove = Vector2.zero;
        }

        public void OnMove(InputAction.CallbackContext context)
        {
            inputMove = context.ReadValue<Vector2>();
            isMoving = true;
        }

        public void OnInteract(InputAction.CallbackContext context)
        {
            wasInteracting = context.action.triggered;
        }

        public void OnSprintAction(InputAction.CallbackContext context)
        {
            if (context.performed)
            {
                isSprinting = true;
            }
            else if (context.canceled)
            {
                isSprinting = false;
            }
        }

        // Update is called once per frame
        void Update()
        {
            CheckSprinting();

            if (!isMoving)
            {
                if (desktopMove != null) desktopMove.keyboardBodyTranslateMultiplier = 0;
                if (xrMove != null) xrMove.moveSpeed = 0;
            }
        }

        public void CheckSprinting()
        {
            if (isSprinting)
            {
                if (desktopMove != null) desktopMove.keyboardBodyTranslateMultiplier = BASE_SPEED_DESKTOP * 2;
                if (xrMove != null) xrMove.moveSpeed = BASE_SPEED_XR * 2;
            }
            else
            {
                if (desktopMove != null) desktopMove.keyboardBodyTranslateMultiplier = BASE_SPEED_DESKTOP;
                if (xrMove != null) xrMove.moveSpeed = BASE_SPEED_XR;
            }
        }
    }
}