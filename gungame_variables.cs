//sing CounterStrikeSharp.API;
//using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
//using GunGame;
using GunGame.Models;

namespace GunGame.Variables
{
    public class GGVariables
    {
        private static GGVariables? _instance;

        private GGVariables() { }

        public static GGVariables Instance
        {
            get
            {
                _instance ??= new GGVariables();
                return _instance;
            }
        }
        public string ServerLanguageCode = "en";
        public ConVar? Mp_friendlyfire { get; set; }
        public Dictionary<int, int> CustomKillsPerLevel = new();
        public bool StatsEnabled { get; set; } = false;
        public List<Weapon> weaponsList = new();
        public int WeaponOrderCount { get; set; }
        public List<string> WeaponsSkipFastSwitch = new();
        public bool IsActive = false;  // GG is active
        public Winner? GameWinner { get; set; }
        public Leader CurrentLeader { get; set; } = new();
        public bool RoundStarted = false;
        public Objectives MapStatus { get; set; }
        public int HostageEntInfo { get; set; }
        public bool IsVotingCalled { get; set; }
        public bool WarmupFinished = false;
        public bool IsCalledEnableFriendlyFire { get; set; }
        public bool IsCalledDisableRtv { get; set; }
        public int PlayerOnGrenade { get; set; }
        public int CTcount { get; set; }
        public int Tcount { get; set; }
        public int WeaponsMaxId { get; set; }
        public int WeaponIdSmokegrenade { get; set; }
        public int WeaponIdFlashbang { get; set; }
        public List<string> InfoMessages = new()
        {
            "set.sound",
            "top.list",
            "your.rank",
        };
        public string LooserName = "";
        public int InfoMessageIndex = 0;
//        public int g_WeaponAmmoTypeHegrenade { get; set; }
//        public int g_WeaponAmmoTypeFlashbang { get; set; }
//        public int g_WeaponAmmoTypeSmokegrenade { get; set; }
    }
}
