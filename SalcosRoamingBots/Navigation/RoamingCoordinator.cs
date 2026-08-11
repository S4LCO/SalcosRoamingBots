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

        private static ManualLogSource _logger;
        private static int _nextReservationId = 1;
        private static float _nextCleanupTime;

        internal static void Initialize(ManualLogSource logger)
        {
            _logger = logger;
            SearchJobs.Clear();
            Reservations.Clear();
            _nextReservationId = 1;
            _nextCleanupTime = Time.time + 10f;
        }

        internal static void Shutdown()
        {
            SearchJobs.Clear();
            Reservations.Clear();
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
            int attempts = SrbSettings.CandidatesPerSearch.Value;
            SearchJobs.Enqueue(new TargetSearchJob(state, generation, attempts));
            RaidStatistics.RecordSearchRequested(SearchJobs.Count);
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
            ReleaseReservation(state);
            state.ClearTarget(true);
            state.ConsecutiveSearchFailures = 0;
        }

        internal static void InterruptTarget(RoamingState state)
        {
            if (state == null)
            {
                return;
            }

            bool hadTarget = state.HasTarget;
            bool hadPendingSearch = state.SearchQueued;
            RaidStatistics.RecordInterruption(hadTarget, hadPendingSearch);
            CancelPendingSearch(state);
            ReleaseReservation(state);
            state.ClearTarget(false);
            state.NextSearchAllowedTime = Time.time + 0.5f;
        }

        internal static void FailTarget(RoamingState state, TargetFailureReason reason)
        {
            if (state == null)
            {
                return;
            }

            bool hadTarget = state.HasTarget;
            RaidStatistics.RecordTargetFailure(reason, hadTarget);
            ReleaseReservation(state);
            state.ClearTarget(false);
            state.ConsecutiveSearchFailures++;
            state.NextSearchAllowedTime = Time.time + SrbSettings.SearchRetryDelay.Value;

            if (SrbSettings.DebugLogging.Value)
            {
                _logger?.LogDebug($"{Describe(state.Bot)} discarded its roaming target: {RaidStatistics.Describe(reason)}");
            }
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

            float failureScale = Mathf.Max(0.3f, 1f - state.ConsecutiveSearchFailures * 0.18f);
            float minimumDistance = Mathf.Min(SrbSettings.MinimumTargetDistance.Value, SrbSettings.MaximumTargetDistance.Value) * failureScale;
            float maximumDistance = Mathf.Max(SrbSettings.MinimumTargetDistance.Value, SrbSettings.MaximumTargetDistance.Value) * failureScale;

            float random = (float)state.Random.NextDouble();
            float distance = Mathf.Sqrt(Mathf.Lerp(minimumDistance * minimumDistance, maximumDistance * maximumDistance, random));
            float angle = (float)(state.Random.NextDouble() * Math.PI * 2.0);
            Vector3 rawCandidate = bot.Position + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);

            if (!NavMesh.SamplePosition(rawCandidate, out NavMeshHit hit, SrbSettings.NavMeshSampleRadius.Value, NavMesh.AllAreas))
            {
                return;
            }

            float directDistance = Vector3.Distance(bot.Position, hit.position);
            if (directDistance < minimumDistance * 0.45f || IsTooCloseToReservation(hit.position, state))
            {
                return;
            }

            NavMeshPath path = job.WorkingPath;
            RaidStatistics.RecordPathCalculation();
            if (!NavMesh.CalculatePath(bot.Position, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                return;
            }

            Vector3[] corners = path.corners;
            if (corners == null || corners.Length < 2)
            {
                return;
            }

            float pathLength = CalculatePathLength(corners);
            float requiredPathLength = SrbSettings.MinimumPathLength.Value * failureScale;
            if (pathLength < requiredPathLength)
            {
                return;
            }

            RaidStatistics.RecordCompleteCandidate();

            float novelty = CalculateNovelty(hit.position, state.RecentDestinations);
            float score = pathLength + novelty * 0.35f;
            if (score <= job.BestScore)
            {
                return;
            }

            job.BestScore = score;
            job.BestDestination = hit.position;
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
                RaidStatistics.RecordSearchFailed();
                state.ConsecutiveSearchFailures++;
                float delayMultiplier = Mathf.Min(4f, 1f + state.ConsecutiveSearchFailures * 0.5f);
                state.NextSearchAllowedTime = Time.time + SrbSettings.SearchRetryDelay.Value * delayMultiplier;

                if (SrbSettings.DebugLogging.Value)
                {
                    _logger?.LogDebug($"No complete roaming path found for {Describe(state.Bot)}; retry {state.ConsecutiveSearchFailures}.");
                }

                return;
            }

            state.ConsecutiveSearchFailures = 0;
            state.NextSearchAllowedTime = 0f;
            state.AssignTarget(job.BestDestination, job.BestCorners, job.BestPathLength);
            RaidStatistics.RecordTargetAssigned(job.BestPathLength);
            Reserve(state, job.BestDestination);

            if (SrbSettings.DebugLogging.Value)
            {
                _logger?.LogDebug($"Assigned {Describe(state.Bot)} a roaming target {VectorText(job.BestDestination)} with {job.BestCorners.Length} corners.");
            }
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
            internal TargetSearchJob(RoamingState state, int generation, int attempts)
            {
                State = state;
                Generation = generation;
                AttemptsRemaining = attempts;
            }

            internal RoamingState State { get; }
            internal int Generation { get; }
            internal int AttemptsRemaining { get; set; }
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
