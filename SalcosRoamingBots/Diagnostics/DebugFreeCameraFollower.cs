using UnityEngine;

namespace SalcosRoamingBots.Diagnostics
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(32000)]
    internal sealed class DebugFreeCameraFollower : MonoBehaviour
    {
        internal Transform TargetCamera { get; set; }

        private void LateUpdate()
        {
            if (TargetCamera != null)
            {
                TargetCamera.SetPositionAndRotation(transform.position, transform.rotation);
            }
        }

        internal void StopFollowing()
        {
            TargetCamera = null;
            enabled = false;
        }
    }
}
