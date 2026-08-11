using BepInEx.Configuration;

namespace SalcosRoamingBots.Configuration
{
    internal static class SrbSettings
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> LayerPriority;
        internal static ConfigEntry<ExternalObjectivePolicy> ObjectivePolicy;
        internal static ConfigEntry<bool> DebugLogging;
        internal static ConfigEntry<bool> RaidSummaryLogging;

        internal static ConfigEntry<bool> EnablePmcs;
        internal static ConfigEntry<bool> EnablePlayerScavs;
        internal static ConfigEntry<bool> EnableScavs;
        internal static ConfigEntry<bool> EnableRaidersAndRogues;
        internal static ConfigEntry<bool> EnableBosses;
        internal static ConfigEntry<bool> EnableFollowers;
        internal static ConfigEntry<bool> EnableSpecialRoles;
        internal static ConfigEntry<bool> GroupLeadersOnly;

        internal static ConfigEntry<float> MinimumTargetDistance;
        internal static ConfigEntry<float> MaximumTargetDistance;
        internal static ConfigEntry<float> MinimumPathLength;
        internal static ConfigEntry<float> NavMeshSampleRadius;
        internal static ConfigEntry<int> CandidatesPerSearch;
        internal static ConfigEntry<float> TargetReachedDistance;
        internal static ConfigEntry<float> MinimumPauseAtTarget;
        internal static ConfigEntry<float> MaximumPauseAtTarget;
        internal static ConfigEntry<float> TargetSeparation;
        internal static ConfigEntry<int> RecentTargetMemory;
        internal static ConfigEntry<bool> AllowSprinting;
        internal static ConfigEntry<float> SprintAboveDistance;
        internal static ConfigEntry<float> PostCombatCooldown;

        internal static ConfigEntry<int> PathCalculationsPerFrame;
        internal static ConfigEntry<float> LayerDecisionInterval;
        internal static ConfigEntry<float> MovementUpdateInterval;
        internal static ConfigEntry<float> ProgressCheckInterval;
        internal static ConfigEntry<float> StuckDistance;
        internal static ConfigEntry<float> StuckTimeout;
        internal static ConfigEntry<float> SearchRetryDelay;

        internal static ConfigEntry<bool> EnableDebugFreeCamera;
        internal static ConfigEntry<KeyboardShortcut> DebugFreeCameraToggle;
        internal static ConfigEntry<float> DebugFreeCameraLookSpeed;
        internal static ConfigEntry<float> DebugFreeCameraMoveSpeed;
        internal static ConfigEntry<float> DebugFreeCameraBoostSpeed;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("1. General", "Enabled", true,
                "Master switch for SRB.");
            LayerPriority = config.Bind("1. General", "BigBrain layer priority", 17,
                new ConfigDescription("Priority of the SRB BigBrain layer. A game restart is required after changing this value.",
                    new AcceptableValueRange<int>(1, 100)));
            ObjectivePolicy = config.Bind("1. General", "External objective mods", ExternalObjectivePolicy.DisableSrb,
                "DisableSrb is the safest option when ORBIT or Questing Bots is installed. YieldByLayerPriority allows both mods to load and lets BigBrain priority decide which active layer controls a bot.");
            DebugLogging = config.Bind("1. General", "Debug logging", false,
                "Write target and recovery details to the BepInEx log.");
            RaidSummaryLogging = config.Bind("1. General", "Raid summary logging", true,
                "Write one compact SRB activity and navigation summary when a raid ends. This uses lightweight counters and does not enable per-bot debug spam.");

            EnablePmcs = config.Bind("2. Bot roles", "PMCs", true, "Allow BEAR and USEC bots to roam.");
            EnablePlayerScavs = config.Bind("2. Bot roles", "Player Scavs", true, "Allow simulated player Scavs to roam.");
            EnableScavs = config.Bind("2. Bot roles", "Scavs", true, "Allow regular Scavs to roam.");
            EnableRaidersAndRogues = config.Bind("2. Bot roles", "Raiders and Rogues", true, "Allow Raiders, Rogues, and arena fighters to roam.");
            EnableBosses = config.Bind("2. Bot roles", "Bosses", false, "Allow boss roles to roam. Experimental.");
            EnableFollowers = config.Bind("2. Bot roles", "Boss followers", false, "Allow boss follower roles to select their own roaming targets. Experimental.");
            EnableSpecialRoles = config.Bind("2. Bot roles", "Other special roles", false, "Allow unclassified non-sniper roles to roam. Experimental.");
            GroupLeadersOnly = config.Bind("2. Bot roles", "Group leaders choose targets", true,
                "Only a solo bot or group leader receives an SRB destination. Followers remain controlled by their normal follow behavior.");

            MinimumTargetDistance = config.Bind("3. Roaming", "Minimum target distance", 100f,
                new ConfigDescription("Preferred minimum straight-line distance to a new target.", new AcceptableValueRange<float>(20f, 600f)));
            MaximumTargetDistance = config.Bind("3. Roaming", "Maximum target distance", 450f,
                new ConfigDescription("Maximum radius used when sampling a new map position.", new AcceptableValueRange<float>(50f, 1200f)));
            MinimumPathLength = config.Bind("3. Roaming", "Minimum path length", 80f,
                new ConfigDescription("Preferred minimum NavMesh path length. It is reduced automatically after failed searches on small maps.", new AcceptableValueRange<float>(10f, 600f)));
            NavMeshSampleRadius = config.Bind("3. Roaming", "NavMesh sample radius", 30f,
                new ConfigDescription("Radius in which a sampled world position may snap to the Waypoints NavMesh.", new AcceptableValueRange<float>(2f, 100f)));
            CandidatesPerSearch = config.Bind("3. Roaming", "Candidates per search", 6,
                new ConfigDescription("Number of candidate paths scored for each destination search.", new AcceptableValueRange<int>(1, 16)));
            TargetReachedDistance = config.Bind("3. Roaming", "Target reached distance", 5f,
                new ConfigDescription("Distance at which a roaming target counts as reached.", new AcceptableValueRange<float>(1f, 15f)));
            MinimumPauseAtTarget = config.Bind("3. Roaming", "Minimum pause at target", 4f,
                new ConfigDescription("Minimum idle time before selecting another remote area.", new AcceptableValueRange<float>(0f, 60f)));
            MaximumPauseAtTarget = config.Bind("3. Roaming", "Maximum pause at target", 14f,
                new ConfigDescription("Maximum idle time before selecting another remote area.", new AcceptableValueRange<float>(0f, 120f)));
            TargetSeparation = config.Bind("3. Roaming", "Target separation", 35f,
                new ConfigDescription("Discourages multiple independently roaming bots from selecting the same area.", new AcceptableValueRange<float>(0f, 150f)));
            RecentTargetMemory = config.Bind("3. Roaming", "Recent target memory", 4,
                new ConfigDescription("Number of recently visited areas a bot tries not to revisit.", new AcceptableValueRange<int>(0, 12)));
            AllowSprinting = config.Bind("3. Roaming", "Allow sprinting", true, "Allow long-distance roaming bots to sprint when able.");
            SprintAboveDistance = config.Bind("3. Roaming", "Sprint above distance", 80f,
                new ConfigDescription("Bots may sprint while farther than this from their destination.", new AcceptableValueRange<float>(10f, 300f)));
            PostCombatCooldown = config.Bind("3. Roaming", "Post-combat cooldown", 12f,
                new ConfigDescription("Time after combat or danger before SRB may resume control.", new AcceptableValueRange<float>(0f, 90f)));

            PathCalculationsPerFrame = config.Bind("4. Performance", "Path calculations per frame", 2,
                new ConfigDescription("Global NavMesh calculation budget shared by every SRB bot.", new AcceptableValueRange<int>(1, 8)));
            LayerDecisionInterval = config.Bind("4. Performance", "Layer decision interval", 0.25f,
                new ConfigDescription("Seconds between the more expensive SRB eligibility checks for each bot.", new AcceptableValueRange<float>(0.05f, 2f)));
            MovementUpdateInterval = config.Bind("4. Performance", "Movement update interval", 0.2f,
                new ConfigDescription("Seconds between SRB movement maintenance updates.", new AcceptableValueRange<float>(0.05f, 1f)));
            ProgressCheckInterval = config.Bind("4. Performance", "Progress check interval", 1f,
                new ConfigDescription("Seconds between stuck-detection samples.", new AcceptableValueRange<float>(0.25f, 5f)));
            StuckDistance = config.Bind("4. Performance", "Stuck progress distance", 1.5f,
                new ConfigDescription("Movement required to reset the stuck timer.", new AcceptableValueRange<float>(0.25f, 10f)));
            StuckTimeout = config.Bind("4. Performance", "Stuck timeout", 18f,
                new ConfigDescription("Seconds without sufficient movement before a new destination is requested.", new AcceptableValueRange<float>(5f, 90f)));
            SearchRetryDelay = config.Bind("4. Performance", "Search retry delay", 2f,
                new ConfigDescription("Base delay after no complete route could be found.", new AcceptableValueRange<float>(0.5f, 20f)));

            EnableDebugFreeCamera = config.Bind("5. Debug tools", "Enable debug free camera", false,
                "Allow the SRB observation camera to be toggled during a raid. The player body remains at its real position and can still be injured or killed.");
            DebugFreeCameraToggle = config.Bind("5. Debug tools", "Debug free camera toggle", new KeyboardShortcut(UnityEngine.KeyCode.F9),
                "Toggle the observation camera during a raid.");
            DebugFreeCameraLookSpeed = config.Bind("5. Debug tools", "Debug free camera look speed", 5f,
                new ConfigDescription("Mouse look speed for the observation camera.", new AcceptableValueRange<float>(0.5f, 20f)));
            DebugFreeCameraMoveSpeed = config.Bind("5. Debug tools", "Debug free camera move speed", 15f,
                new ConfigDescription("Normal observation camera movement speed.", new AcceptableValueRange<float>(1f, 100f)));
            DebugFreeCameraBoostSpeed = config.Bind("5. Debug tools", "Debug free camera boost speed", 60f,
                new ConfigDescription("Observation camera movement speed while holding Left Shift.", new AcceptableValueRange<float>(5f, 300f)));
        }
    }
}
