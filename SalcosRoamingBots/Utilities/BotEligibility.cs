using System;
using System.Collections.Generic;
using EFT;
using SalcosRoamingBots.Configuration;
using UnityEngine;

namespace SalcosRoamingBots.Utilities
{
    internal static class BotEligibility
    {
        private static readonly HashSet<string> ScavRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "assault",
            "assaultGroup",
            "cursedAssault",
            "spiritSpring",
            "spiritWinter",
            "peacemaker",
            "skier"
        };

        private static readonly HashSet<string> RaiderAndRogueRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pmcBot",
            "exUsec",
            "arenaFighter",
            "arenaFighterEvent"
        };

        internal static bool CanRoam(BotOwner bot)
        {
            if (bot == null || bot.IsDead || bot.BotState != EBotState.Active || bot.Mover == null || bot.Profile?.Info?.Settings == null)
            {
                return false;
            }

            if (SrbSettings.GroupLeadersOnly.Value && bot.BotsGroup != null && bot.BotsGroup.MembersCount > 1 && (bot.Boss == null || !bot.Boss.IamBoss))
            {
                return false;
            }

            if (!IsRoleEnabled(bot))
            {
                return false;
            }

            if (NeedsImmediateCare(bot) || IsInOrNearCombat(bot, SrbSettings.PostCombatCooldown.Value))
            {
                return false;
            }

            return true;
        }

        private static bool IsRoleEnabled(BotOwner bot)
        {
            WildSpawnType role = bot.Profile.Info.Settings.Role;
            string roleName = role.ToString();

            if (role == WildSpawnType.pmcBEAR || role == WildSpawnType.pmcUSEC)
            {
                return SrbSettings.EnablePmcs.Value;
            }

            bool isPlayerScav = role == WildSpawnType.assault && !string.IsNullOrEmpty(bot.Profile.Info.MainProfileNickname);
            if (isPlayerScav)
            {
                return SrbSettings.EnablePlayerScavs.Value;
            }

            if (ScavRoles.Contains(roleName))
            {
                return SrbSettings.EnableScavs.Value;
            }

            if (RaiderAndRogueRoles.Contains(roleName))
            {
                return SrbSettings.EnableRaidersAndRogues.Value;
            }

            if (roleName.StartsWith("follower", StringComparison.OrdinalIgnoreCase) || roleName.IndexOf("helper", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return SrbSettings.EnableFollowers.Value;
            }

            if (roleName.StartsWith("boss", StringComparison.OrdinalIgnoreCase)
                || roleName.StartsWith("sectant", StringComparison.OrdinalIgnoreCase)
                || roleName.IndexOf("tagilla", StringComparison.OrdinalIgnoreCase) >= 0
                || roleName.IndexOf("killa", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return SrbSettings.EnableBosses.Value;
            }

            return SrbSettings.EnableSpecialRoles.Value;
        }

        private static bool NeedsImmediateCare(BotOwner bot)
        {
            try
            {
                return bot.Medecine?.FirstAid?.Have2Do == true || bot.Medecine?.SurgicalKit?.HaveWork == true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsInOrNearCombat(BotOwner bot, float cooldown)
        {
            try
            {
                if (bot.Memory == null)
                {
                    return false;
                }

                if (bot.Memory.GoalEnemy != null || bot.Memory.DangerData?.HaveCloseDanger == true)
                {
                    return true;
                }

                float now = Time.time;
                return IsRecent(now, bot.Memory.LastTimeHit, cooldown)
                    || IsRecent(now, bot.Memory.EnemySetTime, cooldown)
                    || IsRecent(now, bot.Memory.LastEnemyTimeSeen, cooldown)
                    || IsRecent(now, bot.Memory.UnderFireTime, cooldown);
            }
            catch
            {
                return true;
            }
        }

        private static bool IsRecent(float now, float eventTime, float cooldown)
        {
            return eventTime > 0f && now >= eventTime && now - eventTime < cooldown;
        }
    }
}

