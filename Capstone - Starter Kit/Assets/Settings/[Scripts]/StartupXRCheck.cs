using System.Collections;
using UnityEngine;

using UnityEngine.XR.Management;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

using Unity.XR.CoreUtils;
using UnityEngine.InputSystem;

namespace MissionBit
{
    public class StartupXRCheck : MonoBehaviour
    {
        public bool IsXRInteractions;
        private bool IsDeviceConnected;


        [SerializeField] private InputActionManager inputActionManger;


        [SerializeField] private GameObject[] XRControls;
        public GameObject XRRig;
        [SerializeField] private GameObject XRDesktop;

        public static StartupXRCheck Instance;

        [SerializeField] private MeshRenderer playerModel;

        public PlayerInput PlayerInput;

        private Transform head;
        public Transform origin;
        public Transform target;


        private Camera mainCamera;

        public InputActionAsset InputManager;


        public bool IsXRSession()
        {
            return IsDeviceConnected;
        }


        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                Cursor.lockState = CursorLockMode.Locked;
            }
        }

        public void Recenter()
        {
            //API (easy) solution
            /*
            XROrigin xrOrigin = GetComponent<XROrigin>();
            xrOrigin.MoveCameraToWorldLocation(target.position);
            xrOrigin.MatchOriginUpCameraForward(target.up, target.forward);
            */


            //position
            /*
            Vector3 offset = head.position - origin.position;
            offset.y = 0;
            origin.position = target.position;
            */

            //rotation
            //Vector3 targetRotation = new Vector3(pitch, yaw, 0);

            Vector3 targetForward = target.forward;
            targetForward.y = 0;
            Vector3 cameraForward = head.forward;
            cameraForward.y = 0;

            float angle = Vector3.SignedAngle(cameraForward, targetForward, Vector3.up);

            origin.RotateAround(head.position, Vector3.up, angle);
        }

        private void Awake()
        {


            Application.runInBackground = true;

            Instance = this;

            if (XRGeneralSettings.Instance != null
                && XRGeneralSettings.Instance.Manager != null
                && XRGeneralSettings.Instance.Manager.activeLoader != null)
            {
                if (QualitySettings.lodBias <= 1)
                    QualitySettings.lodBias *= 3.8f;

                IsDeviceConnected = true;

                if (XRRig != null)
                    XRRig.SetActive(true);
                else
                    Debug.LogError("[StartupXRCheck] XRRig is not assigned. Drag Assets/Settings/[VR]/XR Rig.prefab into the scene and assign it.", this);

                if (XRDesktop != null)
                    XRDesktop.SetActive(false);

                if (IsXRInteractions && XRControls != null)
                {
                    foreach (GameObject controller in XRControls)
                    {
                        if (controller != null)
                            controller.SetActive(true);
                    }
                }
            }
            else
            {
                IsDeviceConnected = false;

                if (XRDesktop != null)
                    XRDesktop.SetActive(true);

                if (XRControls != null)
                {
                    foreach (GameObject controller in XRControls)
                    {
                        if (controller != null)
                            controller.SetActive(false);
                    }
                }

                // Desktop simulator still needs an XR Origin in the scene.
                if (XRRig != null)
                    XRRig.SetActive(true);
            }
        }


        private void Start()
        {
            if (PlayerInput != null)
            {
                if (InputManager != null && PlayerInput.actions != InputManager)
                    PlayerInput.actions = InputManager;

                PlayerInput.enabled = true;
            }

            Cursor.visible = false;

            if (playerModel != null)
                playerModel.enabled = false;

            mainCamera = Camera.main;
            if (mainCamera != null)
                head = mainCamera.transform;
            else
                Debug.LogError("[StartupXRCheck] No Main Camera found. Is XR Rig missing from the scene?", this);
        }
    }
}
