using System;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using SalcosRoamingBots.Configuration;
using UnityEngine;

namespace SalcosRoamingBots.Diagnostics
{
    internal static class DebugFreeCameraController
    {
        private static ManualLogSource _logger;
        private static GameObject _cameraObject;
        private static Camera _sourceCamera;
        private static bool _active;
        private static bool _playerInputSuppressed;
        private static CursorLockMode _previousCursorLock;
        private static bool _previousCursorVisible;
        private static Transform _sourceCameraParent;
        private static int _sourceCameraSiblingIndex;
        private static Vector3 _sourceCameraLocalPosition;
        private static Quaternion _sourceCameraLocalRotation;
        private static Vector3 _sourceCameraLocalScale;

        internal static bool IsActive => _active;

        internal static void Initialize(ManualLogSource logger)
        {
            _logger = logger;
        }

        internal static void Update()
        {
            if (IsActive && _cameraObject == null)
            {
                Deactivate("camera object was destroyed");
                return;
            }

            if (IsActive && !Singleton<GameWorld>.Instantiated)
            {
                Deactivate("raid ended");
                return;
            }

            if (!SrbSettings.EnableDebugFreeCamera.Value)
            {
                if (IsActive)
                {
                    Deactivate("disabled in configuration");
                }

                return;
            }

            if (!SrbSettings.DebugFreeCameraToggle.Value.IsDown())
            {
                return;
            }

            if (IsActive)
            {
                Deactivate("toggle key");
            }
            else
            {
                Activate();
            }
        }

        internal static void DrawOverlay()
        {
            if (!IsActive)
            {
                return;
            }

            GUI.Box(
                new Rect(12f, 12f, 500f, 58f),
                $"SRB DEBUG FREECAM  |  {SrbSettings.DebugFreeCameraToggle.Value.MainKey}: exit  |  " +
                "WASD: move  |  Q/E: down/up  |  Left Shift: boost  |  Esc: release mouse");
        }

        internal static void Shutdown()
        {
            if (IsActive)
            {
                Deactivate("plugin shutdown");
            }

            _logger = null;
        }

        private static void Activate()
        {
            if (!Singleton<GameWorld>.Instantiated)
            {
                _logger?.LogWarning("SRB debug free camera is only available during a raid.");
                return;
            }

            Camera sourceCamera = Camera.main;
            if (sourceCamera == null)
            {
                _logger?.LogWarning("SRB could not enable the debug free camera because the raid camera was not found.");
                return;
            }

            GameObject cameraObject = null;
            try
            {
                _previousCursorLock = Cursor.lockState;
                _previousCursorVisible = Cursor.visible;
                _sourceCamera = sourceCamera;
                Transform sourceTransform = sourceCamera.transform;
                _sourceCameraParent = sourceTransform.parent;
                _sourceCameraSiblingIndex = sourceTransform.GetSiblingIndex();
                _sourceCameraLocalPosition = sourceTransform.localPosition;
                _sourceCameraLocalRotation = sourceTransform.localRotation;
                _sourceCameraLocalScale = sourceTransform.localScale;

                // Move EFT's complete camera rather than cloning only Unity's Camera component.
                // This preserves Tarkov's weather, exposure, night vision, post-processing, and command buffers.
                cameraObject = new GameObject("SRB Debug Free Camera Rig");
                cameraObject.transform.SetPositionAndRotation(sourceTransform.position, sourceTransform.rotation);
                sourceTransform.SetParent(cameraObject.transform, true);

                DebugFreeCameraFollower follower = cameraObject.AddComponent<DebugFreeCameraFollower>();
                follower.TargetCamera = sourceTransform;

                FreeCamera controller = cameraObject.AddComponent<FreeCamera>();
                controller.enableInputCapture = true;
                controller.holdRightMouseCapture = false;
                controller.lookSpeed = SrbSettings.DebugFreeCameraLookSpeed.Value;
                controller.moveSpeed = SrbSettings.DebugFreeCameraMoveSpeed.Value;
                controller.sprintSpeed = SrbSettings.DebugFreeCameraBoostSpeed.Value;

                GamePlayerOwner.SetIgnoreInput(true);
                _playerInputSuppressed = true;
                _cameraObject = cameraObject;
                _active = true;
                controller.CaptureInput();

                _logger?.LogInfo(
                    $"SRB debug free camera enabled. Toggle={SrbSettings.DebugFreeCameraToggle.Value.MainKey}, " +
                    "move=WASD, vertical=Q/E, boost=Left Shift, release mouse=Esc.");
            }
            catch (Exception exception)
            {
                RestorePlayerAndCamera();
                if (cameraObject != null)
                {
                    UnityEngine.Object.Destroy(cameraObject);
                }

                _cameraObject = null;
                _active = false;
                _logger?.LogError($"SRB failed to enable the debug free camera: {exception}");
            }
        }

        private static void Deactivate(string reason)
        {
            GameObject cameraObject = _cameraObject;
            _cameraObject = null;
            _active = false;

            try
            {
                DebugFreeCameraFollower follower = cameraObject != null ? cameraObject.GetComponent<DebugFreeCameraFollower>() : null;
                follower?.StopFollowing();
                FreeCamera controller = cameraObject != null ? cameraObject.GetComponent<FreeCamera>() : null;
                controller?.ReleaseInput();
                RestorePlayerAndCamera();
            }
            catch (Exception exception)
            {
                _logger?.LogError($"SRB failed while restoring the raid camera: {exception}");
            }
            finally
            {
                if (cameraObject != null)
                {
                    UnityEngine.Object.Destroy(cameraObject);
                }

                _sourceCamera = null;
                _sourceCameraParent = null;
                Cursor.lockState = _previousCursorLock;
                Cursor.visible = _previousCursorVisible;
            }

            _logger?.LogInfo($"SRB debug free camera disabled ({reason}).");
        }

        private static void RestorePlayerAndCamera()
        {
            if (_sourceCamera != null)
            {
                Transform sourceTransform = _sourceCamera.transform;
                sourceTransform.SetParent(_sourceCameraParent, false);
                sourceTransform.localPosition = _sourceCameraLocalPosition;
                sourceTransform.localRotation = _sourceCameraLocalRotation;
                sourceTransform.localScale = _sourceCameraLocalScale;

                if (_sourceCameraParent != null)
                {
                    int maximumSiblingIndex = Mathf.Max(0, _sourceCameraParent.childCount - 1);
                    sourceTransform.SetSiblingIndex(Mathf.Min(_sourceCameraSiblingIndex, maximumSiblingIndex));
                }
            }

            if (_playerInputSuppressed)
            {
                GamePlayerOwner.SetIgnoreInput(false);
                _playerInputSuppressed = false;
            }
        }
    }
}
