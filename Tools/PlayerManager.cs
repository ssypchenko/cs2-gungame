#pragma warning disable CS8981// Naming Styles
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace GunGame.Tools
{
    public class PlayerManager
    {
        public PlayerManager(GunGame plugin)
        {
            Plugin = plugin;
        }
        private GunGame Plugin;
        private readonly object lockObject = new();
        private readonly Dictionary<int, GGPlayer> playerMap = new();
        public GGPlayer? CreatePlayerBySlot(int slot)
        {
            if (slot < 0 || slot > Models.Constants.MaxPlayers)
                return null;
            GGPlayer? player;
            lock (lockObject)
            {
                if (!playerMap.TryGetValue(slot, out GGPlayer? pl))
                {
                    pl = new GGPlayer(slot, Plugin);
                    playerMap.Add(slot, pl);
                }
                player = pl;
            }
            if (player.Index == -1)
            {
                var pc = Utilities.GetPlayerFromSlot(slot);
                if (pc != null && pc.IsValid)
                {
                    player.UpdatePlayerController(pc);
                }
                else
                {
                    Plugin.Logger.LogInformation($"Player slot {slot} - absent PlayerController");
                }
            }
            return player;
        }
        public GGPlayer? FindBySlot(int slot, string name = "")
        {
            if (playerMap.TryGetValue(slot, out GGPlayer? player))
            {
                return player;
            }
            else
            {
                Plugin.Logger.LogInformation($"[GUNGAME] Can't find player slot {slot} in playerMap from {name}.");
                var pc = Utilities.GetPlayerFromSlot(slot);
                if (pc != null)
                {
                    Server.ExecuteCommand($"kickid {pc.UserId} NoSteamId");
                }
                return null;
            }
        }
        public bool PlayerExists(int slot)
        {
            return playerMap.TryGetValue(slot, out GGPlayer? player);
        }
        public GGPlayer? FindLeader()
        {
            int leaderId = -1;
            uint leaderLevel = 0;
            foreach (var player in playerMap)
            {
                if (player.Value.Level > leaderLevel)
                {
                    leaderLevel = player.Value.Level;
                    leaderId = player.Key;
                }
            }
            if (leaderId == -1)
                return null;
            else
                return FindBySlot(leaderId, "FindLeader");
        }
        public void ForgetPlayer(int slot)
        {
            lock (lockObject)
            {
                if (playerMap.TryGetValue(slot, out GGPlayer? player))
                {
                    playerMap.Remove(slot);
                }
            }
        }
        public bool IsPlayerNearby(int slot, Vector spawn, double minDistance = 39.0)
        {
            if (playerMap.Count == 1)
                return false;
            double minD = 10000.0;
            double dist;
            string closest = "no";

            foreach (var player in playerMap)
            {
                if (player.Value.Slot == slot)
                    continue;
                var pc = Utilities.GetPlayerFromSlot(player.Value.Slot);
                if (pc != null && Plugin.IsValidPlayer(pc))
                {
                    if (pc.PlayerPawn != null && pc.Pawn != null &&
                        pc.PlayerPawn.IsValid && pc.PlayerPawn.Value != null && pc.Pawn.IsValid && pc.Pawn.Value != null &&
                        pc.PlayerPawn.Value.AbsOrigin != null)
                    {
                        /*                        if (IsPlayerNearEntity(spawn, pc.PlayerPawn.Value.AbsOrigin, minDistance))
                                                {
                                                    return true;
                                                }*/
                        if (pc.Pawn.Value.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                        {
                            continue;
                        }
                        dist = PlayerDistance(spawn, pc.PlayerPawn.Value.AbsOrigin);
                        if (dist < minD)
                        {
                            minD = dist;
                            closest = pc.PlayerName;
                        }
                    }
                }
            }
            if (minD < minDistance)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        private static bool IsPlayerNearEntity(Vector entity, Vector player, double minDistance = 39.0)
        {
            // Calculate the squared distance to avoid the square root for performance reasons
            float squaredDistance = (player.X - entity.X) * (player.X - entity.X) +
                                    (player.Y - entity.Y) * (player.Y - entity.Y) +
                                    (player.Z - entity.Z) * (player.Z - entity.Z);

            // Compare squared distances (since sqrt is monotonic, the comparison is equivalent)
            return squaredDistance <= minDistance * minDistance;
        }
        private static double PlayerDistance(Vector entity, Vector player)
        {
            // Calculate the squared distance to avoid the square root for performance reasons
            double Distance = Math.Sqrt((player.X - entity.X) * (player.X - entity.X) +
                                    (player.Y - entity.Y) * (player.Y - entity.Y) +
                                    (player.Z - entity.Z) * (player.Z - entity.Z));

            // Compare squared distances (since sqrt is monotonic, the comparison is equivalent)
            return Distance;
        }
    }
}
// Colors Available = "{default} {white} {darkred} {green} {lightyellow}" "{lightblue} {olive} {lime} {red} {lightpurple}"
//"{purple} {grey} {yellow} {gold} {silver}" "{blue} {darkblue} {bluegrey} {magenta} {lightred}" "{orange}"