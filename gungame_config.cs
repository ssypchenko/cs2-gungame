using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;
using GunGame.Models;

namespace GunGame;

public class GGConfig : BasePluginConfig
{
    [JsonPropertyName("IsPluginEnabled")]
    public bool IsPluginEnabled { get; set; } = true;
    /* Remove objectives from map. 0 = Disabled, 1 = BOMB, 2 = HOSTAGE, 3 = BOTH*/
    [JsonPropertyName("RemoveObjectives")]
    public int RemoveObjectives { get; set; } = 3;
    /* How many levels they can gain in 1 round (0 - disabled) */
    [JsonPropertyName("MaxLevelPerRound")]
    public int MaxLevelPerRound { get; set; } = 0;
    /**
    * How many kills they need to with the weapon to get the next level
    * Kills will count across all rounds so that you don't have to get them in one round.
    */
    [JsonPropertyName("MinKillsPerLevel")]
    public int MinKillsPerLevel { get; set; } = 3;
    /**
    * How much levels to loose after TK
    * 0 - Disable
    * 1..N - Levels to loose
    */
    [JsonPropertyName("TkLooseLevel")]
    public int TkLooseLevel { get; set; } = 2;
    /* Turbo Mode: give next level weapon on level up */
    [JsonPropertyName("TurboMode")]
    public bool TurboMode { get; set; } = true;
    /**
    * Strip dead players weapon
    *
    * Options:
    *  0 - Disabled (default)
    *  1 - Enabled for alive and dead players (alive players can not drop guns)
    *  2 - Enabled for dead players only (alive players can drop guns)
    */
    [JsonPropertyName("StripDeadPlayersWeapon")]
    public int StripDeadPlayersWeapon { get; set; } = 1;
    /* Allow level up after round end */
    [JsonPropertyName("AllowLevelUpAfterRoundEnd")]
    public bool AllowLevelUpAfterRoundEnd { get; set; } = false;

    /* Auto reload current level weapon on kill */
    [JsonPropertyName("ReloadWeapon")]
    public bool ReloadWeapon { get; set; } = false;

    /* Show multikill hints in chat */
    [JsonPropertyName("MultiKillChat")]
    public bool MultiKillChat { get; set; } = true;

    /* Next frag Sound confirmation */
    [JsonPropertyName("MultiKillSound")]
    public string MultiKillSound { get; set; } = "sounds/buttons/bell1.wav";

    /* Display a join message, popup giving players instructions on how to play */
    [JsonPropertyName("JoinMessage")]
    public bool JoinMessage { get; set; } = false;

    /* Start voting if leader level is less maximum level by this value */
    [JsonPropertyName("VoteLevelLessWeaponCount")]
    public int VoteLevelLessWeaponCount { get; set; } = 0;
/*    public int? ObjectiveBonus { get; set; } */

    /** 
    * Level down player if they kill themself by WorldSpawn Suicide.
    * 0 - Disable
    * 1..N - Levels to loose
    */
    [JsonPropertyName("WorldspawnSuicide")]
    public int WorldspawnSuicide { get; set; } = 1;

    /**
    * This gives the player a weapon with 50 bullets on nade level.
    * Example:
    *     "NadeBonus" "glock"  - gives glock 
    *     "NadeBonus" "deagle" - gives deagle
    *     "NadeBonus" ""       - feature disabled
    */
    [JsonPropertyName("NadeBonusWeapon")]
    public string NadeBonusWeapon { get; set; } = "deagle";

    /* Remove additional ammo in bonus weapon on the nade level (Does not work now) */
    [JsonPropertyName("RemoveBonusWeaponAmmo")]
    public bool RemoveBonusWeaponAmmo { get; set; } = false;

    /* Gives a smoke grenade on nade level */
    [JsonPropertyName("NadeSmoke")]
    public bool NadeSmoke { get; set; } = false;

    /* Gives a Flash grenade on nade level */
    [JsonPropertyName("NadeFlash")]
    public bool NadeFlash { get; set; } = false;

    /**
    * Gives an extra hegrenade to the player if they get a kill by another weapon
    */
    [JsonPropertyName("ExtraNade")]
    public bool ExtraNade { get; set; } = true;

    /* Sound confirmation someone on the nade level */
    [JsonPropertyName("NadeInfoSound")]
    public string NadeInfoSound { get; set; } = "";

    /**
    * Prohibit to shoot and then knife
    * 
    * true - Player will loose a level for shoot and knife
    * false - ignore
    */
    [JsonPropertyName("ShootKnifeBlock")]
    public bool ShootKnifeBlock { get; set; } = true;

    /* Turn Knife Pro allow stealing a player level by killing them with a knife */
    [JsonPropertyName("KnifePro")]
    public bool KnifePro { get; set; } = true;

    /* Molotov Pro allow stealing a player level by killing them with a molotov */
    [JsonPropertyName("MolotovPro")]
    public bool MolotovPro { get; set; } = true;
    
    /* The minimum level that a player must be at before another player can knife steal from. Requires KnifePro on */
    [JsonPropertyName("KnifeProMinLevel")]
    public int KnifeProMinLevel { get; set; } = 3;

    /**
    * Maximum level difference between players to allow steal level 
    * 0 - Disabled
    * 1..N - Level difference between killer and victim
    */
    [JsonPropertyName("KnifeProMaxDiff")]
    public int KnifeProMaxDiff { get; set; } = 0;

    /* If enabled then leave earned frags after stolen level. False - makes zero frags */
    [JsonPropertyName("KnifeProRecalcPoints")]
    public bool KnifeProRecalcPoints { get; set; } = false;

    /* Knife Elite force them to only have a knife after they level up.
           They will get a normal weapon again next round */
    [JsonPropertyName("KnifeElite")]
    public bool KnifeElite { get; set; } = false;

    /* Enables Knife Pro when a player is on hegrenade level */
    [JsonPropertyName("KnifeProHE")]
    public bool KnifeProHE { get; set; } = false;

    /* Sound confirmation someone on the nade level */
    [JsonPropertyName("KnifeInfoSound")]
    public string KnifeInfoSound { get; set; } = "sounds/ui/deathmatch_kill_bonus.wav";

    /* Gives a smoke grenade on knife level */
    [JsonPropertyName("KnifeSmoke")]
    public bool KnifeSmoke { get; set; } = true;

    /* Gives a Flash grenade on knife level */
    [JsonPropertyName("KnifeFlash")]
    public bool KnifeFlash { get; set; } = false;
    
     /**
    * Block weapon switch if killer leveled up with knife
    *
    * Options:
    *     true - Block weapon switch
    *     false - Do not block weapon switch
    */
    [JsonPropertyName("BlockWeaponSwitchIfKnife")]
    public bool BlockWeaponSwitchIfKnife { get; set; } = false;
    
    /* Enables Warmup Round*/
    [JsonPropertyName("WarmupEnabled")]
    public bool WarmupEnabled { get; set; } = true; 
/*    public bool? DisableWarmupOnRoundEnd { get; set; }
    public bool? WarmupInitialized { get; set; } */
    /* Warmup time length */
    [JsonPropertyName("WarmupTimeLength")]
    public int WarmupTimeLength { get; set; } = 35;
/*    public bool? IsVotingCalled { get; set; }
    public bool? g_isCalledEnableFriendlyFire { get; set; }
    public bool? g_isCalledDisableRtv { get; set; } */
    
    /* Multi Level Bonus */
    [JsonPropertyName("MultiLevelBonus")]
    public bool MultiLevelBonus { get; set; } = false;

    /* Enable God Mode when multi leveled */
    [JsonPropertyName("MultiLevelBonusGodMode")]
    public bool MultiLevelBonusGodMode { get; set; } = false;

    /**
    * Custom speed and gravity value multiplier for multi level bonus.
    * 0 - Disabled
    */
    [JsonPropertyName("MultiLevelBonusGravity")]
    public float MultiLevelBonusGravity { get; set; } = 0.6f;
    [JsonPropertyName("MultiLevelBonusSpeed")]
    public float MultiLevelBonusSpeed { get; set; } = 1.3f;

    /* Miltilevel visual effect */
    [JsonPropertyName("MultiLevelEffect")]
    public bool MultiLevelEffect { get; set; } = false;

    /* How much levels is needed to get bonus */
    [JsonPropertyName("MultiLevelAmount")]
    public int MultiLevelAmount { get; set; } = 3;

    /*Sound on Multilevel bonus */
    [JsonPropertyName("MultiLevelSound")]
    public string MultiLevelSound { get; set; } = "sounds/training/highscore.wav";
    
    /**
    * Level down players if they use the "kill" command
    * 0 - Disable
    * 1..N - Levels to loose
    */
    [JsonPropertyName("CommitSuicide")]
    public int CommitSuicide { get; set; } = 1;

    /* Set sv_alltalk 1 after player win */
    [JsonPropertyName("AlltalkOnWin")]
    public bool AlltalkOnWin { get; set; } = true;

    /* Restore level on player reconnect */
    [JsonPropertyName("RestoreLevelOnReconnect")]
    public bool RestoreLevelOnReconnect { get; set; } = true;

/*    public bool? StatsEnabled { get; set; } */
    /**
    * Give random weapon on warmup.
    * If you are using WarmupRandomWeaponMode, you can nou use WarmupNades or WarmupWeapon.
    *
    * 0 - Disable
    * 1 - Random weapon every spawn
    */
    [JsonPropertyName("WarmupRandomWeaponMode")]
    public int WarmupRandomWeaponMode { get; set; } = 0;
    /* Gives unlimited hegrenades to the player if he is on nage level */
    [JsonPropertyName("UnlimitedNades")]
    public bool UnlimitedNades { get; set; } = false;
    /**
    * Gives unlimited hegrenades to the player if warmup is enabled.
    * If you are using WarmupRandomWeaponMode, you can nou use WarmupNades or WarmupWeapon.
    * false - no warmup nades
    * true - nades on warmup
    */
    [JsonPropertyName("WarmupNades")]
    public bool WarmupNades { get; set; } = true;

    /**
    * Weapon for warmup.
    * If you are using WarmupRandomWeaponMode, you can nou use WarmupNades or WarmupWeapon.
    */
    [JsonPropertyName("WarmupWeapon")]
    public string WarmupWeapon { get; set; } = "";

    /**
    * Enable UnlimitedNades depending on the number of players in team.
    *
    * If UnlimitedNades is off and the number of players in one team less or 
    * equal to UnlimitedNadesMinPlayers then enable UnlimitedNades.
    * When it will be more players on both teams, turn UnlimitedNades back to off.
    *
    * 0 - Disable
    * 1 and above - Minimum number of players in each team for UnlimitedNames to be on.
    */
    [JsonPropertyName("UnlimitedNadesMinPlayers")]
    public int UnlimitedNadesMinPlayers { get; set; } = 2;
    /**
    * Number of nades on the nade level.
    *
    * This option is disabled 
    * if less then 2.
    */
    [JsonPropertyName("NumberOfNades")]
    public int NumberOfNades { get; set; } = 1;
    
    /* Show levels in scoreboard */
    [JsonPropertyName("LevelsInScoreboard")]
    public bool LevelsInScoreboard { get; set; } = false;

    /* Give player armor and helmet  on spawn */
    [JsonPropertyName("ArmorKevlarHelmet")]
    public bool ArmorKevlarHelmet { get; set; } = true;

    [JsonPropertyName("TaserSmoke")]
    public bool TaserSmoke { get; set; } = true;

    [JsonPropertyName("TaserFlash")]
    public bool TaserFlash { get; set; } = false;

    /* Show leader's weapon name in chat with leading message */
    [JsonPropertyName("ShowLeaderWeapon")]
    public bool ShowLeaderWeapon { get; set; } = true;
    
    /** 
    * Show players level message in hint box instead of chat.
    * If enabled then multikill chat messages will be shown 
    * in hint box too (requres "MultiKillChat" "1").
    */
    [JsonPropertyName("ShowSpawnMsgInHintBox")]
    public bool ShowSpawnMsgInHintBox { get; set; } = true;

    /**
    * Show leader level info in hint box 
    * (requires "ShowSpawnMsgInHintBox" to be "1")
    */
    [JsonPropertyName("ShowLeaderInHintBox")]
    public bool ShowLeaderInHintBox { get; set; } = true;
/*    
    public int? g_Cfg_ScoreboardClearDeaths { get; set; }
    public int[]? g_Cfg_RandomWeaponReservLevels { get; set; } */

    /**
    * Gives joining players the avg/min level of all other players when they join late.
    * 0 - Disable
    * 1 - Avg level
    * 2 - Min level
    */
    [JsonPropertyName("HandicapMode")]
    public int HandicapMode { get; set; } = 2;

    /**
    * Allow players in the top rank to receive a handicap with the rest of the players.
    *
    * Handicap must also be turned on above for this to work.
    * See also "HandicapTopRank" to set rank limit for tp rank.
    *
    * false - Do not give handicap to the top rank players.
    * true - Give handicap to all players.
    */
    [JsonPropertyName("TopRankHandicap")]
    public bool TopRankHandicap { get; set; } = true;

    /**
    * Do not give handicap to the top rank players.
    *
    * See also "TopRankHandicap" to allow all players to receive handicap.
    *
    * 0 - Give handicap to all players.
    * N - Do not give handicap for the first N players.
    */
    [JsonPropertyName("HandicapTopRank")]
    public int HandicapTopRank { get; set; } = 20;
    
    /**
    * Use spectator's levels to calculate handicap level.
    *
    * false - Handicap does not count levels of spectators.
    * true - Handicap counts levels of spectators.
    */
    [JsonPropertyName("HandicapUseSpectators")]
    public bool HandicapUseSpectators { get; set; } = false;

    /**
    * Give handicap not more then given number of times per map.
    * 0 - disabled
    */
    [JsonPropertyName("HandicapTimesPerMap")]
    public int HandicapTimesPerMap { get; set; } = 0;
    
    /**
    * Gives handicap level automaticaly every defined number of seconds.
    * This only works for players that is on very minimum level from 
    * all the players.
    * Handicap must also be turned on for this to work.
    */
    [JsonPropertyName("HandicapUpdate")]
    public float HandicapUpdate { get; set; } = 150;

    /* Substract handicap level by this value */
    [JsonPropertyName("HandicapLevelSubstract")]
    public int HandicapLevelSubstract { get; set; } = 1;

    /**
    * Dont use bots levels for handicap calculation. 
    * Dont give handicap level to bots too.
    */
    [JsonPropertyName("HandicapSkipBots")]
    public bool HandicapSkipBots { get; set; } = true;

    /**
    * Maximum level that handicap can give.
    * 0 - Disable restriction
    * 1..N - Max level
    */
    [JsonPropertyName("MaxHandicapLevel")]
    public int MaxHandicapLevel { get; set; } = 0;

    /** 
    * Disable rtv on defined level. 0 - disabled.
    */
    [JsonPropertyName("DisableRtvLevel")]
    public int DisableRtvLevel { get; set; } = 15;

    /** 
    * FFA DM mode.
    *
    * If you are using CSS:DM with FFA mode enabled, 
    * then you should set this variable to true
    */
    [JsonPropertyName("FriendlyFireAllowed")]
    public bool FriendlyFireAllowed { get; set; } = false;

    /**
    * Enabled friendly fire automatically when a player reaches hegrenade level.
    *
    * When nobody on nade level, than switches friendly fire back.
    * This does not affect EnableFriendlyFireLevel and EnableFriendlyFireLevel is not requered to be enabled.
    * See also FriendlyFireOnOff.
    */
    [JsonPropertyName("AutoFriendlyFire")]
    public bool AutoFriendlyFire { get; set; } = true;

    /** 
    * Enable friendly fire on defined level.
    *
    * This does not affect AutoFriendlyFire and AutoFriendlyFire is not requered to be 1.
    * See also FriendlyFireOnOff.
    *
    * 0 - Disabled.
    * 1..N - enable friendly fire on defined level.
    */
    [JsonPropertyName("EnableFriendlyFireLevel")] 
    public int EnableFriendlyFireLevel { get; set; } = 0; // это пока не сделано

    /*
    * What to do with friendly fire when EnableFriendlyFireLevel is not 0 and leader reaches EnableFriendlyFireLevel
    * or AutoFriendlyFire is 1 and someone reaches nade level.
    *
    * true - Enable friendy fire
    * false - Disable friendy fire
    */
    [JsonPropertyName("FriendlyFireOnOff")]
    public bool FriendlyFireOnOff { get; set; } = true;

    /* Friendy fire change status sound confirmation  */
    [JsonPropertyName("FriendlyFireInfoSound")]
    public string FriendlyFireInfoSound { get; set; } = "sounds/ui/item_drop5_legendary.wav";


    /**
    * Block weapon switch if you get next hegrenade 
    * after previous hegrenade explode or after getting extra nade.
    *
    * You need SDK Hooks (sdkhooks) if you want to set it to "1"
    *
    * Options:
    *     true - Block weapon switch
    *     false - Do not block weapon switch
    */
    [JsonPropertyName("BlockWeaponSwitchOnNade")]
    public bool BlockWeaponSwitchOnNade { get; set; } = true;
    
    /**
    * If this option is enabled, than player can level up by killing with prop_physics.
    * For example with fuel barrels etc.
    * 
    * true - Enabled
    * false - Disabled
    */
    [JsonPropertyName("CanLevelUpWithPhysics")]
    public bool CanLevelUpWithPhysics { get; set; } = true;

    /**
    * Use "CanLevelUpWithPhysics" option when player is on grenade level.
    * 
    * true - Enabled
    * false - Disabled
    */
    [JsonPropertyName("CanLevelUpWithPhysicsOnGrenade")]
    public bool CanLevelUpWithPhysicsOnGrenade { get; set; } = false;

    /**
    * Use "CanLevelUpWithPhysics" option when player is on knife level.
    * 
    * true - Enabled
    * false - Disabled
    */
    [JsonPropertyName("CanLevelUpWithPhysicsOnKnife")]
    public bool CanLevelUpWithPhysicsOnKnife { get; set; } = false;

    /**
    * If this option is enabled, than player can level up by killing with nade at any time.
    * For example there are maps having grenades on them leaved by the author.
    * 
    * true - Enabled
    * false - Disabled
    */
    [JsonPropertyName("CanLevelUpWithMapNades")]
    public bool CanLevelUpWithMapNades { get; set; } = true;

    /**
    * Use "CanLevelUpWithMapNades" option when player is on knife level.
    * 
    * true - Enabled
    * false - Disabled
    */
    [JsonPropertyName("CanLevelUpWithNadeOnKnife")]
    public bool CanLevelUpWithNadeOnKnife { get; set; } = false;

    /**
    * Disable level down on knifepro.
    *
    * true - Level down disabled
    * false - Level down enabled
    */
    [JsonPropertyName("DisableLevelDown")]
    public bool DisableLevelDown { get; set; } = false;

    /**
    * Prevent players from using kill command.
    */
    [JsonPropertyName("SelfKillProtection")]
    public bool SelfKillProtection { get; set; } = true;
    /**
    * Give extra taser for the kill with another weapon.
    *
    * Options:
    * 0 - disabled
    * 1 - enabled
    */
    [JsonPropertyName("ExtraTaserOnKill")]
    public bool ExtraTaserOnKill { get; set; } = true;

    /* Gives a Flash grenade on molotovlevel */
    [JsonPropertyName("MolotovBonusFlash")]
    public bool MolotovBonusFlash { get; set; } = false;

    /* Gives a smoke grenade on molotov level */
    [JsonPropertyName("MolotovBonusSmoke")]
    public bool MolotovBonusSmoke { get; set; }

    /**
    * This gives the player a weapon with 50 bullets on molotov level.
    *
    * Example:
    *     "MolotovBonusWeapon" "glock"  - gives glock 
    *     "MolotovBonusWeapon" "deagle" - gives deagle
    *     "MolotovBonusWeapon" ""       - feature disabled
    */
    [JsonPropertyName("MolotovBonusWeapon")]
    public string MolotovBonusWeapon { get; set; } = "deagle";

    /**
    * Give extra molotov for the kill by another weapon.
    *
    * Options:
    * 0 - disabled
    * 1 - enabled
    */
    [JsonPropertyName("ExtraMolotovForKill")]
    public bool ExtraMolotovForKill { get; set; } = true;

    /**
    * Delay before end of multiplayer game after gungame win. 
    *
    * Options:
    *      0 - Disabled.
    *      1-N - Number of seconds. At least 5 seconds more than map voting time at the end of map
    */
    [JsonPropertyName("EndGameDelay")]
    public float EndGameDelay { get; set; } = 35;
    /**
    * Freeze players after win.
    *
    * Options:
    *      true - Freeze players.
    *      false - Do not freeze players.
    */
    [JsonPropertyName("WinnerFreezePlayers")]
    public bool WinnerFreezePlayers { get; set; } = true;

    /**
    * Switch weapon without delays when player changes weapon by himself.
    *
    * SDK Hooks (sdkhooks) is required to use this option.
    *
    * Options:
    *      1 - Enabled.
    *      0 - Disabled.
    */
    [JsonPropertyName("FastSwitchOnChangeWeapon")]
    public bool FastSwitchOnChangeWeapon { get; set; } = false;

    /**
    * Switch weapon without delays after level up.
    *
    * Options:
    *      1 - Enabled.
    *      0 - Disabled.
    */
    [JsonPropertyName("FastSwitchOnLevelUp")]
    public bool FastSwitchOnLevelUp { get; set; } = false;

    /**
    * Do not fast switch on level up for this weapons.
    *
    * Comma-separated list 
    * of weapon names from gungame.equip.txt.
    *
    * Options:
    *      "hegrenade"                             - Enabled for hegrenade
    *      "hegrenade,taser"                       - Enabled for hegrenade and taser
    *      "taser,hegrenade,molotov,incgrenade"    - Default value
    *      ""                                      - Disabled
    */
    [JsonPropertyName("FastSwitchSkipWeapons")]
    public string FastSwitchSkipWeapons { get; set; } = "";

    /**
    * Sets how to finish the game after someone has won.
    *
    * Options:
    *      0 - Normal game end with scoreboard, vote next map valve menu and weapon drops.
    *      1 - Silent game end.
    */
    [JsonPropertyName("EndGameSilent")]
    public bool EndGameSilent { get; set; } = false;

    [JsonPropertyName("WarmupTimerSound")]
    public string WarmupTimerSound { get; set; } = "sounds/training/countdown.wav";
    
    /* Enables or disables built in Afk management system */
    [JsonPropertyName("AfkManagement")]
    public bool AfkManagement { get; set; } = true;
        
    /* Kick player on x number of afk deaths. */
    [JsonPropertyName("AfkDeaths")]
    public int AfkDeaths { get; set; } = 2;
    /**
    * What action to deal with the player when the maximum is reach?
    * 0 = Nothing, 1 = Kick, 2 = Move to spectate, 
    */
    [JsonPropertyName("AfkAction")]
    public int AfkAction { get; set; } = 2;

    /* Can bots win the game otherwise when they reach the last weapon and nothing will happen */
    [JsonPropertyName("BotCanWin")]
    public bool BotCanWin { get; set; } = false;

    /* Allow level up by killing a bot with knife */
    [JsonPropertyName("AllowUpByKnifeBot")]
    public bool AllowUpByKnifeBot { get; set; } = false;

    /* Allow level up by killing a bot with hegrenade */
    [JsonPropertyName("AllowLevelUpByExplodeBot")]
    public bool AllowLevelUpByExplodeBot { get; set; } = false;

    /* Allow level up by killing a bot with knife if there is no other human */
    [JsonPropertyName("AllowLevelUpByKnifeBotIfNoHuman")]
    public bool AllowLevelUpByKnifeBotIfNoHuman { get; set; } = false;

    /* Allow level up by killing a bot with hegrenade if there is no other human */
    [JsonPropertyName("AllowLevelUpByExplodeBotIfNoHuman")]
    public bool AllowLevelUpByExplodeBotIfNoHuman { get; set; } = false;

    /* If player wins on bot, then dont add win in stats. */
    [JsonPropertyName("DontAddWinsOnBot")]
    public bool DontAddWinsOnBot { get; set; } = false;

    /* List of sounds after we have the Winner  */
    [JsonPropertyName("WinnerSound")]
    public List<string> WinnerSound { get; set; } = new List<string>
    {
        "sounds/music/3kliksphilip_01/startaction_01.mp3",
        "sounds/music/austinwintory_01/startaction_01.mp3",
        "sounds/music/awolnation_01/startaction_03.mp3",
        "sounds/music/bbnos_01/bombplanted.mp3",
        "sounds/music/chipzel_01/endofmatch.wav",
        "sounds/music/danielsadowski_01/endofmatch.wav",
        "sounds/music/danielsadowski_02/startaction_01.mp3",
        "sounds/music/danielsadowski_04/chooseteam.mp3",
        "sounds/music/darude_01/startround_01.mp3",
        "sounds/music/dren_01/startaction_01.mp3",
        "sounds/music/halo_01/startaction_01.mp3",
        "sounds/music/hotlinemiami_01/startaction_01.mp3",
        "sounds/music/ianhultquist_01/startaction_03.mp3"
    };
    [JsonPropertyName("LevelDownSound")]
    public string LevelDownSound { get; set; } = "sounds/ui/armsrace_demoted.wav";

    [JsonPropertyName("LevelUpSound")]
    public string LevelUpSound { get; set; } = "sounds/ui/armsrace_level_up.wav";
    
    [JsonPropertyName("LevelStealUpSound")]
    public string LevelStealUpSound { get; set; } = "sounds/training/pointscored.wav";
    /* List of sounds after we have the Winner  */
    [JsonPropertyName("TeamKillSound")]
    public List<string> TeamKillSound { get; set; } = new List<string>
    {
        "sounds/vo/agents/balkan/friendlyfire05.wav",
        "sounds/vo/agents/fbihrt_epic/takingfire_friendly_05.wav",
        "sounds/vo/agents/fbihrt_epic/takingfire_friendly_07.wav",
        "sounds/vo/agents/fbihrt_epic/takingfire_friendly_08.wav",
        "sounds/vo/agents/gendarmerie_fem/ff1_sees_friend_killed_05.wav",
        "sounds/vo/agents/jungle_fem/aff1_sees_friend_killed_06.wav",
        "sounds/vo/agents/leet_epic/sees_friend_killed_01.wav",
        "sounds/vo/agents/professional/radiobotunderfirefriendly07.wav",
        "sounds/vo/agents/professional_fem/sees_friend_killed_01.wav",
        "sounds/vo/agents/sas/friendlyfire08.wav",
        "sounds/vo/agents/seal_diver_02/am1_sees_friend_killed_01.wav",
        "sounds/vo/agents/seal_fem/af1_sees_friend_killed_06.wav",
        "sounds/vo/agents/swat_fem/sees_friend_killed_04.wav"
    };
}