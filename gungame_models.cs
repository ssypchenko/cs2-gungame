using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Newtonsoft.Json;
using CounterStrikeSharp.API.Modules.Utils;

namespace GunGame.Models
{
    public class Constants 
    {
        public const int MaxPlayers = 64;
    }
    public class DatabaseSettings
    {
        public DBConfig StatsDB { get; set; } = null!;
        public DBConfig OnlineDB { get; set; }  = null!;
    }
    public class DBConfig
    {
        [JsonPropertyName("DatabaseType")]
        public string DatabaseType { get; set; } = "";
        [JsonPropertyName("DatabaseFilePath")]
        public string DatabaseFilePath { get; set; } = "";
        [JsonPropertyName("GeoDatabaseFilePath")]
        public string GeoDatabaseFilePath { get; set; } = "";
        [JsonPropertyName("DatabaseHost")]
        public string DatabaseHost { get; set; } = "";
        [JsonPropertyName("DatabasePort")]
        public int DatabasePort { get; set; }
        [JsonPropertyName("DatabaseUser")]
        public string DatabaseUser { get; set; } = "";
        [JsonPropertyName("DatabasePassword")]
        public string DatabasePassword { get; set; } = "";
        [JsonPropertyName("DatabaseName")]
        public string DatabaseName { get; set; } = "";
        [JsonPropertyName("Comment")]
        public string Comment { get; set; } = "";
    }
    public class WeaponInfo
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }
        [JsonPropertyName("slot")]
        public int Slot { get; set; }
        [JsonPropertyName("clipsize")]
        public int Clipsize { get; set; }
        [JsonPropertyName("ammotype")]
        public int AmmoType { get; set; }
        [JsonPropertyName("level_index")]
        public int LevelIndex { get; set; }
        [JsonPropertyName("fullname")]
        public string FullName { get; set; } = "";
    }
    public class SpecialWeaponInfo
    {
        public int Knife { get; set; } = 0;
        public int KnifeLevelIndex { get; set; } = 0;
        public int Drop_knife { get; set; } = 0;
        public int Taser { get; set; } = 0;
        public int TaserLevelIndex { get; set; } = 0;
        public int TaserAmmoType { get; set; } = 0;
        public int Flashbang { get; set; } = 0;
        public int Hegrenade { get; set; } = 0;
        public int HegrenadeLevelIndex { get; set; } = 0;
        public int HegrenadeAmmoType { get; set; } = 0;
        public int Smokegrenade { get; set; } = 0;
        public int Molotov { get; set; } = 0;
        public int MolotovLevelIndex { get; set; } = 0;
        public int MolotovAmmoType { get; set; } = 0;
    }
    public class WeaponOrderSettings
    {
        [JsonProperty("WeaponOrder")]
        public Dictionary<int, string> WeaponOrder { get; set; }

        [JsonProperty("MultipleKillsPerLevel")]
        public Dictionary<int, int> MultipleKillsPerLevel { get; set; } = new Dictionary<int, int>();

        [JsonProperty("RandomWeaponReserveLevels")]
        public string RandomWeaponReserveLevels { get; set; } = "";

        [JsonProperty("RandomWeaponOrder")]
        public bool RandomWeaponOrder { get; set; }

        // Constructor to ensure non-nullable initialization
        public WeaponOrderSettings()
        {
            WeaponOrder = new Dictionary<int, string>();
        }
    }
    public class Weapon
    {
        public int Level { get; set; } = 0;
        public string Name { get; set; } = "";
        public string FullName { get; set; } = "";
        public int Index { get; set; } = -1;
        public int LevelIndex { get; set; }  = -1;
        public int Slot { get; set; }  = -1;
        public int Ammo { get; set; }  = -1;
        public int ClipSize { get; set; }  = -1;
    }
    public class Leader
    {
        private readonly object lockObject = new();
        public uint Level { get; private set; } = 0;
        public int Slot { get; private set; } = -1;
        public void SetLeader (int slot, int level)
        {
            lock (lockObject)
            {
                Level = (uint)level;
                Slot = slot;
            }
        }
    }
    public class Winner
    {
        public int Slot { get; set; } = 0;
        public int TeamNum { get; set; } = 0;
        public string Name { get; set; }
        public Winner (GGPlayer winner)
        {
            Slot = winner.Slot;
            TeamNum = winner.GetTeam();
            Name = winner.PlayerName;
        }
    }
    public class WinnerEventArgs : EventArgs
    {
        public int Winner { get; }
        public int Looser { get; }
        public WinnerEventArgs(int winner, int looser)
        {
            Winner = winner;
            Looser = looser;
        }
    }
    public class KillEventArgs : EventArgs
    {
        public bool Result { get; set; }
        public int Killer { get; }
        public int Victim { get; }
        public string Weapon { get; }
        public bool TeamKill { get; }
        public KillEventArgs(int killer, int victim, string weapon, bool teamkill)
        {
            Killer = killer;
            Victim = victim;
            Weapon = weapon;
            TeamKill = teamkill;
            Result = true;
        }
    }
    public class LevelChangeEventArgs : EventArgs
    {
        public int Killer { get; }
        public int Level { get; }
        public int Difference { get; }
        public bool KnifeSteal { get; }
        public bool LastLevel { get; }
        public bool Knife { get; }
        public int Victim { get; }
        public bool Result { get; set; }
        public LevelChangeEventArgs(int killer, int level, int difference, bool knifesteal, bool lastlevel, bool knife, int victim)
        {
            Killer = killer;
            Level = level;
            Difference = difference;
            KnifeSteal = knifesteal;
            LastLevel = lastlevel;
            Knife = knife;
            Victim = victim;
            Result = true;
        }
    }
    public class PointChangeEventArgs : EventArgs
    {
        public int Killer { get; }
        public int Kills { get; }
        public bool Result { get; set; }
        public PointChangeEventArgs(int killer, int kills)
        {
            Killer = killer;
            Kills = kills;
            Result = true;
        }
    }
    public enum GGSounds
    {
        Nade,
        Molotov,
        Knife,
    }

    [Flags]
    public enum Objectives
    {
        RemoveBomb = 1 << 0,
        RemoveHostage = 1 << 1,
        Bomb = 1 << 2,
        Hostage = 1 << 3
    }
    public enum PlayerStates
    {
        KnifeElite = 1 << 0,
        FirstJoin = 1 << 1,
        GrenadeLevel = 1 << 2
    }
}
