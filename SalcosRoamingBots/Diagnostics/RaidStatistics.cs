using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using SalcosRoamingBots.Configuration;
using UnityEngine;

namespace SalcosRoamingBots.Diagnostics
{
    internal enum TargetFailureReason
    {
        EmptyPath,
        MovementException,
        Stuck
    }

    internal static class RaidStatistics
    {
        private static readonly HashSet<string> ActiveBotIds = new HashSet<string>(StringComparer.Ordinal);

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
        private static float _assignedPathDistance;
        private static float _reachedPathDistance;
        private static float _actualRoamingDistance;

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

        internal static void RecordSearchRequested(int queueSize)
        {
            if (!ShouldCollect)
            {
                return;
            }

            _searchRequests++;
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

        internal static void RecordSearchFailed()
        {
            if (ShouldCollect)
            {
                _searchFailures++;
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

        internal static void RecordInterruption(bool hadTarget, bool hadPendingSearch)
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
            }

            ResetCounters();
        }

        private static void ResetCounters()
        {
            ActiveBotIds.Clear();
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
            _assignedPathDistance = 0f;
            _reachedPathDistance = 0f;
            _actualRoamingDistance = 0f;
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
