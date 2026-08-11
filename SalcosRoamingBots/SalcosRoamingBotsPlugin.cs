using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using DrakiaXYZ.BigBrain.Brains;
using SalcosRoamingBots.Brains;
using SalcosRoamingBots.Compatibility;
using SalcosRoamingBots.Configuration;
using SalcosRoamingBots.Diagnostics;
using SalcosRoamingBots.Navigation;

namespace SalcosRoamingBots
{
    [BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
    [BepInDependency(ModInfo.SptCoreGuid, "4.1.0")]
    [BepInDependency(ModInfo.BigBrainGuid, "1.5.0")]
    [BepInDependency(ModInfo.WaypointsGuid, "1.9.0")]
    public sealed class SalcosRoamingBotsPlugin : BaseUnityPlugin
    {
        private static readonly List<string> SupportedBrainNames = new List<string>
        {
            "PmcBear",
            "PmcUsec",
            "Assault",
            "CursAssault",
            "PMC",
            "ExUsec",
            "ArenaFighter",
            "Obdolbs",
            "InfectedSlow",
            "Knight",
            "BigPipe",
            "BirdEye",
            "BossPartisan",
            "SectantWarrior",
            "SectantPriest",
            "Prst event",
            "SctPredvst",
            "PrizrakSt",
            "Oni",
            "Tagilla",
            "TagillaAgro",
            "Killa",
            "KillaAgro",
            "BossBully",
            "BossSanitar",
            "BossGluhar",
            "BossKojaniy",
            "BossBoar",
            "BossKolontay",
            "FollowerBully",
            "FollowerSanitar",
            "TagillaFollower",
            "HelperAgro",
            "FollowerGluharAssault",
            "FollowerGluharProtect",
            "FollowerGluharScout",
            "FollowerKojaniy",
            "BoarSniper",
            "FlBoar",
            "FlBoarCl",
            "FlBoarSt",
            "FlKlnAslt",
            "KolonSec"
        };

        internal static ManualLogSource Log { get; private set; }

        private void Awake()
        {
            Log = Logger;
            Logger.LogInfo($"Loading {ModInfo.Name} {ModInfo.Version}");

            try
            {
                SrbSettings.Bind(Config);
                CompatibilityManager.Scan(Logger);
                RoamingCoordinator.Initialize(Logger);
                RaidStatistics.Initialize(Logger);
                DebugFreeCameraController.Initialize(Logger);

                int layerId = BrainManager.AddCustomLayer(
                    typeof(RoamingLayer),
                    SupportedBrainNames,
                    SrbSettings.LayerPriority.Value);

                Logger.LogInfo($"Registered SRB BigBrain layer {layerId} at priority {SrbSettings.LayerPriority.Value} for {SupportedBrainNames.Count} brain names.");
                Logger.LogInfo($"Completed loading {ModInfo.Name}");
            }
            catch (Exception exception)
            {
                Logger.LogError($"Failed to initialize {ModInfo.Name}: {exception}");
                throw;
            }
        }

        private void Update()
        {
            DebugFreeCameraController.Update();
            RaidStatistics.UpdateRaidState();
            RoamingCoordinator.Update();
        }

        private void OnGUI()
        {
            DebugFreeCameraController.DrawOverlay();
        }

        private void OnDestroy()
        {
            DebugFreeCameraController.Shutdown();
            RaidStatistics.Shutdown();
            RoamingCoordinator.Shutdown();
        }
    }
}
