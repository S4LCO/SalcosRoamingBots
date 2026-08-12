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
    internal enum TargetFailureReason
    {
        EmptyPath,
        MovementException,
        Stuck
    }

    internal enum CandidateRejectionReason
    {
        NavMeshSample,
        TooShort,
        Reserved,
        BlockedSector,
        IncompletePath,
        EmptyPath
    }

    internal static class RaidStatistics
    {
        private static readonly HashSet<string> ActiveBotIds = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<long> VisitedSectors = new HashSet<long>();
        private static readonly HashSet<long> TargetSectors = new HashSet<long>();
        private static readonly Dictionary<string, BotSearchStatistics> BotSearches = new Dictionary<string, BotSearchStatistics>(StringComparer.Ordinal);
        private static readonly List<BotSearchStatistics> SearchHotspots = new List<BotSearchStatistics>();

        private static ManualLogSource _logger;
        private static bool _raidActive;
        private static float _raidStartedAt;
        private static float _nextRaidStateCheck;

        private static int _searchRequests;
        private static int _searchFailures;
        private static int _candidateEvaluations;
        private static int _pathCalculations;
        private static int _completeCandidates;
        private static int _targetsAssigned;
        private static int _targetsReached;
        private static int _targetsInterrupted;
        private static int _pendingSearchesCancelled;
        private static int _targetsFailed;
        private static int _stuckRecoveries;
        private static int _emptyPathFailures;
        private static int _movementExceptions;
        private static int _peakSearchQueue;
        private static int _combatInterruptions;
        private static int _dangerInterruptions;
        private static int _medicalInterruptions;
        private static int _otherLayerInterruptions;
        private static int _otherInterruptions;
        private static int _navigationBackoffs;
        private static int _resumeRequests;
        private static int _resumeSuccesses;
        private static int _resumeFailures;
        private static int _coldTargetSectors;
        private static int _temporarilyBlockedSectors;
        private static int _navMeshSampleRejections;
        private static int _tooShortRejections;
        private static int _reservationRejections;
        private static int _blockedSectorRejections;
        private static int _incompletePathRejections;
        private static int _emptyCandidatePaths;
        private static int _edgeCandidates;
        private static float _assignedPathDistance;
        private static float _reachedPathDistance;
        private static float _actualRoamingDistance;
        private static float _minimumAdaptiveScale;

        internal static void Initialize(ManualLogSource logger)
        {
            _logger = logger;
            _raidActive = false;
            _nextRaidStateCheck = 0f;
            ResetCounters();
        }

        internal static void UpdateRaidState()
        {
            if (Time.unscaledTime < _nextRaidStateCheck)
            {
                return;
            }

            // Singleton<T>.Instantiated is an O(1) lifecycle check and avoids scanning the scene.
            _nextRaidStateCheck = Time.unscaledTime + 1f;
            bool gameWorldExists = Singleton<GameWorld>.Instantiated;

            if (gameWorldExists && !_raidActive)
            {
                BeginRaid();
            }
            else if (!gameWorldExists && _raidActive)
            {
                FinishRaid();
            }
        }

        internal static void Shutdown()
        {
            if (_raidActive)
            {
                FinishRaid();
            }

            _logger = null;
        }

        internal static void RecordBotActivated(BotOwner bot)
        {
            if (!ShouldCollect || bot == null)
            {
                return;
            }

            string id = bot.ProfileId;
            if (!string.IsNullOrEmpty(id))
            {
                ActiveBotIds.Add(id);
            }
        }

        internal static void RecordSearchRequested(RoamingState state, int queueSize)
        {
            if (!ShouldCollect)
            {
                return;
            }

            _searchRequests++;
            GetBotSearchStatistics(state).Searches++;
            if (queueSize > _peakSearchQueue)
            {
                _peakSearchQueue = queueSize;
            }
        }

        internal static void RecordCandidateEvaluation()
        {
            if (ShouldCollect)
            {
                _candidateEvaluations++;
            }
        }

        internal static void RecordPathCalculation()
        {
            if (ShouldCollect)
            {
                _pathCalculations++;
            }
        }

        internal static void RecordCompleteCandidate()
        {
            if (ShouldCollect)
            {
                _completeCandidates++;
            }
        }

        internal static void RecordSearchFailed(RoamingState state, int consecutiveFailures)
        {
            if (ShouldCollect)
            {
                _searchFailures++;
                BotSearchStatistics statistics = GetBotSearchStatistics(state);
                statistics.NoRouteSearches++;
                if (consecutiveFailures > statistics.MaximumFailureStreak)
                {
                    statistics.MaximumFailureStreak = consecutiveFailures;
                }
            }
        }

        internal static void RecordSearchSucceeded(RoamingState state)
        {
            if (ShouldCollect)
            {
                GetBotSearchStatistics(state).SuccessfulSearches++;
            }
        }

        internal static void RecordTargetAssigned(float pathLength)
        {
            if (!ShouldCollect)
            {
                return;
            }

            _targetsAssigned++;
            _assignedPathDistance += Mathf.Max(0f, pathLength);
        }

        internal static void RecordTargetReached(float assignedPathLength)
        {
            if (!ShouldCollect)
            {
                return;
            }

            _targetsReached++;
            _reachedPathDistance += Mathf.Max(0f, assignedPathLength);
        }

        internal static void RecordInterruption(RoamingInterruptionReason reason, bool hadTarget, bool hadPendingSearch)
        {
            if (!ShouldCollect)
            {
                return;
            }

            if (hadTarget)
            {
                _targetsInterrupted++;
            }

            if (hadPendingSearch)
            {
                _pendingSearchesCancelled++;
            }

            switch (reason)
            {
                case RoamingInterruptionReason.Combat:
                    _combatInterruptions++;
                    break;
                case RoamingInterruptionReason.Danger:
                    _dangerInterruptions++;
                    break;
                case RoamingInterruptionReason.Medical:
                    _medicalInterruptions++;
                    break;
                case RoamingInterruptionReason.HigherPriorityLayer:
                    _otherLayerInterruptions++;
                    break;
                case RoamingInterruptionReason.NavigationBackoff:
                    _navigationBackoffs++;
                    break;
                default:
                    _otherInterruptions++;
                    break;
            }
        }

        internal static void RecordResumeRequested()
        {
            if (ShouldCollect)
            {
                _resumeRequests++;
            }
        }

        internal static void RecordResumeSucceeded()
        {
            if (ShouldCollect)
            {
                _resumeSuccesses++;
            }
        }

        internal static void RecordResumeFailed()
        {
            if (ShouldCollect)
            {
                _resumeFailures++;
            }
        }

        internal static void RecordSectorVisited(long sector)
        {
            if (ShouldCollect)
            {
                VisitedSectors.Add(sector);
            }
        }

        internal static void RecordTargetSectorAssigned(long sector, bool wasCold)
        {
            if (!ShouldCollect)
            {
                return;
            }

            TargetSectors.Add(sector);
            if (wasCold)
            {
                _coldTargetSectors++;
            }
        }

        internal static void RecordSectorTemporarilyBlocked()
        {
            if (ShouldCollect)
            {
                _temporarilyBlockedSectors++;
            }
        }

        internal static void RecordCandidateRejected(CandidateRejectionReason reason)
        {
            if (!ShouldCollect)
            {
                return;
            }

            switch (reason)
            {
                case CandidateRejectionReason.NavMeshSample:
                    _navMeshSampleRejections++;
                    break;
                case CandidateRejectionReason.TooShort:
                    _tooShortRejections++;
                    break;
                case CandidateRejectionReason.Reserved:
                    _reservationRejections++;
                    break;
                case CandidateRejectionReason.BlockedSector:
                    _blockedSectorRejections++;
                    break;
                case CandidateRejectionReason.IncompletePath:
                    _incompletePathRejections++;
                    break;
                case CandidateRejectionReason.EmptyPath:
                    _emptyCandidatePaths++;
                    break;
            }
        }

        internal static void RecordEdgeCandidate()
        {
            if (ShouldCollect)
            {
                _edgeCandidates++;
            }
        }

        internal static void RecordAdaptiveScale(float scale)
        {
            if (ShouldCollect && scale < _minimumAdaptiveScale)
            {
                _minimumAdaptiveScale = scale;
            }
        }

        internal static void RecordTargetFailure(TargetFailureReason reason, bool hadTarget)
        {
            if (!ShouldCollect)
            {
                return;
            }

            if (hadTarget)
            {
                _targetsFailed++;
            }

            switch (reason)
            {
                case TargetFailureReason.Stuck:
                    _stuckRecoveries++;
                    break;
                case TargetFailureReason.EmptyPath:
                    _emptyPathFailures++;
                    break;
                case TargetFailureReason.MovementException:
                    _movementExceptions++;
                    break;
            }
        }

        internal static void RecordMovement(float distance)
        {
            if (ShouldCollect && distance > 0f && !float.IsNaN(distance) && !float.IsInfinity(distance))
            {
                _actualRoamingDistance += distance;
            }
        }

        internal static string Describe(TargetFailureReason reason)
        {
            switch (reason)
            {
                case TargetFailureReason.EmptyPath:
                    return "empty path";
                case TargetFailureReason.MovementException:
                    return "movement exception";
                case TargetFailureReason.Stuck:
                    return "no movement progress";
                default:
                    return "unknown reason";
            }
        }

        private static bool ShouldCollect => _raidActive && SrbSettings.RaidSummaryLogging.Value;

        private static void BeginRaid()
        {
            ResetCounters();
            RoamingCoordinator.BeginRaid();
            _raidActive = true;
            _raidStartedAt = Time.realtimeSinceStartup;

            if (SrbSettings.RaidSummaryLogging.Value)
            {
                _logger?.LogInfo("SRB raid statistics started.");
            }
        }

        private static void FinishRaid()
        {
            float duration = Mathf.Max(0f, Time.realtimeSinceStartup - _raidStartedAt);
            _raidActive = false;

            if (SrbSettings.RaidSummaryLogging.Value)
            {
                float completionRate = _targetsAssigned > 0 ? _targetsReached * 100f / _targetsAssigned : 0f;
                _logger?.LogInfo(
                    $"SRB raid summary: duration={FormatDuration(duration)}; roaming bots={ActiveBotIds.Count}; " +
                    $"actual roaming distance={FormatDistance(_actualRoamingDistance)}.");
                _logger?.LogInfo(
                    $"SRB raid targets: assigned={_targetsAssigned}; reached={_targetsReached} ({completionRate:0.0}%); " +
                    $"interrupted={_targetsInterrupted}; failed={_targetsFailed}; assigned route distance={FormatDistance(_assignedPathDistance)}; " +
                    $"reached route distance={FormatDistance(_reachedPathDistance)}.");
                _logger?.LogInfo(
                    $"SRB raid navigation: searches={_searchRequests}; no-route searches={_searchFailures}; " +
                    $"candidate evaluations={_candidateEvaluations}; NavMesh calculations={_pathCalculations}; " +
                    $"complete candidates={_completeCandidates}; peak queue={_peakSearchQueue}; cancelled searches={_pendingSearchesCancelled}; " +
                    $"stuck recoveries={_stuckRecoveries}; empty paths={_emptyPathFailures}; movement exceptions={_movementExceptions}.");
                float averageRoute = _targetsAssigned > 0 ? _assignedPathDistance / _targetsAssigned : 0f;
                float averageAdaptiveScale = RoamingCoordinator.GetAverageAdaptiveDistanceScale();
                _logger?.LogInfo(
                    $"SRB raid coverage: visited sectors={VisitedSectors.Count}; target sectors={TargetSectors.Count}; " +
                    $"first-time target sectors={_coldTargetSectors}; average assigned route={FormatDistance(averageRoute)}; " +
                    $"minimum per-bot adaptive range={_minimumAdaptiveScale * 100f:0}%; average final adaptive range={averageAdaptiveScale * 100f:0}%.");
                _logger?.LogInfo(
                    $"SRB raid handoffs: combat={_combatInterruptions}; danger={_dangerInterruptions}; medical={_medicalInterruptions}; " +
                    $"higher-priority layer={_otherLayerInterruptions}; navigation backoff={_navigationBackoffs}; other={_otherInterruptions}; resume attempts={_resumeRequests}; " +
                    $"resumed={_resumeSuccesses}; resume failed={_resumeFailures}.");
                _logger?.LogInfo(
                    $"SRB raid candidate filtering: NavMesh misses={_navMeshSampleRejections}; incomplete routes={_incompletePathRejections}; " +
                    $"too short={_tooShortRejections}; reserved={_reservationRejections}; blocked sectors={_blockedSectorRejections}; " +
                    $"empty candidate paths={_emptyCandidatePaths}; edge penalties={_edgeCandidates}; sectors blocked after failures={_temporarilyBlockedSectors}.");
                LogSearchHotspots();
            }

            RoamingCoordinator.EndRaid();
            ResetCounters();
        }

        private static void ResetCounters()
        {
            ActiveBotIds.Clear();
            VisitedSectors.Clear();
            TargetSectors.Clear();
            BotSearches.Clear();
            SearchHotspots.Clear();
            _searchRequests = 0;
            _searchFailures = 0;
            _candidateEvaluations = 0;
            _pathCalculations = 0;
            _completeCandidates = 0;
            _targetsAssigned = 0;
            _targetsReached = 0;
            _targetsInterrupted = 0;
            _pendingSearchesCancelled = 0;
            _targetsFailed = 0;
            _stuckRecoveries = 0;
            _emptyPathFailures = 0;
            _movementExceptions = 0;
            _peakSearchQueue = 0;
            _combatInterruptions = 0;
            _dangerInterruptions = 0;
            _medicalInterruptions = 0;
            _otherLayerInterruptions = 0;
            _otherInterruptions = 0;
            _navigationBackoffs = 0;
            _resumeRequests = 0;
            _resumeSuccesses = 0;
            _resumeFailures = 0;
            _coldTargetSectors = 0;
            _temporarilyBlockedSectors = 0;
            _navMeshSampleRejections = 0;
            _tooShortRejections = 0;
            _reservationRejections = 0;
            _blockedSectorRejections = 0;
            _incompletePathRejections = 0;
            _emptyCandidatePaths = 0;
            _edgeCandidates = 0;
            _assignedPathDistance = 0f;
            _reachedPathDistance = 0f;
            _actualRoamingDistance = 0f;
            _minimumAdaptiveScale = 1f;
        }

        private static BotSearchStatistics GetBotSearchStatistics(RoamingState state)
        {
            BotOwner bot = state?.Bot;
            string id = bot?.ProfileId;
            if (string.IsNullOrEmpty(id))
            {
                id = bot != null ? $"instance:{bot.GetHashCode()}" : "unknown";
            }

            if (BotSearches.TryGetValue(id, out BotSearchStatistics statistics))
            {
                return statistics;
            }

            string nickname = bot?.Profile?.Info?.Nickname ?? "unknown bot";
            string role = bot?.Profile?.Info?.Settings != null ? bot.Profile.Info.Settings.Role.ToString() : "unknown role";
            statistics = new BotSearchStatistics($"{nickname} ({role})");
            BotSearches.Add(id, statistics);
            return statistics;
        }

        private static void LogSearchHotspots()
        {
            SearchHotspots.Clear();
            foreach (BotSearchStatistics statistics in BotSearches.Values)
            {
                if (statistics.NoRouteSearches > 0)
                {
                    SearchHotspots.Add(statistics);
                }
            }

            SearchHotspots.Sort((left, right) =>
            {
                int failureComparison = right.NoRouteSearches.CompareTo(left.NoRouteSearches);
                return failureComparison != 0 ? failureComparison : right.Searches.CompareTo(left.Searches);
            });

            if (SearchHotspots.Count == 0)
            {
                _logger?.LogInfo("SRB raid search hotspots: none.");
                return;
            }

            int count = Mathf.Min(3, SearchHotspots.Count);
            string summary = string.Empty;
            for (int i = 0; i < count; i++)
            {
                BotSearchStatistics statistics = SearchHotspots[i];
                float failureRate = statistics.Searches > 0 ? statistics.NoRouteSearches * 100f / statistics.Searches : 0f;
                if (i > 0)
                {
                    summary += "; ";
                }

                summary += $"{statistics.Label}={statistics.NoRouteSearches}/{statistics.Searches} no-route ({failureRate:0}%), max streak={statistics.MaximumFailureStreak}";
            }

            _logger?.LogInfo($"SRB raid search hotspots: {summary}.");
        }

        private sealed class BotSearchStatistics
        {
            internal BotSearchStatistics(string label)
            {
                Label = label;
            }

            internal string Label { get; }
            internal int Searches { get; set; }
            internal int SuccessfulSearches { get; set; }
            internal int NoRouteSearches { get; set; }
            internal int MaximumFailureStreak { get; set; }
        }

        private static string FormatDuration(float seconds)
        {
            TimeSpan duration = TimeSpan.FromSeconds(seconds);
            return duration.TotalHours >= 1d
                ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
                : $"{duration.Minutes}:{duration.Seconds:00}";
        }

        private static string FormatDistance(float meters)
        {
            return meters >= 1000f ? $"{meters / 1000f:0.00} km" : $"{meters:0} m";
        }
    }
}
