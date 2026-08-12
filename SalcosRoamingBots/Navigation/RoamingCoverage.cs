using System;
using System.Collections.Generic;
using SalcosRoamingBots.Configuration;
using SalcosRoamingBots.Diagnostics;
using SalcosRoamingBots.Models;
using UnityEngine;

namespace SalcosRoamingBots.Navigation
{
    internal static class RoamingCoverage
    {
        internal const float SectorSize = 50f;

        private static readonly Dictionary<SectorKey, SectorActivity> Activity = new Dictionary<SectorKey, SectorActivity>();
        private static readonly Dictionary<SectorKey, float> BlockedUntil = new Dictionary<SectorKey, float>();

        internal static void Reset()
        {
            Activity.Clear();
            BlockedUntil.Clear();
        }

        internal static void RecordVisit(RoamingState state, Vector3 position)
        {
            if (state == null)
            {
                return;
            }

            SectorKey sector = SectorKey.FromPosition(position);
            if (state.HasVisitedSector && state.LastVisitedSectorX == sector.X && state.LastVisitedSectorZ == sector.Z)
            {
                return;
            }

            state.HasVisitedSector = true;
            state.LastVisitedSectorX = sector.X;
            state.LastVisitedSectorZ = sector.Z;

            if (!Activity.TryGetValue(sector, out SectorActivity activity))
            {
                activity = new SectorActivity();
                Activity.Add(sector, activity);
            }

            activity.Visits++;
            RaidStatistics.RecordSectorVisited(sector.Encoded);
        }

        internal static int GetHeat(Vector3 position)
        {
            SectorKey sector = SectorKey.FromPosition(position);
            if (!Activity.TryGetValue(sector, out SectorActivity activity))
            {
                return 0;
            }

            return activity.Visits + activity.TargetsAssigned * 2;
        }

        internal static void RecordTargetAssigned(Vector3 position)
        {
            SectorKey sector = SectorKey.FromPosition(position);
            int priorHeat = 0;
            if (!Activity.TryGetValue(sector, out SectorActivity activity))
            {
                activity = new SectorActivity();
                Activity.Add(sector, activity);
            }
            else
            {
                priorHeat = activity.Visits + activity.TargetsAssigned * 2;
            }

            activity.TargetsAssigned++;
            RaidStatistics.RecordTargetSectorAssigned(sector.Encoded, priorHeat == 0);
        }

        internal static void RecordTargetReached(Vector3 position)
        {
            BlockedUntil.Remove(SectorKey.FromPosition(position));
        }

        internal static void RecordFailure(Vector3 position, TargetFailureReason reason)
        {
            float cooldown = SrbSettings.FailedAreaCooldown.Value;
            if (cooldown <= 0f || reason == TargetFailureReason.EmptyPath)
            {
                return;
            }

            SectorKey sector = SectorKey.FromPosition(position);
            float multiplier = reason == TargetFailureReason.Stuck ? 1f : 0.5f;
            BlockedUntil[sector] = Mathf.Max(GetBlockedUntil(sector), Time.time + cooldown * multiplier);
            RaidStatistics.RecordSectorTemporarilyBlocked();
        }

        internal static bool IsTemporarilyBlocked(Vector3 position)
        {
            SectorKey sector = SectorKey.FromPosition(position);
            if (!BlockedUntil.TryGetValue(sector, out float until))
            {
                return false;
            }

            if (Time.time < until)
            {
                return true;
            }

            BlockedUntil.Remove(sector);
            return false;
        }

        private static float GetBlockedUntil(SectorKey sector)
        {
            return BlockedUntil.TryGetValue(sector, out float until) ? until : 0f;
        }

        private sealed class SectorActivity
        {
            internal int Visits;
            internal int TargetsAssigned;
        }

        private readonly struct SectorKey : IEquatable<SectorKey>
        {
            private SectorKey(int x, int z)
            {
                X = x;
                Z = z;
            }

            internal int X { get; }
            internal int Z { get; }
            internal long Encoded => ((long)X << 32) ^ (uint)Z;

            internal static SectorKey FromPosition(Vector3 position)
            {
                return new SectorKey(
                    Mathf.FloorToInt(position.x / SectorSize),
                    Mathf.FloorToInt(position.z / SectorSize));
            }

            public bool Equals(SectorKey other)
            {
                return X == other.X && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is SectorKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (X * 397) ^ Z;
                }
            }
        }
    }
}
