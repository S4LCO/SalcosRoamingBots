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
        }

        public override string GetName()
        {
            return "SRB Roaming";
        }

        public override bool IsActive()
        {
            if (Time.time < _nextEligibilityCheck)
            {
                return _previousActive;
            }

            _nextEligibilityCheck = Time.time + SrbSettings.LayerDecisionInterval.Value;

            try
            {
                _previousActive = Time.time >= _state.DisabledUntil
                    && CompatibilityManager.IsRoamingGloballyAllowed()
                    && BotEligibility.CanRoam(BotOwner);
            }
            catch (Exception exception)
            {
                _previousActive = false;
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
            RoamingCoordinator.InterruptTarget(_state);
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
