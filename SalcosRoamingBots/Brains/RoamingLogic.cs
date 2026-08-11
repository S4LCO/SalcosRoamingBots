using System;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using SalcosRoamingBots.Configuration;
using SalcosRoamingBots.Diagnostics;
using SalcosRoamingBots.Models;
using SalcosRoamingBots.Navigation;
using UnityEngine;

namespace SalcosRoamingBots.Brains
{
    internal sealed class RoamingLogic : CustomLogic
    {
        private RoamingState _state;
        private int _appliedPathVersion = -1;
        private float _nextMovementUpdate;
        private float _nextErrorLogTime;

        public RoamingLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            try
            {
                BotOwner.PatrollingData?.Pause();
            }
            catch
            {
                // Patrolling state differs between some EFT brains. Movement can still proceed.
            }
        }

        public override void Stop()
        {
            try
            {
                if (_state != null)
                {
                    _state.LayerActive = false;
                }

                BotOwner.Mover?.Sprint(false);
                BotOwner.Mover?.Stop();
                BotOwner.PatrollingData?.Unpause();
            }
            catch
            {
                // Never let cleanup break the bot brain that is taking over.
            }

            _appliedPathVersion = -1;
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (!(data is RoamingActionData roamingData))
            {
                return;
            }

            _state = roamingData.State;
            _state.LayerActive = true;

            if (Time.time < _nextMovementUpdate)
            {
                return;
            }

            _nextMovementUpdate = Time.time + SrbSettings.MovementUpdateInterval.Value;

            try
            {
                UpdateRoaming();
            }
            catch (Exception exception)
            {
                _state.DisabledUntil = Time.time + 30f;
                RoamingCoordinator.FailTarget(_state, TargetFailureReason.MovementException);
                BotOwner.Mover?.Stop();

                if (Time.time >= _nextErrorLogTime)
                {
                    _nextErrorLogTime = Time.time + 30f;
                    SalcosRoamingBotsPlugin.Log?.LogError($"SRB movement update failed: {exception}");
                }
            }
        }

        private void UpdateRoaming()
        {
            if (_state == null || !_state.LayerActive || BotOwner == null || BotOwner.IsDead || BotOwner.Mover == null)
            {
                return;
            }

            if (!_state.HasTarget)
            {
                BotOwner.Mover.Sprint(false);
                if (Time.time >= _state.HoldUntil)
                {
                    RoamingCoordinator.RequestTarget(_state);
                }

                return;
            }

            float remainingDistance = Vector3.Distance(BotOwner.Position, _state.Destination);
            if (remainingDistance <= SrbSettings.TargetReachedDistance.Value)
            {
                BotOwner.Mover.Sprint(false);
                BotOwner.Mover.Stop();
                RoamingCoordinator.CompleteTarget(_state);
                _state.HoldUntil = Time.time + _state.NextFloat(SrbSettings.MinimumPauseAtTarget.Value, SrbSettings.MaximumPauseAtTarget.Value);
                _appliedPathVersion = -1;
                return;
            }

            if (_appliedPathVersion != _state.PathVersion)
            {
                ApplyPath();
            }

            MaintainMovement(remainingDistance);
            CheckProgress();
        }

        private void ApplyPath()
        {
            if (_state.PathCorners == null || _state.PathCorners.Length < 2)
            {
                RoamingCoordinator.FailTarget(_state, TargetFailureReason.EmptyPath);
                return;
            }

            if (BotOwner.BotLay.IsLay)
            {
                BotOwner.BotLay.GetUp(true);
            }

            BotOwner.WeaponManager?.Stationary?.StartMove();
            BotOwner.Mover.GoToByWay(_state.PathCorners, SrbSettings.TargetReachedDistance.Value);

            _appliedPathVersion = _state.PathVersion;
            _state.LastProgressPosition = BotOwner.Position;
            _state.LastProgressTime = Time.time;
        }

        private void MaintainMovement(float remainingDistance)
        {
            BotOwner.SetPose(1f);
            BotOwner.BotLay.GetUp(true);
            BotOwner.BewarePlantedMine?.Update();
            BotOwner.SetTargetMoveSpeed(1f);
            BotOwner.DoorOpener?.UpdateDoorInteractionStatus();
            BotOwner.Steering?.LookToMovingDirection();
            BotOwner.MagazineChecker?.ManualUpdate();

            bool canSprint = SrbSettings.AllowSprinting.Value
                && remainingDistance > SrbSettings.SprintAboveDistance.Value
                && !BotOwner.Mover.NoSprint
                && BotOwner.GetPlayer?.Physical?.CanSprint == true
                && BotOwner.GetPlayer.Physical.Stamina.NormalValue > 0.3f;

            BotOwner.Mover.Sprint(canSprint);
        }

        private void CheckProgress()
        {
            if (Time.time < _state.NextProgressCheckTime)
            {
                return;
            }

            _state.NextProgressCheckTime = Time.time + SrbSettings.ProgressCheckInterval.Value;
            float movement = Vector3.Distance(_state.LastMovementSamplePosition, BotOwner.Position);
            RaidStatistics.RecordMovement(movement);
            _state.LastMovementSamplePosition = BotOwner.Position;
            float progress = Vector3.Distance(_state.LastProgressPosition, BotOwner.Position);
            if (progress >= SrbSettings.StuckDistance.Value)
            {
                _state.LastProgressPosition = BotOwner.Position;
                _state.LastProgressTime = Time.time;
                return;
            }

            if (Time.time - _state.LastProgressTime < SrbSettings.StuckTimeout.Value)
            {
                return;
            }

            BotOwner.Mover.Sprint(false);
            BotOwner.Mover.Stop();
            RoamingCoordinator.FailTarget(_state, TargetFailureReason.Stuck);
            _state.HoldUntil = Time.time + _state.NextFloat(1f, 3f);
            _appliedPathVersion = -1;
        }
    }
}
