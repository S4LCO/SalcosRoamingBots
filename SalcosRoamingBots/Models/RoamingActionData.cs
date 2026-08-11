using DrakiaXYZ.BigBrain.Brains;

namespace SalcosRoamingBots.Models
{
    internal sealed class RoamingActionData : CustomLayer.ActionData
    {
        internal RoamingActionData(RoamingState state)
        {
            State = state;
        }

        internal RoamingState State { get; }
    }
}

