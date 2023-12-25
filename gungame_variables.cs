using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using GunGame;
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
                if (_instance == null)
                {
                    _instance = new GGVariables();
                }
                return _instance;
            }
        }
        public ConVar? mp_friendlyfire { get; set; } = ConVar.Find("mp_friendlyfire");
        public Dictionary<int, int> CustomKillsPerLevel = new();
        public bool StatsEnabled { get; set; } = false;
        public List<Weapon> weaponsList = new();
        public int WeaponOrderCount { get; set; }
        public bool IsActive = false;  // GG is active
        public GGPlayer? GameWinner { get; set; }
        public Leader CurrentLeader { get; set; } = new();
        public bool RoundStarted = false;
        public Objectives MapStatus { get; set; }
        public int HostageEntInfo { get; set; }
        public bool IsVotingCalled { get; set; }
        public bool WarmupFinished = false;
        public bool isCalledEnableFriendlyFire { get; set; }
        public bool isCalledDisableRtv { get; set; }
        public int PlayerOnGrenade { get; set; }
        public int CTcount { get; set; }
        public int Tcount { get; set; }
        public int g_WeaponsMaxId { get; set; }
        public int g_WeaponIdSmokegrenade { get; set; }
        public int g_WeaponIdFlashbang { get; set; }
        public int g_WeaponAmmoTypeHegrenade { get; set; }
        public int g_WeaponAmmoTypeFlashbang { get; set; }
        public int g_WeaponAmmoTypeSmokegrenade { get; set; }
    }
}
