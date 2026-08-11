using BepInEx.Bootstrap;
using BepInEx.Logging;
using SalcosRoamingBots.Configuration;

namespace SalcosRoamingBots.Compatibility
{
    internal static class CompatibilityManager
    {
        internal const string SainGuid = "me.sol.sain";
        internal const string LootingBotsGuid = "me.skwizzy.lootingbots";
        internal const string QuestingBotsGuid = "com.danw.questingbots";
        internal const string OrbitGuid = "com.chazut.orbit";

        internal static bool SainDetected { get; private set; }
        internal static bool LootingBotsDetected { get; private set; }
        internal static bool QuestingBotsDetected { get; private set; }
        internal static bool OrbitDetected { get; private set; }

        internal static bool ExternalObjectiveModDetected => QuestingBotsDetected || OrbitDetected;

        internal static void Scan(ManualLogSource logger)
        {
            SainDetected = Chainloader.PluginInfos.ContainsKey(SainGuid);
            LootingBotsDetected = Chainloader.PluginInfos.ContainsKey(LootingBotsGuid);
            QuestingBotsDetected = Chainloader.PluginInfos.ContainsKey(QuestingBotsGuid);
            OrbitDetected = Chainloader.PluginInfos.ContainsKey(OrbitGuid);

            logger.LogInfo($"Compatibility scan: SAIN={SainDetected}, LootingBots={LootingBotsDetected}, QuestingBots={QuestingBotsDetected}, ORBIT={OrbitDetected}");

            if (ExternalObjectiveModDetected && SrbSettings.ObjectivePolicy.Value == ExternalObjectivePolicy.DisableSrb)
            {
                logger.LogWarning("An external objective mod was detected. SRB roaming is suspended by the current compatibility policy.");
            }
        }

        internal static bool IsRoamingGloballyAllowed()
        {
            if (!SrbSettings.Enabled.Value)
            {
                return false;
            }

            return !ExternalObjectiveModDetected || SrbSettings.ObjectivePolicy.Value == ExternalObjectivePolicy.YieldByLayerPriority;
        }
    }
}

