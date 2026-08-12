using System;
using System.Text;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using SalcosRoamingBots.Compatibility;
using SalcosRoamingBots.Configuration;
using SalcosRoamingBots.Diagnostics;
using SalcosRoamingBots.Models;
using SalcosRoamingBots.Navigation;
using SalcosRoamingBots.Utilities;
using UnityEngine;

namespace SalcosRoamingBots.Brains
{
    internal sealed class RoamingLayer : CustomLayer
    {
        private readonly RoamingState _state;
        private readonly RoamingActionData _actionData;
        private float _nextEligibilityCheck;
        private bool _previousActive;
        private float _nextErrorLogTime;

        public RoamingLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
        {
            _state = new RoamingState(botOwner);
            _actionData = new RoamingActionData(_state);
            RoamingCoordinator.RegisterState(_state);
        }

        public override string GetName()
        {
            return "SRB Roaming";
        }

        public override bool IsActive()
        {
            if (Time.time < _state.EmergencyYieldUntil)
            {
                _previousActive = false;
                return false;
            }

            if (!_state.HasTarget && !_state.SearchQueued && Time.time < _state.NextSearchAllowedTime)
            {
                _state.PendingInterruptionReason = RoamingInterruptionReason.NavigationBackoff;
                _previousActive = false;
                return false;
            }

            if (Time.time < _nextEligibilityCheck)
            {
                return _previousActive;
            }

            _nextEligibilityCheck = Time.time + SrbSettings.LayerDecisionInterval.Value;

            try
            {
                if (Time.time < _state.DisabledUntil)
                {
                    _previousActive = false;
                    _state.PendingInterruptionReason = RoamingInterruptionReason.BotUnavailable;
                }
                else if (!SrbSettings.Enabled.Value)
                {
                    _previousActive = false;
                    _state.PendingInterruptionReason = RoamingInterruptionReason.Disabled;
                }
                else if (!CompatibilityManager.IsRoamingGloballyAllowed())
                {
                    _previousActive = false;
                    _state.PendingInterruptionReason = RoamingInterruptionReason.Compatibility;
                }
                else
                {
                    BotEligibilityResult result = BotEligibility.Evaluate(BotOwner);
                    _previousActive = result.CanRoam;
                    if (result.CanRoam)
                    {
                        _state.PendingInterruptionReason = RoamingInterruptionReason.None;
                    }
                    else
                    {
                        _state.PendingInterruptionReason = result.Reason;
                    }
                }
            }
            catch (Exception exception)
            {
                _previousActive = false;
                _state.PendingInterruptionReason = RoamingInterruptionReason.BotUnavailable;
                _state.DisabledUntil = Time.time + 30f;
                if (Time.time >= _nextErrorLogTime)
                {
                    _nextErrorLogTime = Time.time + 30f;
                    SalcosRoamingBotsPlugin.Log?.LogError($"SRB eligibility check failed for a bot: {exception}");
                }
            }

            return _previousActive;
        }

        public override Action GetNextAction()
        {
            return new Action(typeof(RoamingLogic), "Map-wide roaming", _actionData);
        }

        public override bool IsCurrentActionEnding()
        {
            return false;
        }

        public override void Start()
        {
            _state.LayerActive = true;
            RaidStatistics.RecordBotActivated(BotOwner);
        }

        public override void Stop()
        {
            _state.LayerActive = false;
            RoamingInterruptionReason reason = _state.PendingInterruptionReason;
            if (reason == RoamingInterruptionReason.None || reason == RoamingInterruptionReason.HigherPriorityLayer)
            {
                RoamingInterruptionReason safetyBlock = BotEligibility.GetImmediateSafetyBlock(BotOwner, SrbSettings.PostCombatCooldown.Value);
                if (safetyBlock != RoamingInterruptionReason.None)
                {
                    reason = safetyBlock;
                }
            }

            RoamingCoordinator.InterruptTarget(_state, reason);
        }

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            stringBuilder.Append("SRB: ");
            if (_state.HasTarget)
            {
                stringBuilder.Append($"target {_state.Destination}, path v{_state.PathVersion}");
            }
            else if (_state.SearchQueued)
            {
                stringBuilder.Append("searching");
            }
            else
            {
                stringBuilder.Append("idle");
            }
        }
    }
}
