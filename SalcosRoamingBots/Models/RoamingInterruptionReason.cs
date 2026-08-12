namespace SalcosRoamingBots.Models
{
    internal enum RoamingInterruptionReason
    {
        None,
        Combat,
        Danger,
        Medical,
        HigherPriorityLayer,
        GroupRole,
        BotRole,
        Compatibility,
        Disabled,
        BotUnavailable,
        NavigationBackoff
    }
}
