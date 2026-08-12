using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using SalcosRoamingBots.Configuration;
using SalcosRoamingBots.Models;
using SalcosRoamingBots.Navigation;
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
        private static readonly List<RoamingState> OverlayStates = new List<RoamingState>();
        private static RoamingState _selectedState;
        private static GUIStyle _markerStyle;
        private static GUIStyle _selectedMarkerStyle;

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

            if (IsActive)
            {
                DebugFreeCameraFollower follower = _cameraObject.GetComponent<DebugFreeCameraFollower>();
                if (_selectedState != null && (_selectedState.Bot == null || _selectedState.Bot.IsDead))
                {
                    _selectedState = null;
                    follower?.ClearFollowTarget();
                }

                if (SrbSettings.DebugSelectNextBot.Value.IsDown())
                {
                    SelectNextBot(follower);
                }

                if (SrbSettings.DebugFollowSelectedBot.Value.IsDown())
                {
                    ToggleSelectedBotFollow(follower);
                }
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

            string selected = _selectedState?.Bot?.Profile?.Info != null
                ? $"Selected: {_selectedState.Bot.Profile.Info.Nickname} ({GetStateText(_selectedState)})"
                : "Selected: none";
            float displayedRangeScale = _selectedState?.AdaptiveDistanceScale ?? RoamingCoordinator.GetAverageAdaptiveDistanceScale();
            GUI.Box(
                new Rect(12f, 12f, 660f, 76f),
                $"SRB DEBUG FREECAM  |  {SrbSettings.DebugFreeCameraToggle.Value.MainKey}: exit  |  " +
                "WASD/Q/E: move  |  Left Shift: boost  |  Esc: mouse\n" +
                $"{SrbSettings.DebugSelectNextBot.Value.MainKey}: next bot  |  {SrbSettings.DebugFollowSelectedBot.Value.MainKey}: follow  |  " +
                $"{(_selectedState != null ? "Selected" : "Average")} range scale: {displayedRangeScale * 100f:0}%  |  {selected}");

            if (SrbSettings.DebugBotOverlay.Value)
            {
                DrawBotOverlay();
            }
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
            _selectedState = null;
            OverlayStates.Clear();

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

        private static void SelectNextBot(DebugFreeCameraFollower follower)
        {
            RoamingCoordinator.GetLiveStates(OverlayStates);
            if (OverlayStates.Count == 0)
            {
                _selectedState = null;
                follower?.ClearFollowTarget();
                return;
            }

            int currentIndex = -1;
            for (int i = 0; i < OverlayStates.Count; i++)
            {
                if (ReferenceEquals(OverlayStates[i], _selectedState))
                {
                    currentIndex = i;
                    break;
                }
            }

            _selectedState = OverlayStates[(currentIndex + 1) % OverlayStates.Count];
            if (follower?.IsFollowing == true)
            {
                follower.SetFollowTarget(_selectedState.Bot);
            }
        }

        private static void ToggleSelectedBotFollow(DebugFreeCameraFollower follower)
        {
            if (follower == null)
            {
                return;
            }

            if (_selectedState == null || _selectedState.Bot == null || _selectedState.Bot.IsDead)
            {
                SelectNextBot(follower);
            }

            if (_selectedState == null)
            {
                return;
            }

            if (follower.IsFollowing)
            {
                follower.ClearFollowTarget();
            }
            else
            {
                follower.SetFollowTarget(_selectedState.Bot);
            }
        }

        private static void DrawBotOverlay()
        {
            Camera camera = _sourceCamera != null ? _sourceCamera : Camera.main;
            if (camera == null)
            {
                return;
            }

            EnsureGuiStyles();
            RoamingCoordinator.GetLiveStates(OverlayStates);

            for (int i = 0; i < OverlayStates.Count; i++)
            {
                RoamingState state = OverlayStates[i];
                BotOwner bot = state.Bot;
                if (bot == null || bot.IsDead)
                {
                    continue;
                }

                Vector3 worldPosition = bot.Position + Vector3.up * 2.1f;
                Vector3 screenPosition = camera.WorldToScreenPoint(worldPosition);
                if (screenPosition.z <= 0f)
                {
                    continue;
                }

                float distance = Vector3.Distance(camera.transform.position, bot.Position);
                bool selected = ReferenceEquals(state, _selectedState);
                GUIStyle style = selected ? _selectedMarkerStyle : _markerStyle;
                style.normal.textColor = GetStateColor(state, selected);
                string nickname = bot.Profile?.Info?.Nickname ?? "Bot";
                string role = bot.Profile?.Info?.Settings != null ? bot.Profile.Info.Settings.Role.ToString() : "unknown";
                string prefix = selected ? "> " : string.Empty;
                GUI.Label(
                    new Rect(screenPosition.x - 110f, Screen.height - screenPosition.y - 10f, 260f, 36f),
                    $"{prefix}{nickname} [{role}]  {distance:0}m\n{GetStateText(state)}",
                    style);
            }

            if (SrbSettings.DebugRouteLines.Value && _selectedState?.HasTarget == true)
            {
                DrawSelectedRoute(camera, _selectedState);
            }
        }

        private static void DrawSelectedRoute(Camera camera, RoamingState state)
        {
            Vector3[] corners = state.PathCorners;
            if (corners == null || corners.Length < 2)
            {
                return;
            }

            Vector3 previous = state.Bot.Position + Vector3.up * 0.25f;
            Color color = new Color(0.2f, 1f, 0.45f, 0.9f);
            for (int i = 1; i < corners.Length; i++)
            {
                Vector3 next = corners[i] + Vector3.up * 0.25f;
                DrawWorldLine(camera, previous, next, color, 2f);
                previous = next;
            }
        }

        private static void DrawWorldLine(Camera camera, Vector3 worldStart, Vector3 worldEnd, Color color, float width)
        {
            Vector3 start = camera.WorldToScreenPoint(worldStart);
            Vector3 end = camera.WorldToScreenPoint(worldEnd);
            if (start.z <= 0f || end.z <= 0f)
            {
                return;
            }

            Vector2 pointA = new Vector2(start.x, Screen.height - start.y);
            Vector2 pointB = new Vector2(end.x, Screen.height - end.y);
            Vector2 delta = pointB - pointA;
            float length = delta.magnitude;
            if (length < 1f)
            {
                return;
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, pointA);
            GUI.DrawTexture(new Rect(pointA.x, pointA.y - width * 0.5f, length, width), Texture2D.whiteTexture);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        private static void EnsureGuiStyles()
        {
            if (_markerStyle != null)
            {
                return;
            }

            _markerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.UpperCenter
            };
            _selectedMarkerStyle = new GUIStyle(_markerStyle)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold
            };
        }

        private static Color GetStateColor(RoamingState state, bool selected)
        {
            if (selected)
            {
                return new Color(1f, 0.85f, 0.2f);
            }

            if (state.LayerActive && state.HasTarget)
            {
                return new Color(0.25f, 1f, 0.45f);
            }

            if (state.SearchQueued)
            {
                return new Color(0.25f, 0.9f, 1f);
            }

            if (state.LastInterruptionReason == RoamingInterruptionReason.Combat
                || state.LastInterruptionReason == RoamingInterruptionReason.Danger)
            {
                return new Color(1f, 0.4f, 0.3f);
            }

            return Color.white;
        }

        private static string GetStateText(RoamingState state)
        {
            if (state.LayerActive && state.HasTarget)
            {
                float remaining = Vector3.Distance(state.Bot.Position, state.Destination);
                return $"SRB roaming - {remaining:0}m remaining";
            }

            if (state.SearchQueued)
            {
                return state.CanResumeTarget() ? "SRB validating previous route" : "SRB searching";
            }

            if (state.LayerActive && Time.time < state.HoldUntil)
            {
                return "SRB arrival pause";
            }

            if (!state.HasTarget && !state.SearchQueued && Time.time < state.NextSearchAllowedTime)
            {
                return $"SRB navigation backoff - {state.NextSearchAllowedTime - Time.time:0}s";
            }

            if (state.CanResumeTarget())
            {
                return $"Yielded: {state.LastInterruptionReason} - route saved";
            }

            return state.LayerActive ? "SRB idle" : $"Other AI: {state.LastInterruptionReason}";
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
