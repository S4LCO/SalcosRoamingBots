using System;
using System.Collections.Generic;
using EFT;
using SalcosRoamingBots.Configuration;
using UnityEngine;

namespace SalcosRoamingBots.Models
{
    internal sealed class RoamingState
    {
        private readonly List<Vector3> _recentDestinations = new List<Vector3>();

        internal RoamingState(BotOwner bot)
        {
            Bot = bot;
            int profileSeed = bot?.ProfileId?.GetHashCode() ?? 0;
            Random = new System.Random(profileSeed ^ Environment.TickCount);
            LastProgressPosition = bot?.Position ?? Vector3.zero;
            LastProgressTime = Time.time;
        }

        internal BotOwner Bot { get; }
        internal System.Random Random { get; }
        internal IReadOnlyList<Vector3> RecentDestinations => _recentDestinations;

        internal bool LayerActive { get; set; }
        internal bool SearchQueued { get; set; }
        internal int SearchGeneration { get; set; }
        internal int ConsecutiveSearchFailures { get; set; }
        internal float AdaptiveDistanceScale { get; set; } = 1f;
        internal float NextSearchAllowedTime { get; set; }
        internal float HoldUntil { get; set; }
        internal float DisabledUntil { get; set; }
        internal float EmergencyYieldUntil { get; set; }
        internal int ReservationId { get; set; }
        internal RoamingInterruptionReason PendingInterruptionReason { get; set; }
        internal RoamingInterruptionReason LastInterruptionReason { get; set; }
        internal float LastInterruptionTime { get; set; }

        internal bool HasVisitedSector { get; set; }
        internal int LastVisitedSectorX { get; set; }
        internal int LastVisitedSectorZ { get; set; }

        internal bool HasTarget { get; private set; }
        internal Vector3 Destination { get; private set; }
        internal Vector3[] PathCorners { get; private set; } = Array.Empty<Vector3>();
        internal float AssignedPathLength { get; private set; }
        internal int PathVersion { get; private set; }

        internal bool HasSuspendedTarget { get; private set; }
        internal Vector3 SuspendedDestination { get; private set; }
        internal float SuspendedTargetExpiresAt { get; private set; }

        internal Vector3 LastProgressPosition { get; set; }
        internal float LastProgressTime { get; set; }
        internal float NextProgressCheckTime { get; set; }
        internal Vector3 LastMovementSamplePosition { get; set; }

        internal void AssignTarget(Vector3 destination, Vector3[] corners, float pathLength)
        {
            Destination = destination;
            PathCorners = corners;
            AssignedPathLength = pathLength;
            HasTarget = true;
            PathVersion++;
            LastProgressPosition = Bot.Position;
            LastMovementSamplePosition = Bot.Position;
            LastProgressTime = Time.time;
            NextProgressCheckTime = Time.time + SrbSettings.ProgressCheckInterval.Value;
        }

        internal void ClearTarget(bool remember)
        {
            if (HasTarget && remember)
            {
                Remember(Destination);
            }

            HasTarget = false;
            PathCorners = Array.Empty<Vector3>();
            AssignedPathLength = 0f;
            PathVersion++;
        }

        internal void SuspendCurrentTarget(float lifetime)
        {
            if (!HasTarget || lifetime <= 0f)
            {
                return;
            }

            SuspendedDestination = Destination;
            SuspendedTargetExpiresAt = Time.time + lifetime;
            HasSuspendedTarget = true;
        }

        internal bool CanResumeTarget()
        {
            if (!HasSuspendedTarget)
            {
                return false;
            }

            if (Time.time <= SuspendedTargetExpiresAt)
            {
                return true;
            }

            ClearSuspendedTarget();
            return false;
        }

        internal void ClearSuspendedTarget()
        {
            HasSuspendedTarget = false;
            SuspendedDestination = Vector3.zero;
            SuspendedTargetExpiresAt = 0f;
        }

        internal float NextFloat(float minimum, float maximum)
        {
            if (maximum <= minimum)
            {
                return minimum;
            }

            return minimum + (float)Random.NextDouble() * (maximum - minimum);
        }

        private void Remember(Vector3 destination)
        {
            int capacity = SrbSettings.RecentTargetMemory.Value;
            if (capacity <= 0)
            {
                _recentDestinations.Clear();
                return;
            }

            _recentDestinations.Add(destination);
            while (_recentDestinations.Count > capacity)
            {
                _recentDestinations.RemoveAt(0);
            }
        }
    }
}
