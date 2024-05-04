using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API;
using GunGame.Models;
using GunGame.Variables;
using Microsoft.Extensions.Logging;
using GunGame.Extensions;
using CounterStrikeSharp.API.Modules.Utils;

namespace GunGame.Tools
{
    internal class SpawnManager : CustomManager
    {
        public SpawnManager(GunGame plugin) : base(plugin)
        {
        }

        public void InitSpawnPoints()
        {
            // get map spawn point
            GGVariables.Instance.spawnPoints = new();
            var tSpawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_terrorist");
            var ctSpawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_counterterrorist");
            var dmSpawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_deathmatch_spawn");

            GGVariables.Instance.spawnPoints[2] = new();
            GGVariables.Instance.spawnPoints[3] = new();
            GGVariables.Instance.spawnPoints[4] = new();

            foreach (var entity in tSpawns)
            {
                if (entity != null && entity.IsValid && entity.AbsOrigin != null && entity.AbsRotation != null)
                {
                    GGVariables.Instance.spawnPoints[2].Add(new SpawnInfo(entity.AbsOrigin, entity.AbsRotation));
                }
            }

            foreach (var entity in ctSpawns)
            {
                if (entity != null && entity.IsValid && entity.AbsOrigin != null && entity.AbsRotation != null)
                {
                    GGVariables.Instance.spawnPoints[3].Add(new SpawnInfo(entity.AbsOrigin, entity.AbsRotation));
                }
            }
            foreach (var entity in dmSpawns)
            {
                if (entity != null && entity.IsValid && entity.AbsOrigin != null && entity.AbsRotation != null)
                {
                    GGVariables.Instance.spawnPoints[4].Add(new SpawnInfo(entity.AbsOrigin, entity.AbsRotation));
                }
            }

            if (GGVariables.Instance.spawnPoints[4].Count < 1 && Config.RespawnByPlugin == RespawnType.DmSpawns)
                Config.RespawnByPlugin = RespawnType.Both;

            Logger.LogInformation($"***** Read {GGVariables.Instance.spawnPoints[3].Count} ct spawn, {GGVariables.Instance.spawnPoints[2].Count} t spawn, {GGVariables.Instance.spawnPoints[4].Count} dm spawn");
        }


        public void RespawnPlayer(CCSPlayerController player, bool spawnpoint = true)
        {
            if (Config.RespawnByPlugin == RespawnType.Disabled)
                return;

            if (!Plugin.IsValidPlayer(player))
                return;

            CCSPlayerController pl = player;
            if ((Config.RespawnByPlugin == RespawnType.OnlyT && player.TeamNum != 2)
                || (Config.RespawnByPlugin == RespawnType.OnlyCT && player.TeamNum != 3)
                || (player.TeamNum != 2 && player.TeamNum != 3))
            {
                return;
            }
            Plugin.AddTimer(1.0f, () =>
            {
                if (!Plugin.IsValidPlayer(pl) || pl.PlayerPawn == null || !pl.PlayerPawn.IsValid || pl.PlayerPawn.Value == null)
                    return;

                double thisDeathTime = Server.EngineTime;
                double deltaDeath = thisDeathTime - Plugin.LastDeathTime[pl.Slot];
                Plugin.LastDeathTime[pl.Slot] = thisDeathTime;
                if (deltaDeath < 0)
                {
                    Logger.LogError($"CRITICAL: Delta death is negative for slot {pl.Slot}!!!");
                    return;
                }
                SpawnInfo spawn = null!;
                if ((pl.TeamNum == 2 || pl.TeamNum == 3) && spawnpoint)
                {
                    spawn = GetSuitableSpawnPoint(pl.Slot, pl.TeamNum, Config.SpawnDistance);
                    if (spawn == null)
                    {
                        Logger.LogError($"Spawn point not found for {pl.PlayerName} ({pl.Slot})");
                    }
                }
                pl.Respawn();
                if (spawn != null)
                {
                    player.PlayerPawn.Value!.Teleport(spawn.Position, spawn.Rotation, new Vector(0, 0, 0));
                }
            });
        }
        private SpawnInfo GetSuitableSpawnPoint(int slot, int team, double minDistance = 39.0)
        {
            int spawnType;
            if (Config.RespawnByPlugin == RespawnType.DmSpawns)
                spawnType = 4;
            else
                spawnType = team;

            if (!GGVariables.Instance.spawnPoints.TryGetValue(spawnType, out List<SpawnInfo>? value))
            {
                Logger.LogError($"SpawnPoints not ContainsKey {spawnType}");
                return null!;
            }

            // Shuffle the spawn points list to randomize the selection process
            var shuffledSpawns = new List<SpawnInfo>(value);

            // Shuffle the copy
            shuffledSpawns.Shuffle();
            foreach (var spawn in shuffledSpawns)
            {
                if (!PlayerManager.IsPlayerNearby(slot, spawn.Position, minDistance))
                {
                    return spawn;
                }

                /*                if (GGVariables.Instance.Position[slot] != spawn.Position)
                                {
                                    // Found a suitable spawn point
                                    GGVariables.Instance.Position[slot] = spawn.Position;
                                    return spawn;
                                } */
            }
            Logger.LogInformation($"No suitable spawn points for player {slot}");
            // No suitable spawn point found
            return null!;
        }



        public void SetSpawnRules(int spawnType)
        {
            if (spawnType == 1)
            {
                Config.RespawnByPlugin = RespawnType.OnlyT;
                Server.ExecuteCommand("mp_respawn_on_death_t 0");
                Server.ExecuteCommand("mp_respawn_on_death_ct 1");
                Console.WriteLine("Plugin Respawn T on");
            }
            else if (spawnType == 2)
            {
                Config.RespawnByPlugin = RespawnType.OnlyCT;
                Server.ExecuteCommand("mp_respawn_on_death_t 1");
                Server.ExecuteCommand("mp_respawn_on_death_ct 0");
                Console.WriteLine("Plugin Respawn CT on");
            }
            if (spawnType == 3)
            {
                Config.RespawnByPlugin = RespawnType.Both;
                Server.ExecuteCommand("mp_respawn_on_death_t 0");
                Server.ExecuteCommand("mp_respawn_on_death_ct 0");
                Console.WriteLine("Plugin Respawn T and CT on");
            }
            else if (spawnType == 4)
            {
                Config.RespawnByPlugin = RespawnType.DmSpawns;
                Server.ExecuteCommand("mp_respawn_on_death_t 0");
                Server.ExecuteCommand("mp_respawn_on_death_ct 0");
                Console.WriteLine("Plugin Respawn DM on");
            }
            else if (spawnType == 0)
            {
                Config.RespawnByPlugin = RespawnType.Disabled;
                Server.ExecuteCommand("mp_respawn_on_death_t 1");
                Server.ExecuteCommand("mp_respawn_on_death_ct 1");
                Console.WriteLine("Plugin Respawn off");
            }
            else
            {
                Console.WriteLine($"Error set Respawn Rules with code {spawnType}");
                Logger.LogError($"Error set Respawn Rules with code {spawnType}");
            }
        }
    }
}
