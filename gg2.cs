#pragma warning disable CS8981 // Naming Styles
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Newtonsoft.Json;
using GunGame;
using GunGame.Models;
using GunGame.Variables;

namespace GunGame
{
    public class GunGame : BasePlugin, IPluginConfig<GGConfig>
    {
        public bool Hot_Reload = false;
        public GunGame ()
        {
            playerManager = new(this);
        }
        public override string ModuleName => "CS2_GunGame";
        public override string ModuleVersion => "v1.0.0";
        public override string ModuleAuthor => "Sergey";
        public override string ModuleDescription => "GunGame mode for CS2";
        public bool WeaponLoaded = false;
        private bool warmupInitialized = false;
        private int WarmupCounter = 0;
        private bool IsObjectiveHooked = false;        
        public GGConfig Config { get; set; } = new();
        public void OnConfigParsed (GGConfig config)
        { 
            this.Config = config;
            GGVariables.Instance.MapStatus = (Objectives)Config.RemoveObjectives;
        }
        private bool LoadConfig()
        {
            string configPath = Server.GameDirectory + "/csgo/cfg/gungame/gungame.json";
            try
            {
                string jsonString = File.ReadAllText(configPath);

                if (string.IsNullOrEmpty(jsonString))
                {
                    Logger.LogError("Error loading config. csgo/cfg/gungame/gungame.json is wrong or empty");
                    return false;
                }

                var cnfg = System.Text.Json.JsonSerializer.Deserialize<GGConfig>(jsonString);
                if (cnfg != null)
                {
                    Config = cnfg;
                    return true;
                }
                else
                {
                    Logger.LogError("Error loading config. csgo/cfg/gungame/gungame.json is wrong or empty");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GunGame] Error reading or deserializing gungame.json file: {ex.Message}");
                return false;
            }
        }
        public PlayerManager playerManager; 
        public Dictionary<ulong, int> PlayerLevelsBeforeDisconnect = new();
        public Dictionary<ulong, int> PlayerHandicapTimes = new();
        private CounterStrikeSharp.API.Modules.Timers.Timer? warmupTimer = null;
        private CounterStrikeSharp.API.Modules.Timers.Timer? endGameTimer = null;
        int endGameCount = 0;
        private CounterStrikeSharp.API.Modules.Timers.Timer? HandicapUpdateTimer = null;
        public Dictionary<string, WeaponInfo>Weapon_from_List = new Dictionary<string, WeaponInfo>();
        public SpecialWeaponInfo SpecialWeapon = new();
        public Random random = new();
        public bool[,] g_Shot = new bool[64,64];
        private bool TryGetWeaponInfo(string weaponName, out WeaponInfo weaponInfo)
        {
            if (Weapon_from_List.TryGetValue(weaponName, out var info))
            {
                weaponInfo = info;
                return true;
            }
            else
            {
                if (weaponName.StartsWith("knife") || weaponName == "weapon_bayonet")
                {
                    if (Weapon_from_List.TryGetValue("knife", out var knifeinfo))
                    {
                        weaponInfo = knifeinfo;
                        return true;
                    }
                }
            }

            weaponInfo = new WeaponInfo
            {
                Index = 0,
                Slot = 0,
                Clipsize = 0,
                AmmoType = 0,
                LevelIndex = 0,
                FullName = ""
            };

            return false;
        }
        List<(int, string)> knives = new List<(int, string)>
        {
            (41, "knifegg"),
            (42, "knife"),
            (59, "knife_t"),
            (80, "knife_ghost"),
            (500, "weapon_bayonet"),  //503 Classic Knife
            (505, "knife_flip"),
            (506, "knife_gut"),
            (507, "knife_karambit"),
            (508, "knife_m9_bayonet"),
            (509, "knife_tactical"), 
            (512, "knife_falchion"),
            (514, "knife_survival_bowie"),
            (515, "knife_butterfly"),
            (516, "knife_push"), 
            (517, "knife_cord"),
            (518, "knife_canis"),
            (519, "knife_ursus"),
            (520, "knife_gypsy_jackknife"), //Navaja Knife  521 Nomad Knife
            (522, "knife_stiletto"),
            (523, "knife_widowmaker"), //Talon Knife  524 Default Knife, 526 Kukri Knife
            (525, "knife_skeleton"),
        };
        public override void Load(bool hotReload)
        {
            Console.WriteLine($"GunGame started, Max Players: {Server.MaxPlayers}");
            if (hotReload)
            {
                Hot_Reload = true;
                Logger.LogInformation("[GUNGAME] Hot Reload");
            }
            if (LoadConfig())
            {
                SetupGameWeapons();
                SetupWeaponsLevels();
                
                if (Config.IsPluginEnabled && WeaponLoaded) {
                    GG_Startup();
                }
            }
            else
            {
                Logger.LogError("Error loading config on Load");
            }
        }
        public override void Unload(bool hotReload)
        {
            //*********************************************
        }
        private void GG_Startup()
        {
            if ( !GGVariables.Instance.IsActive )
            {
                GGVariables.Instance.IsActive = true;
            }
            Logger.LogInformation("[GUNGAME] GG Start");

            InitVariables();
            SetupListeners();

            RegisterEventHandler<EventPlayerDeath>(EventPlayerDeathHandler);
            RegisterEventHandler<EventPlayerHurt>(EventPlayerHurtHandler);
            RegisterEventHandler<EventPlayerTeam>(EventPlayerTeamHandler);
            RegisterEventHandler<EventPlayerSpawn>(EventPlayerSpawnHandler);
            RegisterEventHandler<EventRoundStart>(EventRoundStartHandler);
            RegisterEventHandler<EventRoundEnd>(EventRoundEndHandler);
            RegisterEventHandler<EventHegrenadeDetonate>(EventHegrenadeDetonateHandler);
            RegisterEventHandler<EventWeaponFire>(EventWeaponFireHandler);
            RegisterEventHandler<EventItemPickup>(EventItemPickupHandler, HookMode.Post);
            if ( Config.HandicapUpdate > 0 )
            {
                HandicapUpdateTimer ??= AddTimer((float)Config.HandicapUpdate, Timer_HandicapUpdate, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            }
            else if (HandicapUpdateTimer != null)
            {
                HandicapUpdateTimer.Kill();
                HandicapUpdateTimer = null;
            }
        }
        private void SetupGameWeapons()
        {
            string WeaponFile = Server.GameDirectory + "/csgo/cfg/gungame/weapons.json";

            try
            {
                string jsonString = File.ReadAllText(WeaponFile);

                if (string.IsNullOrEmpty(jsonString))
                {
                    return;
                }

                var deserializedDictionary = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, WeaponInfo>>(jsonString);

                if (deserializedDictionary != null)
                {
                    Weapon_from_List = deserializedDictionary;
                }
                else
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GunGame] Error reading or deserializing weapons.json file: {ex.Message}");
                return;
            }
            GGVariables.Instance.g_WeaponsMaxId = 0;
            foreach (var kvp in Weapon_from_List)
            {
                WeaponInfo weaponInfo = kvp.Value;
                if (weaponInfo != null)
                {
                    GGVariables.Instance.g_WeaponsMaxId++;
                    switch (kvp.Key)
                    {
                        case "knife":
                            SpecialWeapon.Knife = weaponInfo.Index;
                            SpecialWeapon.KnifeLevelIndex = weaponInfo.LevelIndex;
                            break;

                        case "knifegg":
                            SpecialWeapon.Drop_knife = weaponInfo.Index;
                            break;

                        case "taser":
                            SpecialWeapon.Taser = weaponInfo.Index;
                            SpecialWeapon.TaserLevelIndex = weaponInfo.LevelIndex;
                            SpecialWeapon.TaserAmmoType = weaponInfo.AmmoType;
                            break;

                        case "flashbang":
                            SpecialWeapon.Flashbang = weaponInfo.Index;
                            GGVariables.Instance.g_WeaponIdFlashbang = weaponInfo.Index;
                            GGVariables.Instance.g_WeaponAmmoTypeFlashbang = weaponInfo.AmmoType;
                            break;

                        case "hegrenade":
                            SpecialWeapon.Hegrenade = weaponInfo.Index;
                            SpecialWeapon.HegrenadeLevelIndex = weaponInfo.LevelIndex;
                            SpecialWeapon.HegrenadeAmmoType = weaponInfo.AmmoType;
                            break;

                        case "smokegrenade":
                            SpecialWeapon.Smokegrenade = weaponInfo.Index;
                            GGVariables.Instance.g_WeaponIdSmokegrenade = weaponInfo.Index;
                            GGVariables.Instance.g_WeaponAmmoTypeSmokegrenade = weaponInfo.AmmoType;
                            break;

                        case "molotov":
                            SpecialWeapon.Molotov = weaponInfo.Index;
                            SpecialWeapon.MolotovLevelIndex = weaponInfo.LevelIndex;
                            SpecialWeapon.MolotovAmmoType = weaponInfo.AmmoType;
                            break;
                    }
                }
            }
            if (!(GGVariables.Instance.g_WeaponsMaxId != 0
                && SpecialWeapon.Knife != 0
                && SpecialWeapon.Hegrenade != 0
                && GGVariables.Instance.g_WeaponIdSmokegrenade != 0
                && GGVariables.Instance.g_WeaponIdFlashbang != 0))
            {
                string error = string.Format("FATAL ERROR: Some of the weapons not found MAXID=[{0}] KNIFE=[{1}] HE=[{2}] SMOKE=[{3}] FLASH=[{4}]. You should update your {5} and take it from the release zip file.",
                    GGVariables.Instance.g_WeaponsMaxId, SpecialWeapon.Knife, SpecialWeapon.Hegrenade, GGVariables.Instance.g_WeaponIdSmokegrenade, GGVariables.Instance.g_WeaponIdFlashbang, WeaponFile);
                
                Console.WriteLine(error);
                return;
            }
            if (!(SpecialWeapon.HegrenadeAmmoType != 0
                && GGVariables.Instance.g_WeaponAmmoTypeFlashbang != 0
                && GGVariables.Instance.g_WeaponAmmoTypeSmokegrenade != 0))
            {
                string error = string.Format("FATAL ERROR: Some of the ammo types not found HE=[{0}] FLASH=[{1}] SMOKE=[{2}]. You should update your {3} and take it from the release zip file.",
                    GGVariables.Instance.g_WeaponAmmoTypeHegrenade, GGVariables.Instance.g_WeaponAmmoTypeFlashbang, GGVariables.Instance.g_WeaponAmmoTypeSmokegrenade, WeaponFile);

                Console.WriteLine(error);
                return;
            }
            if (SpecialWeapon.Taser == 0)
            {
                string error = $"FATAL ERROR: Some of the weapons not found TASER=[{SpecialWeapon.Taser}]. You should update your {WeaponFile} and take it from the release zip file.";
                Console.WriteLine(error);
                return;
            }

            if (SpecialWeapon.MolotovAmmoType == 0 || SpecialWeapon.TaserAmmoType == 0)
            {
                string error = $"FATAL ERROR: Some of the ammo types not found MOLOTOV=[{SpecialWeapon.MolotovAmmoType}] TASER=[{SpecialWeapon.TaserAmmoType}]. You should update your {WeaponFile} and take it from the release zip file.";
                Console.WriteLine(error);
                return;
            }
        }
        private void SetupWeaponsLevels()
        {
            string WeaponLevelsFile = Server.GameDirectory + "/csgo/cfg/gungame/gungame_weapons.json";
            WeaponOrderSettings WeaponSettings;
            try
            {
                string jsonString = File.ReadAllText(WeaponLevelsFile);

                if (string.IsNullOrEmpty(jsonString))
                {
                    return;
                }

                // Deserialize JSON string to Dictionary<string, WeaponInfo>
                var deserializedDictionary = JsonConvert.DeserializeObject<WeaponOrderSettings>(jsonString);
                if (deserializedDictionary != null)
                {
                    WeaponSettings = deserializedDictionary;
                }
                else
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GunGame] Error reading or deserializing gungame_weapons.json file: {ex.Message}");
                return;
            }
            GGVariables.Instance.WeaponOrderCount = WeaponSettings.WeaponOrder?.Count ?? 0;   
            
            SetCustomKillPerLevel(WeaponSettings.MultipleKillsPerLevel);
            if (WeaponSettings.WeaponOrder != null)
            {
                MakeWeaponList(WeaponSettings.WeaponOrder, WeaponSettings.RandomWeaponReserveLevels, WeaponSettings.RandomWeaponOrder);
            }
            if (GGVariables.Instance.weaponsList.Count > 0)
                WeaponLoaded = true;
        }
        private static void SetCustomKillPerLevel(Dictionary<int, int> multipleKillsPerLevel)
        {
            GGVariables.Instance.CustomKillsPerLevel.Clear();
            foreach (var kvp in multipleKillsPerLevel)
            {
                if (kvp.Key > 0 && kvp.Value > 0) {
                    if (!GGVariables.Instance.CustomKillsPerLevel.ContainsKey(kvp.Key) ) {
                        GGVariables.Instance.CustomKillsPerLevel.Add(kvp.Key, kvp.Value);
                    }
                }
            }
        }
        private void MakeWeaponList(Dictionary<int, string> weaponOrder, string randomWeaponReserveLevels, bool randomOrder)
        {
            List <string> result = new();

            if (randomOrder)
            {
                result = RandomizeWeaponOrder(weaponOrder, randomWeaponReserveLevels);
            }
            else
            {
                // Use the original order
                int i = 1;
                foreach (var kvp in weaponOrder)
                {
                    result.Add(weaponOrder[i++]);
                }
            }

            for (int i = 0; i < GGVariables.Instance.WeaponOrderCount; i++)
            {
                if (TryGetWeaponInfo(result[i], out WeaponInfo weaponInfo))
                {
                    GGVariables.Instance.weaponsList.Add(new Weapon {
                        Level = i+1,
                        Name = result[i],
                        FullName = weaponInfo.FullName,
                        Index = weaponInfo.Index,
                        LevelIndex = weaponInfo.LevelIndex,
                        Slot = weaponInfo.Slot,
                        ClipSize = weaponInfo.Clipsize,
                        Ammo = weaponInfo.AmmoType});
                }
            }
        }
        static List<string> RandomizeWeaponOrder(Dictionary<int, string> weaponOrder, string reserveLevels)
        {
            // Split the reserve levels string into integers
            HashSet<int> reservedIndexes = new HashSet<int>(Array.ConvertAll(reserveLevels.Split(','), int.Parse));

            // Get the weapons that should not be randomized
            Dictionary<int, string> reservedWeapons = weaponOrder
                .Where(kv => reservedIndexes.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            // Get the weapons that should be randomized
            Dictionary<int, string> randomizableWeapons = weaponOrder
                .Where(kv => !reservedIndexes.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            // Shuffle the randomizable weapons
            List<string> shuffledWeapons = randomizableWeapons.Values.OrderBy(x => Guid.NewGuid()).ToList();

            // Place reserved weapons at their original positions
            foreach (var reservedWeapon in reservedWeapons)
            {
                shuffledWeapons.Insert(reservedWeapon.Key - 1, reservedWeapon.Value);
            }

            return shuffledWeapons;
        }
        private void SetupListeners()
        {
            RegisterListener<Listeners.OnClientConnected>((client) =>
            {
                var ggplayer = playerManager.CreatePlayerBySlot(client);
            });
            RegisterListener<Listeners.OnClientAuthorized>((slot, id) =>
            {
                var player = playerManager.GetOrCreatePlayerBySlot(slot);
                if (player != null)
                {
                    if ( Config.RestoreLevelOnReconnect )
                    {    
                        if ( PlayerLevelsBeforeDisconnect.TryGetValue(id.SteamId64, out int level) )
                        {
                            if ( player.Level < level )
                            {
                                Logger.LogInformation($"[GUNGAME]* OnClientAuthorized: Restore level {level} to {player.PlayerName}");
                                player.SetLevel (level); // saved level
                                RecalculateLeader(player.Slot, 0);
                            }
                        }
                    }               
                    UpdatePlayerScoreLevel(player.Slot);
                }
                else 
                {
                    Logger.LogError($"[GUNGAME]* OnClientAuthorized: Can't create player {slot} {id.SteamId64}");
                }
            });
            RegisterListener<Listeners.OnMapStart>(name =>
            {
                Logger.LogInformation($"Map {name} start {(Hot_Reload ? "Hot reload" : " ")}");
                Console.WriteLine("[GUNGAME]********* Map Start");
                if (LoadConfig())
                {
                    SetupGameWeapons();
                    SetupWeaponsLevels();
                
                    if (Config.IsPluginEnabled && WeaponLoaded)
                    {
                        InitVariables();
                        
                        FindMapObjective();
                        if ( !IsObjectiveHooked )
                        {
                            if (GGVariables.Instance.MapStatus.HasFlag(Objectives.Bomb))
                            {
                                IsObjectiveHooked = true;
                                RegisterEventHandler<EventBombPlanted>(EventBombHandler);
                                RegisterEventHandler<EventBombExploded>(EventBombHandler);
                                RegisterEventHandler<EventBombDefused>(EventBombHandler);
                                RegisterEventHandler<EventBombPickup>(EventBombPickupHandler);
                            }
                        
                            if (GGVariables.Instance.MapStatus.HasFlag(Objectives.Hostage))
                            {
                                IsObjectiveHooked = true;
                                RegisterEventHandler<EventHostageKilled>(EventHostageKilledHandler);
                            }
                        }

                        if (Config.WarmupEnabled)
                        {
                            StartWarmupRound();
                        }
                    }
                }
                else
                {
                    Logger.LogError("Error loading config on Mapstart");
                }
            });
            RegisterListener<Listeners.OnMapEnd>(() => 
            {
                Console.WriteLine("[GUNGAME]********* Map end");
                if (warmupTimer != null)
                {
                    warmupTimer.Kill();
                    warmupTimer = null;
                }
                warmupInitialized = false;
                GGVariables.Instance.WarmupFinished = false;
                
                PlayerLevelsBeforeDisconnect.Clear();
                if ( IsObjectiveHooked )
                {
                    if (GGVariables.Instance.MapStatus.HasFlag(Objectives.Bomb))
                    {
                        IsObjectiveHooked = false;
                        DeregisterEventHandler("bomb_planted", EventBombHandler<EventBombPlanted>, true);
                        DeregisterEventHandler("bomb_exploded", EventBombHandler<EventBombExploded>, true);
                        DeregisterEventHandler("bomb_defused", EventBombHandler<EventBombDefused>, true);
                        DeregisterEventHandler("bomb_pickup", EventBombPickupHandler, true);
                    }
                
                    if (GGVariables.Instance.MapStatus.HasFlag(Objectives.Hostage))
                    {
                        IsObjectiveHooked = false;
                        DeregisterEventHandler("hostage_killed", EventHostageKilledHandler<EventHostageKilled>, true);
                    }
                }
            });
            RegisterListener<Listeners.OnClientDisconnect>( slot =>
            {
                var player = playerManager.FindBySlot(slot);
                if (player == null) {
                    return;
                }

                if ( GGVariables.Instance.CurrentLeader.Slot == slot )
                {
                    RecalculateLeader(slot, (int)player.Level, 0);
                    if ( GGVariables.Instance.CurrentLeader.Slot == slot )
                    {
                        GGVariables.Instance.CurrentLeader.Slot = -1;
                    }
                }
                if ( Config.AutoFriendlyFire && player.State.HasFlag(PlayerStates.GrenadeLevel) )
                {
                    player.State &= ~PlayerStates.GrenadeLevel;

                    if ( --GGVariables.Instance.PlayerOnGrenade < 1 )
                    {
                        GGVariables.Instance.PlayerOnGrenade = 0;
                        if ( Config.FriendlyFireOnOff ) {
                            ChangeFriendlyFire(false);
                        } else {
                            ChangeFriendlyFire(true);
                        }
                    }
                } 
                PlayerLevelsBeforeDisconnect[player.SavedSteamID] = (int) player.Level;
/*                if (playerController != null && playerController.Pawn != null &&
                playerController.Pawn.Value != null &&  playerController.Pawn.Value.LifeState == (byte)LifeState_t.LIFE_ALIVE)
                {
                    StopTripleEffects(player);
                } */
                playerManager.ForgetPlayer(player.Slot);
            }); 
            RegisterListener<Listeners.OnClientDisconnectPost>( slot =>
            {
                playerManager.ForgetPlayer(slot);
                AddTimer(2.0f, () => {
                    var playerController = Utilities.GetPlayerFromSlot(slot);
                    if (playerController != null)
                    { 
                        if (playerController.IsValid)
                        {
                            if (playerController.TeamNum == 0 && playerController.Connected == PlayerConnectedState.PlayerDisconnected)
                            {
                                playerController.CommitSuicide(false,false);
                                playerController.Remove();
                            }
                        }
                    }    
                });
            });
        }
        private static void InitVariables ()
        {
            GGVariables.Instance.Tcount = 0;
            GGVariables.Instance.CTcount = 0;
            GGVariables.Instance.CurrentLeader.Slot = -1;
            GGVariables.Instance.CurrentLeader.Level = 0;

            GGVariables.Instance.MapStatus = 0;
            GGVariables.Instance.HostageEntInfo = 0;
            GGVariables.Instance.IsVotingCalled = false;
            GGVariables.Instance.isCalledEnableFriendlyFire = false;
            GGVariables.Instance.isCalledDisableRtv = false;
            GGVariables.Instance.GameWinner = null;

            GGVariables.Instance.mp_friendlyfire = ConVar.Find("mp_friendlyfire");
        }
        public void StartWarmupRound()
        {
            Console.WriteLine("[GunGame] StartWarmupRound called.");
            warmupInitialized = true;
            WarmupCounter = 0;
            if (warmupTimer == null)
            {
                 warmupTimer= AddTimer(1.0f, EndOfWarmup, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            }
            Server.ExecuteCommand("exec gungame/gungame.warmupstart.cfg");
        }
        public void EndOfWarmup()
        {
            if ((GGVariables.Instance.CTcount + GGVariables.Instance.Tcount) == 0) {
                WarmupCounter = 0;
                return;
            }

            if ( ++WarmupCounter < Config.WarmupTimeLength )
            {   
                var playerEntities = Utilities.GetPlayers().Where(p => p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV);
                if (playerEntities != null && playerEntities.Count() > 0)
                {
                    foreach (var playerController in playerEntities)
                    {
                        if (IsValid(playerController))
                        {
                            var seconds = Config.WarmupTimeLength - WarmupCounter;
                            playerController.PrintToCenter(Localizer["warmup.left", seconds]);
                            if ( (Config.WarmupTimeLength - WarmupCounter) == 4)
                            {
                                PlaySound(null!, Config.WarmupTimerSound);
                            }
                        }
                    }
                }
                return;
            }
            var mp_restartgame = ConVar.Find("mp_restartgame");
            
            if (mp_restartgame != null )
            {
                mp_restartgame.SetValue((int)1);
                if (warmupTimer != null)
                {
                    warmupTimer.Kill();
                    warmupTimer = null;
                }
                warmupInitialized = false;
                GGVariables.Instance.WarmupFinished = true;

                Server.ExecuteCommand("exec gungame/gungame.warmupend.cfg");
            }
            Console.WriteLine("WarmUp End");
            if (Config.ShootKnifeBlock)
            {
                var playerEntities = Utilities.GetPlayers();
                if (playerEntities != null && playerEntities.Count > 0)
                {
                    foreach (var playerController in playerEntities)
                    {
                        if (IsValid(playerController) && !playerController.IsBot)
                        {
                            ForgiveShots((int)playerController.Index);
                        }
                    }
                }
            }
        }
/**************  Events **********************************************************/
/**************  Events **********************************************************/
/**************  Events **********************************************************/
        private HookResult EventPlayerSpawnHandler(EventPlayerSpawn @event, GameEventInfo info)
        {
            Process currentProc = Process.GetCurrentProcess();
            if (!GGVariables.Instance.IsActive) {
                return HookResult.Continue;
            }
            var playerController = @event.Userid; 
            if (!IsValid(playerController) || !IsClientInTeam(playerController) || playerController.IsHLTV) {
                return HookResult.Continue;
            }
            var client = playerManager.GetPlayer(playerController, "EventPlayerSpawnHandler");
            if ( client == null ) {
                Logger.LogError($"[GUNGAME]PlayerSpawnHandler: Can't find player for {playerController.PlayerName}");
                return HookResult.Continue;
            }
            if (Config.AfkManagement)
            {
                AddTimer(0.2f, () =>
                {
                    if (playerController != null && playerController.IsValid 
                        && playerController.PlayerPawn != null && playerController.PlayerPawn.Value != null)
                    {
                        var angles = playerController.PlayerPawn.Value?.EyeAngles;
                        var origin = playerController.PlayerPawn.Value?.CBodyComponent?.SceneNode?.AbsOrigin;
                        
                        client.Angles = new QAngle(
                            x: angles?.X,
                            y: angles?.Y,
                            z: angles?.Z
                        );
                        
                        client.Origin = new Vector(
                            x: origin?.X,
                            y: origin?.Y,
                            z: origin?.Z
                        );
                    }
                });
            }
            if (client.LevelWeapon == null)
            {
                Logger.LogError($"[GUNGAME]PlayerSpawnHandler: {playerController.PlayerName} slot {client.Slot} does not have LevelWeapon");
            }
            UpdatePlayerScoreLevel(client.Slot);
            
            client.TeamChange = false;

            // Reset Knife Elite state 
            if ( Config.KnifeElite )
            {
                client.State &= ~PlayerStates.KnifeElite;
            }
            if ( !client.State.HasFlag(PlayerStates.FirstJoin) )
            {
                client.State |= PlayerStates.FirstJoin;

                if ( !playerController.IsBot )
                {
//                    PlaySoundDelayed(1.5f, client, "Welcome");

                    //*** Show join message.

                    if ( Config.JoinMessage )
                    {     

                        //ShowJoinMsgPanel(client);
                    }
                }
                
                if ( !warmupInitialized 
                && !GGVariables.Instance.StatsEnabled || IsPlayerWinsLoaded(client) 
                    ) // HINT: gungame_stats
                {
                    SetHandicapForClient(client);
                }
            }
                        /* For deathmatch when they get respawn after round start freeze after game winner. */
            if (GGVariables.Instance.GameWinner!= null) {
                if (Config.WinnerFreezePlayers) 
                {
                    AddTimer(0.3f, () =>
                    {
                        FreezePlayer(playerController);
                    });
                }
                return HookResult.Continue;
            }
            client.CurrentLevelPerRound = 0;
            client.CurrentLevelPerRoundTriple = 0;

            if ( (CsTeam)playerController.TeamNum == CsTeam.CounterTerrorist )
            {
                if ( GGVariables.Instance.MapStatus.HasFlag(Objectives.Bomb) && !GGVariables.Instance.MapStatus.HasFlag(Objectives.RemoveBomb) )
                {
                    // Give them a defuser if objective is not removed
//                    SetEntData(client, OffsetDefuser, 1);
                }
            }

            if ( Config.WarmupEnabled && !GGVariables.Instance.WarmupFinished )
            {
                if (!playerController.IsBot)
                {
                    if ( !warmupInitialized ) {
                        playerController.PrintToChat(Localizer["warmup.notstarted"]);
                    } else {
                        playerController.PrintToChat(Localizer["warmup.started"]);
                    }
                }

                GiveWarmUpWeaponDelayed(0.5f, client.Slot);

                return HookResult.Continue;
            }

            AddTimer(0.1f, () =>
            {
                GiveNextWeapon(client.Slot, false, true);
            });
            
            int Level = (int)client.Level;
            int killsPerLevel = GetCustomKillPerLevel(Level);

            if (!playerController.IsBot)
            {
                if ( !Config.ShowSpawnMsgInHintBox )
                {
                    playerController.PrintToCenter(Localizer["your.level", Level, GGVariables.Instance.WeaponOrderCount]);
                    if ( Config.ShowLeaderInHintBox && GGVariables.Instance.CurrentLeader.Slot > -1 )
                    {
                        int leaderLevel = (int) GGVariables.Instance.CurrentLeader.Level;
                        if ( client.Level == GGVariables.Instance.CurrentLeader.Level ) {
                            playerController.PrintToChat(Localizer["you.leader"]);
                        } else if ( Level == leaderLevel ) {
                            playerController.PrintToChat(Localizer["onleader.level"]);
                        } else {
                            playerController.PrintToChat(Localizer["leader.level", leaderLevel]);
                        }
                    }
                    if ( Config.MultiKillChat && ( killsPerLevel > 1 ) )
                    {
                        playerController.PrintToChat(Localizer["kills.toadvance",killsPerLevel - client.CurrentKillsPerWeap]);
                    }
                }
                else
                {
                    playerController.PrintToChat(Localizer["your.level", Level, GGVariables.Instance.WeaponOrderCount]);

                    if ( Config.MultiKillChat && ( killsPerLevel > 1 ) )
                    {   
                        playerController.PrintToChat(Localizer["kills.toadvance",killsPerLevel - client.CurrentKillsPerWeap]);
                    }
                }
            }
            return HookResult.Continue;
        }
        private HookResult EventPlayerDeathHandler(EventPlayerDeath @event, GameEventInfo info)
        {
            if (!GGVariables.Instance.IsActive) {
                return HookResult.Continue;
            }
            CCSPlayerController VictimController = @event.Userid;
            CCSPlayerController KillerController = @event.Attacker;
            string weapon_used = @event.Weapon;
            
            if (!IsValid(VictimController))
            {
                return HookResult.Continue;
            }
            var Victim = playerManager.GetPlayer(VictimController, "EventPlayerDeathHandler");
            if ( Victim == null ) {
                return HookResult.Continue;
            }
            if (Config.ShootKnifeBlock)
            {
                ForgiveShots((int)VictimController.Index);
            }
//            StopBonusGravity(Victim);
            StopTripleEffects(Victim);
//            UpdatePlayerScoreDelayed(Victim);
//            UpdatePlayerScoreDelayed(Killer);

            /* They change team at round end don't punish them. */
            if ( !GGVariables.Instance.RoundStarted && !Config.AllowLevelUpAfterRoundEnd )
            {
                return HookResult.Continue;
            }
            if (Config.AfkManagement)
            {
                var angles = VictimController.PlayerPawn.Value?.EyeAngles;
                var origin = VictimController.PlayerPawn.Value?.CBodyComponent?.SceneNode?.AbsOrigin;
                if (Victim.Angles != null && Victim.Origin != null && angles != null && origin != null
                    && Victim.Angles.X == angles.X && Victim.Angles.Y == angles.Y
                    && Victim.Origin.X == origin.X && Victim.Origin.Y == origin.Y)
                {
                    KillerController?.PrintToCenter(Localizer["kill.afk"]);
                    if (Config.AfkAction > 0 && ++Victim.AfkCount >= Config.AfkDeaths)
                    {
                        if (Config.AfkAction == 1) //Kick
                        {
                            Server.ExecuteCommand($"kickid {VictimController.UserId} '{Localizer["max.afkdeath"]}'");
                        }
                        else if (Config.AfkAction == 2)
                        {
                            VictimController.PlayerPawn.Value?.CommitSuicide(false, true);
                            VictimController.ChangeTeam(CsTeam.Spectator);
                            Victim.AfkCount = 0;
                        }
                    }
                    return HookResult.Continue;
                }
                else
                {
                    Victim.AfkCount = 0;
                }
            }
            /* Kill self with world spawn */
            if (weapon_used == "world" || weapon_used == "world ent")
            {
                Console.WriteLine($"{VictimController.PlayerName} - died from world");
                if ( GGVariables.Instance.RoundStarted && Config.WorldspawnSuicide > 0)
                {
                    // kill self with world spawn
                    ClientSuicide(Victim, Config.WorldspawnSuicide);
                }
                return HookResult.Continue;
            }

            /* They killed themself by kill command or by hegrenade etc */
            if (IsValid(KillerController) && KillerController.Index == VictimController.Index)
            {
                Console.WriteLine($"{VictimController.PlayerName} - suicide");
                /* (Weapon is event weapon name, can be 'world' or 'hegrenade' etc) */ /* weapon is not 'world' (ie not kill command) */
                if ( Config.CommitSuicide > 0 && GGVariables.Instance.RoundStarted 
                && !(weapon_used == "world" || weapon_used == "world ent") && !Victim.TeamChange )
                {
                    // killed himself by kill command or by hegrenade
                    ClientSuicide(Victim, Config.CommitSuicide);
                }
                return HookResult.Continue;
            }

            // Victim > 0 && Killer > 0
            if (KillerController == null)
            {
                return HookResult.Continue;
            }
            if (KillerController.IsValid && KillerController.DesignerName == "cs_player_controller")
            {
                Console.WriteLine($"{KillerController.PlayerName} killed {VictimController.PlayerName} with {weapon_used}");
            }
            else
            {
                Logger.LogError($"{VictimController.PlayerName} killed not by player. Designer name: {KillerController.DesignerName}");
            }
            var ggKiller = playerManager.GetPlayer(KillerController, "EventPlayerDeathHandler");

            if (ggKiller == null)
            {
                return HookResult.Continue;
            }
            GGPlayer Killer = ggKiller;
            if (!TryGetWeaponInfo(weapon_used, out WeaponInfo usedWeaponInfo))
            {
                Logger.LogError($"[GUNGAME] **** Cant get weapon info for weapon used in kill {weapon_used} by {Killer.PlayerName}");
                return HookResult.Continue;
            }

            if ( warmupInitialized )
            {
                if (Config.ReloadWeapon)
                {
                    ReloadActiveWeapon(Killer, usedWeaponInfo.Index);
                }
                return HookResult.Continue;
            }

        /* Here is a place that the forward sends and receives the answer to count this kill as a kill or not.
         * Let's deal with the forwards and finish it
         */
            /*************** FFA - teamkill is considered as kill ****************/
            bool TeamKill = (!Config.FriendlyFireAllowed) && (VictimController.TeamNum == KillerController.TeamNum);

/*            Call_StartForward(FwdDeath);
            Call_PushCell(Killer);
            Call_PushCell(Victim);
            Call_PushCell(WeaponIndex);
            Call_PushCell(TeamKill && GetConVarInt(mp_friendlyfire));
            Call_Finish(ret); 

            if ( ret || TeamKill ) 
            {
                if ( ret == Plugin_Changed )
                { */
            int level;
            bool returned_value = false; // if another plugin asks this not to be considered a kill, it will return true
            if (returned_value || TeamKill)
            {
                if (returned_value)
                {    
                    ReloadActiveWeapon(Killer, usedWeaponInfo.Index);
                    return HookResult.Continue;
                } 
                if ( TeamKill )
                {
//                    PlayRandomSound(0,"TeamKillSounds","NoTeamKillSounds");
                    Killer.CurrentLevelPerRound -= Config.TkLooseLevel;
                    if ( Killer.CurrentLevelPerRound < 0 )
                    {
                        Killer.CurrentLevelPerRound = 0;
                    }
                    Killer.CurrentLevelPerRoundTriple = 0;
                    
                    int oldLevel = (int) Killer.Level;
                    level = ChangeLevel(Killer, -Config.TkLooseLevel);
                    if ( level == oldLevel )
                    {
                        return 0;
                    }

                    if ( Config.TurboMode )
                    {
                        GiveNextWeapon(Killer.Slot);
                    }
                    return HookResult.Continue;
                }
            } 
            level = (int) Killer.Level;
            
            /* Give them another grenade if they killed another person with another weapon */
            if ( (Killer.LevelWeapon.LevelIndex == SpecialWeapon.HegrenadeLevelIndex)  //киллер на гранате
                && (usedWeaponInfo.LevelIndex != SpecialWeapon.HegrenadeLevelIndex)   // а убил не гранатой
                && !( (usedWeaponInfo.LevelIndex == SpecialWeapon.KnifeLevelIndex) && Config.KnifeProHE ) // TODO: Remove this statement and make check if killer not leveled up, than give extra nade.
            ) 
            {
        /************* Here is the idea about sticky grenade ************/        
                GiveExtraNade(KillerController);
            }
            
            /* Give them another taser if they killed another person with another weapon */
            if ( (Killer.LevelWeapon.LevelIndex == SpecialWeapon.TaserLevelIndex) 
                && (usedWeaponInfo.LevelIndex != SpecialWeapon.TaserLevelIndex)
                && Config.ExtraTaserOnKill) 
            {
                GiveExtraTaser(KillerController);
            }

            /* Give them another molotov if they killed another person with another weapon */
            if ((Killer.LevelWeapon.LevelIndex == SpecialWeapon.Molotov)
                && (usedWeaponInfo.LevelIndex != SpecialWeapon.MolotovLevelIndex)
                && Config.ExtraMolotovForKill) 
            {
                GiveExtraMolotov(KillerController, Killer.LevelWeapon.Index);
            }

            if ( (Config.MaxLevelPerRound > 0) && Killer.CurrentLevelPerRound >= Config.MaxLevelPerRound )
            {
                return HookResult.Continue;
            }

            int oldLevelKiller;
            int killsPerLevel = GetCustomKillPerLevel(level);

            if ( Config.KnifePro && (usedWeaponInfo.LevelIndex == SpecialWeapon.KnifeLevelIndex) 
                || (Config.MolotovPro && (usedWeaponInfo.LevelIndex == SpecialWeapon.MolotovLevelIndex) ) )
            {
                bool follow = true;
                int VictimLevel = (int) Victim.Level;
                if ( VictimLevel < Config.KnifeProMinLevel )
                {
                    if (!KillerController.IsBot) KillerController.PrintToChat(Localizer["level.low",Victim.PlayerName, Config.KnifeProMinLevel]);
                    follow = false;
                }
                if ( follow && (Config.KnifeProMaxDiff > 0) && ( Config.KnifeProMaxDiff < (VictimLevel - level) ) )
                {
                    if (!KillerController.IsBot) KillerController.PrintToChat(Localizer["level.difference", Victim.PlayerName, Config.KnifeProMaxDiff]);
                    follow = false;
                }
                if ( follow && !Config.DisableLevelDown ) 
                {
                    int ChangedLevel = ChangeLevel(Victim, -1, true);
                    if ( ChangedLevel != VictimLevel ) 
                    {
                        Server.PrintToChatAll (Localizer["level.stolen", Killer.PlayerName, Victim.PlayerName]);
                        if (usedWeaponInfo.LevelIndex == SpecialWeapon.KnifeLevelIndex) 
                        {
//                                PlaySoundDelayed (0.7f, null, "KnifeKillSound");
                    /***** Here a function call is started that tells others that they have been killed with a knife ********
                        *       Call_StartForward(FwdKnifeSteal);
                        *       Call_PushCell(Killer);
                        *       Call_PushCell(Victim);
                        *       Call_Finish(ret); */
                        }
                        else if (usedWeaponInfo.LevelIndex == SpecialWeapon.MolotovLevelIndex)
                        {
//                                PlaySoundDelayed (0.7f, null, "MolotovKillSound");
                        }
       
                    }
                }
/************  these conditions are used to decide whether the level should be raised or not (before this, the victim’s level was subtracted) *************/
    // if on a knife and you need to go through more than one knife
                if (follow && Killer.LevelWeapon.LevelIndex == SpecialWeapon.KnifeLevelIndex && killsPerLevel > 1)
                {
                    follow = false;
                }
    // you can't pass a grenade with a knife
                if ( follow && !Config.KnifeProHE && Killer.LevelWeapon.LevelIndex == SpecialWeapon.HegrenadeLevelIndex ) 
                {
                    return HookResult.Continue;
                }
    // you can't pass the taser with a knife
                if (follow && Killer.LevelWeapon.LevelIndex == SpecialWeapon.TaserLevelIndex) 
                {
                    return HookResult.Continue;
                }
    // You can't get past Molotov with a knife
                if (follow && Killer.LevelWeapon.LevelIndex == SpecialWeapon.MolotovLevelIndex && killsPerLevel > 1) 
                {
                    follow = false;
                }
                if (follow)
                {
                    oldLevelKiller = level;
                    level = ChangeLevel(Killer, 1, true);
                    if ( oldLevelKiller == level ) {
                        return HookResult.Continue;
                    }
                    PrintLeaderToChat(Killer, oldLevelKiller, level);
                    Killer.CurrentLevelPerRound++;

                    if (Config.TurboMode) {
                        GiveNextWeapon(Killer.Slot, true); // true - level up with knife
                    }
                    CheckForTripleLevel(Killer);

                    return HookResult.Continue;
                }
            }
            bool LevelUpWithPhysics = false;

    /* They didn't kill with the weapon required */
            if (usedWeaponInfo.LevelIndex != Killer.LevelWeapon.LevelIndex) 
            {
                if (usedWeaponInfo.LevelIndex == SpecialWeapon.HegrenadeLevelIndex) 
                {
                    // Killed with grenade made by map author
                    if ( Config.CanLevelUpWithMapNades
                        && ( Config.CanLevelUpWithNadeOnKnife
                            || !(Killer.LevelWeapon.LevelIndex == SpecialWeapon.KnifeLevelIndex))) 
                    {
                        LevelUpWithPhysics = true;
                    } else 
                    {
                        return HookResult.Continue;
                    }
                } else {
                    // Maybe killed with physics made by map author
                    if ( 
                        Config.CanLevelUpWithPhysics
                        && ( weapon_used == "prop_physics") || (weapon_used == "prop_physics_multiplayer") 
                        && ( 
                            ( ( Killer.LevelWeapon.LevelIndex != SpecialWeapon.HegrenadeLevelIndex) && !(Killer.LevelWeapon.LevelIndex == SpecialWeapon.KnifeLevelIndex) )
                            || ( Config.CanLevelUpWithPhysicsOnGrenade && (Killer.LevelWeapon.LevelIndex == SpecialWeapon.HegrenadeLevelIndex) )
                            || ( Config.CanLevelUpWithPhysicsOnKnife && (Killer.LevelWeapon.LevelIndex == SpecialWeapon.KnifeLevelIndex) ))) 
                    {
                        LevelUpWithPhysics = true;
                    } else 
                    {
                        return HookResult.Continue;
                    }
                }
            }
            
            if ( ( killsPerLevel > 1 ) && !LevelUpWithPhysics)
            {
                int kills = ++Killer.CurrentKillsPerWeap;
                
                /* An external function is called, which confirms whether to give a kill or not. If not, true is returned */
                bool Handled = false;

                if ( kills <= killsPerLevel )
                {
/*                    Call_StartForward(FwdPoint);
                    Call_PushCell(Killer);
                    Call_PushCell(kills);
                    Call_PushCell(1);
                    Call_Finish(Handled); */

                    if ( Handled )
                    {
                        Killer.CurrentKillsPerWeap--;
                        return HookResult.Continue;
                    }
        //  **************************************** check logic
                    if ( kills < killsPerLevel )
                    {
                        PlaySound(KillerController, Config.MultiKillSound);

                        if ( Config.MultiKillChat )
                        {
        // ************************* For now we’re just print into the chat, maybe we’ll improve it later
                            if (!KillerController.IsBot) KillerController.PrintToChat(Localizer["kills.toadvance", killsPerLevel - kills]);

/*                            if ( !g_Cfg_ShowSpawnMsgInHintBox )
                            {
                                char subtext[64];
                                FormatLanguageNumberTextEx(Killer, subtext, sizeof(subtext), killsPerLevel - kills, "points");
                                CPrintToChat(Killer, "%t", "You need kills to advance to the next level", subtext, kills, killsPerLevel);
                            }
                            else
                            {
                                SetGlobalTransTarget(Killer);
                                char textHint[256];
                                char subtext[64];
                                FormatLanguageNumberTextEx(Killer, subtext, sizeof(subtext), killsPerLevel - kills, "points");
                                Format(textHint, sizeof(textHint), "%t", "You need kills to advance to the next level", subtext, kills, killsPerLevel);
                                CRemoveTags(textHint, sizeof(textHint));
                                
                                UTIL_ShowHintTextMulti(Killer, textHint, 3, 1.0);
                            } */
                        }
                        
                        if ( Config.ReloadWeapon && Killer != null && Killer.LevelWeapon != null)
                        {
                            ReloadActiveWeapon(Killer, Killer.LevelWeapon.Index);
                        }
                        return HookResult.Continue;
                    }
                }
            }

            // reload weapon
            if ( !Config.TurboMode && Config.ReloadWeapon)
            {
                ReloadActiveWeapon(Killer, Killer.LevelWeapon.Index);
            }
                
            if ( Config.KnifeElite )
            {
                Killer.State |= PlayerStates.KnifeElite;
            }

            oldLevelKiller = level;
            level = ChangeLevel(Killer, 1, false);
            if ( oldLevelKiller == level )
            {
                return HookResult.Continue;
            }
            Killer.CurrentLevelPerRound++;
            PrintLeaderToChat(Killer, oldLevelKiller, level);

            if ( Config.TurboMode || Config.KnifeElite)
            {
                GiveNextWeapon(Killer.Slot, usedWeaponInfo.LevelIndex == SpecialWeapon.KnifeLevelIndex);
            }
            CheckForTripleLevel(Killer);

            return HookResult.Continue;
        }
        private HookResult EventPlayerHurtHandler(EventPlayerHurt eventInfo, GameEventInfo gameEventInfo)
        {
            if (!Config.ShootKnifeBlock) {
                return HookResult.Continue;
            }
            if (eventInfo == null) return HookResult.Continue;

            var attacker = eventInfo.Attacker; 
            var victim = eventInfo.Userid; 
            var weapon = eventInfo.Weapon;

            if (attacker != null && victim != null && IsPlayer((int)attacker.Index) && IsPlayer((int)victim.Index))
            {
                bool found = false;
                if (IsWeaponKnife(weapon)) // if damage by knife
                {
                    if (g_Shot[(int)attacker.Index, (int)victim.Index])
                    {
                        if (attacker.PlayerPawn != null && attacker.PlayerPawn.Value != null
                        &&  attacker.PlayerPawn.Value.WeaponServices != null)
                        {
                            foreach (var clientWeapon in attacker.PlayerPawn.Value.WeaponServices.MyWeapons)
                            {
                                if (clientWeapon is { IsValid: true, Value.IsValid: true })
                                {
                                    if (Weapon_from_List.TryGetValue(RemoveWeaponPrefix(clientWeapon.Value.DesignerName), out var weaponInfo))
                                    {
                                        if (weaponInfo.Slot == 1 || weaponInfo.Slot == 2)
                                        {
                                            found = true; //but player has anothe weapon in slots 1 or 2
                                            break;
                                        }
                                    }
                                }
                            }

                            if (found) // punish him for shoot and knife
                            {
    //                            attacker.PlayerPawn.Value.Render = Color.FromArgb(255, 255, 0, 0);
                                AddTimer(0.2f, () =>
                                {
                                    attacker.CommitSuicide(true,true);
                                    attacker.PrintToCenter(Localizer["dontshoot.knife"]);
                                    Server.PrintToChatAll(Localizer["triedshoot.knife", attacker.PlayerName]);
                                });
                                
                            }
                        }
                    }
                }
                else if (!weapon.Equals("hegrenade"))
                {
                    g_Shot[(int)attacker.Index, (int)victim.Index] = true;
                }
            }
            return HookResult.Continue;
        }
        private HookResult EventWeaponFireHandler(EventWeaponFire @event, GameEventInfo info)
        {
            if (Config.AfkManagement)
            {
                var playerController = @event.Userid; 
                if (!IsValid(playerController)) {
                    return HookResult.Continue;
                }
                var client = playerManager.GetPlayer(playerController, "EventPlayerSpawnHandler");
                if ( client != null ) {
                    client.Angles!.X +=500;
                }
            }
            return HookResult.Continue;
        }
        private HookResult EventPlayerTeamHandler(EventPlayerTeam @event, GameEventInfo info)
        {

            int oldTeam = @event.Oldteam;
            int newTeam = @event.Team;
            bool disconnect = @event.Disconnect;
            
            if ( oldTeam == (int)CsTeam.Terrorist)
            {
                GGVariables.Instance.Tcount--;
                if (GGVariables.Instance.Tcount < 0) {
                    GGVariables.Instance.Tcount = 0;
                }
            }
            else if (oldTeam == (int)CsTeam.CounterTerrorist)
            {
                GGVariables.Instance.CTcount--;
                if (GGVariables.Instance.CTcount < 0) {
                    GGVariables.Instance.CTcount = 0;
                }
            }

            /* Player disconnected and didn't join a new team */
            if ( !disconnect )
            {
                if ( newTeam == (int)CsTeam.Terrorist) {
                    GGVariables.Instance.Tcount++;
                }
                else if (newTeam == (int)CsTeam.CounterTerrorist) {
                    GGVariables.Instance.CTcount++;
                }
            }

            if ( Config.UnlimitedNadesMinPlayers > 0)
            {
                if ( GGVariables.Instance.Tcount <= Config.UnlimitedNadesMinPlayers || GGVariables.Instance.CTcount <= Config.UnlimitedNadesMinPlayers )
                {
                    Config.UnlimitedNades = true;
                }
                else
                {
                    Config.UnlimitedNades = false;
                }
            }
            CCSPlayerController playerController = @event.Userid;
            if (!IsValid(playerController)) {
                return HookResult.Continue;
            }

/*            var player = playerManager.GetPlayer(playerController, "EventPlayerTeamHandler");
            if ( player != null && !disconnect && (oldTeam >= 2) && (newTeam >= 2) && playerController.PawnIsAlive )
            {
                StopTripleEffects(player);
            } */
/*            if ( player == null || disconnect || (oldTeam < 2) || (newTeam < 2) || !playerController.PawnIsAlive || (oldTeam == newTeam) )
            {
                return HookResult.Continue;
            }
            player.TeamChange = true;
            player.TeamNum = newTeam; */
            return HookResult.Continue;
        }
        private HookResult EventRoundStartHandler<T>(T @event, GameEventInfo info) where T : GameEvent
        {
            Console.WriteLine("[GUNGAME]********* Event Round Start");
            if ( !GGVariables.Instance.IsActive )
            {
                return HookResult.Continue;
            }
            var points = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("point_servercommand");
            foreach (var point in points)
            {
                point.Remove();
            }

            NativeAPI.IssueServerCommand("mp_t_default_secondary  \"\"");
            NativeAPI.IssueServerCommand("mp_ct_default_secondary  \"\"");
            NativeAPI.IssueServerCommand("mp_t_default_melee  \"\"");
            NativeAPI.IssueServerCommand("mp_ct_default_melee  \"\"");
            NativeAPI.IssueServerCommand("mp_equipment_reset_rounds 0");
/*            var convar = ConVar.Find("mp_t_default_primary");
            if (convar != null) convar.StringValue = "";
            convar = ConVar.Find("mp_t_default_secondary");
            if (convar != null) convar.StringValue = "";
            convar = ConVar.Find("mp_ct_default_primary");
            if (convar != null) convar.StringValue = "";
            convar = ConVar.Find("mp_ct_default_secondary");
            if (convar != null) convar.StringValue = "";   */
            for (int i = 0; i <= Server.MaxPlayers; i++)
            {
                var client = playerManager.FindBySlot(i);
                if (client != null)
                {
                    client.SetLevel(1);
                    UpdatePlayerScoreLevel(client.Slot);
                }
            }
            if (!warmupInitialized && Config.WarmupEnabled && warmupTimer == null && !GGVariables.Instance.WarmupFinished)
            {
                StartWarmupRound();
                return HookResult.Continue;
            }
            if ( (Config.WarmupTimeLength - WarmupCounter) > 1 )
            {
                return HookResult.Continue;
            }
            if (GGVariables.Instance.GameWinner != null) {
            /* Lock all player since the winner was declare already if new round happened. */
                if (Config.WinnerFreezePlayers) {
                    FreezeAllPlayers();
                }
            }
            GGVariables.Instance.RoundStarted = true;

            /* Only remove the hostages on after it been initialized */
/*            if(GGVariables.Instance.MapStatus.HasFlag(Objectives.Hostage) && GGVariables.Instance.MapStatus.HasFlag(Objectives.RemoveHostage))
            {
                //Delay for 0.1 because data need to be filled for hostage entity index 
                Logger.Instance.Log("Map requires remove hostages");
                AddTimer(1.0f, RemoveHostages);
            } */

            PlaySoundForLeaderLevel();

            // Disable warmup
/*            if ( Config.WarmupEnabled && GGVariables.Instance.DisableWarmupOnRoundEnd )
            {
                Config.WarmupEnabled = false;
                GGVariables.Instance.DisableWarmupOnRoundEnd = false;
            } */
//            RemoveEntityByClassName("game_player_equip");
            Console.WriteLine("[GUNGAME]******** Event Round Started");
            return HookResult.Continue;
        }
        private HookResult EventRoundEndHandler<T>(T @event, GameEventInfo info) where T : GameEvent
        {
            /* Round has ended. */
            GGVariables.Instance.RoundStarted = false;
            Hot_Reload = false;
            return HookResult.Continue;
        }
        private HookResult EventHegrenadeDetonateHandler(EventHegrenadeDetonate @event, GameEventInfo info)
        {
            var playerController = @event.Userid;    
            if (!IsValid(playerController))
            {
                Console.WriteLine($"EventHegrenadeDetonateHandler - Not Valid playerController");
                return HookResult.Continue;
            }
            var player = playerManager.GetPlayer(playerController, "EventHegrenadeDetonateHandler"); 

            if (player == null) {
                Console.WriteLine($"EventHegrenadeDetonateHandler - player is null");
                return HookResult.Continue;
            }
            if ( ( Config.WarmupNades && warmupInitialized )
                || (player.LevelWeapon != null && player.LevelWeapon.LevelIndex == SpecialWeapon.HegrenadeLevelIndex
                    && ( Config.UnlimitedNades 
                    || ( Config.NumberOfNades > 0 && player.NumberOfNades > 0 ) ) ) )
            {  // Do not give them another nade if they already have one   
                if (!HasHegrenade(playerController)) 
                {
                    if ( Config.NumberOfNades > 0) {
                        player.NumberOfNades--;
                    }
                    Weapon? he = GGVariables.Instance.weaponsList.FirstOrDefault(w => w.Name == "hegrenade");
                    if (he == null) {
                        return HookResult.Continue;
                    }
                    playerController.GiveNamedItem(he.FullName);
                }
            }
            return HookResult.Continue;
        }
        private static HookResult EventBombHandler<T>(T @event, GameEventInfo info) where T : GameEvent
        {
            /****************************************************/
            return HookResult.Continue;
        }
        private HookResult EventBombPickupHandler(EventBombPickup @event, GameEventInfo info)
        {
/*            if (GGVariables.Instance.IsActive && GGVariables.Instance.MapStatus.HasFlag(Objectives.RemoveBomb) )
            {
                var playerController = @event.Userid;    
                if (utils.IsValid(playerController) && playerController.PawnIsAlive)
                {
                    utils.ForceDropC4(playerManager.GetOrCreatePlayer(playerController));
                }
            } */
            return HookResult.Continue;
        }
        private HookResult EventItemPickupHandler(EventItemPickup @event, GameEventInfo info)
        {
            if (!GGVariables.Instance.IsActive)
            {
                return HookResult.Continue;
            }
            if (!@event.Userid.IsValid)
            {
                return HookResult.Continue;
            }

            CCSPlayerController playerController = @event.Userid;

            if (playerController.Connected != PlayerConnectedState.PlayerConnected)
            {
                return HookResult.Continue;
            }
            if (!playerController.PlayerPawn.IsValid)
            {
                return HookResult.Continue;
            }
            
            return HookResult.Continue;

/*            if (wep.WeaponName != null && wep.WeaponName.EndsWith("hegrenade")) {
                return HookResult.Continue;
            }
            return HookResult.Handled; */
/*
            if (Config.KnifeElite) 
            {
                var player = playerManager.GetOrCreatePlayer(playerController);
                if (player != null && player.State.HasFlag(PlayerStates.KnifeElite))
                {
                    playerController.RemoveWeapons();
                    playerController.GiveNamedItem("weapon_knife");
                }
            }
            return HookResult.Continue; */
        }
        private HookResult EventHostageKilledHandler<T>(T @event, GameEventInfo info) where T : GameEvent
        {
            /****************************************************/
            return HookResult.Continue;
        }


/************** Utils **********************************************************/
/************** Utils **********************************************************/
/************** Utils **********************************************************/
        private static bool IsValid(CCSPlayerController? player)
        {
            if (player == null || !player.IsValid
            || player.PlayerPawn == null || !player.PlayerPawn.IsValid)
            {
                return false;
            }
            return true;
        }
        private static bool IsPlayer(int index)
        {
            return index > -1 && index <= Server.MaxPlayers;
        }
        private static bool IsClientInTeam (CCSPlayerController player)
        {
            if (player.TeamNum == 2 || player.TeamNum == 3) {
                return true;
            }
            else {
                return false;
            }
        }
        private void GiveWarmUpWeaponDelayed (float delay, int slot)
        {
            AddTimer(delay, () =>
            {
                CCSPlayerController playerController = Utilities.GetPlayerFromSlot(slot); // index is captured from the local variable
                
                if (IsValid(playerController) && playerController.Pawn != null && 
                playerController.Pawn.Value != null && playerController.Pawn.Value.LifeState == (byte)LifeState_t.LIFE_ALIVE ) 
                {
                    var player = playerManager.FindBySlot(slot);
                    if (player == null) {
                        Console.WriteLine($"[ERROR] Timer_GiveWarmUpWeapon: cant get player for slot {slot}");
                        return;
                    } 
                    if (Config.WarmupRandomWeaponMode > 0) 
                    {   
                        player.SetLevel(random.Next(1,GGVariables.Instance.WeaponOrderCount));
                        GiveNextWeapon(slot);
                        return;
                    }
                    bool nades = Config.WarmupNades;
                    bool wpn = false;
                    if (Config.WarmupWeapon.Length > 0 )
                    {
                        Weapon? weapon = GGVariables.Instance.weaponsList.FirstOrDefault(w => w.Name.Equals(Config.WarmupWeapon));
                        if (weapon != null && !weapon.Name.Contains("knife", StringComparison.OrdinalIgnoreCase) )
                        {
                            wpn = true;
                        }
                    }
                    playerController.RemoveWeapons();
                    if ( Config.ArmorKevlarHelmet ) playerController.GiveNamedItem("item_assaultsuit");
                    playerController.GiveNamedItem("weapon_knife");

                    if (nades) {
                        playerController.GiveNamedItem("weapon_hegrenade");
                    }
                    
                    if (wpn) {
                        playerController.GiveNamedItem("weapon_" + Config.WarmupWeapon);
                    }

                    if (!nades && !wpn) {
                        player.UseWeapon(3); // give knife
                    }
                }
            });
        }
        private void GiveNextWeapon(int slot, bool levelupWithKnife = false, bool spawn = false)
        {
            var playerController = Utilities.GetPlayerFromSlot(slot);
            if (!IsValid(playerController) || !IsClientInTeam(playerController) || 
                playerController.Pawn == null || playerController.Pawn.Value == null ||
                playerController.Pawn.Value.LifeState != (byte)LifeState_t.LIFE_ALIVE ) 
            {
                return;
            }
            playerController.RemoveWeapons();
            if ( Config.ArmorKevlarHelmet ) playerController.GiveNamedItem("item_assaultsuit");
            bool dropKnife;
            var player = playerManager.FindBySlot(slot);
            if (player == null) {
                Console.WriteLine($"[ERROR] GiveNextWeapon: cant get player for slot {slot}");
                return;
            } 
            if (player.LevelWeapon.LevelIndex == SpecialWeapon.Drop_knife) {
                dropKnife = true;
            }
            else {
                dropKnife = false;
            }
//            bool blockSwitch = Plugin.Config.BlockWeaponSwitchIfKnife && levelupWithKnife && !dropKnife;
//            int newWeapon = -1;
//            if (blockSwitch) {
//                player.BlockSwitch = true;
//            }
            CheckForFriendlyFire(player);

            if (player.LevelWeapon.LevelIndex != SpecialWeapon.Drop_knife) {
                playerController.GiveNamedItem("weapon_knife");
            }

/*            if (player.State.HasFlag(PlayerStates.KnifeElite)) { // FIXME: when do we call UTIL_GiveNextWeapon with KNIFE_ELITE flag set in PlayerState?
                if (blockSwitch) {
                    player.BlockSwitch = false;
                } else {
                    player.UseWeapon(3); //knife slot
                }
                return;
            } */

            if (player.LevelWeapon.Slot == 4)  // grenades slot
            { 
                if (player.LevelWeapon.LevelIndex == SpecialWeapon.HegrenadeLevelIndex) {
                    // BONUS WEAPONS FOR HEGRENADE
                    if (Config.NumberOfNades > 0) {
                        player.NumberOfNades = Config.NumberOfNades - 1;
                    }
                    if (Config.NadeBonusWeapon.Length > 0) // на гранате дают оружие
                    {
                        playerController.GiveNamedItem("weapon_" + Config.NadeBonusWeapon);
//                        int ent = GivePlayerItemWrapper(player, "weapon_" + Plugin.Config.NadeBonusWeapon);
/* *******************  we don’t use it, then add it - remove additional ammunition from weapons

                        // Remove bonus weapon ammo! So player can not reload weapon!
                        if ( (ent != -1) && RemoveBonusWeaponAmmo ) {
                            new iAmmo = UTIL_GetAmmoType(ent); // TODO: not needed
            
                            if ((iAmmo != -1) && (ent != INVALID_ENT_REFERENCE)) {
                                new Handle:Info = CreateDataPack();
                                WritePackCell(Info, client);
                                WritePackCell(Info, ent);
                                ResetPack(Info);
            
                                CreateTimer(0.1, UTIL_DelayAmmoRemove, Info, TIMER_HNDL_CLOSE);
                            }
                        } */
                    }
                    if (Config.NadeSmoke) {
                        playerController.GiveNamedItem("weapon_smokegrenade");
//                        GivePlayerItemWrapper(player, "weapon_smokegrenade", !player.BlockSwitch);
                    }
                    if (Config.NadeFlash) {
                        playerController.GiveNamedItem("weapon_flashbang");
//                        GivePlayerItemWrapper(player, "weapon_flashbang", !player.BlockSwitch);
                    }
                } 
                else if (player.LevelWeapon.LevelIndex == SpecialWeapon.MolotovLevelIndex) 
                {
                    // BONUS WEAPONS FOR MOLOTOV
                    if (Config.MolotovBonusWeapon.Length > 0) {
                        playerController.GiveNamedItem("weapon_" + Config.MolotovBonusWeapon);
//                        int ent = GivePlayerItemWrapper(player, "weapon_" + Plugin.Config.MolotovBonusWeapon);
/* *******************  we don’t use it, then add it - remove additional ammunition from weapons                       
                        // Remove bonus weapon ammo! So player can not reload weapon!
                        if ( (ent != -1) && RemoveBonusWeaponAmmo ) {
                            new iAmmo = UTIL_GetAmmoType(ent); // TODO: not needed
            
                            if ((iAmmo != -1) && (ent != INVALID_ENT_REFERENCE)) {
                                new Handle:Info = CreateDataPack();
                                WritePackCell(Info, client);
                                WritePackCell(Info, ent);
                                ResetPack(Info);
            
                                CreateTimer(0.1, UTIL_DelayAmmoRemove, Info, TIMER_HNDL_CLOSE);
                            }
                        } */
                    }
                    if (Config.MolotovBonusSmoke) {
                        playerController.GiveNamedItem("weapon_smokegrenade");
//                        GivePlayerItemWrapper(player, "weapon_smokegrenade", !player.BlockSwitch);
                    }
                    if (Config.MolotovBonusFlash) {
                        playerController.GiveNamedItem("weapon_flashbang");
//                        GivePlayerItemWrapper(player, "weapon_flashbang", !player.BlockSwitch);
                    }
                }
            }

            if (player.LevelWeapon.Slot == 3)  // knife slot
            {
                if (player.LevelWeapon.LevelIndex == SpecialWeapon.KnifeLevelIndex) 
                {
                    // BONUS WEAPONS FOR KNIFE
                    if (Config.KnifeSmoke) {
                        playerController.GiveNamedItem("weapon_smokegrenade");
//                        GivePlayerItemWrapper(player, "weapon_smokegrenade", !player.BlockSwitch);
                    }
                    if (Config.KnifeFlash) {
                        playerController.GiveNamedItem("weapon_flashbang");
//                        GivePlayerItemWrapper(player, "weapon_flashbang", !player.BlockSwitch);
                    }
                    if (dropKnife) {
                        // LEVEL WEAPON KNIFEGG
//                        playerController.GiveNamedItem(player.LevelWeapon.FullName);
//                        if (player.LevelWeapon.FullName.Equals("weapon_knifegg"))
//                        {
//                            playerController.GiveNamedItem("weapon_knife");
//                        }
                        player.UseWeapon(3);
                        // Change Color of Knife to Gold. - it doesn't work now
                        if (playerController.PlayerPawn != null && playerController.PlayerPawn.Value != null 
                            && playerController.PlayerPawn.Value.WeaponServices != null 
                            && playerController.PlayerPawn.Value.WeaponServices.ActiveWeapon != null)
                        {
                            var activeWeapon = playerController.PlayerPawn.Value.WeaponServices?.ActiveWeapon.Value;
                            if (activeWeapon != null)
                            {
                                activeWeapon.Render = Color.FromArgb(255, 223, 0);
                            }
                        }
                    }
                } 
                else 
                {
                    // LEVEL WEAPON TASER
                    // this is, for example, TASER (csgo)
                    if (Config.TaserSmoke) {
                        playerController.GiveNamedItem("weapon_smokegrenade");
//                        GivePlayerItemWrapper(player, "weapon_smokegrenade", !player.BlockSwitch);
                    }
                    if (Config.TaserFlash) {
                        playerController.GiveNamedItem("weapon_flashbang");
//                        GivePlayerItemWrapper(player, "weapon_flashbang", !player.BlockSwitch);
                    }
                    playerController.GiveNamedItem(player.LevelWeapon.FullName);
                }
                player.UseWeapon(3);
            } 
            else
            {
                // LEVEL WEAPON PRIMARY/SECONDARY
                /* Give new weapon */
                playerController.GiveNamedItem(player.LevelWeapon.FullName);
            }

/*            if (blockSwitch) {
                player.BlockSwitch = false;
            } else 
            {
                player.UseWeapon(1); //
                FastSwitchWithCheck(player, newWeapon, true, player.LevelWeapon.LevelIndex);
            } */
        }
        private void GiveExtraNade(CCSPlayerController player)
        {
            if ( Config.ExtraNade) 
            {
                /* Do not give them another nade if they already have one */
                if (!HasHegrenade(player)) 
                {
                    player.GiveNamedItem("weapon_hegrenade");
/*                    GivePlayerItemWrapper(player, "weapon_hegrenade", Plugin.Config.BlockWeaponSwitchOnNade);
  Here about the switching lock. Let's put it aside for now
                    if (!blockWeapSwitch) {
                        UTIL_UseWeapon(client, g_WeaponIdHegrenade);
                        UTIL_FastSwitchWithCheck(client, newWeapon, true, g_WeaponIdHegrenade);
                    } */
                }
            }
        }
        private void GiveExtraTaser (CCSPlayerController player)
        {
            // For now we'll get by without Taser
        }
        private void GiveExtraMolotov (CCSPlayerController player, int weapon_index)
        {
            // and without molotov
        }
        private void ReloadActiveWeapon (GGPlayer player, int weapon_index)
        {
            // pass from WeaponInfo. Can I take the whole class? Then I'll do it
/*            new Slots:slot = g_WeaponSlot[WeaponId];
            if ((slot == Slot_Primary )
                || (slot == Slot_Secondary)
                || (g_WeaponLevelIndex[WeaponId] == g_WeaponLevelIdTaser)
            ) {
                new ent = GetEntPropEnt(client, Prop_Send, "m_hActiveWeapon");
                if ((ent > -1) && g_WeaponAmmo[WeaponId]) {
                    SetEntProp(ent, Prop_Send, "m_iClip1", g_WeaponAmmo[WeaponId] + (g_GameName==GameName:Csgo?1:0)); // "+1" is needed because ammo is refilling before last shot is counted
                }
            } */
        }
        private void CheckForFriendlyFire(GGPlayer player)
        {
            if ( !Config.AutoFriendlyFire )
            {
                return;
            }
            var pState = player.State;
            if ( pState.HasFlag(PlayerStates.GrenadeLevel)  && player.LevelWeapon != null && player.LevelWeapon.LevelIndex != SpecialWeapon.HegrenadeLevelIndex )
            {
                player.State &= ~PlayerStates.GrenadeLevel;

                if ( --GGVariables.Instance.PlayerOnGrenade < 1 )
                {
                    GGVariables.Instance.PlayerOnGrenade = 0;
                    if ( Config.FriendlyFireOnOff ) { 
                        ChangeFriendlyFire(false);
                    } 
                    else {
                        ChangeFriendlyFire(true); 
                    }
                }
                return;
            }
            if ( (!pState.HasFlag(PlayerStates.GrenadeLevel)) && player.LevelWeapon != null && player.LevelWeapon.LevelIndex == SpecialWeapon.HegrenadeLevelIndex)
            {
                GGVariables.Instance.PlayerOnGrenade++;
                player.State |= PlayerStates.GrenadeLevel;

                if (GGVariables.Instance.mp_friendlyfire == null)
                {
                    Console.WriteLine("GGVariables.Instance.mp_friendlyfire == null");
                    return;
                }

                if ( !GGVariables.Instance.mp_friendlyfire.GetPrimitiveValue<bool>() )
                {
                    if ( Config.FriendlyFireOnOff ) {
                        ChangeFriendlyFire(true);
                    } 
                    else {
                        ChangeFriendlyFire(false);
                    }
                }
                return;
            }
        }
        private void UpdatePlayerScoreLevel (int slot)
        {
            if ( warmupInitialized )
            {
                return;
            }
            if ( Config.LevelsInScoreboard )
            {
                SetClientScoreAndDeaths(slot);
            }
        }
        private static void SetClientScoreAndDeaths (int player)
        {
            // then we'll learn
        }
        private static void FreezeAllPlayers()
        {
            if (GGVariables.Instance.mp_friendlyfire != null)
            {
                GGVariables.Instance.mp_friendlyfire.SetValue(false);
            }
            
            var playerEntities = Utilities.GetPlayers();
            if (playerEntities != null && playerEntities.Count > 0)
            {
                foreach (var playerController in playerEntities)
                {
                    FreezePlayer(playerController);
                }
            }
        }
        private static void FreezePlayer (CCSPlayerController player)
        {
            if (player.IsValid && player.PlayerPawn != null && player.PlayerPawn.Value != null
            && (player.Connected == PlayerConnectedState.PlayerConnected) && (player.TeamNum > 1) && !player.IsHLTV)
            {
                player.PlayerPawn.Value.MoveType = MoveType_t.MOVETYPE_NONE;
                player.RemoveWeapons();
                player.GiveNamedItem("weapon_knife");
            } 
        }
        private void CheckForTripleLevel(GGPlayer client)
        {
            client.CurrentLevelPerRoundTriple++;
            if ( Config.MultiLevelBonus && client.CurrentLevelPerRoundTriple == Config.MultiLevelAmount )
            {
                Server.PrintToChatAll(Localizer["player.leveled", client.PlayerName, Config.MultiLevelAmount]);
/*
                UTIL_StartTripleEffects(client);
                CreateTimer(10.0, RemoveBonus, client);

                Call_StartForward(FwdTripleLevel);
                Call_PushCell(client);
                Call_Finish(); */
            }
        }
        public void PlaySoundDelayed(float delay, CCSPlayerController player, string str)
        {
            AddTimer(delay, () => PlaySound(player, str));
        }
        public void PlaySound(CCSPlayerController player, string str)
        {
            if ( IsValid(player)) {
                if (!player.IsBot)
                    NativeAPI.IssueClientCommand (player.Slot, "play " + str);
//                player.ExecuteClientCommand("play " + str);
                return;
            }
            else
            {
                var playerEntities = Utilities.GetPlayers().Where(p => p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV);
                if (playerEntities != null && playerEntities.Count() > 0)
                {
                    foreach (var playerController in playerEntities)
                    {
                        NativeAPI.IssueClientCommand (playerController.Slot, "play " + str);
//                        playerController.ExecuteClientCommand("play " + str);
                    }
                }
            }
        }
        public void PlayRandomSoundDelayed(float delay, List<string> soundList)
        {
            AddTimer(delay, () => PlayRandomSound(soundList));
        }
        public void PlayRandomSound (List<string> soundList)
        {
            if (soundList == null || soundList.Count == 0)
            {
                Console.WriteLine("The sound list is empty or null.");
                return;
            }
            int index = random.Next(soundList.Count);
            var playerEntities = Utilities.GetPlayers().Where(p => p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Count() > 0)
            {
                foreach (var playerController in playerEntities)
                {
                    NativeAPI.IssueClientCommand (playerController.Slot, "play " + soundList[index]);
//                    playerController.ExecuteClientCommand("play " + soundList[index]);
                }
            }
        }
        public void PlaySoundForLeaderLevel()
        {
            if (GGVariables.Instance.CurrentLeader == null ) {
                return;
            }
            Weapon? wep = GGVariables.Instance.weaponsList.FirstOrDefault(w => w.Level == GGVariables.Instance.CurrentLeader.Level);
            if (wep != null && wep.LevelIndex == SpecialWeapon.HegrenadeLevelIndex)
            {
                PlaySoundDelayed(1.0f, null!, Config.NadeInfoSound);
                return;
            }
            if (wep != null &&  wep.LevelIndex == SpecialWeapon.KnifeLevelIndex) {
                PlaySoundDelayed(1.0f, null!, Config.KnifeInfoSound);
                return;
            }
        }
        private void StopTripleEffects (GGPlayer player)
        {
            if ( !player.TripleEffects ) {
                return;
            }
            player.TripleEffects = false;
            if ( Config.MultiLevelBonusGodMode ) {
                SetClientGodMode(player, false); // false - dont get damages
            }
            var playerController = Utilities.GetPlayerFromIndex(player.Index);
            if (playerController != null && IsValid(playerController)) 
            {
/*                if ( Config.MultiLevelBonusGravity > 0 ) {
                    playerController.PlayerPawn.Value.GravityScale = 1.0f;
                }
                if ( Config.MultiLevelBonusSpeed > 0) {
                    playerController.PlayerPawn.Value.Speed = 1.0f;
                } */
            }
//           PlaySound(null, "Triple"); //, client, true);
            if ( Config.MultiLevelEffect ) {
                StopEffectClient(player.Index);
            }
        }
        private static void StopEffectClient (int player)
        {
            // Later
/*            if ( g_Ent_Effect[client] < 0 ) {
                return;
            }
            if ( IsValidEdict(g_Ent_Effect[client]) ) {
                if ( g_Cfg_MultilevelEffectType == 1 ) {
                    UTIL_StopMultilevelEffect1(client);
                } else {
                    UTIL_StopMultilevelEffect2(client);
                }
            }
            g_Ent_Effect[client] = -1; */
        }
        private static void SetClientGodMode (GGPlayer player, bool getDamages = false)
        {
            // we dont use it, later
        }
        private bool HasHegrenade(CCSPlayerController playerController)
        {
            bool found = false;
            if (playerController != null && playerController.IsValid 
                && playerController.PlayerPawn != null && playerController.PlayerPawn.Value != null
                &&  playerController.PlayerPawn.Value.WeaponServices != null)
            {
                foreach (var clientWeapon in playerController.PlayerPawn.Value.WeaponServices.MyWeapons)
                {
                    if (clientWeapon is { IsValid: true, Value.IsValid: true })
                    {
                        if (clientWeapon.Value.DesignerName.Equals("weapon_hegrenade"))
                        {
                            found = true;
                            break;
                        }
                    }
                }
            }
            return found;
        }
        private static bool HasKnife(CCSPlayerController playerController)
        {
            bool found = false;
            if (playerController != null && playerController.IsValid 
                && playerController.PlayerPawn != null && playerController.PlayerPawn.Value != null
                &&  playerController.PlayerPawn.Value.WeaponServices != null)
            {
                foreach (var clientWeapon in playerController.PlayerPawn.Value.WeaponServices.MyWeapons)
                {
                    if (clientWeapon is { IsValid: true, Value.IsValid: true })
                    {
                        if (clientWeapon.Value.DesignerName.Contains("bayonet") || clientWeapon.Value.DesignerName.Contains("knife"))
                        {
                            found = true;
                            break;
                        }
                    }
                }
            }
            return found;
        }
        private static bool IsWeaponKnife(string weapon)
        {
            return weapon.Contains("bayonet") || weapon.Contains("knife");
        }
        private void RecalculateLeader (int slot, int oldLevel, int newLevel= 0)
        {
            if ( newLevel == oldLevel ) {
                return;
            }
            if ( newLevel < oldLevel )
            {
                if ( GGVariables.Instance.CurrentLeader.Slot < 0 ) {
                    return;
                }
                if ( slot == GGVariables.Instance.CurrentLeader.Slot )
                {
                    // was the leader
                    GGVariables.Instance.CurrentLeader.Slot = FindLeader();
                    if ( GGVariables.Instance.CurrentLeader.Slot != slot )
                    {
                        PlaySoundForLeaderLevel();
                    }
                    return;
                }
                return;  // was not a leader
            }       
            if (GGVariables.Instance.CurrentLeader.Slot < 0)  // newLevel > oldLevel
            {
                GGVariables.Instance.CurrentLeader.Slot = slot;
                GGVariables.Instance.CurrentLeader.Level = (uint) newLevel;
                PlaySoundForLeaderLevel();
                return;
            }
            if ( GGVariables.Instance.CurrentLeader.Slot == slot ) // still leading
            {
                PlaySoundForLeaderLevel();
                return;
            }
            if ( newLevel < GGVariables.Instance.CurrentLeader.Level ) // CurrentLeader != client
            {
                // not leading
                return;
            }
            if ( newLevel > GGVariables.Instance.CurrentLeader.Level )
            {
                GGVariables.Instance.CurrentLeader.Slot = slot;
                GGVariables.Instance.CurrentLeader.Level = (uint) newLevel;
                PlaySoundForLeaderLevel(); // start leading
                return;
            }
            // new level == leader level // tied to the lead
            PlaySoundForLeaderLevel();
        }
        private int FindLeader()
        {
            int leaderId = -1;
            int leaderLevel = 0;
            int currentLevel;
            var playerEntities = Utilities.GetPlayers().ValidOnly();
            if (playerEntities.Count() > 0)
            {
                foreach (var playerController in playerEntities)
                {
                    if (playerController.UserId < 0 || playerController.UserId > Server.MaxPlayers)
                    {
                        continue;
                    }
                    var player = playerManager.FindByIndex((int)playerController.Index);

                    if (player == null)
                    {
                        continue;
                    }
                    currentLevel = (int) player.Level;

                    if ( currentLevel > leaderLevel )
                    {
                        leaderLevel = currentLevel;
                        leaderId = player.Index;
                    }
                }
            }
            return leaderId;
        }
        private void FindMapObjective()
        {
/*            var Zones = Utilities.FindAllEntitiesByDesignerName<CBombTarget>("func_bomb_target");

            //Loop through each zone in the buyZone
            foreach(var zone in Zones) {
                //Check to see if entity is valid.
                if(zone.IsValid) {
                    //Delete the buyzone.
                    zone.Remove();
                    Logger.Instance.Log("Found the func_bomb_target");
                }
            }
            Zones = Utilities.FindAllEntitiesByDesignerName<CBombTarget>("info_bomb_target");

            //Loop through each zone in the buyZone
            foreach(var zone in Zones) {
                //Check to see if entity is valid.
                if(zone.IsValid) {
                    //Delete the buyzone.
                    zone.Remove();
                    Logger.Instance.Log("Found the func_bomb_target");
                }
            } */
        }
        private void ChangeFriendlyFire (bool Status)
        {
            if (GGVariables.Instance.mp_friendlyfire == null)
                return;
            GGVariables.Instance.mp_friendlyfire.Public = true; // set FCVAR_NOTIFY
            GGVariables.Instance.mp_friendlyfire.SetValue(Status);
            if (Status) {
                Server.PrintToChatAll(Localizer["friendlyfire.on"]);
            } else {
                Server.PrintToChatAll(Localizer["friendlyfire.off"]);
            }
            PlaySound(null!, Config.FriendlyFireInfoSound);
        }
        private void Timer_HandicapUpdate()
        {
            if ( warmupInitialized || Config.HandicapMode == 0 )
            {
                return;
            }

            // get very minimum level
            int minimum = GetHandicapMinimumLevel(Config.HandicapSkipBots);
            if ( minimum == -1 ) {
                return;
            }
            // get handicap level for players above very minimum level
            int level = GetHandicapLevel(-1, minimum);
            if ( level <= minimum ) {
                return;
            }
            var playerEntities = Utilities.GetPlayers().Where(p => p.Connected == PlayerConnectedState.PlayerConnected && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Count() > 0)
            {
                foreach (var playerController in playerEntities)
                {
                    var player = playerManager.FindBySlot(playerController.Slot); 
                    if (player != null && playerController.TeamNum > 0 && player.Level == minimum)
                    {
                        if ( Config.HandicapSkipBots && playerController.IsBot ) {
                            continue;
                        }
                        if ( !playerController.IsBot
                            && !Config.TopRankHandicap 
                            && GGVariables.Instance.StatsEnabled 
                            && ( !IsPlayerWinsLoaded(player) //* HINT: gungame_stats
                                || IsPlayerInTopRank(player) ) //* HINT: gungame_stats
                        )
                        {
                            continue;
                        }
                        player.SetLevel(level);
                        player.CurrentKillsPerWeap = 0;
                        if (!playerController.IsBot) playerController.PrintToChat(Localizer["handicap.updated"]);
                        if ( Config.TurboMode && playerController != null && playerController.Pawn != null 
                            && playerController.Pawn.Value != null 
                            && playerController.Pawn.Value.LifeState == (byte)LifeState_t.LIFE_ALIVE)
                        {
                            GiveNextWeapon(player.Slot);
                        }
                        UpdatePlayerScoreLevel(player.Slot);
                    }
                }
            }
        }
        private int GetHandicapMinimumLevel (bool skipBots = false, int aboveLevel = -1, int skipClient = -1)
        {
            int minimum = -1;
            int level = 0;
            var playerEntities = Utilities.GetPlayers().Where(p => p.Connected == PlayerConnectedState.PlayerConnected && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Count() > 0)
            {
                foreach (var playerController in playerEntities)
                {
                    if ( playerController.TeamNum > 0 && ( Config.HandicapUseSpectators || playerController.TeamNum > 1 ))
                    {
                        if ( ( skipBots && playerController.IsBot ) || ( skipClient == playerController.Slot ) )
                        {
                            continue;
                        }
                        var player = playerManager.FindBySlot(playerController.Slot);
                        if (player != null)
                        {
                            level = (int)player.Level;
                            if ( aboveLevel >= level ) {
                                continue;
                            }
                            if ( (minimum == -1) || (level < minimum) )
                            {                 
                                minimum = level;
                            }
                        }
                    
                    }
                }
            }
            return minimum;
        }
        private int GetHandicapLevel ( int skipClient = -1, int aboveLevel = -1)
        {
            int level = 0;
            if ( Config.HandicapMode == 1 ) {
                level = GetAverageLevel(Config.HandicapSkipBots, aboveLevel, skipClient);
            } else if ( Config.HandicapMode == 2 ) {
                level = GetHandicapMinimumLevel(Config.HandicapSkipBots, aboveLevel, skipClient);
            }
            if ( level == -1 ) {
                return 0;
            }
            level -= Config.HandicapLevelSubstract;
            if ( Config.MaxHandicapLevel > 0 && Config.MaxHandicapLevel < level ) {
                level = Config.MaxHandicapLevel;
            }
            if ( level < 1 ) {
                return 0;
            }
            return level;
        }
        private int GetAverageLevel(bool skipBots = false, int aboveLevel = -1, int skipClient = -1)
        {
            int count = 0, level = 0, tmpLevel;
            var playerEntities = Utilities.GetPlayers().Where(p => p.Connected == PlayerConnectedState.PlayerConnected && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Count() > 0)
            {
                foreach (var playerController in playerEntities)
                {
                    if ( playerController.TeamNum > 0 && ( Config.HandicapUseSpectators || playerController.TeamNum > 1 ))
                    {
                        if ( ( skipBots && playerController.IsBot ) || ( skipClient == playerController.Slot ) )
                        {
                            continue;
                        }
                        var player = playerManager.FindBySlot(playerController.Slot);
                        if (player != null)
                        {
                            tmpLevel = (int) player.Level;
                            if ( aboveLevel >= tmpLevel ) {
                                continue;
                            }
                            level += tmpLevel;
                            count++;
                        }
                    }
                }
            }
            if ( count == 0) {
                return -1;
            }
            double average = level / count;
            return (int)Math.Floor(average);;
        }
        private bool SetHandicapForClient (GGPlayer player, int first = 0) // if it’s the first time, then 1, so that toprank is not taken into account)
        {

            if ( Config.HandicapTimesPerMap > 0)
            {
                if ( !PlayerHandicapTimes.TryGetValue(player.SavedSteamID, out int handicapTimes) )
                {
                    handicapTimes = 0;
                }

                if ( handicapTimes >= Config.HandicapTimesPerMap ) {
                    return false;
                }

                handicapTimes++;
                PlayerHandicapTimes[player.SavedSteamID] = handicapTimes;
            } 
            
            return GiveHandicapLevel(player, first);
        }
        private bool GiveHandicapLevel(GGPlayer player, int first = 0)
        {
            if ( Config.HandicapMode == 0) {
                return false;
            }

            if ( !player.IsBot
                 && ! Config.TopRankHandicap 
                 && GGVariables.Instance.StatsEnabled 
                 && ( !IsPlayerWinsLoaded(player) /* HINT: gungame_stats */
                    || IsPlayerInTopRank(player) ) /* HINT: gungame_stats */
            )
            {
                return false;
            }

            int level = GetHandicapLevel(player.Slot);
            if ( player.Level < level )
            {
                player.SetLevel(level);
                player.CurrentKillsPerWeap = 0;
                UpdatePlayerScoreLevel(player.Slot);
            }
            return true;
        }
        private bool IsPlayerWinsLoaded(GGPlayer player)
        {
            return false;
        }
        private bool IsPlayerInTopRank(GGPlayer player)
        {
            return false;
        }
        private int GetCustomKillPerLevel (int level)
        {
            if (GGVariables.Instance.CustomKillsPerLevel.TryGetValue(level, out int kills))
            {
                return kills;
            }
            return Config.MinKillsPerLevel;
        }
        private void ClientSuicide (GGPlayer player, int loose) // how many levels to "loose"
        {
            int oldLevel = (int) player.Level;
            int newLevel = ChangeLevel(player, -loose);
            if ( oldLevel == newLevel ) {
                return;
            }
            var playerEntities = Utilities.GetPlayers();
            if (playerEntities != null && playerEntities.Count > 0)
            {
                foreach (var playerController in playerEntities)
                {
                    if ( playerController.IsValid && !playerController.IsBot 
                        && !playerController.IsHLTV && IsClientInTeam(playerController) )
                    {
                        if (loose > 1) {
                                playerController.PrintToChat(Localizer["suiside.levels", playerController.PlayerName, loose]);
                        }
                        else {
                                playerController.PrintToChat(Localizer["suiside.alevel", playerController.PlayerName]);
                        }
                    }
                }
            }
            PrintLeaderToChat(player, oldLevel, newLevel);
        }
        private void PrintLeaderToChat(GGPlayer player, int oldLevel, int newLevel)
        {
            if ( GGVariables.Instance.CurrentLeader.Slot != player.Slot || newLevel <= oldLevel )
            {
                return;
            }
            // newLevel > oldLevel
            if ( GGVariables.Instance.CurrentLeader.Slot == player.Slot )
            {
                // say leading on level X
                if ( Config.ShowLeaderWeapon && player.LevelWeapon != null) 
                {
                    Server.PrintToChatAll(Localizer["leading.onweapon", player.PlayerName, player.LevelWeapon.Name]);
                } else {
                    Server.PrintToChatAll(Localizer["leading.onlevel", player.PlayerName, newLevel]);
                }
                return;
            }
            // CurrentLeader != client
            if ( newLevel < GGVariables.Instance.CurrentLeader.Level )
            {
                CCSPlayerController playerController = Utilities.GetPlayerFromSlot(player.Slot);
                if (IsValid(playerController))
                {
                    // say how much to the lead
                    playerController.PrintToChat(Localizer["levels.behind", GGVariables.Instance.CurrentLeader.Level-newLevel]);
                    return;
                }
            }
            // new level == leader level
            // say tied to the lead on level X
            Server.PrintToChatAll(Localizer["tiedwith.leader", player.PlayerName, newLevel]);
        }
        private int ChangeLevel(GGPlayer player, int difference, bool KnifeSteal = false)
        {
            if ( difference == 0 || !GGVariables.Instance.IsActive || warmupInitialized || GGVariables.Instance.GameWinner != null )
            {
                return (int) player.Level;
            }
            
            int oldLevel = (int) player.Level; 
            int Level = oldLevel + difference;

            if ( Level < 1 ) {
                Level = 1;
            }
            if ( (!Config.BotCanWin) && player.IsBot && (Level > GGVariables.Instance.WeaponOrderCount) )
            {
                /* Bot can't win so just keep them at the last level */
                return oldLevel;
            }
            if ( !GGVariables.Instance.IsVotingCalled && Level > (GGVariables.Instance.WeaponOrderCount - Config.VoteLevelLessWeaponCount) )
            {
                GGVariables.Instance.IsVotingCalled = true;
                Server.ExecuteCommand("exec gungame/gungame.mapvote.cfg");
            }

            if ( Config.DisableRtvLevel > 0 && !GGVariables.Instance.isCalledDisableRtv && Level >= Config.DisableRtvLevel )
            {
                GGVariables.Instance.isCalledDisableRtv = true;
                Server.ExecuteCommand("exec gungame/gungame.disable_rtv.cfg");
            }
            
            if ( Config.EnableFriendlyFireLevel > 0 && !GGVariables.Instance.isCalledEnableFriendlyFire && Level >= Config.EnableFriendlyFireLevel  )
            {
                GGVariables.Instance.isCalledEnableFriendlyFire = true;
                if ( Config.FriendlyFireOnOff ) {
                    ChangeFriendlyFire(true);
                } else {
                    ChangeFriendlyFire(false);
                }
            } 
            
            if ( Level > GGVariables.Instance.WeaponOrderCount )
            {
                /* Winner Winner Winner. They won the prize of gaben plus a hat. */
                GGVariables.Instance.GameWinner = new(player);

                /*
                int r = (team == TEAM_T ? 255 : 0);
                int g =  team == TEAM_CT ? 128 : (team == TEAM_T ? 0 : 255);
                int b = (team == TEAM_CT ? 255 : 0);
                UTIL_PrintToUpperLeft(r, g, b, "%t", "Has won", Name); */

                var playerEntities = Utilities.GetPlayers();
                if (playerEntities != null && playerEntities.Count > 0)
                {
                    foreach (var playerController in playerEntities)
                    {
                        if (IsValid(playerController) && !playerController.IsBot)
                        {
                            playerController.PrintToCenterHtml(Localizer["winner.is", GGVariables.Instance.GameWinner.Name]);
                        }
                    }
                }

/*                Call_StartForward(FwdWinner);
                Call_PushCell(client);
                Call_PushString(WeaponOrderName[Level - 1]);
                Call_PushCell(victim);
                Call_Finish(); */

                if (Config.WinnerFreezePlayers) {
                    FreezeAllPlayers();
                }
                EndMultiplayerGameDelayed();

/* they're probably letting someone else celebrate their victory here
                new result;
                Call_StartForward(FwdSoundWinner);
                Call_PushCell(client);
                Call_Finish(result);

                if ( !result ) {
                    UTIL_PlaySoundDelayed(1.7, 0, Winner);
                } */
                PlayRandomSoundDelayed(1.7f, Config.WinnerSound);

                if ( Config.AlltalkOnWin )
                {
                    var sv_full_alltalk = ConVar.Find("sv_full_alltalk");
                    sv_full_alltalk?.SetValue(true);
                }
//                player.SetLevel(oldLevel);
                return oldLevel;
            }

            // Client got new level
            player.SetLevel(Level);
            if ( KnifeSteal && Config.KnifeProRecalcPoints && (oldLevel != Level) ) {
                player.CurrentKillsPerWeap = player.CurrentKillsPerWeap * GetCustomKillPerLevel(Level) / GetCustomKillPerLevel(oldLevel);
            } else {
                player.CurrentKillsPerWeap = 0;
            }
            var pc = Utilities.GetPlayerFromSlot(player.Slot);
            if ( IsValid(pc))
            {
                if ( difference < 0 )
                {
                    PlaySound(pc, Config.LevelDownSound);
                }
                else 
                {
                    if ( KnifeSteal )
                    {
                        PlaySound(pc, Config.LevelStealUpSound);
                    }
                    else
                    {
                        PlaySound(pc, Config.LevelUpSound);
                    }
                }
            }
            RecalculateLeader(player.Slot, oldLevel, Level);
//            UpdatePlayerScoreDelayed(player);

            return Level;
        }
        public void ForgiveShots(int client)
        {
            // Forgive player for attacking with a gun
            for (int vict = 0; vict <= Server.MaxPlayers; vict++)
                g_Shot[client, vict] = false;
            // Forgive players who attacked with a gun
            for (int attck = 0; attck <= Server.MaxPlayers; attck++)
                g_Shot[attck, client] = false;
        }
        private void EndMultiplayerGameDelayed()
        {
            if (Config.EndGameDelay > 0) {
                Console.WriteLine($"Call EndMultiplayerGame in {Config.EndGameDelay} seconds");
                endGameTimer??= AddTimer(1.0f, EndMultiplayerGame, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
                endGameCount = 0;
//                AddTimer(Config.EndGameDelay, EndMultiplayerGame, TimerFlags.STOP_ON_MAPCHANGE);
            }
            else {
                endGameCount = (int)Config.EndGameDelay;
                Logger.LogInformation($"Call EndMultiplayerGame now");
                EndMultiplayerGame();
            }
        }
        private void EndMultiplayerGame()
        {
            if ( ++endGameCount < Config.EndGameDelay )
            {   
                var seconds = Config.EndGameDelay - endGameCount;
                if (seconds < 6)
                {
                    var playerEntities = Utilities.GetPlayers().Where(p => p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV);
                    if (playerEntities != null && playerEntities.Count() > 0)
                    {
                        foreach (var playerController in playerEntities)
                        {
                            if (IsValid(playerController))
                            {
                                playerController.PrintToCenter(Localizer["mapend.left", seconds]);
                            }
                        }
                    }
                }
                return;
            }
            if (endGameTimer != null)
            {
                endGameTimer.Kill();
                endGameTimer = null;
            }
            if (Config.EndGameSilent) {
                Logger.LogInformation($"Call EndMultiplayerGameSilent now");
                EndMultiplayerGameSilent();
            } else {
                Logger.LogInformation($"Call EndMultiplayerGameNormal now");
                EndMultiplayerGameNormal();
            }
        }
        private void EndMultiplayerGameSilent ()
        {
            Console.WriteLine("EndMultiplayerGameSilent");
            var gameEnd = NativeAPI.CreateEvent("game_end", true);
            NativeAPI.FireEvent(gameEnd, false);
        }
        private void EndMultiplayerGameNormal ()
        {
            Logger.LogInformation("EndMultiplayerGameNormal");
            var mp_timelimit = ConVar.Find("mp_timelimit");
            var mp_fraglimit = ConVar.Find("mp_fraglimit");
            var mp_maxrounds = ConVar.Find("mp_maxrounds");
            var mp_winlimit = ConVar.Find("mp_winlimit");
            mp_timelimit?.SetValue(0f);
            mp_fraglimit?.SetValue(0);
            mp_maxrounds?.SetValue(0);
            mp_winlimit?.SetValue(0);
                

            var mp_ignore_round_win_conditions = ConVar.Find("mp_ignore_round_win_conditions");
            var mp_match_end_changelevel = ConVar.Find("mp_match_end_changelevel");
            
            mp_ignore_round_win_conditions?.SetValue(false);
            mp_match_end_changelevel?.SetValue(true); 

            CCSGameRules gRules = GetGameRules();
            if (gRules != null)
            {
                if (GGVariables.Instance.GameWinner != null && (CsTeam)GGVariables.Instance.GameWinner.TeamNum == CsTeam.Terrorist) {
                    gRules.TerminateRound(0.1f, RoundEndReason.TerroristsWin);
                } else {
                    gRules.TerminateRound(0.1f, RoundEndReason.CTsWin);
                }
            }
            else
            {
                Logger.LogError("gRules null");
            }
        }
        private static CCSGameRules GetGameRules()
        {
            return CounterStrikeSharp.API.Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;
        }
        public string RemoveWeaponPrefix(string input)
        {
            const string prefix = "weapon_";

            if (input.StartsWith(prefix))
            {
                return input.Substring(prefix.Length);
            }

            return input;
        }

        [ConsoleCommand("ggtest_dump", "Test system work")]
        public void OnCommand(CCSPlayerController? player, CommandInfo command)
        {
            Console.WriteLine($"GG Test command");
/*            if (GGVariables.Instance.MapStatus.HasFlag(Objectives.RemoveBomb))
            {
                Console.WriteLine($"Remove Bomb Objectives set");
            }
            if (GGVariables.Instance.MapStatus.HasFlag(Objectives.RemoveHostage))
            {
                Console.WriteLine($"Remove Hostage Objectives set");
            }
*/
            Console.WriteLine($"Weapons in settings {Weapon_from_List.Count}");

            Console.WriteLine($"Instance.WeaponOrderCount {GGVariables.Instance.WeaponOrderCount}");
            Console.WriteLine($"weaponList {GGVariables.Instance.weaponsList.Count}");
            
            var playerEntities = Utilities.GetPlayers();
            
            if (playerEntities != null && playerEntities.Count > 0)
            {
                Console.WriteLine("Players Online:");
                foreach (var playerController in playerEntities)
                {
                    
                    if (IsValid(playerController) && !playerController.IsBot)
                    {
                        Console.WriteLine($"{playerController.PlayerName} index {playerController.Index}");
                        for (int i = 0; i <= Server.MaxPlayers; i++)
                        {
                            if (g_Shot[playerController.Index,i])
                                Console.WriteLine($"{playerController.PlayerName} shot index {i}");
                        }
                    }
                }
            }
/*            GGPlayer? client;
            CCSPlayerController playerController;
            for (int i = 0; i <= Server.MaxPlayers; i++)
            {
                client = playerManager.FindBySlot(i);
                if (client != null)
                {
                    playerController = Utilities.GetPlayerFromSlot(i);

                    Console.WriteLine($"{i} Controller {playerController.PlayerName} index {playerController.Index} slot {playerController.UserId & 0xFF} team {playerController.TeamNum}");
                    Console.WriteLine($"{i} Player {client.PlayerName} index {client.Index} slot {client.Slot} team {client.GetTeam()}");
                }
            } */
        }
    }
    public static class extension
    {
        public static IEnumerable<CCSPlayerController> ValidOnly(this IEnumerable<CCSPlayerController> players)
        {
            return players.Where(p => p.IsValid);
        }
    }

    public class PlayerManager
    {
        public PlayerManager (GunGame plugin)
        {
            Plugin = plugin;
        }
        private GunGame Plugin;
        private readonly Dictionary<int, GGPlayer> playerMap = new();
        private readonly Dictionary<int, GGPlayer> playerMapIndex = new();
        public GGPlayer? CreatePlayerBySlot(int slot)
        {
            if (slot < 0 || slot > Server.MaxPlayers)
                return null;
            if (playerMap.ContainsKey(slot))  // Call from Connect, so delete if was before
            {
                playerMap.Remove(slot);
            }
            var newPlayer = new GGPlayer(slot);
            var playerController = Utilities.GetPlayerFromSlot(slot);
                
            if (playerController != null && playerController.IsValid)
            {
                newPlayer.UpdatePlayerController(playerController);
                if (playerMapIndex.ContainsKey((int)playerController.Index))
                {
                    playerMapIndex.Remove((int)playerController.Index);
                }
                playerMapIndex.Add((int)playerController.Index, newPlayer);
                if (Plugin.Config.ShootKnifeBlock)
                {
                    Plugin.ForgiveShots((int)playerController.Index);
                }
            }
            playerMap.Add(slot, newPlayer);
            return newPlayer;
        }
        public GGPlayer? GetOrCreatePlayerBySlot(int slot)
        {
            if (slot < 0 || slot > Server.MaxPlayers)
                return null;
            GGPlayer player;
            if (!playerMap.ContainsKey(slot))
            {
                var newPlayer = new GGPlayer(slot);
                // If the player doesn't exist, create a new one and store it
                playerMap.Add(slot, newPlayer);
                player = newPlayer;
            }
            else
            {
                player = playerMap[slot];
            }
            if (player.Index == -1)
            {
                var playerController = Utilities.GetPlayerFromSlot(slot);
                    
                if (playerController != null && playerController.IsValid)
                {
                    player.UpdatePlayerController(playerController);
                    playerMapIndex.Add((int)playerController.Index, player);
                    if (Plugin.Config.ShootKnifeBlock) {
                        Plugin.ForgiveShots((int)playerController.Index);
                    }
                }
            }
            return player;
        }
        public GGPlayer? GetPlayer(CCSPlayerController? playerController, string module = "")
        {
            if (playerController == null || !playerController.IsValid ) {
                return null;
            }
            if (playerMapIndex.ContainsKey((int)playerController.Index))
            {
                return playerMapIndex[(int)playerController.Index];
            }
            else
            {
                Console.WriteLine($"[GUNGAME] ************ Error: Call from {module}; Can't find player {playerController.PlayerName} in playerMapIndex. Try to create");
                if (playerController.UserId == null) {
                    return null;
                }
                int slot = (int)(playerController.UserId & 0xFF);
                return CreatePlayerBySlot(slot);
            }
        }
        public GGPlayer? FindBySlot (int slot)
        {
            if (playerMap.ContainsKey(slot)) {
                return playerMap[slot];
            }
            return null;
        }
        public GGPlayer? FindByIndex (int index)
        {
            if (playerMapIndex.ContainsKey(index)) {
                return playerMapIndex[index];
            }
            return null;
        } 
        public void ForgetPlayer (int slot)
        {
            if ( playerMap.ContainsKey(slot)) {
                int index = playerMap[slot].Index;
                playerMap.Remove(slot);
                playerMapIndex.Remove(index);
            }
        }
    }
    public class GGPlayer
    {    
        private readonly object lockObject = new object();
        public GGPlayer(int slot)
        {
            Slot = slot;
            Level = 1;
            PlayerName = "";
            Index = -1;
            LevelWeapon = GGVariables.Instance.weaponsList.FirstOrDefault(w => w.Level == 1)!;
            Angles = new QAngle();
            Origin = new Vector();
        }
        public void UpdatePlayerController(CCSPlayerController playerController)
        {
            Index = (int)playerController.Index;
//            this.Slot = (int)(playerController.UserId & 0xFF)!;
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
        public QAngle? Angles { get; set; }
        public Vector? Origin { get; set; }
        public int AfkCount { get; set; } = 0;
        public PlayerStates State { get; set;}
        public void SetLevel( int setLevel)
        {
            if (setLevel > GGVariables.Instance.WeaponOrderCount || setLevel < 1)
                return;
            lock (lockObject)
            {
                this.Level = (uint)setLevel;
                LevelWeapon = GGVariables.Instance.weaponsList.FirstOrDefault(w => w.Level == Level)!;
            }
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
    }
/*    public class SchemaString<SchemaClass> : NativeObject where SchemaClass : NativeObject
    {
        public SchemaString(SchemaClass instance, string member) : base(Schema.GetSchemaValue<nint>(instance.Handle, typeof(SchemaClass).Name!, member))
            { }

        public unsafe void Set(string str)
        {
            byte[] bytes = this.GetStringBytes(str);

            for (int i = 0; i < bytes.Length; i++)
            {
                Unsafe.Write((void*)(this.Handle.ToInt64() + i), bytes[i]);
            }

            Unsafe.Write((void*)(this.Handle.ToInt64() + bytes.Length), 0);
        }

        private byte[] GetStringBytes(string str)
        {
            return Encoding.ASCII.GetBytes(str);
        }
    } */
}
//Colors Available = "{default} {white} {darkred} {green} {lightyellow}" "{lightblue} {olive} {lime} {red} {lightpurple}"
                      //"{purple} {grey} {yellow} {gold} {silver}" "{blue} {darkblue} {bluegrey} {magenta} {lightred}" "{orange}"