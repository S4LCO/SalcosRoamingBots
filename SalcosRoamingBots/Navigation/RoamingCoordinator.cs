using System;
using System.Collections.Generic;
using BepInEx.Logging;
using EFT;
using SalcosRoamingBots.Configuration;
using SalcosRoamingBots.Diagnostics;
using SalcosRoamingBots.Models;
using UnityEngine;
using UnityEngine.AI;

namespace SalcosRoamingBots.Navigation
{
    internal static class RoamingCoordinator
    {
        private static readonly Queue<TargetSearchJob> SearchJobs = new Queue<TargetSearchJob>();
        private static readonly Dictionary<int, TargetReservation> Reservations = new Dictionary<int, TargetReservation>();
        private static readonly List<int> StaleReservationIds = new List<int>();
        private static readonly List<WeakReference<RoamingState>> KnownStates = new List<WeakReference<RoamingState>>();

        private static ManualLogSource _logger;
        private static int _nextReservationId = 1;
        private static float _nextCleanupTime;
        internal static void Initialize(ManualLogSource logger)
        {
            _logger = logger;
            SearchJobs.Clear();
            Reservations.Clear();
            KnownStates.Clear();
            RoamingCoverage.Reset();
            _nextReservationId = 1;
            _nextCleanupTime = Time.time + 10f;
        }

        internal static void BeginRaid()
        {
            SearchJobs.Clear();
            Reservations.Clear();
            RoamingCoverage.Reset();
            _nextReservationId = 1;
            _nextCleanupTime = Time.time + 10f;

            for (int i = KnownStates.Count - 1; i >= 0; i--)
            {
                if (!KnownStates[i].TryGetTarget(out RoamingState state) || state == null)
                {
                    KnownStates.RemoveAt(i);
                    continue;
                }

                state.Bot?.Mover?.Sprint(false);
                state.Bot?.Mover?.Stop();
                state.SearchQueued = false;
                state.SearchGeneration++;
                state.ClearTarget(false);
                state.ClearSuspendedTarget();
                state.ReservationId = 0;
                state.ConsecutiveSearchFailures = 0;
                state.AdaptiveDistanceScale = 1f;
                state.NextSearchAllowedTime = 0f;
                state.HasVisitedSector = false;
            }
        }

        internal static void EndRaid()
        {
            SearchJobs.Clear();
            Reservations.Clear();
            KnownStates.Clear();
            RoamingCoverage.Reset();
        }

        internal static void Shutdown()
        {
            EndRaid();
            _logger = null;
        }

        internal static void RegisterState(RoamingState state)
        {
            if (state != null)
            {
                KnownStates.Add(new WeakReference<RoamingState>(state));
            }
        }

        internal static void GetLiveStates(List<RoamingState> destination)
        {
            destination.Clear();
            for (int i = KnownStates.Count - 1; i >= 0; i--)
            {
                if (!KnownStates[i].TryGetTarget(out RoamingState state) || state == null || !IsBotAlive(state.Bot))
                {
                    KnownStates.RemoveAt(i);
                    continue;
                }

                destination.Add(state);
            }
        }

        internal static float GetAverageAdaptiveDistanceScale()
        {
            float total = 0f;
            int count = 0;
            for (int i = KnownStates.Count - 1; i >= 0; i--)
            {
                if (!KnownStates[i].TryGetTarget(out RoamingState state) || state == null || !IsBotAlive(state.Bot))
                {
                    continue;
                }

                total += state.AdaptiveDistanceScale;
                count++;
            }

            return count > 0 ? total / count : 1f;
        }

        internal static void Update()
        {
            if (!SrbSettings.Enabled.Value)
            {
                return;
            }

            CleanupReservationsIfNeeded();

            int budget = SrbSettings.PathCalculationsPerFrame.Value;
            while (budget-- > 0 && SearchJobs.Count > 0)
            {
                TargetSearchJob job = SearchJobs.Dequeue();
                if (!job.IsCurrent())
                {
                    continue;
                }

                EvaluateCandidate(job);
                job.AttemptsRemaining--;

                if (job.AttemptsRemaining > 0)
                {
                    SearchJobs.Enqueue(job);
                }
                else
                {
                    FinishSearch(job);
                }
            }
        }

        internal static void RequestTarget(RoamingState state)
        {
            if (state == null || state.SearchQueued || state.HasTarget || Time.time < state.NextSearchAllowedTime)
            {
                return;
            }

            state.SearchQueued = true;
            int generation = ++state.SearchGeneration;
            bool resume = SrbSettings.ResumeAfterCombat.Value
                && state.CanResumeTarget()
                && !RoamingCoverage.IsTemporarilyBlocked(state.SuspendedDestination);

            TargetSearchJob job;
            if (resume)
            {
                job = new TargetSearchJob(state, generation, 1, true, state.SuspendedDestination);
                RaidStatistics.RecordResumeRequested();
            }
            else
            {
                if (state.HasSuspendedTarget)
                {
                    state.ClearSuspendedTarget();
                }

                job = new TargetSearchJob(state, generation, SrbSettings.CandidatesPerSearch.Value, false, Vector3.zero);
            }

            SearchJobs.Enqueue(job);
            RaidStatistics.RecordSearchRequested(state, SearchJobs.Count);
        }

        internal static void CancelPendingSearch(RoamingState state)
        {
            if (state == null || !state.SearchQueued)
            {
                return;
            }

            state.SearchQueued = false;
            state.SearchGeneration++;
        }

        internal static void CompleteTarget(RoamingState state)
        {
            RaidStatistics.RecordTargetReached(state.AssignedPathLength);
            RoamingCoverage.RecordTargetReached(state.Destination);
            ReleaseReservation(state);
            state.ClearTarget(true);
            state.ConsecutiveSearchFailures = 0;
        }

        internal static void InterruptTarget(RoamingState state, RoamingInterruptionReason reason)
        {
            if (state == null)
            {
                return;
            }

            if (reason == RoamingInterruptionReason.None)
            {
                reason = RoamingInterruptionReason.HigherPriorityLayer;
            }

            bool hadTarget = state.HasTarget;
            bool hadPendingSearch = state.SearchQueued;
            bool shouldSuspend = hadTarget
                && SrbSettings.ResumeAfterCombat.Value
                && (reason == RoamingInterruptionReason.Combat || reason == RoamingInterruptionReason.Danger);

            if (shouldSuspend)
            {
                state.SuspendCurrentTarget(Mathf.Max(60f, SrbSettings.PostCombatCooldown.Value + 120f));
            }
            else if (reason == RoamingInterruptionReason.Disabled
                || reason == RoamingInterruptionReason.Compatibility
                || reason == RoamingInterruptionReason.BotUnavailable)
            {
                state.ClearSuspendedTarget();
            }

            state.LastInterruptionReason = reason;
            state.LastInterruptionTime = Time.time;
            state.PendingInterruptionReason = RoamingInterruptionReason.None;
            RaidStatistics.RecordInterruption(reason, hadTarget, hadPendingSearch);
            CancelPendingSearch(state);
            ReleaseReservation(state);
            state.ClearTarget(false);
            state.NextSearchAllowedTime = Mathf.Max(state.NextSearchAllowedTime, Time.time + 0.5f);
        }

        internal static void FailTarget(RoamingState state, TargetFailureReason reason)
        {
            if (state == null)
            {
                return;
            }

            bool hadTarget = state.HasTarget;
            if (hadTarget)
            {
                RoamingCoverage.RecordFailure(state.Bot.Position, reason);
            }

            RaidStatistics.RecordTargetFailure(reason, hadTarget);
            ReleaseReservation(state);
            state.ClearTarget(false);
            state.ClearSuspendedTarget();
            state.ConsecutiveSearchFailures++;
            state.NextSearchAllowedTime = Time.time + SrbSettings.SearchRetryDelay.Value;

            if (SrbSettings.DebugLogging.Value)
            {
                _logger?.LogDebug($"{Describe(state.Bot)} discarded its roaming target: {RaidStatistics.Describe(reason)}");
            }
        }

        internal static void RecordBotPosition(RoamingState state, Vector3 position)
        {
            RoamingCoverage.RecordVisit(state, position);
        }

        private static void EvaluateCandidate(TargetSearchJob job)
        {
            RoamingState state = job.State;
            BotOwner bot = state.Bot;
            if (!IsBotAlive(bot))
            {
                return;
            }

            RaidStatistics.RecordCandidateEvaluation();

            if (job.IsResume)
            {
                float sampleRadius = Mathf.Min(8f, SrbSettings.NavMeshSampleRadius.Value);
                if (!NavMesh.SamplePosition(job.FixedDestination, out NavMeshHit resumeHit, sampleRadius, NavMesh.AllAreas))
                {
                    RaidStatistics.RecordCandidateRejected(CandidateRejectionReason.NavMeshSample);
                    return;
                }

                EvaluatePosition(job, resumeHit.position, true, 0f);
                return;
            }

            float failureScale = Mathf.Max(0.3f, 1f - state.ConsecutiveSearchFailures * 0.18f);
            float adaptiveScale = SrbSettings.AdaptiveDistanceScaling.Value ? state.AdaptiveDistanceScale : 1f;
            float totalScale = failureScale * adaptiveScale;
            float minimumDistance = Mathf.Min(SrbSettings.MinimumTargetDistance.Value, SrbSettings.MaximumTargetDistance.Value) * totalScale;
            float maximumDistance = Mathf.Max(SrbSettings.MinimumTargetDistance.Value, SrbSettings.MaximumTargetDistance.Value) * totalScale;

            float random = (float)state.Random.NextDouble();
            float distance = Mathf.Sqrt(Mathf.Lerp(minimumDistance * minimumDistance, maximumDistance * maximumDistance, random));
            float angle = (float)(state.Random.NextDouble() * Math.PI * 2.0);
            Vector3 rawCandidate = bot.Position + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);

            if (!NavMesh.SamplePosition(rawCandidate, out NavMeshHit hit, SrbSettings.NavMeshSampleRadius.Value, NavMesh.AllAreas))
            {
                RaidStatistics.RecordCandidateRejected(CandidateRejectionReason.NavMeshSample);
                return;
            }

            EvaluatePosition(job, hit.position, false, minimumDistance);
        }

        private static void EvaluatePosition(TargetSearchJob job, Vector3 candidate, bool resume, float minimumDistance)
        {
            RoamingState state = job.State;
            BotOwner bot = state.Bot;
            float directDistance = Vector3.Distance(bot.Position, candidate);

            if ((!resume && directDistance < minimumDistance * 0.45f)
                || (resume && directDistance <= SrbSettings.TargetReachedDistance.Value * 1.5f))
            {
                RaidStatistics.RecordCandidateRejected(CandidateRejectionReason.TooShort);
                return;
            }

            if (IsTooCloseToReservation(candidate, state))
            {
                RaidStatistics.RecordCandidateRejected(CandidateRejectionReason.Reserved);
                return;
            }

            if (RoamingCoverage.IsTemporarilyBlocked(candidate))
            {
                RaidStatistics.RecordCandidateRejected(CandidateRejectionReason.BlockedSector);
                return;
            }

            NavMeshPath path = job.WorkingPath;
            RaidStatistics.RecordPathCalculation();
            if (!NavMesh.CalculatePath(bot.Position, candidate, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                RaidStatistics.RecordCandidateRejected(CandidateRejectionReason.IncompletePath);
                return;
            }

            Vector3[] corners = path.corners;
            if (corners == null || corners.Length < 2)
            {
                RaidStatistics.RecordCandidateRejected(CandidateRejectionReason.EmptyPath);
                return;
            }

            float pathLength = CalculatePathLength(corners);
            float failureScale = Mathf.Max(0.3f, 1f - state.ConsecutiveSearchFailures * 0.18f);
            float adaptiveScale = SrbSettings.AdaptiveDistanceScaling.Value ? state.AdaptiveDistanceScale : 1f;
            if (!resume && pathLength < SrbSettings.MinimumPathLength.Value * failureScale * adaptiveScale)
            {
                RaidStatistics.RecordCandidateRejected(CandidateRejectionReason.TooShort);
                return;
            }

            RaidStatistics.RecordCompleteCandidate();
            job.CompleteCandidates++;

            float novelty = CalculateNovelty(candidate, state.RecentDestinations);
            float coverageBonus = 0f;
            if (SrbSettings.CoverageAwareRoaming.Value)
            {
                int heat = RoamingCoverage.GetHeat(candidate);
                coverageBonus = SrbSettings.MaximumTargetDistance.Value * 0.45f / (1f + heat * 0.7f);
            }

            float detourRatio = pathLength / Mathf.Max(1f, directDistance);
            float detourPenalty = Mathf.Max(0f, detourRatio - 2.2f) * Mathf.Min(pathLength, SrbSettings.MaximumTargetDistance.Value) * 0.35f;
            float blockedPathPenalty = CalculateBlockedPathPenalty(corners);
            float edgePenalty = 0f;
            if (NavMesh.FindClosestEdge(candidate, out NavMeshHit edgeHit, NavMesh.AllAreas) && edgeHit.distance < 2.5f)
            {
                edgePenalty = (2.5f - edgeHit.distance) * SrbSettings.MaximumTargetDistance.Value * 0.4f;
                RaidStatistics.RecordEdgeCandidate();
            }

            float score = resume
                ? pathLength + 10000f
                : pathLength + novelty * 0.35f + coverageBonus - detourPenalty - blockedPathPenalty - edgePenalty;

            if (score <= job.BestScore)
            {
                return;
            }

            job.BestScore = score;
            job.BestDestination = candidate;
            job.BestCorners = corners;
            job.BestPathLength = pathLength;
        }

        private static void FinishSearch(TargetSearchJob job)
        {
            RoamingState state = job.State;
            if (!job.IsCurrent())
            {
                return;
            }

            state.SearchQueued = false;

            if (job.BestCorners == null || job.BestCorners.Length < 2)
            {
                if (job.IsResume)
                {
                    state.ClearSuspendedTarget();
                    RaidStatistics.RecordResumeFailed();
                    state.NextSearchAllowedTime = Time.time + Mathf.Max(0.5f, SrbSettings.SearchRetryDelay.Value);
                }
                else
                {
                    state.ConsecutiveSearchFailures++;
                    RaidStatistics.RecordSearchFailed(state, state.ConsecutiveSearchFailures);
                    AdjustAdaptiveDistance(state, false);
                    float delayMultiplier = Mathf.Min(45f, Mathf.Pow(1.75f, Mathf.Min(8, state.ConsecutiveSearchFailures)));
                    float retryDelay = Mathf.Min(90f, SrbSettings.SearchRetryDelay.Value * delayMultiplier);
                    state.NextSearchAllowedTime = Time.time + retryDelay;
                }

                if (SrbSettings.DebugLogging.Value)
                {
                    string searchType = job.IsResume ? "resume route" : "complete roaming path";
                    _logger?.LogDebug($"No {searchType} found for {Describe(state.Bot)}; retry {state.ConsecutiveSearchFailures}.");
                }

                return;
            }

            if (!job.IsResume)
            {
                AdjustAdaptiveDistance(state, true);
                RaidStatistics.RecordSearchSucceeded(state);
            }

            state.ConsecutiveSearchFailures = 0;
            state.NextSearchAllowedTime = 0f;
            state.AssignTarget(job.BestDestination, job.BestCorners, job.BestPathLength);
            if (job.IsResume)
            {
                state.ClearSuspendedTarget();
                RaidStatistics.RecordResumeSucceeded();
            }

            RaidStatistics.RecordTargetAssigned(job.BestPathLength);
            RoamingCoverage.RecordTargetAssigned(job.BestDestination);
            Reserve(state, job.BestDestination);

            if (SrbSettings.DebugLogging.Value)
            {
                string resumed = job.IsResume ? "resumed" : "assigned";
                _logger?.LogDebug($"{Describe(state.Bot)} {resumed} roaming target {VectorText(job.BestDestination)} with {job.BestCorners.Length} corners.");
            }
        }

        private static void AdjustAdaptiveDistance(RoamingState state, bool success)
        {
            if (!SrbSettings.AdaptiveDistanceScaling.Value)
            {
                state.AdaptiveDistanceScale = 1f;
                return;
            }

            state.AdaptiveDistanceScale = success
                ? Mathf.Min(1f, state.AdaptiveDistanceScale + 0.02f)
                : Mathf.Max(0.35f, state.AdaptiveDistanceScale - 0.08f);
            RaidStatistics.RecordAdaptiveScale(state.AdaptiveDistanceScale);
        }

        private static bool IsTooCloseToReservation(Vector3 candidate, RoamingState requestingState)
        {
            float separation = SrbSettings.TargetSeparation.Value;
            if (separation <= 0f)
            {
                return false;
            }

            float separationSquared = separation * separation;
            foreach (TargetReservation reservation in Reservations.Values)
            {
                if (reservation.Id == requestingState.ReservationId || !reservation.IsAlive())
                {
                    continue;
                }

                if ((reservation.Position - candidate).sqrMagnitude < separationSquared)
                {
                    return true;
                }
            }

            return false;
        }

        private static float CalculateNovelty(Vector3 candidate, IReadOnlyList<Vector3> recentDestinations)
        {
            if (recentDestinations == null || recentDestinations.Count == 0)
            {
                return SrbSettings.MaximumTargetDistance.Value;
            }

            float nearest = float.MaxValue;
            for (int i = 0; i < recentDestinations.Count; i++)
            {
                float distance = Vector3.Distance(candidate, recentDestinations[i]);
                if (distance < nearest)
                {
                    nearest = distance;
                }
            }

            return nearest;
        }

        private static float CalculatePathLength(Vector3[] corners)
        {
            float length = 0f;
            for (int i = 1; i < corners.Length; i++)
            {
                length += Vector3.Distance(corners[i - 1], corners[i]);
            }

            return length;
        }

        private static float CalculateBlockedPathPenalty(Vector3[] corners)
        {
            if (corners == null || corners.Length < 2 || SrbSettings.FailedAreaCooldown.Value <= 0f)
            {
                return 0f;
            }

            for (int i = 1; i < corners.Length; i++)
            {
                Vector3 start = corners[i - 1];
                Vector3 end = corners[i];
                float distance = Vector3.Distance(start, end);
                int samples = Mathf.Max(1, Mathf.CeilToInt(distance / (RoamingCoverage.SectorSize * 0.5f)));
                for (int sample = 0; sample <= samples; sample++)
                {
                    if (RoamingCoverage.IsTemporarilyBlocked(Vector3.Lerp(start, end, sample / (float)samples)))
                    {
                        return SrbSettings.MaximumTargetDistance.Value * 0.5f;
                    }
                }
            }

            return 0f;
        }

        private static void Reserve(RoamingState state, Vector3 position)
        {
            ReleaseReservation(state);
            int id = _nextReservationId++;
            state.ReservationId = id;
            Reservations[id] = new TargetReservation(id, state.Bot, position);
        }

        private static void ReleaseReservation(RoamingState state)
        {
            if (state == null || state.ReservationId == 0)
            {
                return;
            }

            Reservations.Remove(state.ReservationId);
            state.ReservationId = 0;
        }

        private static void CleanupReservationsIfNeeded()
        {
            if (Time.time < _nextCleanupTime)
            {
                return;
            }

            _nextCleanupTime = Time.time + 10f;
            StaleReservationIds.Clear();

            foreach (KeyValuePair<int, TargetReservation> pair in Reservations)
            {
                if (!pair.Value.IsAlive())
                {
                    StaleReservationIds.Add(pair.Key);
                }
            }

            for (int i = 0; i < StaleReservationIds.Count; i++)
            {
                Reservations.Remove(StaleReservationIds[i]);
            }

            for (int i = KnownStates.Count - 1; i >= 0; i--)
            {
                if (!KnownStates[i].TryGetTarget(out RoamingState state) || state == null || !IsBotAlive(state.Bot))
                {
                    KnownStates.RemoveAt(i);
                }
            }
        }

        private static bool IsBotAlive(BotOwner bot)
        {
            return bot != null && !bot.IsDead && bot.BotState == EBotState.Active;
        }

        private static string Describe(BotOwner bot)
        {
            if (bot?.Profile?.Info == null)
            {
                return "unknown bot";
            }

            return $"{bot.Profile.Info.Nickname} ({bot.Profile.Info.Settings.Role})";
        }

        private static string VectorText(Vector3 vector)
        {
            return $"({vector.x:0.0}, {vector.y:0.0}, {vector.z:0.0})";
        }

        private sealed class TargetSearchJob
        {
            internal TargetSearchJob(RoamingState state, int generation, int attempts, bool isResume, Vector3 fixedDestination)
            {
                State = state;
                Generation = generation;
                AttemptsRemaining = attempts;
                IsResume = isResume;
                FixedDestination = fixedDestination;
            }

            internal RoamingState State { get; }
            internal int Generation { get; }
            internal int AttemptsRemaining { get; set; }
            internal bool IsResume { get; }
            internal Vector3 FixedDestination { get; }
            internal int CompleteCandidates { get; set; }
            internal NavMeshPath WorkingPath { get; } = new NavMeshPath();
            internal float BestScore { get; set; } = float.MinValue;
            internal Vector3 BestDestination { get; set; }
            internal Vector3[] BestCorners { get; set; }
            internal float BestPathLength { get; set; }

            internal bool IsCurrent()
            {
                return State != null
                    && State.SearchQueued
                    && State.SearchGeneration == Generation
                    && IsBotAlive(State.Bot);
            }
        }

        private sealed class TargetReservation
        {
            private readonly WeakReference<BotOwner> _bot;

            internal TargetReservation(int id, BotOwner bot, Vector3 position)
            {
                Id = id;
                _bot = new WeakReference<BotOwner>(bot);
                Position = position;
            }

            internal int Id { get; }
            internal Vector3 Position { get; }

            internal bool IsAlive()
            {
                return _bot.TryGetTarget(out BotOwner bot) && IsBotAlive(bot);
            }
        }
    }
}
