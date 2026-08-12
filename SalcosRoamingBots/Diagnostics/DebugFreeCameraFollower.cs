using EFT;
using UnityEngine;

namespace SalcosRoamingBots.Diagnostics
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(32000)]
    internal sealed class DebugFreeCameraFollower : MonoBehaviour
    {
        internal Transform TargetCamera { get; set; }

        private BotOwner _followBot;
        private Vector3 _followOffset;
        private Vector3 _lastRigPosition;

        internal BotOwner FollowBot => _followBot;
        internal bool IsFollowing => _followBot != null && !_followBot.IsDead;

        private void LateUpdate()
        {
            if (_followBot != null)
            {
                if (_followBot.IsDead || _followBot.BotState != EBotState.Active)
                {
                    ClearFollowTarget();
                }
                else
                {
                    // FreeCamera may have moved the rig since the previous LateUpdate. Preserve that
                    // movement as an orbit-offset adjustment before following the bot's new position.
                    _followOffset += transform.position - _lastRigPosition;
                    Vector3 focus = _followBot.Position + Vector3.up * 1.5f;
                    transform.position = focus + _followOffset;
                    Vector3 lookDirection = focus - transform.position;
                    if (lookDirection.sqrMagnitude > 0.01f)
                    {
                        transform.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
                    }

                    _lastRigPosition = transform.position;
                }
            }

            if (TargetCamera != null)
            {
                TargetCamera.SetPositionAndRotation(transform.position, transform.rotation);
            }
        }

        internal void StopFollowing()
        {
            ClearFollowTarget();
            TargetCamera = null;
            enabled = false;
        }

        internal void SetFollowTarget(BotOwner bot)
        {
            _followBot = bot;
            if (_followBot == null)
            {
                return;
            }

            Vector3 focus = _followBot.Position + Vector3.up * 1.5f;
            _followOffset = -transform.forward * 12f + Vector3.up * 5f;
            transform.position = focus + _followOffset;
            Vector3 lookDirection = focus - transform.position;
            if (lookDirection.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            }

            _lastRigPosition = transform.position;
        }

        internal void ClearFollowTarget()
        {
            _followBot = null;
            _followOffset = Vector3.zero;
            _lastRigPosition = transform.position;
        }
    }
}
