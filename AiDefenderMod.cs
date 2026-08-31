using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace MyPuckMod
{
    /// <summary>
    /// Vier-Bot-Team-Modus.
    ///
    /// /aiteam red  -> 4 rote Bots: 1 Goalie, 1 Defender, 2 aggressive Skater
    /// /aiteam blue -> 4 blaue Bots: 1 Goalie, 1 Defender, 2 aggressive Skater
    /// /aibotclear  -> entfernt alle Bots
    /// </summary>
    public class AiDefenderMod : IPuckPlugin
    {
        private Action<Dictionary<string, object>> onChatCommandAction;
        private Harmony harmony;

        private static bool IsServer
        {
            get
            {
                return NetworkManager.Singleton != null &&
                    NetworkManager.Singleton.IsServer;
            }
        }

        public bool OnEnable()
        {
            try
            {
                harmony = new Harmony("mypuckmod.aiteam");
                harmony.PatchAll();

                onChatCommandAction = OnChatCommand;
                EventManager.AddEventListener(
                    "Event_Server_OnChatCommand",
                    onChatCommandAction);

                Debug.Log("[AiDefenderMod] Four-bot team mode enabled.");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[AiDefenderMod] Enable failed: " + exception);
                return false;
            }
        }

        public bool OnDisable()
        {
            try
            {
                if (onChatCommandAction != null)
                {
                    EventManager.RemoveEventListener(
                        "Event_Server_OnChatCommand",
                        onChatCommandAction);
                    onChatCommandAction = null;
                }

                if (harmony != null)
                {
                    harmony.UnpatchSelf();
                    harmony = null;
                }

                TeamBotRegistry.Clear();
                Debug.Log("[AiDefenderMod] Disabled.");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[AiDefenderMod] Disable failed: " + exception);
                return false;
            }
        }

        private void OnChatCommand(Dictionary<string, object> message)
        {
            if (!IsServer)
            {
                return;
            }

            string command = (string)message["command"];
            string[] args = (string[])message["args"];

            if (string.IsNullOrEmpty(command))
            {
                return;
            }

            command = command.Trim().ToLowerInvariant();

            if (command == "/aibotclear")
            {
                int removed = BotManager.Server_DespawnBots();
                TeamBotRegistry.Clear();
                Debug.Log("[AiDefenderMod] Removed " + removed + " bot(s).");
                return;
            }

            if (command != "/aiteam")
            {
                return;
            }

            PlayerTeam team = ParseTeam(args);
            if (team == PlayerTeam.None)
            {
                Debug.LogWarning("[AiDefenderMod] Use /aiteam red or /aiteam blue.");
                return;
            }

            SpawnFourBotTeam(team);
        }

        private static PlayerTeam ParseTeam(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return PlayerTeam.None;
            }

            if (args[0].Equals("red", StringComparison.OrdinalIgnoreCase))
            {
                return PlayerTeam.Red;
            }

            if (args[0].Equals("blue", StringComparison.OrdinalIgnoreCase))
            {
                return PlayerTeam.Blue;
            }

            return PlayerTeam.None;
        }

        private static void SpawnFourBotTeam(PlayerTeam team)
        {
            BotManager.Server_DespawnBots();
            TeamBotRegistry.Clear();

            int spawned = BotManager.Server_SpawnBots(4, team);
            PlayerManager playerManager = MonoBehaviourSingleton<PlayerManager>.Instance;

            if (!playerManager || spawned == 0)
            {
                Debug.LogWarning("[AiDefenderMod] No bot positions were available.");
                return;
            }

            List<Player> bots = playerManager.GetPlayers(false)
                .Where(player => player.OwnerClientId >= BotManager.BotClientIdBase)
                .OrderBy(player => player.OwnerClientId)
                .ToList();

            if (bots.Count == 0)
            {
                return;
            }

            Player goalie = bots[0];
            MoveBotToGoaliePosition(goalie, team);

            TeamBotRegistry.GoalieId = goalie.OwnerClientId;
            TeamBotRegistry.DefenderId = bots.Count > 1 ? bots[1].OwnerClientId : 0UL;

            for (int i = 2; i < bots.Count; i++)
            {
                TeamBotRegistry.AggressiveIds.Add(bots[i].OwnerClientId);
            }

            ApplyBrains(bots);

            Debug.Log("[AiDefenderMod] Spawned " + bots.Count +
                " bots for " + team + ": goalie, defender, " +
                TeamBotRegistry.AggressiveIds.Count + " attackers.");
        }

        private static void MoveBotToGoaliePosition(Player goalie, PlayerTeam team)
        {
            PlayerPosition[] positions = UnityEngine.Object.FindObjectsByType<PlayerPosition>(
                FindObjectsSortMode.None);

            PlayerPosition goaliePosition = positions.FirstOrDefault(position =>
                !position.IsClaimed &&
                position.Team == team &&
                position.Role == PlayerRole.Goalie);

            if (goaliePosition == null)
            {
                Debug.LogWarning("[AiDefenderMod] No free goalie position found. " +
                    "The first bot remains a defensive skater.");
                return;
            }

            if (goalie.PlayerPosition != null)
            {
                goalie.PlayerPosition.Server_Unclaim();
            }

            goaliePosition.Server_Claim(goalie);
        }

        private static void ApplyBrains(List<Player> bots)
        {
            foreach (Player bot in bots)
            {
                BotInputDriver driver = bot.GetComponent<BotInputDriver>();
                if (driver == null)
                {
                    continue;
                }

                if (bot.OwnerClientId == TeamBotRegistry.GoalieId)
                {
                    driver.SetBrain(new TeamGoalieBrain());
                }
                else if (bot.OwnerClientId == TeamBotRegistry.DefenderId)
                {
                    driver.SetBrain(new TeamDefenderBrain());
                }
                else if (TeamBotRegistry.AggressiveIds.Contains(bot.OwnerClientId))
                {
                    driver.SetBrain(new TeamAggressiveBrain());
                }
            }
        }
    }

    internal static class TeamBotRegistry
    {
        internal static ulong GoalieId;
        internal static ulong DefenderId;
        internal static readonly HashSet<ulong> AggressiveIds = new HashSet<ulong>();

        internal static void Clear()
        {
            GoalieId = 0UL;
            DefenderId = 0UL;
            AggressiveIds.Clear();
        }
    }
}
