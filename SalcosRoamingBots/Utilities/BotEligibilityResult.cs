using SalcosRoamingBots.Models;

namespace SalcosRoamingBots.Utilities
{
    internal readonly struct BotEligibilityResult
    {
        internal BotEligibilityResult(bool canRoam, RoamingInterruptionReason reason)
        {
            CanRoam = canRoam;
            Reason = reason;
        }

        internal bool CanRoam { get; }
        internal RoamingInterruptionReason Reason { get; }

        internal static BotEligibilityResult Allowed => new BotEligibilityResult(true, RoamingInterruptionReason.None);
    }
}
