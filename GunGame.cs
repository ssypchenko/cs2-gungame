#pragma warning disable CS8981// Naming Styles
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
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
using GunGame.API;
using GunGame.Extensions;
using GunGame.Models;
using GunGame.Online;
using GunGame.Stats;
using GunGame.Variables;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GunGame
{
    public class GunGame : BasePlugin, IPluginConfig<GGConfig>
    {
        public bool Hot_Reload = false;
        public GunGame (IStringLocalizer<GunGame> localizer)
        {
            playerManager = new(this);
            _localizer = localizer;
        }
        public readonly IStringLocalizer<GunGame> _localizer;
        public PlayerLanguageManager playerLanguageManager = new ();

        public override string ModuleName => "CS2_GunGame";
        public override string ModuleVersion => "v1.1.0";
        public override string ModuleAuthor => "Sergey";
        public override string ModuleDescription => "GunGame mode for CS2";

        public CoreAPI CoreAPI { get; set; } = null!;
        private static PluginCapability<IAPI> APICapability { get; } = new("gungame:api");
        public bool LogConnections = false;
        public bool WeaponLoaded = false;
        private bool warmupInitialized = false;
        private int WarmupCounter = 0;
        private bool IsObjectiveHooked = false;  
        
        public GGConfig Config { get; set; } = new();
        public DatabaseSettings dbSettings = new();
        public StatsManager statsManager { get; set; } = null!;
        public OnlineManager onlineManager { get; set; } = null!;
        public DatabaseOperationQueue dbQueue { get; set; } = null!;

        public void OnConfigParsed (GGConfig config)
        { 
            this.Config = config;
            GGVariables.Instance.MapStatus = (Objectives)Config.RemoveObjectives;
        }
        private void LoadDBConfig()
        {
            string configFile = Server.GameDirectory + "/csgo/cfg/gungame-db.json";
            if (!File.Exists(configFile))
            {
                CreateDefaultConfigFile(configFile);
            }
            try
            {
                string jsonString = File.ReadAllText(configFile);
                if (string.IsNullOrEmpty(jsonString))
                {
                    Console.WriteLine("[GunGame] ****** LoadDBConfig: Error loading DataBase config. csgo/cfg/gungame-db.json is wrong or empty. Continue without GunGame statistics");
                    return;
                }
                dbSettings = System.Text.Json.JsonSerializer.Deserialize<DatabaseSettings>(jsonString)!;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GunGame] ******* LoadDBConfig: Error reading or deserializing gungame-db.json file: {ex.Message}. Continue without GunGame statistics");
                return;
            }
            if (dbSettings == null || dbSettings.StatsDB == null)
            {
                Console.WriteLine($"[GunGame - FATAL] ****** LoadDBConfig: Error reading or deserializing gungame-db.json file. Continue without GunGame statistics");
                Logger.LogError($"[GunGame - FATAL] ****** LoadDBConfig: Error reading or deserializing gungame-db.json file. Continue without GunGame statistics");
                return;
            }
            DBConfig statsDBConfig = dbSettings.StatsDB;
            DBConfig onlineDBConfig = dbSettings.OnlineDB;
            statsManager = new(statsDBConfig, this);
            onlineManager = new(onlineDBConfig, this);
            dbQueue = new DatabaseOperationQueue(this);
        }
        private void CreateDefaultConfigFile(string configFile)
        {
            
            dbSettings.StatsDB = new DBConfig
            {
                DatabaseType = "SQLite",
                DatabaseFilePath = "/csgo/cfg/gungame-db.sqlite",
                GeoDatabaseFilePath = "/csgo/cfg/GeoLite2-Country.mmdb",
                DatabaseHost = "your_mysql_host",
                DatabaseName = "your_mysql_database",
                DatabaseUser = "your_mysql_username",
                DatabasePassword = "your_mysql_password",
                DatabasePort = 3306,
                Comment = "use SQLite or MySQL as Database Type"
            };
            dbSettings.OnlineDB = new DBConfig
            {
                DatabaseType = "",
                DatabaseFilePath = "",
                DatabaseHost = "your_mysql_host",
                DatabaseName = "your_mysql_database",
                DatabaseUser = "your_mysql_username",
                DatabasePassword = "your_mysql_password",
                DatabasePort = 3306,
                Comment = "use MySQL only as Database Type"
            };

            string defaultConfigJson = System.Text.Json.JsonSerializer.Serialize(dbSettings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFile, defaultConfigJson);
        }
        private bool LoadConfig()
        {
            string configLocalPath = Path.Combine("csgo/cfg/", GGVariables.Instance.ActiveConfigFolder, "/gungame.json");
            string configPath = Path.Combine(Server.GameDirectory, configLocalPath);

            Logger.LogInformation($"Loading config: {configLocalPath}");
            try
            {
                string jsonString = File.ReadAllText(configPath);

                if (string.IsNullOrEmpty(jsonString))
                {
                    Logger.LogError("Error loading config: csgo/cfg/" + GGVariables.Instance.ActiveConfigFolder + "/gungame.json is wrong or empty");
                    return false;
                }

                var cnfg = System.Text.Json.JsonSerializer.Deserialize<GGConfig>(jsonString);
                if (cnfg == null)
                {
                    Logger.LogError("Error deserialize config, csgo/cfg/" + GGVariables.Instance.ActiveConfigFolder + "/gungame.json is wrong or empty");
                    return false;
                }

                bool updateRequired = false;
                foreach (var property in typeof(GGConfig).GetProperties())
                {
                    var loadedValue = property.GetValue(cnfg);
                    var defaultValue = property.GetValue(Config);

                    if (loadedValue == null)
                    {
                        property.SetValue(cnfg, defaultValue);
                        updateRequired = true;
                    }
                }

                if (updateRequired)
                {
                    var updatedJsonContent = System.Text.Json.JsonSerializer.Serialize(cnfg, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(configPath, updatedJsonContent);
                    Logger.LogInformation("[GunGame] Config updated due to missing or obsolete keys.");
                }
                Config = cnfg;
                GGVariables.Instance.WeaponsSkipFastSwitch = Config.FastSwitchSkipWeapons.Split(',')
                    .Select(weapon => $"weapon_{weapon.Trim()}").ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GunGame] Error reading or deserializing /csgo/cfg/{GGVariables.Instance.ActiveConfigFolder}/gungame.json file: {ex.Message}");
                return false;
            }
            SetSpawnRules((int)Config.RespawnByPlugin);
            return true;
        }
        public PlayerManager playerManager; 
        public Dictionary<ulong, int> PlayerLevelsBeforeDisconnect = new();
        public Dictionary<ulong, int> PlayerHandicapTimes = new();
        private CounterStrikeSharp.API.Modules.Timers.Timer? warmupTimer = null;
        private CounterStrikeSharp.API.Modules.Timers.Timer? endGameTimer = null;
        private CounterStrikeSharp.API.Modules.Timers.Timer? _infoTimer = null;
        int endGameCount = 0;
        private CounterStrikeSharp.API.Modules.Timers.Timer? HandicapUpdateTimer = null;
        public Dictionary<string, WeaponInfo>Weapon_from_List = new Dictionary<string, WeaponInfo>();
        public SpecialWeaponInfo SpecialWeapon = new();
        public Random random = new();
        public bool[,] g_Shot = new bool[65,65];
        public double [] LastDeathTime = new double[65];
        public List<string> WinnerMessage = new()
        {
            "<font color='",
            "'>",
            "<br>",
            "</font>"
        };
        public int HandicapTopWins = 0; // number of wins of a lowest player in TopRank restricted by number HandicapTopRank
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
        public override void Load(bool hotReload)
        {
            CoreAPI = new CoreAPI(this);
            if (CoreAPI != null)
            {
                Capabilities.RegisterPluginCapability(APICapability, () => CoreAPI);
                Logger.LogInformation("API registered");
            }
            LoadDBConfig();
            if (hotReload)
            {
                Hot_Reload = true;
                Logger.LogInformation("[GUNGAME] Hot Reload");
            }
            if (LoadConfig())
            {
                SetupGameWeapons();
                SetupWeaponsLevels();

                if (Config.IsPluginEnabled && WeaponLoaded) 
                {
                    if (hotReload)
                    {
                        FindMapObjective();
                        var playerEntities = GetValidPlayersWithBots();
                        if (playerEntities != null && playerEntities.Any())
                        {
                            foreach (var playerController in playerEntities)
                            {
                                var player = playerManager.CreatePlayerBySlot(playerController.Slot);
                                if (player != null)
                                {
                                    StopTripleEffects(player);
                                    player.ResetPlayer();
                                }
                            }
                        }
                    }
                    GG_Startup();
                }
                var tempCulture = playerLanguageManager.GetDefaultLanguage();
                GGVariables.Instance.ServerLanguageCode = tempCulture.Name.ToLower();
            }
            else
            {
                Console.WriteLine("Error loading config on Load");
                Logger.LogError("Error loading config on Load");
            }
        }
        public override void Unload(bool hotReload)
        {
            //*********************************************
            dbQueue.Stop();
        }
        private void RestartGame()
        {
            if (LoadConfig())
            {
                SetupGameWeapons();
                SetupWeaponsLevels();
                InitVariables();
                if (Config.IsPluginEnabled && WeaponLoaded)
                {
                    FindMapObjective();
                    StatsLoadRank();
                    var playerEntities = GetValidPlayersWithBots();
                    if (playerEntities != null && playerEntities.Any())
                    {
                        foreach (var playerController in playerEntities)
                        {
                            var player = playerManager.CreatePlayerBySlot(playerController.Slot);
                            if (player != null)
                            {
                                StopTripleEffects(player);
                                player.ResetPlayer();
                            }
                        }
                    }
                    GGVariables.Instance.Tcount = CountPlayersForTeam(CsTeam.Terrorist);
                    GGVariables.Instance.CTcount = CountPlayersForTeam(CsTeam.CounterTerrorist);
                    
                    var mp_restartgame = ConVar.Find("mp_restartgame");
            
                    if (mp_restartgame != null )
                    {
                        mp_restartgame.SetValue((int)1);
                    }
                    if (warmupTimer != null)
                    {
                        warmupTimer.Kill();
                        warmupTimer = null;
                    }
                    warmupInitialized = false;
                    GGVariables.Instance.WarmupFinished = false;
                }
            }
            else
            {
                Console.WriteLine("Error loading config on Restart");
                Logger.LogError("Error loading config on Restart");
            }
        }
        private void GG_Startup()
        {
            if ( !GGVariables.Instance.IsActive )
            {
                GGVariables.Instance.IsActive = true;
            }
            Logger.LogInformation($"[GUNGAME] GG Start, version {ModuleVersion}");

            InitVariables();
            SetupListeners();
            RegisterEvents();
            
            if ( Config.HandicapUpdate > 0 )
            {
                HandicapUpdateTimer ??= AddTimer((float)Config.HandicapUpdate, Timer_HandicapUpdate, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            }
            else if (HandicapUpdateTimer != null)
            {
                HandicapUpdateTimer.Kill();
                HandicapUpdateTimer = null;
            }
            if (_infoTimer == null)
            {
                _infoTimer = AddTimer(40.0f, TimerInfo, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            }
        }
        private void SetupGameWeapons()
        {
            Weapon_from_List = new Dictionary<string, WeaponInfo>();
            GGVariables.Instance.weaponsList = new();
            string WeaponFile = Server.GameDirectory + "/csgo/cfg/" + GGVariables.Instance.ActiveConfigFolder + "/weapons.json";
            try
            {
                string jsonString = File.ReadAllText(WeaponFile);

                if (string.IsNullOrEmpty(jsonString))
                {
                    Logger.LogError("Error loading weapons data: csgo/cfg/" + GGVariables.Instance.ActiveConfigFolder + "/weapons.json is wrong or empty");
                    return;
                }

                var deserializedDictionary = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, WeaponInfo>>(jsonString);

                if (deserializedDictionary != null)
                {
                    Weapon_from_List = deserializedDictionary;
                }
                else
                {
                    Logger.LogError("Error deserialize weapons data: csgo/cfg/" + GGVariables.Instance.ActiveConfigFolder + "/weapons.json is wrong or empty");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GunGame] Error reading or deserializing weapons data: csgo/cfg/{GGVariables.Instance.ActiveConfigFolder}/weapons.json file: {ex.Message}");
                Logger.LogError($"[GunGame] Error reading or deserializing weapons data: csgo/cfg/{GGVariables.Instance.ActiveConfigFolder}/weapons.json file: {ex.Message}");
                return;
            }
            GGVariables.Instance.WeaponsMaxId = 0;
            foreach (var kvp in Weapon_from_List)
            {
                WeaponInfo weaponInfo = kvp.Value;
                if (weaponInfo != null)
                {
                    GGVariables.Instance.WeaponsMaxId++;
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
//                            SpecialWeapon.TaserAmmoType = weaponInfo.AmmoType;
                            break;

                        case "flashbang":
                            SpecialWeapon.Flashbang = weaponInfo.Index;
                            GGVariables.Instance.WeaponIdFlashbang = weaponInfo.Index;
//                            GGVariables.Instance.g_WeaponAmmoTypeFlashbang = weaponInfo.AmmoType;
                            break;

                        case "hegrenade":
                            SpecialWeapon.Hegrenade = weaponInfo.Index;
                            SpecialWeapon.HegrenadeLevelIndex = weaponInfo.LevelIndex;
//                            SpecialWeapon.HegrenadeAmmoType = weaponInfo.AmmoType;
                            break;

                        case "smokegrenade":
                            SpecialWeapon.Smokegrenade = weaponInfo.Index;
                            GGVariables.Instance.WeaponIdSmokegrenade = weaponInfo.Index;
//                            GGVariables.Instance.g_WeaponAmmoTypeSmokegrenade = weaponInfo.AmmoType;
                            break;

                        case "molotov":
                            SpecialWeapon.Molotov = weaponInfo.Index;
                            SpecialWeapon.MolotovLevelIndex = weaponInfo.LevelIndex;
//                            SpecialWeapon.MolotovAmmoType = weaponInfo.AmmoType;
                            break;
                    }
                }
            }
            if (!(GGVariables.Instance.WeaponsMaxId != 0
                && SpecialWeapon.Knife != 0
                && SpecialWeapon.Hegrenade != 0
                && GGVariables.Instance.WeaponIdSmokegrenade != 0
                && GGVariables.Instance.WeaponIdFlashbang != 0))
            {
                string error = string.Format("FATAL ERROR: Some of the weapons not found MAXID=[{0}] KNIFE=[{1}] HE=[{2}] SMOKE=[{3}] FLASH=[{4}]. You should update your {5} and take it from the release zip file.",
                    GGVariables.Instance.WeaponsMaxId, SpecialWeapon.Knife, SpecialWeapon.Hegrenade, GGVariables.Instance.WeaponIdSmokegrenade, GGVariables.Instance.WeaponIdFlashbang, WeaponFile);
                
                Console.WriteLine(error);
                return;
            }
/*            if (!(SpecialWeapon.HegrenadeAmmoType != 0
                && GGVariables.Instance.g_WeaponAmmoTypeFlashbang != 0
                && GGVariables.Instance.g_WeaponAmmoTypeSmokegrenade != 0))
            {
                string error = string.Format("FATAL ERROR: Some of the ammo types not found HE=[{0}] FLASH=[{1}] SMOKE=[{2}]. You should update your {3} and take it from the release zip file.",
                    GGVariables.Instance.g_WeaponAmmoTypeHegrenade, GGVariables.Instance.g_WeaponAmmoTypeFlashbang, GGVariables.Instance.g_WeaponAmmoTypeSmokegrenade, WeaponFile);

                Console.WriteLine(error);
                return;
            } */
            if (SpecialWeapon.Taser == 0)
            {
                string error = $"FATAL ERROR: Some of the weapons not found TASER=[{SpecialWeapon.Taser}]. You should update your {WeaponFile} and take it from the release zip file.";
                Console.WriteLine(error);
                return;
            }

/*            if (SpecialWeapon.MolotovAmmoType == 0 || SpecialWeapon.TaserAmmoType == 0)
            {
                string error = $"FATAL ERROR: Some of the ammo types not found MOLOTOV=[{SpecialWeapon.MolotovAmmoType}] TASER=[{SpecialWeapon.TaserAmmoType}]. You should update your {WeaponFile} and take it from the release zip file.";
                Console.WriteLine(error);
                return;
            } */
        }
        private void SetupWeaponsLevels()
        {
            string WeaponLevelsFile = Server.GameDirectory + "/csgo/cfg/" + GGVariables.Instance.ActiveConfigFolder + "/gungame_weapons.json";
            WeaponOrderSettings WeaponSettings;
            try
            {
                string jsonString = File.ReadAllText(WeaponLevelsFile);

                if (string.IsNullOrEmpty(jsonString))
                {
                    return;
                }
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
        private void RegisterEvents()
        {
            RegisterEventHandler<EventPlayerDeath>(EventPlayerDeathHandler);
            RegisterEventHandler<EventPlayerHurt>(EventPlayerHurtHandler);
            RegisterEventHandler<EventPlayerTeam>(EventPlayerTeamHandler);
            RegisterEventHandler<EventPlayerSpawn>(EventPlayerSpawnHandler);
            RegisterEventHandler<EventRoundStart>(EventRoundStartHandler);
            RegisterEventHandler<EventRoundEnd>(EventRoundEndHandler);
            RegisterEventHandler<EventHegrenadeDetonate>(EventHegrenadeDetonateHandler);
            RegisterEventHandler<EventWeaponFire>(EventWeaponFireHandler);
            RegisterEventHandler<EventItemPickup>(EventItemPickupHandler, HookMode.Post);
        }
        private void SetupListeners()
        {
            RegisterListener<Listeners.OnClientConnected>(slot => {
                var player = playerManager.CreatePlayerBySlot(slot);
                if (player == null)
                {
                    Logger.LogError($"[GUNGAME]* OnClientConnected: Can't create player {slot}");
                    return;
                }
                Logger.LogInformation($"{player.PlayerName} ({slot}) connected");
            });
            RegisterListener<Listeners.OnClientPutInServer>(slot => {
                var player = playerManager.CreatePlayerBySlot(slot);
                if (player == null)
                {
                    Logger.LogError($"[GUNGAME]* OnClientPutInServer: Can't create player {slot}");
                    return;
                }
//                Logger.LogInformation($"{player.PlayerName} ({slot}) put in server");
            });
            RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorized);            
            RegisterListener<Listeners.OnMapStart>(name =>
            {
                Logger.LogInformation($"[GunGame] map {Server.MapName} loaded");
                if (onlineManager != null && onlineManager.OnlineReportEnable)
                {
                    _ = onlineManager.ClearAllPlayerData(name);
                }
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
                        StatsLoadRank();
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
                AddTimer(3.6f, () => {
                    // get map spawn point
                    GGVariables.Instance.spawnPoints = new();
                    var tSpawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_terrorist");
                    var ctSpawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_counterterrorist");
                    var dmSpawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_deathmatch_spawn");

                    GGVariables.Instance.spawnPoints[2] = new ();
                    GGVariables.Instance.spawnPoints[3] = new ();
                    GGVariables.Instance.spawnPoints[4] = new ();

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
                });
                AddTimer(10.0f, () => {
                    LogConnections = true;
                });
            });
            RegisterListener<Listeners.OnMapEnd>(() => 
            {
                LogConnections = false;
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
                        DeregisterEventHandler<EventBombPlanted>(EventBombHandler);
                        DeregisterEventHandler<EventBombExploded>(EventBombHandler);
                        DeregisterEventHandler<EventBombDefused>(EventBombHandler);
                        DeregisterEventHandler<EventBombPickup>(EventBombPickupHandler);
                    }
                
                    if (GGVariables.Instance.MapStatus.HasFlag(Objectives.Hostage))
                    {
                        IsObjectiveHooked = false;
                        DeregisterEventHandler<EventHostageKilled>(EventHostageKilledHandler);
                    }
                }
            });
            RegisterListener<Listeners.OnClientDisconnect>( slot =>
            {
                var player = playerManager.FindBySlot(slot, "OnClientDisconnect");
                if (player == null) {
                    return;
                }
                if (onlineManager != null && onlineManager.OnlineReportEnable)
                {
                    _ = onlineManager.RemovePlayerData(player);
                }
                if ( GGVariables.Instance.CurrentLeader.Slot == slot )
                {
                    int playerLevel = (int)player.Level;
                    player.SetLevel(0);
                    RecalculateLeader(slot, playerLevel, 0);
                    if ( GGVariables.Instance.CurrentLeader.Slot == slot )
                    {
                        GGVariables.Instance.CurrentLeader.SetLeader(-1, 0);
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
                if (LogConnections) Logger.LogInformation($"{player.PlayerName} ({player.Slot}) disconnected");
                playerManager.ForgetPlayer(player.Slot);
            }); 
            RegisterListener<Listeners.OnClientDisconnectPost>( slot =>
            {
                playerManager.ForgetPlayer(slot);
                AddTimer(1.0f, () => {
                    var playerController = Utilities.GetPlayerFromSlot(slot);
                    if (playerController != null && playerController.IsValid)
                    {
                        if( playerController.Connected == PlayerConnectedState.PlayerDisconnected)
                        { 
                            //this is just in case to try to fight a cs2 bug when player's pawn stay after disconnect
                            playerController.CommitSuicide(false,false);
                            playerController.Remove();
                        }
                    }
                });
            });
        }
        private void OnClientAuthorized(int slot, SteamID id)
        {
            var playerController = Utilities.GetPlayerFromSlot(slot);
            if (playerController == null || !playerController.IsValid)
            {
                Logger.LogError($"[GUNGAME]* OnClientAuthorized: Wrong playerController slot {slot} SteamId {id.SteamId64}");
                return;
            }
            var player = playerManager.FindBySlot(playerController.Slot, "OnClientAuthorized");
            if (player == null)
            {
                Logger.LogError($"[GUNGAME]* OnClientAuthorized: Can't create player {slot} SteamId {id.SteamId64}");
                return;
            }
           
            if (playerController.AuthorizedSteamID == null)
            {
                Logger.LogInformation($"{playerController.PlayerName} has AuthorizedSteamID - null");
                if (++player.IdAttempts > 5)
                {
                    Logger.LogInformation($"{player.PlayerName} kicked because of 6 of unsuccessful authorisation attempts.");
                    Server.ExecuteCommand($"kickid {slot} NoSteamId");
                    return;
                }
                AddTimer(5.0f, () =>
                {
                    OnClientAuthorized(slot, id);
                });
                return;
            }
            if (player.SavedSteamID != playerController.AuthorizedSteamID.SteamId64)
            {
                player.UpdatePlayerController(playerController);
                Logger.LogInformation($"[GunGame] Update playerController for {player.PlayerName} ({playerController.Slot})");
            }
            var playerIP = GetPlayerIp(playerController);
            if (playerIP != null && IsValidIP(playerIP))
            {
                player.IP = playerIP;
            }
            else 
            {
                Logger.LogInformation($"[GUNGAME]* OnClientPutInServer: bad player IP {player.PlayerName} {playerIP}");
            }
            if ( Config.RestoreLevelOnReconnect )
            {    
                if ( PlayerLevelsBeforeDisconnect.TryGetValue(player.SavedSteamID, out int level) )
                {
                    if ( player.Level < level )
                    {
                        Logger.LogInformation($"[GUNGAME]* OnClientPutInServer: Restore level {level} to {player.PlayerName}");
                        player.SetLevel (level); // saved level
                        RecalculateLeader(player.Slot, 0);
                    }
                }
            }
            UpdatePlayerScoreLevel(slot);
            if (statsManager != null)
                dbQueue.EnqueueOperation(async () => await statsManager.GetPlayerWins(player));
//            _ = statsManager?.GetPlayerWins(player);
        }
        private void InitVariables ()
        {
            GGVariables.Instance.Round = 0;
            GGVariables.Instance.Tcount = 0;
            GGVariables.Instance.CTcount = 0;
            GGVariables.Instance.CurrentLeader.SetLeader(-1, 0);

            GGVariables.Instance.MapStatus = 0;
            GGVariables.Instance.HostageEntInfo = 0;
            GGVariables.Instance.IsVotingCalled = false;
            GGVariables.Instance.IsCalledEnableFriendlyFire = false;
            GGVariables.Instance.IsCalledDisableRtv = false;
            GGVariables.Instance.GameWinner = null;
            GGVariables.Instance.FirstRound = false;

            GGVariables.Instance.Mp_friendlyfire = ConVar.Find("mp_friendlyfire");

            PlayerLevelsBeforeDisconnect.Clear();
        }
        public void StartWarmupRound()
        {
            Console.WriteLine("[GunGame]********** Start WarmupRound");
            warmupInitialized = true;
            WarmupCounter = 0;
            if (warmupTimer == null)
            {
                 warmupTimer= AddTimer(1.0f, EndOfWarmup, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            }
            Server.ExecuteCommand("exec " + GGVariables.Instance.ActiveConfigFolder + "/gungame.warmupstart.cfg");
        }
        public void EndOfWarmup()
        {
            if ((GGVariables.Instance.CTcount + GGVariables.Instance.Tcount) == 0) {
                WarmupCounter = 0;
                return;
            }

            if ( ++WarmupCounter < Config.WarmupTimeLength )
            {   
                var seconds = Config.WarmupTimeLength - WarmupCounter;
                var playerEntities = GetValidPlayers();
                if (playerEntities != null && playerEntities.Any())
                {
                    foreach (var playerController in playerEntities)
                    {
                        var player = playerManager.FindBySlot(playerController.Slot, "EndOfWarmup");
                        if (player != null)
                        {
                            playerController.PrintToCenter(player.Translate("warmup.left", seconds));
                        }
                        else 
                        {
                            playerController.PrintToCenter(Localizer["warmup.left", seconds]);
                        }
                        
                        if ( (Config.WarmupTimeLength - WarmupCounter) == 4)
                        {
                            PlaySound(null!, Config.WarmupTimerSound);
                        }
                    }
                }
                return;
            }
            var mp_restartgame = ConVar.Find("mp_restartgame");
            GGVariables.Instance.FirstRound = true; // to do necessary staff on the first round after warmup
            
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

                Server.ExecuteCommand("exec " + GGVariables.Instance.ActiveConfigFolder + "/gungame.warmupend.cfg");
            }
            Console.WriteLine("WarmUp End");
            
            AddTimer(3.0f, () => {
                var entities = Utilities.FindAllEntitiesByDesignerName<CCSWeaponBaseGun>("weapon_");
                foreach (var entity in entities)
                {
                    if (!entity.IsValid)
                    {
                        continue;
                    }
                    if (entity.State != CSWeaponState_t.WEAPON_NOT_CARRIED)
                    {
                        continue;
                    }
                    if (entity.DesignerName.StartsWith("weapon_") == false)
                    {
                        continue;
                    }
                    entity.Remove();
                }
            });
        }
/**************  Events **********************************************************/
/**************  Events **********************************************************/
/**************  Events **********************************************************/
        private HookResult EventPlayerSpawnHandler(EventPlayerSpawn @event, GameEventInfo info)
        {
//            Process currentProc = Process.GetCurrentProcess();
            if (!GGVariables.Instance.IsActive) {
                return HookResult.Continue;
            }
            var playerController = @event.Userid;
            if (playerController == null || !IsValidPlayer(playerController) || !IsClientInTeam(playerController)) 
            {
//                Logger.LogError($"PlayerSpawn {@event.Userid.Slot} - bad playerController");
                return HookResult.Continue;
            }
            var client = playerManager.FindBySlot(playerController.Slot, "EventPlayerSpawnHandler");
            if ( client == null ) {
                Logger.LogError($"[GUNGAME]PlayerSpawnHandler: Can't find player for {playerController.PlayerName}");
                return HookResult.Continue;
            }
            if (Config.AfkManagement)
            {
                AddTimer(0.3f, () =>
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

                if (Config.ShootKnifeBlock)
                {
                    ForgiveShots(client.Slot);
                }
                if ( !playerController.IsBot )
                {
//                    PlaySoundDelayed(1.5f, client, "Welcome");

                    //*** Show join message.

                    if ( Config.JoinMessage )
                    {     
/**************************************************************************************/
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
                    AddTimer(0.4f, () =>
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

                GiveWarmUpWeaponDelayed(0.5f, client.Slot);
/*                AddTimer(0.7f, () => {
                    if (playerController != null && playerController.IsValid && !playerController.IsBot)
                    {
                        var player = playerManager.FindBySlot(playerController.Slot, "EventPlayerSpawnHandler");
                        if ( player != null)
                        {
                            if ( !warmupInitialized ) {

                                playerController.PrintToChat(player.Translate("warmup.notstarted"));
                            } else {
                                playerController.PrintToChat(player.Translate("warmup.started"));
                            } 
                        } 
                    }
                }); */
                

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
                    playerController.PrintToCenter(client.Translate("your.level", Level, GGVariables.Instance.WeaponOrderCount));
                    if ( Config.ShowLeaderInHintBox && GGVariables.Instance.CurrentLeader.Slot > -1 )
                    {
                        int leaderLevel = (int) GGVariables.Instance.CurrentLeader.Level;
                        if ( client.Level == GGVariables.Instance.CurrentLeader.Level ) {
                            playerController.PrintToChat(client.Translate("you.leader"));
                        } else if ( Level == leaderLevel ) {
                            playerController.PrintToChat(client.Translate("onleader.level"));
                        } else {
                            playerController.PrintToChat(client.Translate("leader.level", leaderLevel));
                        }
                    }
                    if ( Config.MultiKillChat && ( killsPerLevel > 1 ) )
                    {
                        playerController.PrintToChat(client.Translate("kills.toadvance",killsPerLevel - client.CurrentKillsPerWeap));
                    }
                }
                else
                {
                    playerController.PrintToChat(client.Translate("your.level", Level, GGVariables.Instance.WeaponOrderCount));

                    if ( Config.MultiKillChat && ( killsPerLevel > 1 ) )
                    {   
                        playerController.PrintToChat(client.Translate("kills.toadvance",killsPerLevel - client.CurrentKillsPerWeap));
                    }
                }
            }
            return HookResult.Continue;
        }
        private HookResult EventPlayerDeathHandler(EventPlayerDeath @event, GameEventInfo info)
        {
            if (@event == null || @event.Userid == null)
            {
                return HookResult.Continue;
            }
            CCSPlayerController VictimController = @event.Userid;
            string weapon_used = @event.Weapon;
            
            if (VictimController == null || !IsValidPlayer(VictimController))
            {
                return HookResult.Continue;
            }
            if (!GGVariables.Instance.IsActive) {
                Respawn(VictimController);
                return HookResult.Continue;
            }
            var Victim = playerManager.FindBySlot(VictimController.Slot, "EventPlayerDeathHandler");
            if ( Victim == null ) {
                Logger.LogError($"EventPlayerDeathHandler: Victim Controller {VictimController.PlayerName} can't find player in player map");
                Respawn(VictimController, false);
                return HookResult.Continue;
            }
            if (Config.ShootKnifeBlock)
            {
                ForgiveShots(VictimController.Slot);
            }
            StopTripleEffects(Victim);
//            UpdatePlayerScoreDelayed(Victim);
//            UpdatePlayerScoreDelayed(Killer);

            /* They change team at round end don't punish them and don't respawn then. */
            if ( !GGVariables.Instance.RoundStarted && !Config.AllowLevelUpAfterRoundEnd )
            {
                Respawn(VictimController, false);
                return HookResult.Continue;
            }
            CCSPlayerController KillerController = null!;
            GGPlayer Killer = null!;
            if (@event.Attacker != null)
            {
                KillerController = @event.Attacker;
                
                var ggkiller = playerManager.FindBySlot(KillerController.Slot, "EventPlayerDeathHandler");
                if (ggkiller != null)
                {
                    Killer = ggkiller;
                    if (Config.AfkManagement)
                    {
                        var angles = VictimController.PlayerPawn.Value?.EyeAngles;
                        var origin = VictimController.PlayerPawn.Value?.CBodyComponent?.SceneNode?.AbsOrigin;
                        if (Victim.Angles != null && Victim.Origin != null && angles != null && origin != null
                            && Victim.Angles.X == angles.X && Victim.Angles.Y == angles.Y
                            && Victim.Origin.X == origin.X && Victim.Origin.Y == origin.Y)
                        {
                            if (IsValidHuman(KillerController))
                            {
                                KillerController?.PrintToCenter(Killer.Translate("kill.afk"));
                            }
                            if (Config.AfkAction > 0 && ++Victim.AfkCount >= Config.AfkDeaths)
                            {
                                if (Config.AfkAction == 1) //Kick
                                {
                                    Server.ExecuteCommand($"kickid {VictimController.UserId} '{Localizer["max.afkdeath"]}'");
                                }
                                else if (Config.AfkAction == 2)
                                {
                                    VictimController.ChangeTeam(CsTeam.Spectator);
                                    VictimController.PlayerPawn.Value?.CommitSuicide(false, true);
                                    Victim.AfkCount = 0;
        //                            Respawn(VictimController, false);
                                }
                            }
                            else
                            {
                                Respawn(VictimController);
                            }
                            return HookResult.Continue;
                        }
                        else
                        {
                            Victim.AfkCount = 0;
                        }
                    }
                }
            }
            /* Kill self with world spawn */
            if (!KillerController.IsValid)
            {
                if (weapon_used == "world" || weapon_used == "worldent" || weapon_used == "trigger_hurt" || weapon_used == "env_fire")
                {
//                    Console.WriteLine($"{VictimController.PlayerName} - died from world");
                    if (GGVariables.Instance.RoundStarted && Config.WorldspawnSuicide > 0)
                    {
                        // kill self with world spawn
                        ClientSuicide(Victim, Config.WorldspawnSuicide);
//                        Logger.LogInformation($"{VictimController.PlayerName} killed by {weapon_used}");
                    }
                    Respawn(VictimController);
                    return HookResult.Continue;
                }
            }

            /* They killed themself by kill command or by hegrenade etc */
            if (IsValidPlayer(KillerController) && KillerController.Index == VictimController.Index)
            {
//                Console.WriteLine($"{VictimController.PlayerName} - suicide");
                /* (Weapon is event weapon name, can be 'world' or 'hegrenade' etc) */ /* weapon is not 'world' (ie not kill command) */
                if (Config.CommitSuicide > 0 && GGVariables.Instance.RoundStarted && !Victim.TeamChange )
                {
                    // killed himself by kill command or by hegrenade
                    ClientSuicide(Victim, Config.CommitSuicide);
//                    Logger.LogInformation($"{VictimController.PlayerName} killed by {weapon_used} - Commit Suicide");
                }
                Respawn(VictimController);
                return HookResult.Continue;
            }

            // Victim > 0 && Killer > 0
            if (KillerController == null)
            {
                Logger.LogInformation($"******** {VictimController.PlayerName} killed by Killer == null");
                Respawn(VictimController);
                return HookResult.Continue;
            }
            if (KillerController.DesignerName == "cs_player_controller")
            {
                Console.WriteLine($"{KillerController.PlayerName} killed {VictimController.PlayerName} with {weapon_used}");
            }
            else
            {
                Logger.LogError($"{VictimController.PlayerName} killed not by player. Designer name: {KillerController.DesignerName}");
            }

            if (Killer == null)
            {
                Logger.LogError($"******** {VictimController.PlayerName} killed not by player in player map");
                Respawn(VictimController);
                return HookResult.Continue;
            }

            if (!TryGetWeaponInfo(weapon_used, out WeaponInfo usedWeaponInfo))
            {
                Logger.LogError($"[GUNGAME] **** Cant get weapon info for weapon used in kill {weapon_used} by {Killer.PlayerName}");
                Respawn(VictimController);
                return HookResult.Continue;
            }

            if ( warmupInitialized )
            {
                if (Config.ReloadWeapon)
                {
                    ReloadActiveWeapon(Killer, usedWeaponInfo.Index);
                }
                Respawn(VictimController);
                return HookResult.Continue;
            }

        /* Here is a place that the forward sends and receives the answer to count this kill as a kill or not.
         * Let's deal with the forwards and finish it
         */
            /*************** FFA - teamkill is considered as kill ****************/
            bool TeamKill = (!Config.FriendlyFireAllowed) && (VictimController.TeamNum == KillerController.TeamNum);

//            bool AcceptKill = RaiseKillEvent (KillerController.Slot, VictimController.Slot, weapon_used, TeamKill);
            bool AcceptKill = CoreAPI.RaiseKillEvent (KillerController.Slot, VictimController.Slot, weapon_used, TeamKill);

            if (!AcceptKill)
            {
                Logger.LogInformation($"********* Killer {Killer.PlayerName} - victim {Victim.PlayerName} kill is not accepted");
                Respawn(VictimController);
                return HookResult.Continue;
            }
                
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
            bool stop_further_processing = false; // if another plugin asks this not to be considered a kill, it will return true
            // Here is the part from Bot Management
            if (VictimController.IsBot)
            {
                if (usedWeaponInfo.LevelIndex == SpecialWeapon.KnifeLevelIndex)
                {
                    if (!Config.AllowUpByKnifeBot) // can't level up by knife on Bot
                    {
                        if (Config.AllowLevelUpByKnifeBotIfNoHuman) // can level up by knife on Bot
                        { 
                            if (HumansPlay() != 1)                  // but only if no other humsns 
                            {
                                stop_further_processing = true;
                                if (KillerController.IsValid && !Killer.IsBot) 
                                    KillerController.PrintToCenter(Killer.Translate("cantknife.leveluponbotwithhumans"));
                            }
                        }
                        else
                        {
                            stop_further_processing = true;
                            if (KillerController.IsValid && !Killer.IsBot) 
                                KillerController.PrintToCenter(Killer.Translate("cantknife.leveluponbot"));
                        }
                    }
                }
                else if (usedWeaponInfo.LevelIndex == SpecialWeapon.HegrenadeLevelIndex)
                {
                    if (!Config.AllowLevelUpByExplodeBot) // can't level up by he on Bot
                    {
                        if (Config.AllowLevelUpByExplodeBotIfNoHuman) // can level up by he on Bot
                        { 
                            if (HumansPlay() != 1)                  // but only if no other humsns 
                            {
                                stop_further_processing = true;
                                if (KillerController.IsValid && !Killer.IsBot) 
                                    KillerController.PrintToCenter(Killer.Translate("canthe.leveluponbotwithhumans"));
                            }
                        }
                        else
                        {
                            stop_further_processing = true;
                            if (KillerController.IsValid && !Killer.IsBot) 
                                KillerController.PrintToCenter(Killer.Translate("canthe.leveluponbot"));
                        }
                    }
                }
            }
            
            if (stop_further_processing || TeamKill)
            {
                if (stop_further_processing)
                {    
                    ReloadActiveWeapon(Killer, usedWeaponInfo.Index);
                    Respawn(VictimController);
                    return HookResult.Continue;
                } 
                if ( TeamKill )
                {
                    PlayRandomSoundDelayed(0.7f, Config.TeamKillSound);
                    Killer.CurrentLevelPerRound -= Config.TkLooseLevel;
                    if ( Killer.CurrentLevelPerRound < 0 )
                    {
                        Killer.CurrentLevelPerRound = 0;
                    }
                    Killer.CurrentLevelPerRoundTriple = 0;
                    
                    int oldLevel = (int) Killer.Level;
                    level = ChangeLevel(Killer, -Config.TkLooseLevel, false, VictimController);
                    if ( level == oldLevel )
                    {
                        Respawn(VictimController);
                        return HookResult.Continue;
                    }

                    if ( Config.TurboMode )
                    {
                        GiveNextWeapon(Killer.Slot);
                    }
                    Respawn(VictimController);
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
                Respawn(VictimController);
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
                    if (!KillerController.IsBot) KillerController.PrintToCenter(Killer.Translate("level.low",Victim.PlayerName, Config.KnifeProMinLevel));
                    follow = false;
                }
                if ( follow && (Config.KnifeProMaxDiff > 0) && ( Config.KnifeProMaxDiff < (VictimLevel - level) ) )
                {
                    if (!KillerController.IsBot) KillerController.PrintToCenter(Killer.Translate("level.difference", Victim.PlayerName, Config.KnifeProMaxDiff));
                    follow = false;
                }
                if ( follow && !Config.DisableLevelDown ) 
                {
                    int ChangedLevel = ChangeLevel(Victim, -1, true, KillerController);
                    if ( ChangedLevel != VictimLevel ) 
                    {
                        var playerEntities = GetValidPlayers();
//                        Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV);
                        if (playerEntities != null && playerEntities.Any())
                        {
                            foreach (var pc in playerEntities)
                            {
                                var pl = playerManager.FindBySlot(pc.Slot, "EventPlayerDeathHandler");
                                if ( pl != null)
                                {
                                    pc.PrintToChat(pl.Translate("level.stolen", Killer.PlayerName, Victim.PlayerName));
                                }
                            }
                        }
//                        Server.PrintToChatAll (Localizer["level.stolen", Killer.PlayerName, Victim.PlayerName]);
                        if (usedWeaponInfo.LevelIndex == SpecialWeapon.KnifeLevelIndex) 
                        {
                            PlayRandomSoundDelayed(0.7f, Config.KnifeStealSound);
//                            RaiseKnifeStealEvent(KillerController.Slot, VictimController.Slot);
                            CoreAPI.RaiseKnifeStealEvent(KillerController.Slot, VictimController.Slot);
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
                    Respawn(VictimController);
                    return HookResult.Continue;
                }
    // you can't pass the taser with a knife
                if (follow && Killer.LevelWeapon.LevelIndex == SpecialWeapon.TaserLevelIndex) 
                {
                    Respawn(VictimController);
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
                    level = ChangeLevel(Killer, 1, true, VictimController);
                    if ( oldLevelKiller == level ) {
                        Respawn(VictimController);
                        return HookResult.Continue;
                    }
                    PrintLeaderToChat(Killer, oldLevelKiller, level);
                    Killer.CurrentLevelPerRound++;

                    if (Config.TurboMode) {
                        GiveNextWeapon(Killer.Slot, true); // true - level up with knife
                    }
                    CheckForTripleLevel(Killer);
                    Respawn(VictimController);
                    return HookResult.Continue;
                }
            }
            bool LevelUpWithPhysics = false;

            CoreAPI.RaiseWeaponFragEvent(Killer.Slot, usedWeaponInfo.FullName);

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
                        Respawn(VictimController);
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
                        Respawn(VictimController);
                        return HookResult.Continue;
                    }
                }
            }
            
            if ( ( killsPerLevel > 1 ) && !LevelUpWithPhysics)
            {
                int kills = ++Killer.CurrentKillsPerWeap;
                if ( kills <= killsPerLevel )
                {
        /* An external function is called, which confirms whether to give a kill or not. If not, true is returned */
//                    bool Accepted = RaisePointChangeEvent(Killer.Slot, kills);
                    bool Accepted = CoreAPI.RaisePointChangeEvent(Killer.Slot, kills);
/*                    Call_StartForward(FwdPoint);
                    Call_PushCell(Killer);
                    Call_PushCell(kills);
                    Call_PushCell(1);
                    Call_Finish(Handled); */

                    if (!Accepted)
                    {
//                        Logger.LogInformation($"************* killer {Killer.PlayerName} victim {Victim.PlayerName} - point is not accepted ********");
                        Console.WriteLine("************* Point is not accepted ********");
                        Killer.CurrentKillsPerWeap--;
                        Respawn(VictimController);
                        return HookResult.Continue;
                    }
        //  **************************************** check logic
                    if ( kills < killsPerLevel )
                    {
                        if (!KillerController.IsBot)
                            PlaySound(KillerController, Config.MultiKillSound);

                        if ( Config.MultiKillChat )
                        {
        // ************************* For now we’re just print into the chat, maybe we’ll improve it later
                            if (!KillerController.IsBot) KillerController.PrintToChat(Killer.Translate("kills.toadvance", killsPerLevel - kills));

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
                        Respawn(VictimController);
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
            level = ChangeLevel(Killer, 1, false, VictimController);
            if ( oldLevelKiller == level )
            {
                Respawn(VictimController);
                return HookResult.Continue;
            }
            Killer.CurrentLevelPerRound++;
            PrintLeaderToChat(Killer, oldLevelKiller, level);

            if ( Config.TurboMode || Config.KnifeElite)
            {
                GiveNextWeapon(Killer.Slot, usedWeaponInfo.LevelIndex == SpecialWeapon.KnifeLevelIndex);
            }
            CheckForTripleLevel(Killer);
            Respawn(VictimController);
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

            if (attacker != null && victim != null && IsPlayer(attacker.Slot) && IsPlayer(victim.Slot))
            {
                bool found = false;
                if (IsWeaponKnife(weapon)) // if damage by knife
                {
                    if (g_Shot[attacker.Slot, victim.Slot])
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
//                                attacker.PlayerPawn.Value.Render = Color.FromArgb(255, 255, 0, 0);
                                if (victim.PlayerPawn != null && victim.PlayerPawn.Value != null)
                                {
                                    victim.PlayerPawn.Value.Health += eventInfo.DmgHealth;
                                }
                                AddTimer(0.2f, () =>
                                {
                                    attacker.CommitSuicide(true,true);
                                });

                                if (!attacker.IsBot)
                                {
                                    int slot = attacker.Slot;
                                    AddTimer(1.0f, () =>
                                    {
                                        var pc = Utilities.GetPlayerFromSlot(slot);
                                        if (pc != null && pc.IsValid)
                                        {
                                            var pl = playerManager.FindBySlot(slot, "EventPlayerHurtHandler1");
                                            if (pl != null)
                                            {
                                                pc.PrintToCenter(pl.Translate("dontshoot.knife"));
                                            }
                                            else
                                            {
                                                pc.PrintToCenter(Localizer["dontshoot.knife"]);
                                            }
                                        }   
                                    });
                                }
//                                Server.PrintToChatAll(Localizer["triedshoot.knife", attacker.PlayerName]);
                                var playerEntities = GetValidPlayers();
//                                Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV);
                                if (playerEntities != null && playerEntities.Any())
                                {
                                    foreach (var pc in playerEntities)
                                    {
                                        var pl = playerManager.FindBySlot(pc.Slot, "EventPlayerHurtHandler2");
                                        if ( pl != null)
                                        {
                                            pc.PrintToChat(pl.Translate("triedshoot.knife", attacker.PlayerName));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else if (!weapon.Equals("hegrenade") && !weapon.Equals("inferno"))
                {
                    g_Shot[attacker.Slot, victim.Slot] = true;
                }
            }
            return HookResult.Continue;
        }
        private HookResult EventWeaponFireHandler(EventWeaponFire @event, GameEventInfo info)
        {
            if (Config.AfkManagement)
            {
                var playerController = @event.Userid; 
                if (playerController == null || !IsValidPlayer(playerController)) {
                    return HookResult.Continue;
                }
                var client = playerManager.FindBySlot(playerController.Slot, "EventPlayerSpawnHandler");
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
            if (@event != null && @event.Userid != null)
            {
                var playerController = @event.Userid;
                if (!playerController.IsValid)
                    return HookResult.Continue;
                int slot = playerController.Slot;
                if (newTeam == 2 || newTeam == 3)
                {
                    AddTimer (0.4f, () => {
                        if (IsValidPlayer(playerController))
                            Respawn(playerController);
                    });
                }
                if (@event.Isbot || !(newTeam == 2 || newTeam == 3 || oldTeam == 2 || oldTeam == 3))
                {
                    return HookResult.Continue;
                }
                
                AddTimer (0.5f, () => {
                    GGVariables.Instance.Tcount = CountPlayersForTeam(CsTeam.Terrorist);
                    GGVariables.Instance.CTcount = CountPlayersForTeam(CsTeam.CounterTerrorist);
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
                });
                if (disconnect || newTeam == 0)
                {
                    return HookResult.Continue;
                }
                
                if (playerController.IsBot) {
                    return HookResult.Continue;
                }
                var player = playerManager.FindBySlot(playerController.Slot, "EventPlayerTeamHandler");
                if (player == null)
                {
                    Logger.LogError($"[GunGame] EventPlayerTeamHandler: player == null: oldTeam {oldTeam}, newTeam {newTeam}, disconnect {(disconnect ? "yes" : "no")}, slot {playerController.Slot}, {playerController.PlayerName}");
                    return HookResult.Continue;
                }
                if (player.SavedSteamID != 0)
                {
                    if (onlineManager != null && onlineManager.OnlineReportEnable)
                    {
                        if (newTeam == (int)CsTeam.Terrorist)
                            _ = onlineManager.SavePlayerData(player, "t");
                        else if (newTeam == (int)CsTeam.CounterTerrorist)
                            _ = onlineManager.SavePlayerData(player, "ct");
                        else if (newTeam == (int)CsTeam.Spectator)
                            _ = onlineManager.SavePlayerData(player, "spectr");
                    }
                }
                else
                {
                    Logger.LogError($"[GunGame] *********** EventPlayerTeamHandler Error SteamId for {player.PlayerName}");
                }   
                if ( oldTeam >= 2 && newTeam >= 2)
                {
    //                StopTripleEffects(player);
                    player.TeamChange = true;
                }
            }
            return HookResult.Continue;
        }
        private HookResult EventRoundStartHandler<T>(T @event, GameEventInfo info) where T : GameEvent
        {
            GGVariables.Instance.Round++;
            Console.WriteLine($"[GUNGAME]********* Round {GGVariables.Instance.Round} Start");
            Logger.LogInformation($"[GUNGAME]********* Round {GGVariables.Instance.Round} Start");
            if ( !GGVariables.Instance.IsActive)
            {
                return HookResult.Continue;
            }
            if (GGVariables.Instance.Round == 1)
            {
                var points = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("point_servercommand");
                foreach (var point in points)
                {
                    point.Remove();
                }
// Player client crashes here
//                NativeAPI.IssueServerCommand("mp_t_default_secondary  \"\"");
//                NativeAPI.IssueServerCommand("mp_ct_default_secondary  \"\"");
//                NativeAPI.IssueServerCommand("mp_t_default_melee  \"\"");
//                NativeAPI.IssueServerCommand("mp_ct_default_melee  \"\"");
//                NativeAPI.IssueServerCommand("mp_equipment_reset_rounds 0");

            }

            if (GGVariables.Instance.Round == 2 || !Config.WarmupEnabled)
            {
                var playerEntities = GetValidPlayersWithBots();
                if (playerEntities != null && playerEntities.Count > 0)
                {
                    foreach (var playerController in playerEntities)
                    {
                        var client = playerManager.FindBySlot(playerController.Slot, "EventRoundStartHandler");
                        if (client != null)
                        {
                            client.SetLevel(1);
                            UpdatePlayerScoreLevel(playerController.Slot);
                            if (Config.ShootKnifeBlock)
                            {
                                ForgiveShots(playerController.Slot);
                            }
                        }
                    }
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
                // Lock all player since the winner was declare already if new round happened.
                if (Config.WinnerFreezePlayers) {
                    FreezeAllPlayers();
                    return HookResult.Continue;
                }
            }

            /* Only remove the hostages on after it been initialized */
/*            if(GGVariables.Instance.MapStatus.HasFlag(Objectives.Hostage) && GGVariables.Instance.MapStatus.HasFlag(Objectives.RemoveHostage))
            {
                //Delay for 0.1 because data need to be filled for hostage entity index 
                Logger.Instance.Log("Map requires remove hostages");
                AddTimer(1.0f, RemoveHostages);
            } */

//            PlaySoundForLeaderLevel();

            // Disable warmup
/*            if ( Config.WarmupEnabled && GGVariables.Instance.DisableWarmupOnRoundEnd )
            {
                Config.WarmupEnabled = false;
                GGVariables.Instance.DisableWarmupOnRoundEnd = false;
            } */
//            RemoveEntityByClassName("game_player_equip");
            GGVariables.Instance.RoundStarted = true;
            return HookResult.Continue;
        }
        private HookResult EventRoundEndHandler<T>(T @event, GameEventInfo info) where T : GameEvent
        {
            /* Round has ended. */
            LogConnections = false;
            GGVariables.Instance.RoundStarted = false;
            Hot_Reload = false;
            return HookResult.Continue;
        }
        private HookResult EventHegrenadeDetonateHandler(EventHegrenadeDetonate @event, GameEventInfo info)
        {  
            if (@event.Userid == null || !IsValidPlayer(@event.Userid))
            {
                return HookResult.Continue;
            }
            var playerController = @event.Userid;  
            var player = playerManager.FindBySlot(playerController.Slot, "EventHegrenadeDetonateHandler"); 

            if (player == null) {
                return HookResult.Continue;
            }
            if ( ( Config.WarmupNades && warmupInitialized )
                || (player.LevelWeapon != null && player.LevelWeapon.LevelIndex == SpecialWeapon.HegrenadeLevelIndex
                    && ( Config.UnlimitedNades 
                    || ( Config.NumberOfNades > 0 && player.NumberOfNades > 0 ) ) ) )
            {  // Do not give them another nade if they already have one   
                if (!HasWeapon(playerController, "weapon_hegrenade")) 
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
            if (@event.Userid == null || !IsValidPlayer(@event.Userid))
            {
                return HookResult.Continue;
            }
            // future code here to handle pick up items
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
        private static HookResult EventHostageKilledHandler<T>(T @event, GameEventInfo info) where T : GameEvent
        {
            /****************************************************/
            return HookResult.Continue;
        }

/************** Utils **********************************************************/
/************** Utils **********************************************************/
/************** Utils **********************************************************/
        private void GiveWarmUpWeaponDelayed (float delay, int slot)
        {
            AddTimer(delay, () =>
            {
                var playerController = Utilities.GetPlayerFromSlot(slot);
                if (playerController != null && IsValidPlayer(playerController) && playerController.Pawn != null && 
                playerController.Pawn.Value != null && playerController.Pawn.Value.LifeState == (byte)LifeState_t.LIFE_ALIVE ) 
                {
                    var player = playerManager.FindBySlot(slot, "GiveWarmUpWeaponDelayed");
                    if (player == null) {
                        Logger.LogError($"[ERROR] GiveWarmUpWeapon: can't get player for slot {slot}");
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
            if (playerController == null || !IsValidPlayer(playerController) || !IsClientInTeam(playerController) || 
                playerController.Pawn == null || playerController.Pawn.Value == null ||
                playerController.Pawn.Value.LifeState != (byte)LifeState_t.LIFE_ALIVE ) 
            {
                return;
            }
            
            playerController.RemoveWeapons();

            if ( Config.ArmorKevlarHelmet ) playerController.GiveNamedItem("item_assaultsuit");
            bool dropKnife;
            var player = playerManager.FindBySlot(slot, "GiveNextWeapon");
            if (player == null) {
                Logger.LogError($"[ERROR] GiveNextWeapon: cant get player for slot {slot}");
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
                    }
                    if (Config.NadeFlash) {
                        playerController.GiveNamedItem("weapon_flashbang");
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
                    }
                    if (Config.MolotovBonusFlash) {
                        playerController.GiveNamedItem("weapon_flashbang");
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
                    }
                    if (Config.KnifeFlash) {
                        playerController.GiveNamedItem("weapon_flashbang");
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
                        if (player.Level == GGVariables.Instance.WeaponOrderCount) // on the last level
                        {
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
                } 
                else 
                {
                    // LEVEL WEAPON TASER
                    if (Config.TaserSmoke) {
                        playerController.GiveNamedItem("weapon_smokegrenade");
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
                if (Config.FastSwitchOnLevelUp && !GGVariables.Instance.WeaponsSkipFastSwitch.Contains(player.LevelWeapon.FullName))
                {
                    var weapon = playerController.PlayerPawn?.Value?.WeaponServices?.ActiveWeapon?.Value;
                    if (weapon != null && weapon.IsValid)
                    {
                        var wep = new CCSWeaponBase(weapon.Handle);

                        if (wep != null && wep.IsValid)
                        {
                            Server.NextFrame(() =>
                            {
                                wep.NextPrimaryAttackTick = Server.TickCount + 1;
                            });
                        }
                    }
                }
            }

            if (player.LevelWeapon.Slot == 4)  // grenades slot
            { 
                player.UseWeapon(4);
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
                if (!HasWeapon(player, "weapon_hegrenade")) 
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
            if (HasWeapon(player, "weapon_taser"))
            {
                if (IsValidPlayer(player) && player.PlayerPawn != null && player.PlayerPawn.Value != null
                &&  player.PlayerPawn.Value.WeaponServices != null)
                {
                    foreach (var clientWeapon in player.PlayerPawn.Value.WeaponServices.MyWeapons)
                    {
                        if (clientWeapon is { IsValid: true, Value.IsValid: true })
                        {
                            if (clientWeapon.Value.DesignerName.Equals("weapon_taser"))
                            {
                                clientWeapon.Value.Clip1 = 1;
                                Utilities.SetStateChanged(clientWeapon.Value,"CBasePlayerWeapon","m_iClip1");
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                player.GiveNamedItem("weapon_taser");
            }
        }
        private void GiveExtraMolotov (CCSPlayerController player, int weapon_index)
        {
            if (!HasWeapon(player, "weapon_molotov"))
            {
                RemoveGrenades(player);
                player.GiveNamedItem("weapon_molotov");
                if (Config.MolotovBonusSmoke) {
                        player.GiveNamedItem("weapon_smokegrenade");
                }
                if (Config.MolotovBonusFlash) {
                    player.GiveNamedItem("weapon_flashbang");
                }
            }
        }
        private static void ReloadActiveWeapon (GGPlayer player, int weapon_index)
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

                if (GGVariables.Instance.Mp_friendlyfire == null)
                {
                    Console.WriteLine("GGVariables.Instance.mp_friendlyfire == null");
                    return;
                }

                if ( !GGVariables.Instance.Mp_friendlyfire.GetPrimitiveValue<bool>() )
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
            // when we'll learn
        }
        private void FreezeAllPlayers()
        {
            if (GGVariables.Instance.Mp_friendlyfire != null)
            {
                GGVariables.Instance.Mp_friendlyfire.SetValue(false);
            }
            
            var playerEntities = GetValidPlayersWithBots();
//            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var playerController in playerEntities)
                {
                    FreezePlayer(playerController);
                }
            }
        }
        private void FreezePlayer (CCSPlayerController player)
        {
            if (IsValidPlayer(player) && player.PlayerPawn != null && player.PlayerPawn.Value != null
            && (player.TeamNum > 1))
            {
                player.PlayerPawn.Value.MoveType = MoveType_t.MOVETYPE_NONE;
                Schema.SetSchemaValue(player.PlayerPawn.Value.Handle, "CBaseEntity", "m_nActualMoveType", 0);
                Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseEntity", "m_MoveType");
                player.RemoveWeapons();
                player.GiveNamedItem("weapon_knife");
            } 
        }
        private void CheckForTripleLevel(GGPlayer client)
        {
            client.CurrentLevelPerRoundTriple++;
            if ( Config.MultiLevelBonus && client.CurrentLevelPerRoundTriple == Config.MultiLevelAmount )
            {
//                Server.PrintToChatAll(Localizer["player.leveled", client.PlayerName, Config.MultiLevelAmount]);
                var playerEntities = GetValidPlayers();
//                Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV);
                if (playerEntities != null && playerEntities.Any())
                {
                    foreach (var pc in playerEntities)
                    {
                        var pl = playerManager.FindBySlot(pc.Slot, "CheckForTripleLevel");
                        if ( pl != null)
                        {
                            pc.PrintToChat(pl.Translate("player.leveled", client.PlayerName, Config.MultiLevelAmount));
                        }
                    }
                }
                StartTripleEffects(client);
                AddTimer(10.0f, () => {
                    StopTripleEffects(client);
                });
/*
                UTIL_StartTripleEffects(client);
                CreateTimer(10.0, RemoveBonus, client);

                Call_StartForward(FwdTripleLevel);
                Call_PushCell(client);
                Call_Finish(); */
            }
        }
        private void StartTripleEffects(GGPlayer player)
        {
            if (player == null || player.TripleEffects == true)
                return;
            var playerController = Utilities.GetPlayerFromSlot(player.Slot);
            if (playerController == null)
                return;
            player.TripleEffects = true;
            if (playerController.PlayerPawn != null && playerController.PlayerPawn.Value != null)
            {
                if (Config.MultiLevelBonusGodMode)
                {
                    playerController.PlayerPawn.Value.TakesDamage = false;
                }
                if (Config.MultiLevelBonusGravity != 1)
                {
                    playerController.PlayerPawn.Value.GravityScale = Config.MultiLevelBonusGravity;
                }
                if (Config.MultiLevelBonusSpeed != 1)
                {
                    playerController.PlayerPawn.Value.VelocityModifier = Config.MultiLevelBonusSpeed;
                }
                PlaySound(null!, Config.MultiLevelSound);
            }        
        }
        public void PlaySoundDelayed(float delay, CCSPlayerController player, string str)
        {
            AddTimer(delay, () => PlaySound(player, str));
        }
        public void PlaySound(CCSPlayerController playerController, string str)
        {
            if (IsValidPlayer(playerController)) 
            {
                if (!playerController.IsBot)
                {
                    var player = playerManager.FindBySlot(playerController.Slot, "PlaySound pc");
                    if (player != null && player.Music) 
                        playerController.ExecuteClientCommand("play " + str);
    //                    NativeAPI.IssueClientCommand (player.Slot, "play " + str);                
                }
            }
            else
            {
                var playerEntities = GetValidPlayers();
//                Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV);
                if (playerEntities != null && playerEntities.Any())
                {
                    foreach (var pc in playerEntities)
                    {
                        var player = playerManager.FindBySlot(pc.Slot, "PlaySound all");
                        if (player != null && player.Music) 
                            pc.ExecuteClientCommand("play " + str);
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
                Logger.LogError("The sound list is empty or null.");
                return;
            }
            int index = random.Next(soundList.Count);
            var playerEntities = GetValidPlayers();
//            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var playerController in playerEntities)
                {
                    var player = playerManager.FindBySlot(playerController.Slot, "PlayRandomSound");
                    if (player != null && player.Music) 
                        playerController.ExecuteClientCommand("play " + soundList[index]);
                }
            }
        }
        public void PlaySoundForLeaderLevel(int slot = -1)
        {
            if (slot < 0 ) {
                return;
            }
            var player = playerManager.FindBySlot(slot, "PlaySoundForLeaderLevel");
            if (player == null)
            {
                Logger.LogError($"[GunGame] ******* PlaySoundForLeaderLevel: Can't find player from slot {slot}");
                return;
            }
            Weapon? wep = GGVariables.Instance.weaponsList.FirstOrDefault(w => w.Level == player.Level);
            if (wep != null && wep.LevelIndex == SpecialWeapon.HegrenadeLevelIndex)
            {
                PlaySoundDelayed(1.0f, null!, Config.NadeInfoSound);
                return;
            }
            if (wep != null &&  wep.LevelIndex == SpecialWeapon.KnifeLevelIndex) {
                PlaySoundDelayed(2.0f, null!, Config.KnifeInfoSound);
                return;
            }
        }
        private void StopTripleEffects (GGPlayer player)
        {
            if ( player == null || !player.TripleEffects ) {
                return;
            }
            player.CurrentLevelPerRoundTriple = 0;
            player.TripleEffects = false;
            var playerController = Utilities.GetPlayerFromSlot(player.Slot);
            if (playerController == null)
                return;
            if (playerController.PlayerPawn != null && playerController.PlayerPawn.Value != null)
            {
                if ( Config.MultiLevelBonusGodMode ) {
                    playerController.PlayerPawn.Value.TakesDamage = true;
                }
                if ( Config.MultiLevelBonusGravity != 0 ) {
                    playerController.PlayerPawn.Value.GravityScale = 1.0f;
                }
                if ( Config.MultiLevelBonusSpeed != 0) {
                    playerController.PlayerPawn.Value.Speed = 1.0f;
                }
            }
            if ( Config.MultiLevelEffect ) {
                StopEffectClient(playerController);
            }
        }
        private static void StopEffectClient (CCSPlayerController playerController)
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
        private static bool HasWeapon(CCSPlayerController playerController, string weapon)
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
                        if (clientWeapon.Value.DesignerName.Equals(weapon))
                        {
                            found = true;
                            break;
                        }
                    }
                }
            }
            return found;
        }
        private static void RemoveGrenades(CCSPlayerController playerController)
        {
            if (playerController == null)
                return;
            var weapons = playerController.PlayerPawn.Value?.WeaponServices;

            foreach (var weapon in weapons!.MyWeapons)
            {
                if (weapon == null || weapon.Value == null) continue;

                CCSWeaponBaseVData? _weapon = weapon.Value.As<CCSWeaponBase>().VData;

                if (_weapon == null) continue;
                if (_weapon.GearSlot == gear_slot_t.GEAR_SLOT_GRENADES)
                {
                    weapon.Value.Remove();
                }
            }
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
                    var newLeader = playerManager.FindLeader();
                    if ( newLeader != null )
                    {
                        GGVariables.Instance.CurrentLeader.SetLeader(newLeader.Slot, (int)newLeader.Level);
                        if ( newLeader.Slot != slot )
                        {
                            PlaySoundForLeaderLevel(newLeader.Slot);
                        }
                    }
                    else
                    {
                        GGVariables.Instance.CurrentLeader.SetLeader(-1, 0);
                    }
                    return;
                }
                return;  // was not a leader
            }       
            if (GGVariables.Instance.CurrentLeader.Slot < 0)  // newLevel > oldLevel
            {
                GGVariables.Instance.CurrentLeader.SetLeader(slot, newLevel);
                PlaySoundForLeaderLevel(slot);
                return;
            }
            if ( GGVariables.Instance.CurrentLeader.Slot == slot ) // still leading
            {
                PlaySoundForLeaderLevel(slot);
                return;
            }
            if ( newLevel <= GGVariables.Instance.CurrentLeader.Level ) // CurrentLeader != client
            {
                // not leading
                return;
            }
            if ( newLevel > GGVariables.Instance.CurrentLeader.Level )
            {
                GGVariables.Instance.CurrentLeader.SetLeader(slot, newLevel);
                PlaySoundForLeaderLevel(slot); // start leading
                return;
            }
            // new level == leader level // tied to the lead
            PlaySoundForLeaderLevel(slot);
        }
        private static void FindMapObjective()
        {
/*          this part does not work for now. So I'm waiting while the platform will allows us to do this.  
            var Zones = Utilities.FindAllEntitiesByDesignerName<CBombTarget>("func_bomb_target");

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
            if (GGVariables.Instance.Mp_friendlyfire == null)
                return;
            GGVariables.Instance.Mp_friendlyfire.Public = true; // set FCVAR_NOTIFY
            GGVariables.Instance.Mp_friendlyfire.SetValue(Status);
            string text;
            if (Status) {
                text = "friendlyfire.on";
            } else {
                text = "friendlyfire.off";
            }
//            Server.PrintToChatAll(Localizer["friendlyfire.on"]);
            var playerEntities = GetValidPlayers();
//            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var pc in playerEntities)
                {
                    var pl = playerManager.FindBySlot(pc.Slot, "ChangeFriendlyFire");
                    if ( pl != null)
                    {
                        pc.PrintToCenter(pl.Translate(text));
                    }
                }
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
            var playerEntities = GetValidPlayersWithBots();
//            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var playerController in playerEntities)
                {
                    var player = playerManager.FindBySlot(playerController.Slot, "Timer_HandicapUpdate"); 
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
                        if (!playerController.IsBot) 
                        {
                            var pl = playerManager.FindBySlot(playerController.Slot, "Timer_HandicapUpdate");
                            if (pl != null)
                            {
                                playerController.PrintToChat(pl.Translate("handicap.updated"));
                            }
                            else
                            {
                                playerController.PrintToChat(Localizer["handicap.updated"]);
                            }
                        }
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
            var playerEntities = GetValidPlayersWithBots();
//            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var playerController in playerEntities)
                {
                    if ( playerController.TeamNum > 0 && ( Config.HandicapUseSpectators || playerController.TeamNum > 1 ))
                    {
                        if ( ( skipBots && playerController.IsBot ) || ( skipClient == playerController.Slot ) )
                        {
                            continue;
                        }
                        var player = playerManager.FindBySlot(playerController.Slot, "GetHandicapMinimumLevel");
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
            var playerEntities = GetValidPlayersWithBots();
//            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var playerController in playerEntities)
                {
                    if ( playerController.TeamNum > 0 && ( Config.HandicapUseSpectators || playerController.TeamNum > 1 ))
                    {
                        if ( ( skipBots && playerController.IsBot ) || ( skipClient == playerController.Slot ) )
                        {
                            continue;
                        }
                        var player = playerManager.FindBySlot(playerController.Slot, "GetAverageLevel");
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
                Logger.LogInformation($"Give Handicap level to {player.PlayerName} ({player.Slot}), up from {player.Level} to {level}");
                player.SetLevel(level);
                player.CurrentKillsPerWeap = 0;
                UpdatePlayerScoreLevel(player.Slot);
            }
            return true;
        }
        private static bool IsPlayerWinsLoaded(GGPlayer player)
        {
            return player.PlayerWins > -1;
        }
        private bool IsPlayerInTopRank(GGPlayer player)
        {
            if (HandicapTopWins == 0)
            {
                return false;
            }
            return player.PlayerWins >= HandicapTopWins;
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
            string text;
            if (loose > 1) {
                text = "suiside.levels";
            }
            else
            {
                text = "suiside.alevel";
            }
            var playerEntities = GetValidPlayers();
//            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var pc in playerEntities)
                {
                    var pl = playerManager.FindBySlot(pc.Slot, "ClientSuicide");
                    if ( pl != null)
                    {
                        pc.PrintToCenter(pl.Translate(text, player.PlayerName, loose));
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
                if ( Config.ShowLeaderWeapon && player.LevelWeapon.Index > 0) 
                {
                    var pe = GetValidPlayers();
//                    Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV);
                    if (pe != null && pe.Any())
                    {
                        foreach (var pc in pe)
                        {
                            var pl = playerManager.FindBySlot(pc.Slot, "PrintLeaderToChat1");
                            if ( pl != null)
                            {
                                pc.PrintToCenter(pl.Translate("leading.onweapon", player.PlayerName, player.LevelWeapon.Name));
                            }
                        }
                    }
                } else {
//                    Server.PrintToChatAll(Localizer["leading.onlevel", player.PlayerName, newLevel]);
                    var pe = GetValidPlayers();
//                    Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV);
                    if (pe != null && pe.Any())
                    {
                        foreach (var pc in pe)
                        {
                            var pl = playerManager.FindBySlot(pc.Slot, "PrintLeaderToChat2");
                            if ( pl != null)
                            {
                                pc.PrintToCenter(pl.Translate("leading.onlevel", player.PlayerName, newLevel));
                            }
                        }
                    }
                }
                return;
            }
            // CurrentLeader != client
            if ( newLevel < GGVariables.Instance.CurrentLeader.Level )
            {
                var playerController = Utilities.GetPlayerFromSlot(player.Slot);
                if (playerController != null && IsValidHuman(playerController))
                {
                    // say how much to the lead
                    playerController.PrintToChat(player.Translate("levels.behind", GGVariables.Instance.CurrentLeader.Level-newLevel));
                }
                return;
            }
            // new level == leader level
            // say tied to the lead on level X
            var playerEntities = GetValidPlayers();
//            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var pc in playerEntities)
                {
                    var pl = playerManager.FindBySlot(pc.Slot, "PrintLeaderToChat3");
                    if ( pl != null)
                    {
                        pc.PrintToCenter(pl.Translate("tiedwith.leader", player.PlayerName, newLevel));
                    }
                }
            }
        }
        private int ChangeLevel(GGPlayer player, int difference, bool KnifeSteal = false, CCSPlayerController CounterpartController = null!)
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
            bool knife = false;
            Weapon? wep = GGVariables.Instance.weaponsList.FirstOrDefault(w => w.Level == Level);
            if (wep != null && wep.LevelIndex == SpecialWeapon.KnifeLevelIndex)
            {
                knife = true;
            }
            int counterpart = -1;
            if (CounterpartController != null)
                counterpart = CounterpartController.Slot;

//            bool accept = RaiseLevelChangeEvent(player.Slot, Level, difference, KnifeSteal, Level == GGVariables.Instance.WeaponOrderCount, knife, counterpart);
            bool accept = CoreAPI.RaiseLevelChangeEvent(player.Slot, Level, difference, KnifeSteal, Level == GGVariables.Instance.WeaponOrderCount, knife, counterpart);
            if (!accept)
            {
                Logger.LogInformation($"******** killer {player.PlayerName} - changeLevel not accepted");
                Console.WriteLine("******** ChangeLevel not accepted");
                return oldLevel;
            }

            if ( !GGVariables.Instance.IsVotingCalled && Level > (GGVariables.Instance.WeaponOrderCount - Config.VoteLevelLessWeaponCount) )
            {
                GGVariables.Instance.IsVotingCalled = true;
                Server.ExecuteCommand("exec " + GGVariables.Instance.ActiveConfigFolder + "/gungame.mapvote.cfg");
            }

            if ( Config.DisableRtvLevel > 0 && !GGVariables.Instance.IsCalledDisableRtv && Level >= Config.DisableRtvLevel )
            {
                GGVariables.Instance.IsCalledDisableRtv = true;
                Server.ExecuteCommand("exec " + GGVariables.Instance.ActiveConfigFolder + "/gungame.disable_rtv.cfg");
            }
            
            if ( Config.EnableFriendlyFireLevel > 0 && !GGVariables.Instance.IsCalledEnableFriendlyFire && Level >= Config.EnableFriendlyFireLevel  )
            {
                GGVariables.Instance.IsCalledEnableFriendlyFire = true;
                if ( Config.FriendlyFireOnOff ) {
                    ChangeFriendlyFire(true);
                } else {
                    ChangeFriendlyFire(false);
                }
            } 
            
            if ( Level > GGVariables.Instance.WeaponOrderCount )
            {
                /* Winner Winner Winner */
                GGVariables.Instance.GameWinner = new(player);
                Logger.LogInformation($"Winner {player.PlayerName}");
                string fontColour;
                int winnerTeam = player.GetTeam();
                if (winnerTeam == 2 )
                {
                    fontColour = "#FF5959";
                }
                else if (winnerTeam == 3 )
                {
                    fontColour = "#00BFFF";
                }
                else 
                {
                    fontColour = "#FFFFFF";
                }
                if (CounterpartController != null && CounterpartController.IsValid)
                {
                    GGVariables.Instance.LooserName = CounterpartController.PlayerName;
                }
                else
                {
                    GGVariables.Instance.LooserName = "";
                }
                WinnerMessage[0] += fontColour;

//                Server.PrintToChatAll(Localizer["winner.is", player.PlayerName]);
                Listeners.OnTick onTick = new(OnTickHandle);
                RegisterListener(onTick);
                AddTimer(15.0f, () => {
                    RemoveListener(onTick);
                }); 
                /*
                int r = (team == TEAM_T ? 255 : 0);
                int g =  team == TEAM_CT ? 128 : (team == TEAM_T ? 0 : 255);
                int b = (team == TEAM_CT ? 255 : 0);
                UTIL_PrintToUpperLeft(r, g, b, "%t", "Has won", Name); */

                int winnerSlot = player.Slot;
                int looserSlot = -1;
                if (CounterpartController != null && CounterpartController.IsValid && !CounterpartController.IsBot)
                {
                    looserSlot = CounterpartController.Slot;
                }

                if (!(Config.DontAddWinsOnBot && CounterpartController != null && CounterpartController.IsValid && CounterpartController.IsBot))
                {
//                    RaiseWinnerEvent(winnerSlot, looserSlot);
                    CoreAPI.RaiseWinnerEvent(winnerSlot, looserSlot);
                    SavePlayerWins(player);
                }
                
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
                return oldLevel;
            }

            // Client got new level
            player.SetLevel(Level);
            RecalculateLeader(player.Slot, oldLevel, Level);
            if ( KnifeSteal && Config.KnifeProRecalcPoints && (oldLevel != Level) ) {
                player.CurrentKillsPerWeap = player.CurrentKillsPerWeap * GetCustomKillPerLevel(Level) / GetCustomKillPerLevel(oldLevel);
            } else {
                player.CurrentKillsPerWeap = 0;
            }
            
            var pc = Utilities.GetPlayerFromSlot(player.Slot);
            if (pc != null && IsValidPlayer(pc))
            {
                if (onlineManager != null && onlineManager.OnlineReportEnable && player != null)
                {
                    if (pc.TeamNum == (int)CsTeam.Terrorist)
                        _ = onlineManager.SavePlayerData(player, "t");
                    else if (pc.TeamNum == (int)CsTeam.CounterTerrorist)
                        _ = onlineManager.SavePlayerData(player, "ct");
                    else if (pc.TeamNum == (int)CsTeam.Spectator)
                        _ = onlineManager.SavePlayerData(player, "spectr");
                }
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
//            UpdatePlayerScoreDelayed(player);

            return Level;
        }
        public void SavePlayerWins(GGPlayer player)
        {
            Logger.LogInformation($"{player.PlayerName} won, wins was {player.PlayerWins}");
            if (player.PlayerWins < 0)
            {
                Logger.LogError($"{player.PlayerName} win, but his PlayerWins is less than 0, so data need to be updated");
            }
            if (player.SavedSteamID == 0)
            {
                Logger.LogError($"{player.PlayerName} slot {player.Slot} win, but his SteamID is 0, so data can't be saved");
                return;
            }
            if (statsManager != null)
                dbQueue.EnqueueOperation(async () => await statsManager.SavePlayerWin(player));
//            _ = statsManager?.SavePlayerWin(player);
        }
        public void ForgiveShots(int client)
        {
            // Forgive player for attacking with a gun
            for (int vict = 0; vict <= Models.Constants.MaxPlayers; vict++)
                g_Shot[client, vict] = false;
            // Forgive players who attacked with a gun
            for (int attck = 0; attck <= Models.Constants.MaxPlayers; attck++)
                g_Shot[attck, client] = false;
        }
        private void EndMultiplayerGameDelayed()
        {
            LogConnections = false;
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
                    var playerEntities = GetValidPlayers();
//                    Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV);
                    if (playerEntities != null && playerEntities.Any())
                    {
                        foreach (var playerController in playerEntities)
                        {
                            var pl = playerManager.FindBySlot(playerController.Slot, "EndMultiplayerGame");
                            if (pl != null)
                            {
                                playerController.PrintToCenter(pl.Translate("mapend.left", seconds));
                            }
                            else
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
//                Logger.LogInformation($"Call EndMultiplayerGameSilent now");
                EndMultiplayerGameSilent();
            } else {
//                Logger.LogInformation($"Call EndMultiplayerGameNormal now");
                EndMultiplayerGameNormal();
            }
        }
        private static void EndMultiplayerGameSilent ()
        {
            Console.WriteLine("EndMultiplayerGameSilent");
            var gameEnd = NativeAPI.CreateEvent("game_end", true);
            NativeAPI.SetEventInt(gameEnd,"winner",2);
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
        public static string RemoveWeaponPrefix(string input)
        {
            const string prefix = "weapon_";

            if (input.StartsWith(prefix))
            {
                return input.Substring(prefix.Length);
            }

            return input;
        }
        private int HumansPlay()
        {
            return Utilities.GetPlayers()
            .Where(p => IsValidHuman(p) && (p.TeamNum == 2 || p.TeamNum == 3)).Count();
        }
        public async void StatsLoadRank()
        {
//            StatsSQLManager _statsManager = new(dbConnectionString);
            if (statsManager == null)
            {
                // Handle the case where statsManager is null (maybe log an error, throw an exception, or handle it appropriately)
                return;
            }
		    int TotalWinners = await statsManager.GetNumberOfWinners();
            if (Config.HandicapTopRank == 0)
            {
                HandicapTopWins = 0;
                return;
            }
            if (Config.HandicapTopRank >= TotalWinners)
            {
                HandicapTopWins = 1; // Handicap top wins = 1 (handicap top rank is more then total winners
                return;
            }
            HandicapTopWins = await statsManager.GetWinsOfLowestTopPlayer(Config.HandicapTopRank);
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
        private static bool IsPlayer(int slot)
        {
            return slot > -1 && slot <= Models.Constants.MaxPlayers;
        }
        public int CountPlayersForTeam(CsTeam team)
        {
            return Utilities.GetPlayers()
            .Where(
                player =>
                IsValidPlayer(player)
                && player.Team == team
            )
            .Count();
        }
        private void OnTickHandle()
        {
            string wname;
            if (GGVariables.Instance.GameWinner != null)
            {
                wname = GGVariables.Instance.GameWinner.Name;
            }
            else
            {
                wname = "Winner";
            }
            var playerEntities = GetValidPlayers();
//            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var playerController in playerEntities)
                {
                    var pl = playerManager.FindBySlot(playerController.Slot, "OnTickHandle");
                    if (pl != null)
                    {
                        string text = WinnerMessage[0] + WinnerMessage[1] + pl.Translate("winner.is",wname) + WinnerMessage[2] + pl.Translate("looser.is", GGVariables.Instance.LooserName) + WinnerMessage[3];
                        playerController.PrintToCenterHtml(text);
                    }
                }
            }
        }
        private void TimerInfo()
        {
            string message = "";
            if (GGVariables.Instance.InfoMessages.Count > 0)
            {
                message = GGVariables.Instance.InfoMessages[GGVariables.Instance.InfoMessageIndex];

                // Increment the index for the next message
                GGVariables.Instance.InfoMessageIndex++;

                // Reset the index if it reaches the end of the list
                if (GGVariables.Instance.InfoMessageIndex >= GGVariables.Instance.InfoMessages.Count)
                {
                    GGVariables.Instance.InfoMessageIndex = 0;
                }
            }
            var playerEntities = GetValidPlayers();
//            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var player in playerEntities)
                {
                    var pl = playerManager.FindBySlot(player.Slot, "TimerInfo");
                    if (pl != null)
                    {
                        player.PrintToChat(pl.Translate(message));
                    }                        
                }
            }
        }     
        public static string? GetPlayerIp(CCSPlayerController player)
        {
            var playerIp = player.IpAddress;
            if (playerIp == null) { return null; }
            string[] parts = playerIp.Split(':');
            if (parts.Length == 2)
            {
                return parts[0];
            }
            else
            {
                return playerIp;
            }
        }
        public static bool IsValidIP(string input)
		{
			string pattern = @"^(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$";

			return Regex.IsMatch(input, pattern);
		}
/*        public int GetMaxLevel()
        {
            return GGVariables.Instance.WeaponOrderCount;
        } */
        [ConsoleCommand("music", "Turn On/off GG sounds")]
        public async void OnMusicCommand(CCSPlayerController? playerController, CommandInfo command)
        {
            if (playerController != null && playerController.IsValid)
            {
                var player = playerManager.FindBySlot(playerController.Slot, "OnMusicCommand");
                if (player != null)
                {
                    if (statsManager != null)
                    {
                        bool success = await statsManager.ToggleSound(player);
                        Server.NextFrame (() => {
                            if (playerController != null && playerController.IsValid && playerController.Connected == PlayerConnectedState.PlayerConnected)
                            {
                                if (success)
                                {
                                    playerController.PrintToChat(player.Translate("music.success"));
                                }
                                else
                                {
                                    playerController.PrintToChat(player.Translate("music.error"));
                                }
                            }
                        });
                    }
                    else
                    {
                        playerController.PrintToChat(player.Translate("database.error"));
                    }
                }
            }
        }
        [ConsoleCommand("top", "Top GG players")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
        public async void OnTopCommand(CCSPlayerController? playerController, CommandInfo command)
        {
            if (playerController != null && playerController.IsValid)
            {   
                var player = playerManager.FindBySlot(playerController.Slot, "OnTopCommand");
                if (player == null)
                {
                    return;
                }
                if (statsManager == null)
                {
                    playerController.PrintToChat(player.Translate("database.error"));
                    return;
                }
                Dictionary<string,int> TopPlayers = await statsManager.GetTopPlayers(Config.HandicapTopRank);
                Server.NextFrame(() =>
                {
                    if (playerController != null && playerController.IsValid && playerController.Connected == PlayerConnectedState.PlayerConnected)
                    {
                        if (TopPlayers.Count == 0)
                        {
                            playerController.PrintToChat(player.Translate("notop.records"));
                            return;
                        }
                        var topMenu = new ChatMenu("Top GunGame");
                        foreach (var pl in TopPlayers)
                        {
                            if (pl.Value == 0)
                            {
                                break;
                            }
                            topMenu.AddMenuOption($"{pl.Key} - {pl.Value}", TopMenuHandle);
                        }
                        MenuManager.OpenChatMenu(playerController, topMenu);
                    }
                });
            }
        }
        [ConsoleCommand("gg_reset", "Reset GG stats")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
        [RequiresPermissions("@css/root")]
        public async void OnDBResetCommand(CCSPlayerController? playerController, CommandInfo command)
        {
            if (playerController != null && playerController.IsValid)
            {  
                if (statsManager == null)
                {
                    var player = playerManager.FindBySlot(playerController.Slot, "OnDBResetCommand");
                    if (player != null)
                    {
                        playerController.PrintToChat(player.Translate("database.error"));
                    }
                    return;
                }
                await statsManager.ResetStats();
                Server.NextFrame(() =>
                {
                    if (playerController != null && playerController.IsValid && playerController.Connected == PlayerConnectedState.PlayerConnected)
                    {   
                        playerController.PrintToChat("Stats reseted");
                    }
                });
            }
        }
        [ConsoleCommand("rank", "GG rank of the player")]
        public async void OnRankCommand(CCSPlayerController? playerController, CommandInfo command)
        {
            if (playerController != null && playerController.IsValid)
            {  
                var player = playerManager.FindBySlot(playerController.Slot, "OnRankCommand");
                if (player != null)
                {
                    if (statsManager == null)
                    {
                        playerController.PrintToChat(player.Translate("database.error"));
                        return;
                    }
                    int rank = await statsManager.GetPlayerRank(playerController.SteamID.ToString());
                    Server.NextFrame(() =>
                    {
                        if (playerController != null && playerController.IsValid && playerController.Connected == PlayerConnectedState.PlayerConnected)
                        {  
                            if (rank >= 0)
                                playerController.PrintToChat(player.Translate("player.rank", rank));
                            else
                                playerController.PrintToChat(player.Translate("database.error", rank));
                        }
                    });
                }
            }
        }
        void TopMenuHandle(CCSPlayerController caller, ChatMenuOption option)
        {
            // just to not confuse top menu
        }
        [ConsoleCommand("gg_version", "GunGame plugin version")]
        public void OnGGVersion(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null && player.IsValid)
            {
                player.PrintToChat($"GunGame Version: {ModuleVersion}");
                player.PrintToChat($"Website https://github.com/ssypchenko/cs2-gungame");
            }
            else
            {
                Console.WriteLine($"GunGame Version: {ModuleVersion}");
                Console.WriteLine($"Website https://github.com/ssypchenko/cs2-gungame");
            }
        }

        [ConsoleCommand("gg_restart", "Restart game")]
        [RequiresPermissions("@css/rcon")]
        public void OnRestartCommand(CCSPlayerController? playerController, CommandInfo command)
        {
            Server.ExecuteCommand("sv_cheats 1; endround; sv_cheats 0;");
            RestartGame();
//            Load(true);
        }

        [ConsoleCommand("gg_enable", "Enable GunGame")]
        [RequiresPermissions("@css/rcon")]
        public void OnEnableCommand(CCSPlayerController? playerController, CommandInfo command)
        {
            Config.IsPluginEnabled = true;
            Server.ExecuteCommand("sv_cheats 1; endround; sv_cheats 0;");
            Load(true);
        }

        [ConsoleCommand("gg_disable", "Disable GunGame")]
        [RequiresPermissions("@css/rcon")]
        public void OnDisableCommand(CCSPlayerController? playerController, CommandInfo command)
        {
            Config.IsPluginEnabled = false;
            Server.ExecuteCommand("sv_cheats 1; endround; sv_cheats 0;");
            Load(true);
        }
        [ConsoleCommand("css_lang", "Set Player's Language")]
        public async void OnLangCommand(CCSPlayerController? playerController, CommandInfo command)
        {
            if (playerController == null || !playerController.IsValid) { return; }
            if (command.ArgCount < 2) { return; }
            string isoCode = command.GetArg(1);
            // Idk check for ISO by length :^))
            if (isoCode.Length != 2) { return; }
            var player = playerManager.FindBySlot(playerController.Slot, "OnLangCommand");
            if (player != null)
            {
                bool success = await player.UpdateLanguage(isoCode);
                Server.NextFrame(() =>
                {
                    if (playerController != null && playerController.IsValid && playerController.Connected == PlayerConnectedState.PlayerConnected)
                    { 
                        if (success)
                        {
                            playerController.PrintToChat(player.Translate("update.successful"));
                        }
                        else
                        {
                            playerController.PrintToChat(player.Translate("update.unsuccessful"));
                        }
                    }
                });
            }
        }
        [GameEventHandler(HookMode.Post)]
        [ConsoleCommand("gg_config", "Set config folder")]
        [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
        public void OnGGConfigCommand(CCSPlayerController? playerController, CommandInfo command)
        {
            if (command.ArgCount < 2) 
            { 
                Console.WriteLine("Usage: gg_config <configfoldername>");
                return; 
            }
            string newConfig = Server.GameDirectory + "/csgo/cfg/" + command.GetArg(1) + 
            "/gungame.json";
            if (!File.Exists(newConfig))
            {
                Console.WriteLine("Config missing at " + newConfig);
                Logger.LogError("Config missing at " + newConfig);
                return;
            }
            GGVariables.Instance.ActiveConfigFolder = command.GetArg(1);
            RestartGame();
            Logger.LogInformation("Config changed to " + command.GetArg(1));
        }
        [ConsoleCommand("gg_respawn", "Set Respawn by plugin")]
        [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
        public void OnTurnRespawnCommand(CCSPlayerController? playerController, CommandInfo command)
        {
            if (command.ArgCount < 2) { return; }
            if (int.TryParse(command.GetArg(1), out int intValue))
            {
                if (intValue == 4 && GGVariables.Instance.spawnPoints[4].Count < 1)
                    intValue = 3;
                SetSpawnRules(intValue);
            }
            else
            {
                Console.WriteLine($"Error call gg_respawn with arg {command.GetArg(1)}");
                Logger.LogError($"Error call gg_respawn with arg {command.GetArg(1)}");
            }
        }
        [ConsoleCommand("gg_distance", "Set Respawn Distance")]
        [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
        public void OnRespawnDistance(CCSPlayerController? playerController, CommandInfo command)
        {
            if (playerController == null || !playerController.IsValid) { return; }
            if (command.ArgCount < 2) { return; }

            if (double.TryParse(command.GetArg(1), out double distance))
            {
                Config.SpawnDistance = distance;
                Console.WriteLine($"Spawn Distance changed to {Config.SpawnDistance}.");
            }
            else
            {
                Console.WriteLine("String could not be parsed to double.");
            }
        }
        public HookResult OnChat(EventPlayerChat @event, GameEventInfo info)
        {
            _ = updateLang(@event.Userid, @event.Text);
            return HookResult.Continue;
        }
        private async Task updateLang(int userid, string text)
        {
            var pc = Utilities.GetPlayerFromUserid(userid);
            if (pc is null || !pc.IsValid )
                return;

            if (text.StartsWith("lang"))
            {
                string[] words = text.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (words.Length > 1)
                {
                    string isoCode = words[1];
                    if (isoCode.Length != 2) { return; }
                    var player = playerManager.FindBySlot(pc.Slot, "updateLang");
                    if (player != null)
                    {
                        bool success = await player.UpdateLanguage(isoCode);
                        Server.NextFrame(() =>
                        {
                            if (pc != null && pc.IsValid && pc.Connected == PlayerConnectedState.PlayerConnected)
                            { 
                                if (success)
                                {
                                    pc.PrintToChat(player.Translate("update.successful"));
                                }
                                else
                                {
                                    pc.PrintToChat(player.Translate("update.unsuccessful"));
                                }
                            }
                        });
                    }
                }
            }
            return;
        }
        private void Respawn(CCSPlayerController player, bool spawnpoint = true)
        {
            if (Config.RespawnByPlugin == RespawnType.Disabled)
                return;

            if (!IsValidPlayer(player))
                return;

            CCSPlayerController pl = player;
            if ((Config.RespawnByPlugin == RespawnType.OnlyT && player.TeamNum != 2)
                || (Config.RespawnByPlugin == RespawnType.OnlyCT && player.TeamNum != 3)
                || (player.TeamNum != 2 && player.TeamNum != 3))
            {
                return;
            }
            AddTimer(1.0f, () =>
            {
                if (!IsValidPlayer(pl) || pl.PlayerPawn == null || !pl.PlayerPawn.IsValid || pl.PlayerPawn.Value == null) return;
                double thisDeathTime = Server.EngineTime;
                double deltaDeath = thisDeathTime - LastDeathTime[pl.Slot];
                LastDeathTime[pl.Slot] = thisDeathTime;
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

            if (!GGVariables.Instance.spawnPoints.ContainsKey(spawnType))
            {
                Logger.LogError($"SpawnPoints not ContainsKey {spawnType}");
                return null!;
            }

            // Shuffle the spawn points list to randomize the selection process
            var shuffledSpawns = new List<SpawnInfo>(GGVariables.Instance.spawnPoints[spawnType]);

            // Shuffle the copy
            shuffledSpawns.Shuffle();
            foreach (var spawn in shuffledSpawns)
            {
                if (!playerManager.IsPlayerNearby(slot, spawn.Position, minDistance))
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
        private void SetSpawnRules(int spawnType)
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
        public List<CCSPlayerController> GetValidPlayers()
		{
			return Utilities.GetPlayers().FindAll(p => p != null && p.IsValid && p.SteamID.ToString().Length == 17 && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV);
		}
		public List<CCSPlayerController> GetValidPlayersWithBots()
		{
			return Utilities.GetPlayers().FindAll(p =>
			p != null && p.IsValid && p.SteamID.ToString().Length == 17 && p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV ||
			p != null && p.IsValid && p.Connected == PlayerConnectedState.PlayerConnected && p.IsBot && !p.IsHLTV
			);
		}
        public bool IsValidPlayer(CCSPlayerController? p)
        {
            if (p != null && p.IsValid && (p.SteamID.ToString().Length == 17 || (p.SteamID == 0 && p.IsBot)) && 
                p.Connected == PlayerConnectedState.PlayerConnected && !p.IsHLTV)
            {
                if (!p.PlayerPawn.IsValid)
                {
                    Logger.LogInformation($"Name {p.PlayerName} ({p.Slot}) {p.SteamID} - PlayerPawn is not valid");
                }
                return true;
            }
            return false;
        }
        public bool IsValidHuman(CCSPlayerController? p)
        {
            if (p != null && p.IsValid && p.SteamID.ToString().Length == 17 && 
                p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV)
            {
                return true;
            }
            return false;
        }
    }
}
// Colors Available = "{default} {white} {darkred} {green} {lightyellow}" "{lightblue} {olive} {lime} {red} {lightpurple}"
                      //"{purple} {grey} {yellow} {gold} {silver}" "{blue} {darkblue} {bluegrey} {magenta} {lightred}" "{orange}"