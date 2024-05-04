#pragma warning disable CS8981// Naming Styles
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Utils;
using GunGame.Models;
using GunGame.Variables;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace GunGame
{
    public class GGPlayer
    {    
        private readonly object lockObject = new();
        private readonly GunGame Plugin;
        public GGPlayer(int slot, GunGame plugin)
        {
            Plugin = plugin;
            Slot = slot;
            Level = 1;
            PlayerName = "";
            Index = -1;
            SavedSteamID = 0;
            IdAttempts = 0;
            LevelWeapon = GGVariables.Instance.weaponsList.FirstOrDefault(w => w.Level == 1)!;
            Angles = new QAngle();
            Origin = new Vector();
            PlayerWins = -1;
            PlayerPlace = -1;
            IP = "";
            Culture = null!;
            Music = true;
        }
        public void UpdatePlayerController(CCSPlayerController playerController)
        {
            Index = (int)playerController.Index;
            PlayerName = playerController.PlayerName;
            SavedSteamID = playerController.SteamID;
            IsBot = playerController.IsBot;
        }
        public string PlayerName { get; private set; }
        public uint Level { get; private set; } = 1;
        public Weapon LevelWeapon { get; private set; }
        public int Index { get; private set; }
        public int Slot { get; private set; }
        public bool IsBot { get; set;} = false;
        public int NumberOfNades { get; set;} = 0;
        public bool BlockFastSwitchOnChange { get; set;} = true;
        public bool BlockSwitch { get; set;} = false;
        public int CurrentKillsPerWeap { get; set;} = 0;
        public int CurrentLevelPerRound { get; set;} = 0;
        public int CurrentLevelPerRoundTriple { get; set;} = 0;
        public bool TeamChange { get; set;} = false;
        public bool TripleEffects { get; set;} = false;
        public ulong SavedSteamID { get; set;}
        public int IdAttempts { get; set;}
        public QAngle? Angles { get; set; }
        public Vector? Origin { get; set; }
        public int AfkCount { get; set; } = 0;
        public PlayerStates State { get; set;}
        public int PlayerWins { get; set;}
        public int PlayerPlace { get; set;}
        public string IP { get; set;}
        public CultureInfo Culture { get; set;}
        public bool Music { get; set;}
        public void SetLevel( int setLevel)
        {
            if (setLevel > GGVariables.Instance.WeaponOrderCount || setLevel < 0)
                return;
            lock (lockObject)
            {
                this.Level = (uint)setLevel;
                if (setLevel > 0)
                    LevelWeapon = GGVariables.Instance.weaponsList.FirstOrDefault(w => w.Level == Level)!;
                else
                    LevelWeapon = new();
            }
//            if (!IsBot) Plugin.Logger.LogInformation($"{PlayerName} ({Slot}) - level {Level}");
        }
        public void SetSound(bool value)
        {
            if (IsBot)
                return;
            Music = value;
//            Plugin.Logger.LogInformation($"{PlayerName} sound set to {(Music ? "on" : "off")}");
            var playerController = Utilities.GetPlayerFromSlot(Slot);
            if (playerController != null && playerController.IsValid 
                && playerController.Pawn != null && playerController.Pawn.Value != null 
                && playerController.Pawn.Value.LifeState == (byte)LifeState_t.LIFE_ALIVE)
            {
                if (Music)
                {
                    playerController.PrintToChat(Translate("music.on"));
                }
                else
                {
                    playerController.PrintToChat(Translate("music.off"));
                }
            } 
        }
        public void SetWins(int value)
        {
            PlayerWins = value;
//            Plugin.Logger.LogInformation($"{PlayerName} wins set to {value}");
        }
        public void UseWeapon(int slot)
        {
            NativeAPI.IssueClientCommand ((int) this.Slot, $"slot{slot}");
        }
        public int GetTeam()
        {
            var playerController = Utilities.GetPlayerFromSlot(this.Slot);
            if (playerController == null || !playerController.IsValid)
                return 0;
            else
                return playerController.TeamNum;
        }
        public void SavedWins(bool success, int problemId)
        {
            if (success)
            {
                Plugin.Logger.LogInformation($"Winner {PlayerName}, {PlayerWins} total wins");
            }
            else
            {
                Plugin.Logger.LogError($"Failed to save wins for {PlayerName} slot {Slot} SteamID {SavedSteamID} wins {PlayerWins} problem Id {problemId}");
            }
//            Plugin.StatsLoadRank();
        }
        public void SetLanguage()
        {
            var pl = Utilities.GetPlayerFromSlot(Slot);
            if (pl == null || pl.AuthorizedSteamID == null)
                return;
            Plugin.playerLanguageManager.SetLanguage(pl.AuthorizedSteamID, Culture);
            Plugin.Logger.LogInformation($"Set {Culture.DisplayName} language for {PlayerName}");
        }
        public async Task<bool> UpdateLanguage(string isoCode)
        {
            bool found = false;
            try
            {
                Culture = new CultureInfo(isoCode);
                found = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GunGame - FATAL]******* Error with Culture for isoCode {isoCode}: {ex.Message}");
                Server.NextFrame(() =>
                {
                    Plugin.Logger.LogError($"[GunGame - FATAL]******* Error with Culture for isoCode {isoCode}: {ex.Message}");
                });
            }
            bool success = false;
            if (found)
            {
                SetLanguage();
                
                if (Plugin.statsManager != null)
                {
                    success = await Plugin.statsManager.UpdateLanguage(this);
                }
            }
            return success;
        }
        public string Translate(string token_to_localize)
        {
            CultureInfo culture;
            if (Culture == null)
            {
                culture = Plugin.playerLanguageManager.GetDefaultLanguage();
                Culture = culture;
            }
            else
            {
                culture = Culture;
            }
            using (new WithTemporaryCulture(culture))
            {
                return Plugin._localizer[token_to_localize];
            }
        }
        public string Translate(string token_to_localize, params object[] arguments)
        {
            CultureInfo culture;
            if (Culture == null)
            {
                culture = Plugin.playerLanguageManager.GetDefaultLanguage();
                Culture = culture;
            }
            else
            {
                culture = Culture;
            }
            using (new WithTemporaryCulture(culture))
            {
                return Plugin._localizer[token_to_localize, arguments];
            }
        }
        public void ResetPlayer ()
        {
            AfkCount = 0;
            CurrentKillsPerWeap = 0;
            CurrentLevelPerRound = 0;
            CurrentLevelPerRoundTriple = 0;
            NumberOfNades = 0;
            SetLevel(1);
        }
    }
}
// Colors Available = "{default} {white} {darkred} {green} {lightyellow}" "{lightblue} {olive} {lime} {red} {lightpurple}"
                      //"{purple} {grey} {yellow} {gold} {silver}" "{blue} {darkblue} {bluegrey} {magenta} {lightred}" "{orange}"