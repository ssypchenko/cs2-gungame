#pragma warning disable CS8981// Naming Styles
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using MaxMind.GeoIP2;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
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
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CSTrace = CounterStrikeSharp.API.Modules.Utils.Trace;
using CSTraceOptions = CounterStrikeSharp.API.Modules.Utils.TraceOptions;
using Newtonsoft.Json;
using GunGame.API;
using GunGame.Models;
using GunGame.Variables;
using GunGame.Stats;
using GunGame.Online;
using System.Security.Principal;
using System.Net;
using System.Runtime.Intrinsics.X86;
using Serilog;

namespace GunGame
{
    [MinimumApiVersion(372)]
    public partial class GunGame : BasePlugin
    {
        public GunGame(IStringLocalizer<GunGame> localizer)
        {
            playerManager = new(this);
            soundMapper = new(this);
            _localizer = localizer;
        }
        public readonly IStringLocalizer<GunGame> _localizer;
        public PlayerLanguageManager playerLanguageManager = new();
        public override string ModuleName => "CS2_GunGame";
        public override string ModuleVersion => "v1.2.10";
        public override string ModuleAuthor => "Sergey";
        public override string ModuleDescription => "GunGame mode for CS2";
        public CoreAPI CoreAPI { get; set; } = null!;
        private static PluginCapability<IAPI> APICapability { get; } = new("gungame:api");
        //        public bool LogConnections = false;
        public bool WeaponLoaded = false;
        public bool warmupInitialized = false;
        private int WarmupCounter = 0;
        private bool runtimeGameEnabled;
        private int mapGeneration;
        //        private bool IsObjectiveHooked = false;
        public GGConfig Config { get; set; } = new();
        public DatabaseSettings dbSettings = new();
        public StatsManager? statsManager { get; set; }
        public OnlineManager? onlineManager { get; set; }
        public DatabaseOperationQueue? dbQueue { get; set; }
        /*        public void OnConfigParsed (GGConfig config)
                {
                    this.Config = config;
                    GGVariables.Instance.MapStatus = (Objectives)Config.RemoveObjectives;
                } */
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
            Config = new();
            Logger.LogInformation("Loading config: csgo/cfg/" + GGVariables.Instance.ActiveConfigFolder + "/gungame.json");
            string configPath = Server.GameDirectory + "/csgo/cfg/" + GGVariables.Instance.ActiveConfigFolder +
            "/gungame.json";
            try
            {
                string jsonString = File.ReadAllText(configPath);

                if (string.IsNullOrEmpty(jsonString))
                {
                    Logger.LogError("Error loading config: csgo/cfg/" + GGVariables.Instance.ActiveConfigFolder + "/gungame.json is wrong or empty");
                    return false;
                }

                var cnfg = System.Text.Json.JsonSerializer.Deserialize<GGConfig>(jsonString);
                if (cnfg != null)
                {
                    bool updateRequired = false;
                    foreach (var property in typeof(GGConfig).GetProperties())
                    {
                        var loadedValue = property.GetValue(cnfg);
                        var defaultValue = property.GetValue(Config);

                        if (loadedValue == null)
                        {
                            Logger.LogInformation($"[GunGame] Missing property in Config file: {property.Name}");
                            property.SetValue(cnfg, defaultValue);
                            updateRequired = true;
                        }
                    }

                    if (updateRequired)
                    {
                        //                        var updatedJsonContent = System.Text.Json.JsonSerializer.Serialize(cnfg, new JsonSerializerOptions { WriteIndented = true });
                        //                        File.WriteAllText(configPath, updatedJsonContent);
                        Logger.LogError("[GunGame] *************  Config update required due to missing or obsolete keys.");
                    }
                    Config = cnfg;
                    GGVariables.Instance.MapStatus = (Objectives)Config.RemoveObjectives;
                    GGVariables.Instance.WeaponsSkipFastSwitch = Config.FastSwitchSkipWeapons.Split(',')
                        .Select(weapon => $"weapon_{weapon.Trim()}").ToList();
                    LogHandicapDebug(
                        $"Configuration loaded: mode={Config.HandicapMode}, updateSeconds={Config.HandicapUpdate}, " +
                        $"topRankHandicap={Config.TopRankHandicap}, handicapTopRank={Config.HandicapTopRank}, " +
                        $"skipBots={Config.HandicapSkipBots}, useSpectators={Config.HandicapUseSpectators}, " +
                        $"timesPerMap={Config.HandicapTimesPerMap}.");
                }
                else
                {
                    Logger.LogError("Error deserialize config, csgo/cfg/" + GGVariables.Instance.ActiveConfigFolder + "/gungame.json is wrong or empty");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GunGame] Error reading or deserializing /csgo/cfg/{GGVariables.Instance.ActiveConfigFolder}/gungame.json file: {ex.Message}");
                return false;
            }
            SetSpawnRules(Config.RespawnByPlugin);
            soundMapper.Initialize(Config);
            return true;
        }
        public SoundMapper soundMapper;
        public readonly record struct SoundInfo(string SoundValue, bool IsRandom, List<string> SoundList = null!);
        public PlayerManager playerManager;
        public Dictionary<ulong, int> PlayerLevelsBeforeDisconnect = new();
        public Dictionary<ulong, int> PlayerHandicapTimes = new();
        public HashSet<int> SkipSpawn = new();
        public List<CCSWeaponBaseGun> MapWeaponList = new();
        private CounterStrikeSharp.API.Modules.Timers.Timer? warmupTimer = null;
        private CounterStrikeSharp.API.Modules.Timers.Timer? endGameTimer = null;
        private CounterStrikeSharp.API.Modules.Timers.Timer? emergencyTimer = null;
        private CounterStrikeSharp.API.Modules.Timers.Timer? winnerHintTimer = null;
        private Listeners.OnTick? winnerHintTick;

        private CounterStrikeSharp.API.Modules.Timers.Timer? _infoTimer = null;
        int endGameCount = 0;
        private CounterStrikeSharp.API.Modules.Timers.Timer? HandicapUpdateTimer = null;
        public Dictionary<string, WeaponInfo> Weapon_from_List = new Dictionary<string, WeaponInfo>();
        public SpecialWeaponInfo SpecialWeapon = new();
        public Random random = new();
        public List<SpawnInfo> LastSpawns = new List<SpawnInfo>();
        public bool[,] g_Shot = new bool[65, 65];
        private double[] LastDeathTime = new double[65];
        private List<string> WinnerMessage = new()
        {
            "<font color='",
            "'>",
            "<br>",
            "</font>"
        };
        private string LooserName = "";
        public int HandicapTopWins = 0; // number of wins of a lowest player in TopRank restricted by number HandicapTopRank
        private DateTime lastRoundStartEventTime = DateTime.MinValue;
        private static readonly object spawnLock = new object(); // Lock for thread safety
        private static readonly HashSet<Vector> usedSpawnPoints = new HashSet<Vector>(); // Stores currently used spawn points
        private const int RandomNavSpawnMaxAttempts = 96;
        private const float RandomNavSpawnHullHalfWidth = 16.0f;
        private const float RandomNavSpawnHullHeight = 72.0f;
        private const float RandomNavSpawnAreaMargin = 18.0f;
        private const float RandomNavSpawnFloorProbeUp = 16.0f;
        private const float RandomNavSpawnFloorProbeDown = 48.0f;
        private const float RandomNavSpawnFloorOffset = 2.0f;
        private const float RandomNavSpawnApplyDelay = 0.10f;
        private const float RandomNavSpawnVerifyDelay = 0.10f;
        private const float RandomNavSpawnVerifyTolerance = 96.0f;
        private readonly List<CCSNavArea> randomNavSpawnAreas = new();
        private readonly HashSet<int> skipRandomNavSpawnOnce = new();
        private bool randomNavSpawnLoadAttempted;
        private bool randomNavSpawnFallbackWarningLogged;
        private bool randomNavSpawnFailureWarningLogged;
        private static readonly HashSet<string> NonKnifeDamageWeapons = new HashSet<string>(StringComparer.Ordinal)
        {
            "hegrenade",
            "inferno",
            "molotov",
            "incgrenade"
        };
        private bool IsRespawnServiceActive =>
            Config.RespawnByPlugin > 0 &&
            (GGVariables.Instance.IsActive || Config.RespawnWhenGunGameModeDisabled);
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
        public override void OnAllPluginsLoaded(bool hotReload)
        {
            //
        }
        public override void Load(bool hotReload)
        {
            PublishMotdUrl();
            CoreAPI = new CoreAPI(this);
            if (CoreAPI != null)
            {
                Capabilities.RegisterPluginCapability(APICapability, () => CoreAPI);
                Logger.LogInformation("API registered");
            }
            LoadDBConfig();
            if (LoadConfig())
            {
                runtimeGameEnabled = Config.IsPluginEnabled;
                var tempCulture = playerLanguageManager.GetDefaultLanguage();
                GGVariables.Instance.ServerLanguageCode = tempCulture.Name.ToLower();
                SetupListeners();
                RegisterEvents();
                SetupGameWeapons();
                SetupWeaponsLevels();

                if (Config.IsPluginEnabled && runtimeGameEnabled)
                {
                    if (WeaponLoaded)
                    {
                        GGVariables.Instance.IsActive = true;
                        if (hotReload)
                        {
                            Logger.LogInformation("[GUNGAME] Hot Reload");
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
                    else
                    {
                        Console.WriteLine("Weapons are not loaded on Load. Plugin is inactive");
                        Logger.LogError("Weapons are not loaded on Load. Plugin is inactive");
                    }
                }
                else
                {
                    Console.WriteLine("Plugin is disabled in Config - inactive");
                    Logger.LogError("Plugin is disabled in Config - inactive");
                }
            }
            else
            {
                Console.WriteLine("Error loading config on Load. Plugin is inactive");
                Logger.LogError("Error loading config on Load. Plugin is inactive");
            }
        }
        public override void Unload(bool hotReload)
        {
            StopGlobalTimers();
            foreach (var player in playerManager.GetPlayers())
            {
                StopTripleEffects(player);
            }
            playerManager.Clear();
            dbQueue?.Stop();
            dbQueue = null;
            DeregisterEventHandler<EventPlayerDeath>(EventPlayerDeathHandler);
            DeregisterEventHandler<EventPlayerHurt>(EventPlayerHurtHandler);
            DeregisterEventHandler<EventPlayerTeam>(EventPlayerTeamHandler);
            DeregisterEventHandler<EventPlayerSpawn>(EventPlayerSpawnHandler);
            DeregisterEventHandler<EventRoundStart>(EventRoundStartHandler);
            DeregisterEventHandler<EventRoundEnd>(EventRoundEndHandler);
            DeregisterEventHandler<EventHegrenadeDetonate>(EventHegrenadeDetonateHandler);
            DeregisterEventHandler<EventWeaponFire>(EventWeaponFireHandler);
            /*                DeregisterEventHandler<EventItemPickup>(EventItemPickupHandler, HookMode.Post);
                        DeregisterEventHandler<EventBombPlanted>(EventBombHandler);
                        DeregisterEventHandler<EventBombExploded>(EventBombHandler);
                        DeregisterEventHandler<EventBombDefused>(EventBombHandler);
                        DeregisterEventHandler<EventBombPickup>(EventBombPickupHandler);
                        DeregisterEventHandler<EventHostageKilled>(EventHostageKilledHandler); */

            RemoveListener<Listeners.OnClientConnected>(OnClientConnected);
            RemoveListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
            RemoveListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
            RemoveListener<Listeners.OnMapStart>(OnMapStart);
            RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
            RemoveListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        }
        private void RestartGame(bool reloadConfig = true)
        {
            GGVariables.Instance.IsActive = false;
            if (reloadConfig && !LoadConfig())
            {
                Logger.LogError("Error loading config on Restart command. Plugin is inactive");
                return;
            }

            if (reloadConfig)
            {
                runtimeGameEnabled = Config.IsPluginEnabled;
            }

            if (!Config.IsPluginEnabled || !runtimeGameEnabled)
            {
                Logger.LogInformation("GunGame mode is inactive; respawn service remains configured separately.");
                return;
            }

            SetupGameWeapons();
            SetupWeaponsLevels();
            if (!WeaponLoaded)
            {
                Logger.LogError("Weapons are not loaded on Restart command. Plugin is inactive");
                return;
            }

            GGVariables.Instance.IsActive = true;
            GGVariables.Instance.RestartGame = true;
            GG_Startup();
            FindMapObjective();
            StatsLoadRank();
            foreach (var playerController in GetValidPlayersWithBots())
            {
                var player = playerManager.CreatePlayerBySlot(playerController.Slot);
                if (player != null)
                {
                    StopTripleEffects(player);
                    player.ResetPlayer();
                }
            }

            GGVariables.Instance.Tcount = CountPlayersForTeam(CsTeam.Terrorist);
            GGVariables.Instance.CTcount = CountPlayersForTeam(CsTeam.CounterTerrorist);
            ConVar.Find("mp_restartgame")?.SetValue(1);
            StopTimer(ref warmupTimer);
            warmupInitialized = false;
            GGVariables.Instance.WarmupFinished = !Config.WarmupEnabled;
            LoadSpawnPoints();
        }
        private void GG_Startup()
        {
            Logger.LogInformation($"[GUNGAME] GG Start, version {ModuleVersion}");

            InitVariables();

            if (Config.HandicapUpdate > 0)
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
            WeaponLoaded = false;
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
            bool weaponListValid = false;
            if (WeaponSettings.WeaponOrder != null)
            {
                weaponListValid = MakeWeaponList(WeaponSettings.WeaponOrder, WeaponSettings.RandomWeaponReserveLevels, WeaponSettings.RandomWeaponOrder);
            }
            if (weaponListValid)
                WeaponLoaded = true;
        }
        private static void SetCustomKillPerLevel(Dictionary<int, int> multipleKillsPerLevel)
        {
            GGVariables.Instance.CustomKillsPerLevel.Clear();
            foreach (var kvp in multipleKillsPerLevel)
            {
                if (kvp.Key > 0 && kvp.Value > 0)
                {
                    if (!GGVariables.Instance.CustomKillsPerLevel.ContainsKey(kvp.Key))
                    {
                        GGVariables.Instance.CustomKillsPerLevel.Add(kvp.Key, kvp.Value);
                    }
                }
            }
        }
        private bool MakeWeaponList(Dictionary<int, string> weaponOrder, string randomWeaponReserveLevels, bool randomOrder)
        {
            List<string> result = new();
            bool hasErrors = false;

            if (randomOrder)
            {
                result = RandomizeWeaponOrder(weaponOrder, randomWeaponReserveLevels);
            }
            else
            {
                // Use the original order
                for (int level = 1; level <= GGVariables.Instance.WeaponOrderCount; level++)
                {
                    if (weaponOrder.TryGetValue(level, out var weaponName))
                    {
                        result.Add(weaponName);
                    }
                    else
                    {
                        Logger.LogError($"[ERROR] Weapon order is missing level {level} in gungame_weapons.json");
                        result.Add("");
                        hasErrors = true;
                    }
                }
            }

            for (int i = 0; i < GGVariables.Instance.WeaponOrderCount; i++)
            {
                int level = i + 1;
                if (i >= result.Count || string.IsNullOrWhiteSpace(result[i]))
                {
                    Logger.LogError($"[ERROR] Weapon order level {level} is empty in gungame_weapons.json");
                    hasErrors = true;
                    continue;
                }

                if (TryGetWeaponInfo(result[i], out WeaponInfo weaponInfo))
                {
                    if (string.IsNullOrWhiteSpace(weaponInfo.FullName) && weaponInfo.LevelIndex != SpecialWeapon.KnifeLevelIndex)
                    {
                        Logger.LogError($"[ERROR] Weapon '{result[i]}' at level {level} has an empty fullname in weapons.json");
                        hasErrors = true;
                        continue;
                    }

                    GGVariables.Instance.weaponsList.Add(new Weapon
                    {
                        Level = level,
                        Name = result[i],
                        FullName = weaponInfo.FullName,
                        Index = weaponInfo.Index,
                        LevelIndex = weaponInfo.LevelIndex,
                        Slot = weaponInfo.Slot,
                        ClipSize = weaponInfo.Clipsize,
                        Ammo = weaponInfo.AmmoType
                    });
                }
                else
                {
                    Logger.LogError($"[ERROR] Weapon order level {level} references unknown weapon '{result[i]}'. Check weapons.json");
                    hasErrors = true;
                }
            }

            if (GGVariables.Instance.weaponsList.Count != GGVariables.Instance.WeaponOrderCount)
            {
                Logger.LogError($"[ERROR] Weapon order has {GGVariables.Instance.weaponsList.Count} valid levels out of {GGVariables.Instance.WeaponOrderCount}");
                hasErrors = true;
            }

            return !hasErrors && GGVariables.Instance.weaponsList.Count == GGVariables.Instance.WeaponOrderCount;
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
            /*            RegisterEventHandler<EventItemPickup>(EventItemPickupHandler, HookMode.Post);
                        RegisterEventHandler<EventBombPlanted>(EventBombHandler);
                        RegisterEventHandler<EventBombExploded>(EventBombHandler);
                        RegisterEventHandler<EventBombDefused>(EventBombHandler);
                        RegisterEventHandler<EventBombPickup>(EventBombPickupHandler);
                        RegisterEventHandler<EventHostageKilled>(EventHostageKilledHandler); */
        }
        private void SetupListeners()
        {
            RegisterListener<Listeners.OnClientConnected>(OnClientConnected);
            RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
            RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
            RegisterListener<Listeners.OnMapStart>(OnMapStart);
            RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
            RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        }
        private void OnClientConnected(int slot)
        {
            var player = playerManager.CreatePlayerBySlot(slot);
            if (player == null)
            {
                Logger.LogError($"[GUNGAME]* OnClientConnected: Can't create player {slot}");
                return;
            }
        }
        private void OnClientPutInServer(int slot)
        {
            var playerController = Utilities.GetPlayerFromSlot(slot);
            if (playerController == null || !playerController.IsValid)
            {
                Logger.LogError($"[GUNGAME]* OnClientPutInServer: Wrong playerController slot {slot}");
                return;
            }
            var player = playerManager.CreatePlayerBySlot(slot);
            if (player == null)
            {
                Logger.LogError($"[GUNGAME]* OnClientPutInServer: Can't create player {slot}");
                return;
            }
            if (GGVariables.Instance.IsActive)
            {
                if (playerController.AuthorizedSteamID != null)
                {
                    var previousSteamId = player.SavedSteamID;
                    if (player.SavedSteamID != playerController.AuthorizedSteamID.SteamId64)
                    {
                        player.UpdatePlayerController(playerController);
                    }
                    playerManager.RegisterSteamId(player, previousSteamId);
                    if (Config.RestoreLevelOnReconnect)
                    {
                        if (PlayerLevelsBeforeDisconnect.TryGetValue(player.SavedSteamID, out int level))
                        {
                            if (player.Level < level)
                            {
                                Logger.LogInformation($"[GUNGAME]* OnClientPutInServer: Restore level {level} to {player.PlayerName}");
                                player.SetLevel(level); // saved level
                                RecalculateLeader(player.Slot, 0);
                            }
                            PlayerLevelsBeforeDisconnect.Remove(player.SavedSteamID);
                        }
                    }
                    UpdatePlayerScoreLevel(player);
                    player.LevelRestore = false;
                    CancelAuthorisationRetry(player);
                    QueuePlayerStatsLoad(player);
                }
                else
                {
                    player.LevelRestore = true; // if Authorisation later than put in server - flag for authorisation to do stuff
                }
            }
            player.PutInServer = true;
            Logger.LogInformation($"{player.PlayerName} ({slot}) put in server");
        }
        private void OnClientAuthorized(int slot, SteamID id)
        {
            var playerController = Utilities.GetPlayerFromSlot(slot);
            if (playerController == null || !playerController.IsValid)
            {
                Logger.LogError($"[GUNGAME]* OnClientAuthorized: Wrong playerController slot {slot} SteamId {id.SteamId64}");
                return;
            }
            var player = playerManager.CreatePlayerBySlot(slot);
            if (player == null)
            {
                Logger.LogError($"[GUNGAME]* OnClientAuthorized: Can't create player {slot} SteamId {id.SteamId64}");
                return;
            }

            if (playerController.AuthorizedSteamID == null)
            {
                Logger.LogInformation($"{playerController.PlayerName} has AuthorizedSteamID - null");
                if (player.AuthorisationRetryTimer is not null)
                {
                    return;
                }
                if (++player.IdAttempts > 5)
                {
                    CancelAuthorisationRetry(player);
                    if (Config.AllowKickNotConfirmedSteamID && !playerController.IsBot && playerController.UserId.HasValue)
                    {
                        Logger.LogInformation($"{player.PlayerName} kicked because of 6 unsuccessful authorisation attempts.");
                        Server.ExecuteCommand($"kickid {playerController.UserId.Value} NoSteamId");
                    }
                    else
                    {
                        Logger.LogWarning($"{player.PlayerName} has no confirmed SteamID and is excluded from GunGame.");
                    }
                    return;
                }
                player.AuthorisationRetryTimer = AddTimer(5.0f, () =>
                {
                    player.AuthorisationRetryTimer = null;
                    if (!playerManager.IsCurrentPlayer(slot, player))
                    {
                        return;
                    }

                    var retryPlayerController = Utilities.GetPlayerFromSlot(slot);
                    if (retryPlayerController == null || !retryPlayerController.IsValid || retryPlayerController.SteamID != id.SteamId64)
                    {
                        return;
                    }

                    OnClientAuthorized(slot, id);
                }, TimerFlags.STOP_ON_MAPCHANGE);
                return;
            }
            CancelAuthorisationRetry(player);
            player.IdAttempts = 0;
            var previousSteamId = player.SavedSteamID;
            if (player.SavedSteamID != playerController.AuthorizedSteamID.SteamId64)
            {
                player.UpdatePlayerController(playerController);
                Logger.LogInformation($"[GunGame] Update playerController for {player.PlayerName} ({playerController.Slot})");
            }
            playerManager.RegisterSteamId(player, previousSteamId);
            var playerIP = GetPlayerIp(playerController);
            if (playerIP != null && IsValidIP(playerIP))
            {
                player.IP = playerIP;
            }
            else
            {
                Logger.LogInformation($"[GUNGAME]* OnClientPutInServer: bad player IP {player.PlayerName} {playerIP}");
            }
            bool restoreLevel = player.LevelRestore;
            player.LevelRestore = false;
            if (GGVariables.Instance.IsActive && restoreLevel) //player was not restored in PutInServer
            {
                if (Config.RestoreLevelOnReconnect)
                {
                    if (PlayerLevelsBeforeDisconnect.TryGetValue(player.SavedSteamID, out int level))
                    {
                        if (player.Level < level)
                        {
                            Logger.LogInformation($"[GUNGAME]* OnClientAuthorised: Restore level {level} to {player.PlayerName}");
                            player.SetLevel(level); // saved level
                            RecalculateLeader(player.Slot, 0);
                        }
                        PlayerLevelsBeforeDisconnect.Remove(player.SavedSteamID);
                    }
                }
                UpdatePlayerScoreLevel(player);
                QueuePlayerStatsLoad(player);
            }
        }
        private void QueuePlayerStatsLoad(GGPlayer player)
        {
            var stats = statsManager;
            if (stats == null || !player.TryQueueStatsLoad(player.SavedSteamID))
            {
                return;
            }

            dbQueue?.EnqueueOperation(() => stats.GetPlayerWins(player));
        }
        public void OnStatsDatabaseReady()
        {
            LogHandicapDebug("Statistics database is ready. Reloading the Top Rank threshold and queued player statistics.");
            StatsLoadRank();
            foreach (var player in playerManager.GetPlayers())
            {
                QueuePlayerStatsLoad(player);
            }
        }
        private static void CancelAuthorisationRetry(GGPlayer player)
        {
            var retryTimer = player.AuthorisationRetryTimer;
            player.AuthorisationRetryTimer = null;
            retryTimer?.Kill();
        }
        private static void StopTimer(ref CounterStrikeSharp.API.Modules.Timers.Timer? timer)
        {
            var timerToKill = timer;
            timer = null;
            if (timerToKill == null)
            {
                return;
            }

            try
            {
                timerToKill.Kill();
            }
            catch (Exception)
            {
            }
        }
        private void StopGlobalTimers()
        {
            StopTimer(ref warmupTimer);
            StopTimer(ref endGameTimer);
            StopTimer(ref emergencyTimer);
            StopTimer(ref winnerHintTimer);
            StopTimer(ref _infoTimer);
            StopTimer(ref HandicapUpdateTimer);
            if (winnerHintTick != null)
            {
                RemoveListener(winnerHintTick);
                winnerHintTick = null;
            }
        }
        private void ClearMapState()
        {
            PlayerLevelsBeforeDisconnect.Clear();
            PlayerHandicapTimes.Clear();
            SkipSpawn.Clear();
            skipRandomNavSpawnOnce.Clear();
            randomNavSpawnAreas.Clear();
            randomNavSpawnLoadAttempted = false;
            randomNavSpawnFallbackWarningLogged = false;
            randomNavSpawnFailureWarningLogged = false;
            MapWeaponList.Clear();
            Array.Clear(g_Shot);
            Array.Clear(LastDeathTime);
            lock (spawnLock)
            {
                usedSpawnPoints.Clear();
            }

            GGVariables.Instance.Round = 0;
            GGVariables.Instance.Tcount = 0;
            GGVariables.Instance.CTcount = 0;
            GGVariables.Instance.PlayerOnGrenade = 0;
            GGVariables.Instance.RoundStarted = false;
            GGVariables.Instance.CurrentLeader.SetLeader(-1, 0);
            GGVariables.Instance.GameWinner = null;
            GGVariables.Instance.IsVotingCalled = false;
            GGVariables.Instance.IsCalledEnableFriendlyFire = false;
            GGVariables.Instance.IsCalledDisableRtv = false;
            GGVariables.Instance.InfoMessageIndex = 0;
            endGameCount = 0;
            LooserName = "";
            WinnerMessage[0] = "<font color='";
            lastRoundStartEventTime = DateTime.MinValue;
        }
        private void OnMapStart(string name)
        {
            mapGeneration++;
            StopGlobalTimers();
            playerManager.Clear();
            ClearMapState();
            PublishMotdUrl();
            if (emergencyTimer != null)
            {
                var timerToKill = emergencyTimer;
                Server.NextFrame(() =>
                {
                    if (timerToKill != null)
                    {
                        try
                        {
                            timerToKill.Kill();
                        }
                        catch (System.Exception)
                        {

                        }
                    }
                });
                emergencyTimer = null;
            }
            GGVariables.Instance.IsActive = false;
            if (onlineManager != null && onlineManager.OnlineReportEnable)
            {
                _ = onlineManager.ClearAllPlayerData(name);
            }
            if (LoadConfig())
            {
                runtimeGameEnabled = Config.IsPluginEnabled;
                SetupGameWeapons();
                SetupWeaponsLevels();
                if (Config.IsPluginEnabled && runtimeGameEnabled)
                {
                    if (WeaponLoaded)
                    {
                        GGVariables.Instance.IsActive = true;
                        GG_Startup();
                        FindMapObjective();
                        /*                    if ( !IsObjectiveHooked )
                                            {
                                                if (GGVariables.Instance.MapStatus.HasFlag(Objectives.Bomb))
                                                {
                                                    IsObjectiveHooked = true;
                                                }

                                                if (GGVariables.Instance.MapStatus.HasFlag(Objectives.Hostage))
                                                {
                                                    IsObjectiveHooked = true;

                                                }
                                            } */
                        StatsLoadRank();
                        /*                        if (Config.WarmupEnabled)
                                                {
                                                    StartWarmupRound();
                                                } */
                    }
                }
                else
                {
                    Logger.LogInformation("GunGame mode is disabled in Config; respawn service remains configured separately.");
                }
            }
            else
            {
                Logger.LogError("Error loading config on Restart command. Plugin is inactive");
            }
            /*            AddTimer(4.0f, () => // грузятся в OnRoundStart
                        {
                            LoadSpawnPoints();
                        }); */
            Logger.LogInformation($"[GunGame] map {Server.MapName} loaded");
        }
        private void OnMapEnd()
        {
            GGVariables.Instance.IsActive = false;
            StopGlobalTimers();
            playerManager.Clear();
            ClearMapState();
            warmupInitialized = false;
            GGVariables.Instance.WarmupFinished = false;
            /*            if ( IsObjectiveHooked )
                        {
                            if (GGVariables.Instance.MapStatus.HasFlag(Objectives.Bomb))
                            {
                                IsObjectiveHooked = false;

                            }

                            if (GGVariables.Instance.MapStatus.HasFlag(Objectives.Hostage))
                            {
                                IsObjectiveHooked = false;

                            }
                        } */
        }
        private void LoadSpawnPoints()
        {
            GGVariables.Instance.spawnPoints = new();
            var tSpawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_terrorist");
            var ctSpawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_player_counterterrorist");
            var dmSpawns = Utilities.FindAllEntitiesByDesignerName<SpawnPoint>("info_deathmatch_spawn");

            GGVariables.Instance.spawnPoints[2] = new();
            GGVariables.Instance.spawnPoints[3] = new();
            GGVariables.Instance.spawnPoints[4] = new();

            LastSpawns = new List<SpawnInfo>();
            while (LastSpawns.Count <= 4)
            {
                LastSpawns.Add(new SpawnInfo(new Vector(), new QAngle())); // Adding default value
            }
            foreach (var entity in tSpawns)
            {
                if (entity != null && entity.IsValid && entity.AbsOrigin != null && entity.AbsRotation != null)
                {
                    GGVariables.Instance.spawnPoints[2].Add(new SpawnInfo(entity.AbsOrigin, entity.AbsRotation));
                }
                //                    LastSpawns[2] = new SpawnInfo(new Vector(), new QAngle());
            }

            foreach (var entity in ctSpawns)
            {
                if (entity != null && entity.IsValid && entity.AbsOrigin != null && entity.AbsRotation != null)
                {
                    GGVariables.Instance.spawnPoints[3].Add(new SpawnInfo(entity.AbsOrigin, entity.AbsRotation));
                }
                //                    LastSpawns[3] = new SpawnInfo(new Vector(), new QAngle());
            }
            foreach (var entity in dmSpawns)
            {
                if (entity != null && entity.IsValid && entity.AbsOrigin != null && entity.AbsRotation != null)
                {
                    GGVariables.Instance.spawnPoints[4].Add(new SpawnInfo(entity.AbsOrigin, entity.AbsRotation));
                }
                //                    LastSpawns[4] = new SpawnInfo(new Vector(), new QAngle());
            }
            if (GGVariables.Instance.spawnPoints[4].Count < 1 && Config.RespawnByPlugin == 4)
            {
                Logger.LogWarning($"No DM spawn points found ({GGVariables.Instance.spawnPoints[4].Count}), change RespawnByPlugin to 0 (no respawn by plugin)");
                Config.RespawnByPlugin = 0;
            }

            if (Config.RespawnByPlugin == 5)
            {
                LoadRandomNavSpawnAreas();
            }

            Logger.LogInformation($"***** Read {GGVariables.Instance.spawnPoints[3].Count} ct spawn, {GGVariables.Instance.spawnPoints[2].Count} t spawn, {GGVariables.Instance.spawnPoints[4].Count} dm spawn");
            SetSpawnRules(Config.RespawnByPlugin);
        }
        private void OnClientDisconnect(int slot)
        {
            var player = playerManager.FindBySlot(slot, "OnClientDisconnect");
            if (player == null)
            {
                return;
            }
            CancelAuthorisationRetry(player);
            if (!player.PutInServer)
            {
                playerManager.ForgetPlayer(player.Slot);
                return;
            }
            if (onlineManager != null && onlineManager.OnlineReportEnable)
            {
                _ = onlineManager.RemovePlayerData(player);
            }
            if (GGVariables.Instance.IsActive)
            {
                if (player.SavedSteamID != 0 && !PlayerLevelsBeforeDisconnect.TryGetValue(player.SavedSteamID, out int existingLevel))
                {
                    PlayerLevelsBeforeDisconnect[player.SavedSteamID] = (int)player.Level;
                }

                if (GGVariables.Instance.CurrentLeader.Slot == slot)
                {
                    int playerLevel = (int)player.Level;
                    player.SetLevel(0);
                    RecalculateLeader(slot, playerLevel, 0);
                    if (GGVariables.Instance.CurrentLeader.Slot == slot)
                    {
                        GGVariables.Instance.CurrentLeader.SetLeader(-1, 0);
                    }
                }
                if (Config.AutoFriendlyFire && player.State.HasFlag(PlayerStates.GrenadeLevel))
                {
                    player.State &= ~PlayerStates.GrenadeLevel;

                    if (--GGVariables.Instance.PlayerOnGrenade < 1)
                    {
                        GGVariables.Instance.PlayerOnGrenade = 0;
                        if (Config.FriendlyFireOnOff)
                        {
                            ChangeFriendlyFire(false);
                        }
                        else
                        {
                            ChangeFriendlyFire(true);
                        }
                    }
                }
            }
            playerManager.ForgetPlayer(player.Slot);
        }
        private void InitVariables()
        {
            ClearMapState();
            GGVariables.Instance.MapStatus = 0;
            GGVariables.Instance.HostageEntInfo = 0;
            GGVariables.Instance.FirstRound = !Config.WarmupEnabled;
            GGVariables.Instance.WarmupFinished = !Config.WarmupEnabled;

            GGVariables.Instance.Mp_friendlyfire = ConVar.Find("mp_friendlyfire");
        }
        public void StartWarmupRound()
        {
            if (!Config.WarmupEnabled)
            {
                return;
            }

            Console.WriteLine("[GunGame]********** Start WarmupRound");
            Logger.LogInformation("[GunGame]********** Start WarmupRound");
            warmupInitialized = true;
            WarmupCounter = 0;
            if (warmupTimer == null)
            {
                warmupTimer = AddTimer(1.0f, EndOfWarmup, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            }
            else
            {
                Logger.LogInformation("warmupTimer is not null but should be");
            }
            Server.ExecuteCommand("exec " + GGVariables.Instance.ActiveConfigFolder + "/gungame.warmupstart.cfg");
        }
        public void EndOfWarmup()
        {
            if ((GGVariables.Instance.CTcount + GGVariables.Instance.Tcount) == 0)
            {
                WarmupCounter = 0;
                return;
            }

            if (++WarmupCounter < Config.WarmupTimeLength)
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

                        if ((Config.WarmupTimeLength - WarmupCounter) == 4)
                        {
                            PlayConfiguredSound("WarmupTimer");
                            //                            PlaySound(null!, Config.WarmupTimerSound);
                        }
                    }
                }
                return;
            }
            var mp_restartgame = ConVar.Find("mp_restartgame");
            GGVariables.Instance.FirstRound = true; // to do necessary staff on the first round after warmup

            if (mp_restartgame != null)
            {
                mp_restartgame.SetValue((int)1);
                if (warmupTimer != null)
                {
                    warmupTimer.Kill();
                    warmupTimer = null;
                }
                warmupInitialized = false;
                GGVariables.Instance.WarmupFinished = true;
                /*                AddTimer(1.0f, () => {
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
                                }); */

                Server.ExecuteCommand("exec " + GGVariables.Instance.ActiveConfigFolder + "/gungame.warmupend.cfg");
            }
            Console.WriteLine("WarmUp End");

            /*            AddTimer(2.5f, () =>
                        {
                            var entities = Utilities.FindAllEntitiesByDesignerName<CCSWeaponBaseGun>("weapon_");
                            foreach (var entity in entities)
                            {
                                if (entity != null && entity.IsValid
                                    && entity.State == CSWeaponState_t.WEAPON_NOT_CARRIED
                                    && entity.DesignerName.StartsWith("weapon_"))
                                {
                                    MapWeaponList.Add(entity);
                                }
                            }
                        });
                        AddTimer(3.0f, () =>
                        {
                            if (MapWeaponList.Count > 0)
                            {
                                foreach (var entity in MapWeaponList)
                                {
                                    if (entity != null && entity.IsValid && entity.State == CSWeaponState_t.WEAPON_NOT_CARRIED)
                                    {
                                        entity.Remove();
                                    }
                                }
                                MapWeaponList.Clear();
                            }
                        }); */
        }
        /**************  Events **********************************************************/
        /**************  Events **********************************************************/
        /**************  Events **********************************************************/
        private HookResult EventPlayerSpawnHandler(EventPlayerSpawn @event, GameEventInfo info)
        {
            //            Process currentProc = Process.GetCurrentProcess();
            if (@event == null || @event.Userid == null)
            {
                return HookResult.Continue;
            }
            var playerController = @event.Userid;
            if (playerController == null || !IsValidPlayer(playerController) || !IsClientInTeam(playerController))
            {
                //                Logger.LogError($"PlayerSpawn {@event.Userid.Slot} - bad playerController");
                return HookResult.Continue;
            }

            if (Config.RespawnByPlugin == 5 && IsRespawnServiceActive)
            {
                ScheduleRandomNavSpawn(playerController.Slot);
            }

            if (!GGVariables.Instance.IsActive)
            {
                return HookResult.Continue;
            }
            var client = playerManager.FindBySlot(playerController.Slot, "EventPlayerSpawnHandler");
            if (client == null)
            {
                Logger.LogError($"[GUNGAME]PlayerSpawnHandler: Can't find player for {playerController.PlayerName}");
                return HookResult.Continue;
            }
            client.TeamNum = playerController.TeamNum;
            SkipSpawn.Remove(client.Slot);
            client.CancelRespawnTimers();
            if (Config.AfkManagement)
            {
                int clientSlot = client.Slot;
                long clientConnectionId = client.ConnectionId;
                AddTimer(0.3f, () =>
                {
                    if (!playerManager.TryGetCurrentPlayer(clientSlot, clientConnectionId, out var currentPlayer) || currentPlayer == null)
                        return;

                    var currentController = Utilities.GetPlayerFromSlot(clientSlot);
                    if (currentController != null && IsValidPlayer(currentController) && TryGetPlayerPawn(currentController, out var pawn))
                    {
                        var angles = pawn.EyeAngles;
                        var origin = pawn.CBodyComponent?.SceneNode?.AbsOrigin;

                        currentPlayer.Angles = new QAngle(
                            x: angles?.X,
                            y: angles?.Y,
                            z: angles?.Z
                        );

                        currentPlayer.Origin = new Vector(
                            x: origin?.X,
                            y: origin?.Y,
                            z: origin?.Z
                        );
                    }
                }, TimerFlags.STOP_ON_MAPCHANGE);
            }
            if (client.LevelWeapon == null)
            {
                Logger.LogError($"[GUNGAME]PlayerSpawnHandler: {playerController.PlayerName} slot {client.Slot} does not have LevelWeapon");
            }
            UpdatePlayerScoreLevel(client);
            client.TeamChange = false;

            // Reset Knife Elite state
            if (Config.KnifeElite)
            {
                client.State &= ~PlayerStates.KnifeElite;
            }
            if (!client.State.HasFlag(PlayerStates.FirstJoin))
            {
                client.State |= PlayerStates.FirstJoin;

                if (Config.ShootKnifeBlock)
                {
                    ForgiveShots(client.Slot);
                }
                if (!playerController.IsBot)
                {
                    //                    PlaySoundDelayed(1.5f, client, "Welcome");

                    //*** Show join message.

                    if (Config.JoinMessage)
                    {
                        /**************************************************************************************/
                        //ShowJoinMsgPanel(client);
                    }
                }

                if (!warmupInitialized
                && !GGVariables.Instance.StatsEnabled || IsPlayerWinsLoaded(client)
                    ) // HINT: gungame_stats
                {
                    SetHandicapForClient(client);
                }
            }
            /* For deathmatch when they get respawn after round start freeze after game winner. */
            if (GGVariables.Instance.GameWinner != null)
            {
                if (Config.WinnerFreezePlayers)
                {
                    AddTimer(0.4f, () =>
                    {
                        FreezePlayer(playerController);
                    }, TimerFlags.STOP_ON_MAPCHANGE);
                }
                return HookResult.Continue;
            }
            client.CurrentLevelPerRound = 0;
            client.CurrentLevelPerRoundTriple = 0;

            if ((CsTeam)playerController.TeamNum == CsTeam.CounterTerrorist)
            {
                if (GGVariables.Instance.MapStatus.HasFlag(Objectives.Bomb) && !GGVariables.Instance.MapStatus.HasFlag(Objectives.RemoveBomb))
                {
                    // Give them a defuser if objective is not removed
                    //                    SetEntData(client, OffsetDefuser, 1);
                }
            }
            if (Config.WarmupEnabled && !GGVariables.Instance.WarmupFinished)
            {
                GiveWarmUpWeaponDelayed(0.5f, client.Slot);
                return HookResult.Continue;
            }
            int weaponSlot = client.Slot;
            long weaponConnectionId = client.ConnectionId;
            AddTimer(0.1f, () =>
            {
                if (playerManager.TryGetCurrentPlayer(weaponSlot, weaponConnectionId, out _))
                    GiveNextWeapon(weaponSlot, false, true);
            }, TimerFlags.STOP_ON_MAPCHANGE);
            int Level = (int)client.Level;
            int killsPerLevel = GetCustomKillPerLevel(Level);
            if (!playerController.IsBot)
            {
                if (!Config.ShowSpawnMsgInHintBox)
                {
                    playerController.PrintToCenter(client.Translate("your.level", Level, GGVariables.Instance.WeaponOrderCount));
                    if (Config.ShowLeaderInHintBox && GGVariables.Instance.CurrentLeader.Slot > -1)
                    {
                        int leaderLevel = (int)GGVariables.Instance.CurrentLeader.Level;
                        if (client.Level == GGVariables.Instance.CurrentLeader.Level)
                        {
                            playerController.PrintToChat(client.Translate("you.leader"));
                        }
                        else if (Level == leaderLevel)
                        {
                            playerController.PrintToChat(client.Translate("onleader.level"));
                        }
                        else
                        {
                            playerController.PrintToChat(client.Translate("leader.level", leaderLevel));
                        }
                    }
                    if (Config.MultiKillChat && (killsPerLevel > 1))
                    {
                        playerController.PrintToChat(client.Translate("kills.toadvance", killsPerLevel - client.CurrentKillsPerWeap));
                    }
                }
                else
                {
                    playerController.PrintToChat(client.Translate("your.level", Level, GGVariables.Instance.WeaponOrderCount));

                    if (Config.MultiKillChat && (killsPerLevel > 1))
                    {
                        playerController.PrintToChat(client.Translate("kills.toadvance", killsPerLevel - client.CurrentKillsPerWeap));
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
            bool headshot = @event.Headshot;

            if (VictimController == null || !IsValidPlayer(VictimController))
            {
                return HookResult.Continue;
            }
            if (!GGVariables.Instance.IsActive)
            {
                if (IsRespawnServiceActive)
                {
                    Respawn(VictimController);
                }
                return HookResult.Continue;
            }
            var Victim = playerManager.FindBySlot(VictimController.Slot, "EventPlayerDeathHandler");
            if (Victim == null)
            {
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

            CCSPlayerController KillerController = null!;
            GGPlayer Killer = null!;
            if (@event.Attacker != null)
            {
                KillerController = @event.Attacker;

                var ggkiller = playerManager.FindBySlot(KillerController.Slot);
                if (ggkiller != null)
                {
                    Killer = ggkiller;
                    UpdatePlayerScoreLevel(Killer);
                    if (Config.AfkManagement)
                    {
                        QAngle? angles = null;
                        Vector? origin = null;
                        if (TryGetPlayerPawn(VictimController, out var victimPawn))
                        {
                            angles = victimPawn.EyeAngles;
                            origin = victimPawn.CBodyComponent?.SceneNode?.AbsOrigin;
                        }

                        if (Victim.Angles != null && Victim.Origin != null && angles != null && origin != null
                            && Victim.Angles.X == angles.X && Victim.Angles.Y == angles.Y
                            && Victim.Origin.X == origin.X && Victim.Origin.Y == origin.Y)
                        {
                            if (IsValidHuman(KillerController))
                            {
                                KillerController.PrintToCenter(Killer.Translate("kill.afk"));
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
                                    if (victimPawn != null && victimPawn.IsValid)
                                    {
                                        victimPawn.CommitSuicide(false, true);
                                    }
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
            // During warmup RoundStarted stays false, but plugin-managed random respawn must remain active.
            // Only bypass custom spawn selection when the round is genuinely inactive outside warmup,
            // or after a winner has already been declared.
            if ((!GGVariables.Instance.RoundStarted && !Config.AllowLevelUpAfterRoundEnd && !warmupInitialized)
                || GGVariables.Instance.GameWinner != null)
            {
                Respawn(VictimController, false);
                return HookResult.Continue;
            }
            /* Kill self with world spawn */
            if (@event.Attacker == null || !KillerController.IsValid)
            {
                Logger.LogWarning($"Victim {VictimController.PlayerName} KillerController Is null or not Valid, weapon {weapon_used}");
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
                else
                {
                    Logger.LogError($"************* weapon {weapon_used} - not world, worldent, trigger_hurt, env_fire. Check logic");
                }
            }

            /* They killed themself by kill command or by hegrenade etc */
            if (IsValidPlayer(KillerController) && KillerController.Index == VictimController.Index)
            {
                //                Console.WriteLine($"{VictimController.PlayerName} - suicide");
                /* (Weapon is event weapon name, can be 'world' or 'hegrenade' etc) */ /* weapon is not 'world' (ie not kill command) */
                //                Logger.LogInformation($"Victim {VictimController.PlayerName} Killer Is Victim, weapon {weapon_used}");
                if (Config.CommitSuicide > 0 && GGVariables.Instance.RoundStarted && !Victim.TeamChange)
                {
                    // killed himself by kill command or by hegrenade
                    ClientSuicide(Victim, Config.CommitSuicide);
                    //                    Logger.LogInformation($"{VictimController.PlayerName} killed by {weapon_used} - Commit Suicide");
                }
                Respawn(VictimController);
                return HookResult.Continue;
            }
            if (@event.Attacker == null)
            {
                Logger.LogInformation($"******** {VictimController.PlayerName} killed by Killer == null - unhandled **********");
                Respawn(VictimController);
                return HookResult.Continue;
            }
            // Victim > 0 && Killer > 0
            if (KillerController.DesignerName == "cs_player_controller")
            {
                Console.WriteLine($"{KillerController.PlayerName} killed {VictimController.PlayerName} with {weapon_used}");
            }
            else
            {
                Logger.LogError($"{VictimController.PlayerName} killed not by player. Designer name: {KillerController.DesignerName}");
            }

            if (!KillerController.IsValid || Killer == null)
            {
                Logger.LogError($"******** {VictimController.PlayerName} killed not by player in player map or KillerController Invalid");
                Respawn(VictimController);
                return HookResult.Continue;
            }
            // дальше KillerController != null && KillerController.IsValid && Killer != null
            if (!TryGetWeaponInfo(weapon_used, out WeaponInfo usedWeaponInfo))
            {
                Logger.LogError($"[GUNGAME] **** Cant get weapon info for weapon used in kill {weapon_used} by {Killer.PlayerName}");
                Respawn(VictimController);
                return HookResult.Continue;
            }

            if (warmupInitialized)
            {
                if (Config.ReloadWeapon)
                {
                    ReloadActiveWeapon(KillerController);
                }
                Respawn(VictimController);
                return HookResult.Continue;
            }

            /* Here is a place that the forward sends and receives the answer to count this kill as a kill or not.
             * Let's deal with the forwards and finish it
             */
            /*************** FFA - teamkill is considered as kill ****************/
            bool TeamKill = (!Config.FriendlyFireAllowed) && (VictimController.TeamNum == KillerController.TeamNum);

            bool AcceptKill = true; // if another plugin asks this not to be considered a kill, it will return false
            try
            {
                AcceptKill = CoreAPI.RaiseKillEvent(KillerController.Slot, VictimController.Slot, weapon_used, TeamKill, headshot);
            }
            catch (Exception ex)
            {
                Server.NextFrame(() =>
                {
                    Logger.LogError($"[GunGame API ERROR] RaiseKillEvent returned exception: {ex.Message}");
                });
            }

            if (!AcceptKill)
            {
                Logger.LogInformation($"********* Killer {KillerController.PlayerName} - victim {VictimController.PlayerName} kill is not accepted");
                Respawn(VictimController);
                return HookResult.Continue;
            }

            int level;
            bool stop_further_processing = false;
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
                                if (!KillerController.IsBot)
                                    KillerController.PrintToCenter(Killer.Translate("cantknife.leveluponbotwithhumans"));
                            }
                        }
                        else
                        {
                            stop_further_processing = true;
                            if (!KillerController.IsBot)
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
                                if (!KillerController.IsBot)
                                    KillerController.PrintToCenter(Killer.Translate("canthe.leveluponbotwithhumans"));
                            }
                        }
                        else
                        {
                            stop_further_processing = true;
                            if (!KillerController.IsBot)
                                KillerController.PrintToCenter(Killer.Translate("canthe.leveluponbot"));
                        }
                    }
                }
            }

            if (stop_further_processing || TeamKill)
            {
                if (stop_further_processing)
                {
                    if (Config.ReloadWeapon)
                    {
                        ReloadActiveWeapon(KillerController);
                    }
                    Respawn(VictimController);
                    return HookResult.Continue;
                }
                if (TeamKill)
                {
                    PlayConfiguredSound("TeamKill", 0.7f);
                    //                    PlayRandomSoundDelayed(0.7f, Config.TeamKillSound);
                    Killer.CurrentLevelPerRound -= Config.TkLooseLevel;
                    if (Killer.CurrentLevelPerRound < 0)
                    {
                        Killer.CurrentLevelPerRound = 0;
                    }
                    Killer.CurrentLevelPerRoundTriple = 0;

                    int oldLevel = (int)Killer.Level;
                    level = ChangeLevel(Killer, -Config.TkLooseLevel, false, VictimController);
                    if (level == oldLevel)
                    {
                        Respawn(VictimController);
                        return HookResult.Continue;
                    }

                    if (Config.TurboMode)
                    {
                        GiveNextWeapon(Killer.Slot);
                    }
                    else
                    {
                        if (Config.ReloadWeapon)
                        {
                            ReloadActiveWeapon(KillerController);
                        }
                    }
                    Respawn(VictimController);
                    return HookResult.Continue;
                }
            }
            level = (int)Killer.Level;
            var killerLevelWeapon = Killer.LevelWeapon;
            if (killerLevelWeapon == null)
            {
                Logger.LogError($"[ERROR] EventPlayerDeathHandler: killer slot {Killer.Slot} has no level weapon for level {Killer.Level}");
                Respawn(VictimController);
                return HookResult.Continue;
            }

            /* Give them another grenade if they killed another person with another weapon */
            if ((killerLevelWeapon.LevelIndex == SpecialWeapon.HegrenadeLevelIndex)  // killer is on grenade level
                && (usedWeaponInfo.LevelIndex != SpecialWeapon.HegrenadeLevelIndex)   // but did not kill with a grenade
                && !((usedWeaponInfo.LevelIndex == SpecialWeapon.KnifeLevelIndex) && Config.KnifeProHE) // TODO: Remove this statement and make check if killer not leveled up, than give extra nade.
            )
            {
                /************* Here is the idea about sticky grenade ************/
                GiveExtraNade(KillerController);
            }

            /* Give them another taser if they killed another person with another weapon */
            if ((killerLevelWeapon.LevelIndex == SpecialWeapon.TaserLevelIndex)
                && (usedWeaponInfo.LevelIndex != SpecialWeapon.TaserLevelIndex)
                && Config.ExtraTaserOnKill)
            {
                GiveExtraTaser(KillerController);
            }

            /* Give them another molotov if they killed another person with another weapon */
            if (killerLevelWeapon.LevelIndex == SpecialWeapon.MolotovLevelIndex)
            {
                if (usedWeaponInfo.LevelIndex != SpecialWeapon.MolotovLevelIndex
                    && Config.ExtraMolotovForKill)
                {
                    GiveExtraMolotov(KillerController, killerLevelWeapon.Index);
                }
            }

            if ((Config.MaxLevelPerRound > 0) && Killer.CurrentLevelPerRound >= Config.MaxLevelPerRound)
            {
                if (Config.ReloadWeapon)
                {
                    ReloadActiveWeapon(KillerController);
                }
                Respawn(VictimController);
                return HookResult.Continue;
            }

            int oldLevelKiller;
            int killsPerLevel = GetCustomKillPerLevel(level);

            if (Config.KnifePro && (usedWeaponInfo.LevelIndex == SpecialWeapon.KnifeLevelIndex)
                || (Config.MolotovPro && (usedWeaponInfo.LevelIndex == SpecialWeapon.MolotovLevelIndex)))
            {
                bool follow = true;
                int VictimLevel = (int)Victim.Level;
                if (VictimLevel < Config.KnifeProMinLevel)
                {
                    if (!KillerController.IsBot) KillerController.PrintToCenter(Killer.Translate("level.low", Victim.PlayerName, Config.KnifeProMinLevel));
                    follow = false;
                }
                if (follow && (Config.KnifeProMaxDiff > 0) && (Config.KnifeProMaxDiff < (VictimLevel - level)))
                {
                    if (!KillerController.IsBot) KillerController.PrintToCenter(Killer.Translate("level.difference", Victim.PlayerName, Config.KnifeProMaxDiff));
                    follow = false;
                }
                if (follow && !Config.DisableLevelDown)
                {
                    int ChangedLevel = ChangeLevel(Victim, -1, true, KillerController);
                    if (ChangedLevel != VictimLevel)
                    {
                        var playerEntities = GetValidPlayers();
                        if (playerEntities != null && playerEntities.Any())
                        {
                            foreach (var pc in playerEntities)
                            {
                                var pl = playerManager.FindBySlot(pc.Slot, "EventPlayerDeathHandler");
                                if (pl != null)
                                {
                                    pc.PrintToChat(pl.Translate("level.stolen", Killer.PlayerName, Victim.PlayerName));
                                }
                            }
                        }

                        if (usedWeaponInfo.LevelIndex == SpecialWeapon.KnifeLevelIndex)
                        {
                            //                            Logger.LogInformation($"Knife steal by {KillerController.PlayerName} on {VictimController.PlayerName}");
                            PlayConfiguredSound("KnifeSteal", 0.7f);
                            //                            PlayRandomSoundDelayed(0.7f, Config.KnifeStealSound);
                            try
                            {
                                /***** tells others that they have been killed with a knife ********/
                                CoreAPI.RaiseKnifeStealEvent(KillerController.Slot, VictimController.Slot);
                            }
                            catch (Exception ex)
                            {
                                Server.NextFrame(() =>
                                {
                                    Logger.LogError($"[GunGame API ERROR] RaiseKnifeStealEvent returned exception: {ex.Message}");
                                });
                            }
                        }
                        else if (usedWeaponInfo.LevelIndex == SpecialWeapon.MolotovLevelIndex)
                        {
                            //                            Logger.LogInformation($"Molotov kill by {KillerController.PlayerName} on {VictimController.PlayerName}");
                            PlayConfiguredSound("MolotovKill", 0.7f);
                            //                            PlaySoundDelayed(0.7f, null!, Config.MolotovKillSound);
                        }
                    }
                }
                /************  these conditions are used to decide whether the level should be raised or not (before this, the victim’s level was subtracted) *************/
                // if on a knife and you need to go through more than one knife
                if (follow && killerLevelWeapon.LevelIndex == SpecialWeapon.KnifeLevelIndex && killsPerLevel > 1)
                {
                    follow = false;
                }
                // you can't pass a grenade with a knife
                if (follow && !Config.KnifeProHE && killerLevelWeapon.LevelIndex == SpecialWeapon.HegrenadeLevelIndex)
                {
                    Respawn(VictimController);
                    return HookResult.Continue;
                }
                // you can't pass the taser with a knife
                if (follow && killerLevelWeapon.LevelIndex == SpecialWeapon.TaserLevelIndex)
                {
                    Respawn(VictimController);
                    return HookResult.Continue;
                }
                // You can't get past Molotov with a knife
                if (follow && killerLevelWeapon.LevelIndex == SpecialWeapon.MolotovLevelIndex &&
                    (usedWeaponInfo.LevelIndex != SpecialWeapon.MolotovLevelIndex ||
                    (usedWeaponInfo.LevelIndex == SpecialWeapon.MolotovLevelIndex && killsPerLevel > 1)))
                {
                    follow = false;
                }
                if (follow)
                {
                    oldLevelKiller = level;
                    level = ChangeLevel(Killer, 1, true, VictimController);
                    if (oldLevelKiller == level)
                    {
                        Respawn(VictimController);
                        return HookResult.Continue;
                    }
                    PrintLeaderToChat(Killer, oldLevelKiller, level);
                    Killer.CurrentLevelPerRound++;

                    if (Config.TurboMode)
                    {
                        GiveNextWeapon(Killer.Slot, true); // true - level up with knife
                    }
                    CheckForTripleLevel(Killer);
                    Respawn(VictimController);
                    return HookResult.Continue;
                }
            }
            bool LevelUpWithPhysics = false;

            try
            {
                CoreAPI.RaiseWeaponFragEvent(Killer.Slot, usedWeaponInfo.FullName);
            }
            catch (Exception ex)
            {
                Server.NextFrame(() =>
                {
                    Logger.LogError($"[GunGame API ERROR] RaiseWeaponFragEvent returned exception: {ex.Message}");
                });
            }

            /* They didn't kill with the weapon required */
            if (usedWeaponInfo.LevelIndex != killerLevelWeapon.LevelIndex)
            {
                if (usedWeaponInfo.LevelIndex == SpecialWeapon.HegrenadeLevelIndex)
                {
                    // Killed with grenade made by map author
                    if (Config.CanLevelUpWithMapNades
                        && (Config.CanLevelUpWithNadeOnKnife
                            || !(killerLevelWeapon.LevelIndex == SpecialWeapon.KnifeLevelIndex)))
                    {
                        LevelUpWithPhysics = true;
                    }
                    else
                    {
                        if (Config.ReloadWeapon)
                        {
                            ReloadActiveWeapon(KillerController);
                        }
                        Respawn(VictimController);
                        return HookResult.Continue;
                    }
                }
                else
                {
                    // Maybe killed with physics made by map author
                    bool isPhysicsKill = weapon_used == "prop_physics" || weapon_used == "prop_physics_multiplayer";
                    bool levelAllowsPhysics =
                        (killerLevelWeapon.LevelIndex != SpecialWeapon.HegrenadeLevelIndex && killerLevelWeapon.LevelIndex != SpecialWeapon.KnifeLevelIndex)
                        || (Config.CanLevelUpWithPhysicsOnGrenade && killerLevelWeapon.LevelIndex == SpecialWeapon.HegrenadeLevelIndex)
                        || (Config.CanLevelUpWithPhysicsOnKnife && killerLevelWeapon.LevelIndex == SpecialWeapon.KnifeLevelIndex);
                    if (Config.CanLevelUpWithPhysics && isPhysicsKill && levelAllowsPhysics)
                    {
                        LevelUpWithPhysics = true;
                    }
                    else
                    {
                        Respawn(VictimController);
                        return HookResult.Continue;
                    }
                }
            }

            if ((killsPerLevel > 1) && !LevelUpWithPhysics)
            {
                int kills = ++Killer.CurrentKillsPerWeap;
                if (kills <= killsPerLevel)
                {
                    /* An external function is called, which confirms whether to give a kill or not. If not, false is returned */
                    bool Accepted = true;
                    try
                    {
                        Accepted = CoreAPI.RaisePointChangeEvent(Killer.Slot, kills, Victim.Slot);
                    }
                    catch (Exception ex)
                    {
                        Server.NextFrame(() =>
                        {
                            Logger.LogError($"[GunGame API ERROR] RaisePointChangeEvent returned exception: {ex.Message}");
                        });
                    }
                    if (!Accepted)
                    {
                        Console.WriteLine("************* Point is not accepted ********");
                        Killer.CurrentKillsPerWeap--;
                        if (Config.ReloadWeapon)
                        {
                            ReloadActiveWeapon(KillerController);
                        }
                        Respawn(VictimController);
                        return HookResult.Continue;
                    }
                    //  **************************************** check logic
                    if (kills < killsPerLevel)
                    {
                        if (!KillerController.IsBot)
                            PlayConfiguredSound("MultiKill", 0.0f, KillerController);
                        //                            PlaySound(KillerController, Config.MultiKillSound);

                        if (Config.MultiKillChat)
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

                        if (Config.ReloadWeapon)
                        {
                            ReloadActiveWeapon(KillerController);
                        }
                        Respawn(VictimController);
                        return HookResult.Continue;
                    }
                }
            }

            // reload weapon
            if (!Config.TurboMode && Config.ReloadWeapon)
            {
                ReloadActiveWeapon(KillerController);
            }

            if (Config.KnifeElite)
            {
                Killer.State |= PlayerStates.KnifeElite;
            }

            oldLevelKiller = level;
            level = ChangeLevel(Killer, 1, false, VictimController);
            if (oldLevelKiller == level)
            {
                if (Config.ReloadWeapon)
                {
                    ReloadActiveWeapon(KillerController);
                }
                Respawn(VictimController);
                return HookResult.Continue;
            }
            Killer.CurrentLevelPerRound++;
            PrintLeaderToChat(Killer, oldLevelKiller, level);

            if (Config.TurboMode || Config.KnifeElite)
            {
                GiveNextWeapon(Killer.Slot, usedWeaponInfo.LevelIndex == SpecialWeapon.KnifeLevelIndex);
            }
            else if (Config.ReloadWeapon)
            {
                ReloadActiveWeapon(KillerController);
            }
            CheckForTripleLevel(Killer);
            Respawn(VictimController);
            return HookResult.Continue;
        }
        private HookResult EventPlayerHurtHandler(EventPlayerHurt eventInfo, GameEventInfo gameEventInfo)
        {
            if (!Config.ShootKnifeBlock || !GGVariables.Instance.IsActive)
            {
                return HookResult.Continue;
            }
            if (eventInfo == null) return HookResult.Continue;

            var attacker = eventInfo.Attacker;
            var victim = eventInfo.Userid;
            var weapon = eventInfo.Weapon;

            // ShootKnifeBlock applies only to human attackers.
            // Human players are still punished for shooting and then knifing any victim, including bots.
            if (attacker?.IsBot == true)
            {
                return HookResult.Continue;
            }

            if (attacker != null && victim != null && IsPlayer(attacker.Slot) && IsPlayer(victim.Slot))
            {
                bool found = false;
                if (IsWeaponKnife(weapon)) // if damage by knife
                {
                    if (g_Shot[attacker.Slot, victim.Slot])
                    {
                        if (TryGetPlayerPawn(attacker, out var attackerPawn)
                        && attackerPawn.WeaponServices != null)
                        {
                            foreach (var clientWeapon in attackerPawn.WeaponServices.MyWeapons)
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
                                if (TryGetPlayerPawn(victim, out var victimPawn))
                                {
                                    victimPawn.Health += eventInfo.DmgHealth;
                                }
                                AddTimer(0.2f, () =>
                                {
                                    attacker.CommitSuicide(true, true);
                                }, TimerFlags.STOP_ON_MAPCHANGE);

                                if (!attacker.IsBot)
                                {
                                    int slot = attacker.Slot;
                                    AddTimer(2.0f, () =>
                                    {
                                        var pc = Utilities.GetPlayerFromSlot(slot);
                                        if (pc != null && pc.IsValid)
                                        {
                                            string text;
                                            using (new WithTemporaryCulture(pc.GetLanguage()))
                                            {
                                                text = Localizer["dontshoot.knife"];
                                            }
                                            DisplayHint(pc, text);
                                            /*                                            var pl = playerManager.FindBySlot(slot, "EventPlayerHurtHandler1");
                                                                                        if (pl != null)
                                                                                        {
                                                                                            pc.PrintToCenter(pl.Translate("dontshoot.knife"));
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            pc.PrintToCenter(Localizer["dontshoot.knife"]);
                                                                                        } */
                                        }
                                    }, TimerFlags.STOP_ON_MAPCHANGE);
                                }
                                //                                Server.PrintToChatAll(Localizer["triedshoot.knife", attacker.PlayerName]);
                                var playerEntities = GetValidPlayers();
                                //                                Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.Connected && !p.IsBot && !p.IsHLTV);
                                if (playerEntities != null && playerEntities.Any())
                                {
                                    foreach (var pc in playerEntities)
                                    {
                                        var pl = playerManager.FindBySlot(pc.Slot, "EventPlayerHurtHandler2");
                                        if (pl != null)
                                        {
                                            pc.PrintToChat(pl.Translate("triedshoot.knife", attacker.PlayerName));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else if (weapon != null && !NonKnifeDamageWeapons.Contains(weapon))
                {
                    g_Shot[attacker.Slot, victim.Slot] = true;
                }
            }
            return HookResult.Continue;
        }
        private HookResult EventWeaponFireHandler(EventWeaponFire @event, GameEventInfo info)
        {
            if (GGVariables.Instance.IsActive && Config.AfkManagement)
            {
                var playerController = @event.Userid;
                if (playerController == null || !IsValidPlayer(playerController))
                {
                    return HookResult.Continue;
                }
                var client = playerManager.FindBySlot(playerController.Slot, "EventPlayerSpawnHandler");
                if (client != null)
                {
                    client.Angles!.X += 500;
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
                if (!playerController.IsValid || (newTeam == 0 && disconnect))
                    return HookResult.Continue;
                int slot = playerController.Slot;
                var player = playerManager.FindBySlot(slot, "EventPlayerTeamHandler");
                if (player != null)
                {
                    player.TeamNum = newTeam;
                    if (newTeam < (int)CsTeam.Terrorist)
                    {
                        player.CancelRespawnTimers();
                        StopTripleEffects(player);
                        if (player.State.HasFlag(PlayerStates.GrenadeLevel))
                        {
                            player.State &= ~PlayerStates.GrenadeLevel;
                            GGVariables.Instance.PlayerOnGrenade = Math.Max(0, GGVariables.Instance.PlayerOnGrenade - 1);
                        }
                        RecalculateLeader(slot, (int)player.Level, 0);
                    }
                }
                if (newTeam == 2 || newTeam == 3)
                {
                    if (SkipSpawn.Contains(slot))
                        SkipSpawn.Remove(slot);
                    if (player != null && IsRespawnServiceActive)
                    {
                        long connectionId = player.ConnectionId;
                        AddTimer(0.4f, () =>
                        {
                            if (!playerManager.TryGetCurrentPlayer(slot, connectionId, out _))
                                return;

                            var currentController = Utilities.GetPlayerFromSlot(slot);
                            if (currentController != null && IsValidPlayer(currentController))
                                Respawn(currentController);
                        }, TimerFlags.STOP_ON_MAPCHANGE);
                    }
                }
                else
                {
                    if (!SkipSpawn.Contains(slot))
                        SkipSpawn.Add(slot);
                }
                if (!playerController.IsBot)
                {
                    if (player == null)
                    {
                        //                        Logger.LogError($"[GunGame] EventPlayerTeamHandler: player == null: oldTeam {oldTeam}, newTeam {newTeam}, disconnect {(disconnect ? "yes" : "no")}, slot {playerController.Slot}, {playerController.PlayerName}");
                        return HookResult.Continue;
                    }
                    if (oldTeam >= 2 && newTeam >= 2)
                    {
                        //                StopTripleEffects(player);
                        player.TeamChange = true;
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
                }

                AddTimer(0.5f, () =>
                {
                    GGVariables.Instance.Tcount = CountPlayersForTeam(CsTeam.Terrorist);
                    GGVariables.Instance.CTcount = CountPlayersForTeam(CsTeam.CounterTerrorist);
                    if (Config.UnlimitedNadesMinPlayers > 0)
                    {
                        if (GGVariables.Instance.Tcount <= Config.UnlimitedNadesMinPlayers || GGVariables.Instance.CTcount <= Config.UnlimitedNadesMinPlayers)
                        {
                            Config.UnlimitedNades = true;
                        }
                        else
                        {
                            Config.UnlimitedNades = false;
                        }
                    }
                }, TimerFlags.STOP_ON_MAPCHANGE);
            }
            return HookResult.Continue;
        }
        private HookResult EventRoundStartHandler(EventRoundStart @event, GameEventInfo info)
        {
            if (!GGVariables.Instance.RestartGame && (DateTime.Now - lastRoundStartEventTime).TotalSeconds < 3)
                return HookResult.Continue;
            LoadSpawnPoints();
            lastRoundStartEventTime = DateTime.Now;
            GGVariables.Instance.RestartGame = false;
            GGVariables.Instance.Round++;
            Console.WriteLine($"[GUNGAME]********* Round {GGVariables.Instance.Round} Start");
            Logger.LogInformation($"[GUNGAME]********* Round {GGVariables.Instance.Round} Start");
            if (!GGVariables.Instance.IsActive)
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

            if (Config.WarmupEnabled && !GGVariables.Instance.WarmupFinished)
            {
                if (!warmupInitialized && warmupTimer == null)
                {
                    StartWarmupRound();
                }

                return HookResult.Continue;
            }
            if (GGVariables.Instance.GameWinner != null)
            {
                // Lock all player since the winner was declare already if new round happened.
                if (Config.WinnerFreezePlayers)
                {
                    FreezeAllPlayers();
                    return HookResult.Continue;
                }
            }
            if (GGVariables.Instance.FirstRound)
            {
                PlayerLevelsBeforeDisconnect.Clear();
                var playerEntities = GetValidPlayersWithBots();
                if (playerEntities != null && playerEntities.Count > 0)
                {
                    foreach (var playerController in playerEntities)
                    {
                        var client = playerManager.FindBySlot(playerController.Slot, "EventRoundStartHandler");
                        if (client != null)
                        {
                            client.SetLevel(1);
                            UpdatePlayerScoreLevel(client);
                            if (Config.ShootKnifeBlock)
                            {
                                ForgiveShots(playerController.Slot);
                            }
                        }
                    }
                }
                GGVariables.Instance.FirstRound = false;
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
        private HookResult EventRoundEndHandler(EventRoundEnd @event, GameEventInfo info)
        {
            /* Round has ended. */
            //            LogConnections = false;
            GGVariables.Instance.RoundStarted = false;
            return HookResult.Continue;
        }
        private HookResult EventHegrenadeDetonateHandler(EventHegrenadeDetonate @event, GameEventInfo info)
        {
            if (@event.Userid == null || !IsValidPlayer(@event.Userid) || !GGVariables.Instance.IsActive)
            {
                return HookResult.Continue;
            }
            var playerController = @event.Userid;
            var player = playerManager.FindBySlot(playerController.Slot, "EventHegrenadeDetonateHandler");

            if (player == null)
            {
                return HookResult.Continue;
            }
            if ((Config.WarmupNades && warmupInitialized)
                || (player.LevelWeapon != null && player.LevelWeapon.LevelIndex == SpecialWeapon.HegrenadeLevelIndex
                    && (Config.UnlimitedNades
                    || (Config.NumberOfNades > 0 && player.NumberOfNades > 0))))
            {  // Do not give them another nade if they already have one
                if (!HasWeapon(playerController, "weapon_hegrenade"))
                {
                    if (Config.NumberOfNades > 0)
                    {
                        player.NumberOfNades--;
                    }
                    Weapon? he = GGVariables.Instance.weaponsList.FirstOrDefault(w => w.Name == "hegrenade");
                    if (he == null)
                    {
                        return HookResult.Continue;
                    }
                    TryGiveNamedItem(playerController, he.FullName, "EventHegrenadeDetonateHandler");
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
        public bool TryGetPlayerPawn(CCSPlayerController? playerController, out CCSPlayerPawn playerPawn)
        {
            playerPawn = null!;

            if (playerController == null || !IsValidPlayer(playerController))
            {
                return false;
            }

            CHandle<CCSPlayerPawn> pawnHandle;
            try
            {
                pawnHandle = playerController.PlayerPawn;
            }
            catch (ArgumentNullException)
            {
                return false;
            }

            var pawnValue = pawnHandle?.Value;
            if (pawnHandle == null || !pawnHandle.IsValid || pawnValue == null || !pawnValue.IsValid)
            {
                return false;
            }

            playerPawn = pawnValue;
            return true;
        }

        public bool TryGetAlivePlayerPawn(CCSPlayerController? playerController, out CCSPlayerPawn playerPawn)
        {
            if (!TryGetPlayerPawn(playerController, out playerPawn))
            {
                return false;
            }

            return playerPawn.LifeState == (byte)LifeState_t.LIFE_ALIVE;
        }

        private bool TryGetItemServices(CCSPlayerController? playerController, out CCSPlayer_ItemServices itemServices, string source)
        {
            itemServices = null!;
            if (playerController == null || !TryGetPlayerPawn(playerController, out var playerPawn))
            {
                return false;
            }

            itemServices = playerPawn.ItemServices?.As<CCSPlayer_ItemServices>()!;
            if (itemServices == null || itemServices.Handle == IntPtr.Zero)
            {
                Logger.LogError($"[ERROR] {source}: player slot {playerController.Slot} has no valid item services");
                return false;
            }

            return true;
        }

        private bool TryGiveNamedItem(CCSPlayerController? playerController, string itemName, string source)
        {
            if (string.IsNullOrWhiteSpace(itemName))
            {
                Logger.LogError($"[ERROR] {source}: cannot give an empty item name");
                return false;
            }

            if (!TryGetItemServices(playerController, out var itemServices, source))
            {
                return false;
            }

            try
            {
                itemServices.GiveNamedItem<CEntityInstance>(itemName);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ERROR] {source}: cannot give {itemName}: {ex.Message}");
                return false;
            }
        }

        private bool TryRemoveWeapons(CCSPlayerController? playerController, string source)
        {
            if (!TryGetItemServices(playerController, out var itemServices, source))
            {
                return false;
            }

            try
            {
                itemServices.RemoveWeapons();
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ERROR] {source}: cannot remove weapons: {ex.Message}");
                return false;
            }
        }

        private void GiveWarmUpWeaponDelayed(float delay, int slot)
        {
            var scheduledPlayer = playerManager.FindBySlot(slot, "GiveWarmUpWeaponDelayed");
            if (scheduledPlayer == null)
            {
                return;
            }

            long connectionId = scheduledPlayer.ConnectionId;
            AddTimer(delay, () =>
            {
                if (!playerManager.TryGetCurrentPlayer(slot, connectionId, out var currentPlayer) || currentPlayer == null)
                {
                    return;
                }

                var playerController = Utilities.GetPlayerFromSlot(slot);
                if (TryGetAlivePlayerPawn(playerController, out _))
                {
                    if (Config.WarmupRandomWeaponMode > 0)
                    {
                        currentPlayer.SetLevel(random.Next(1, GGVariables.Instance.WeaponOrderCount));
                        GiveNextWeapon(slot);
                        return;
                    }
                    bool nades = Config.WarmupNades;
                    bool wpn = false;
                    if (Config.WarmupWeapon.Length > 0)
                    {
                        Weapon? weapon = GGVariables.Instance.weaponsList.FirstOrDefault(w => w.Name.Equals(Config.WarmupWeapon));
                        if (weapon != null && !weapon.Name.Contains("knife", StringComparison.OrdinalIgnoreCase))
                        {
                            wpn = true;
                        }
                    }
                    try
                    {
                        if (!TryRemoveWeapons(playerController, "GiveWarmUpWeaponDelayed"))
                        {
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Server.NextFrame(() =>
                        {
                            Logger.LogError($"[GunGame ERROR] Cannot remove warm-up weapons: {ex.Message}");
                        });
                        return;
                    }

                    if (Config.ArmorKevlarHelmet) TryGiveNamedItem(playerController, "item_assaultsuit", "GiveWarmUpWeaponDelayed");
                    TryGiveNamedItem(playerController, "weapon_knife", "GiveWarmUpWeaponDelayed");
                    if (nades)
                    {
                        TryGiveNamedItem(playerController, "weapon_hegrenade", "GiveWarmUpWeaponDelayed");
                    }
                    if (wpn)
                    {
                        TryGiveNamedItem(playerController, "weapon_" + Config.WarmupWeapon, "GiveWarmUpWeaponDelayed");
                    }
                    if (!nades && !wpn)
                    {
                        currentPlayer.UseWeapon(3); // Give knife.
                    }
                }
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }
        public bool IsPlayerOnKnifeLevel(int slot)
        {
            var player = playerManager.FindBySlot(slot, "IsPlayerOnKnifeLevel");
            return player?.LevelWeapon != null && player.LevelWeapon.LevelIndex == SpecialWeapon.KnifeLevelIndex;
        }
        public void GiveNextWeapon(int slot, bool levelupWithKnife = false, bool spawn = false)
        {
            var playerController = Utilities.GetPlayerFromSlot(slot);
            if (playerController == null || !TryGetAlivePlayerPawn(playerController, out var playerPawn) || !IsClientInTeam(playerController))
            {
                return;
            }

            var player = playerManager.FindBySlot(slot, "GiveNextWeapon");
            if (player == null)
            {
                Logger.LogError($"[ERROR] GiveNextWeapon: cannot get player for slot {slot}");
                return;
            }

            var levelWeapon = player.LevelWeapon;
            if (levelWeapon == null)
            {
                Logger.LogError($"[ERROR] GiveNextWeapon: player slot {slot} has no level weapon for level {player.Level}");
                return;
            }

            var itemServices = playerPawn.ItemServices?.As<CCSPlayer_ItemServices>();
            if (itemServices == null || itemServices.Handle == IntPtr.Zero)
            {
                Logger.LogError($"[ERROR] GiveNextWeapon: player slot {slot} has no valid item services");
                return;
            }

            if (string.IsNullOrWhiteSpace(levelWeapon.FullName) && levelWeapon.LevelIndex != SpecialWeapon.KnifeLevelIndex)
            {
                Logger.LogError($"[ERROR] GiveNextWeapon: player slot {slot} has empty weapon name for level {player.Level}. Check gungame_weapons.json level {player.Level} and weapons.json");
                return;
            }

            void GiveNamedItem(string itemName)
            {
                if (!string.IsNullOrWhiteSpace(itemName))
                {
                    try
                    {
                        itemServices.GiveNamedItem<CEntityInstance>(itemName);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"[ERROR] GiveNextWeapon: cannot give {itemName}: {ex.Message}");
                    }
                }
            }

            try
            {
                itemServices.RemoveWeapons();
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ERROR] GiveNextWeapon: cannot remove weapons: {ex.Message}");
                return;
            }

            if (Config.ArmorKevlarHelmet) GiveNamedItem("item_assaultsuit");
            bool dropKnife;
            if (levelWeapon.LevelIndex == SpecialWeapon.Drop_knife)
            {
                dropKnife = true;
            }
            else
            {
                dropKnife = false;
            }
            //            bool blockSwitch = Plugin.Config.BlockWeaponSwitchIfKnife && levelupWithKnife && !dropKnife;
            //            int newWeapon = -1;
            //            if (blockSwitch) {
            //                player.BlockSwitch = true;
            //            }
            CheckForFriendlyFire(player);

            if (levelWeapon.LevelIndex != SpecialWeapon.Drop_knife)
            {
                GiveNamedItem("weapon_knife");
            }

            /*            if (player.State.HasFlag(PlayerStates.KnifeElite)) { // FIXME: when do we call UTIL_GiveNextWeapon with KNIFE_ELITE flag set in PlayerState?
                            if (blockSwitch) {
                                player.BlockSwitch = false;
                            } else {
                                player.UseWeapon(3); //knife slot
                            }
                            return;
                        } */

            if (levelWeapon.Slot == 4)  // grenades slot
            {
                if (levelWeapon.LevelIndex == SpecialWeapon.HegrenadeLevelIndex)
                {
                    // BONUS WEAPONS FOR HEGRENADE
                    if (Config.NumberOfNades > 0)
                    {
                        player.NumberOfNades = Config.NumberOfNades - 1;
                    }
                    if (!string.IsNullOrEmpty(Config.NadeBonusWeapon)) // give a bonus weapon on grenade level
                    {
                        GiveNamedItem("weapon_" + Config.NadeBonusWeapon);
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
                    if (Config.NadeSmoke)
                    {
                        GiveNamedItem("weapon_smokegrenade");
                    }
                    if (Config.NadeFlash)
                    {
                        GiveNamedItem("weapon_flashbang");
                    }
                }
                else if (levelWeapon.LevelIndex == SpecialWeapon.MolotovLevelIndex)
                {
                    // BONUS WEAPONS FOR MOLOTOV
                    if (!string.IsNullOrEmpty(Config.MolotovBonusWeapon))
                    {
                        GiveNamedItem("weapon_" + Config.MolotovBonusWeapon);
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
                    if (Config.MolotovBonusSmoke)
                    {
                        GiveNamedItem("weapon_smokegrenade");
                    }
                    if (Config.MolotovBonusFlash)
                    {
                        GiveNamedItem("weapon_flashbang");
                    }
                }
            }

            if (levelWeapon.Slot == 3)  // knife slot
            {
                if (levelWeapon.LevelIndex == SpecialWeapon.KnifeLevelIndex)
                {
                    // BONUS WEAPONS FOR KNIFE
                    if (Config.KnifeSmoke)
                    {
                        GiveNamedItem("weapon_smokegrenade");
                    }
                    if (Config.KnifeFlash)
                    {
                        GiveNamedItem("weapon_flashbang");
                    }
                    if (dropKnife)
                    {
                        // LEVEL WEAPON KNIFEGG
                        //                        playerController.GiveNamedItem(player.LevelWeapon.FullName);
                        //                        if (player.LevelWeapon.FullName.Equals("weapon_knifegg"))
                        //                        {
                        //                            playerController.GiveNamedItem("weapon_knife");
                        //                        }
                        player.UseWeapon(3);
                        // Change knife colour to gold. It does not work now
                        if (player.Level == GGVariables.Instance.WeaponOrderCount) // on the last level
                        {
                            var activeWeapon = playerPawn.WeaponServices?.ActiveWeapon?.Value;
                            if (activeWeapon != null && activeWeapon.IsValid)
                            {
                                activeWeapon.Render = Color.FromArgb(255, 223, 0);
                            }
                        }
                    }
                }
                else
                {
                    // LEVEL WEAPON TASER
                    if (Config.TaserSmoke)
                    {
                        GiveNamedItem("weapon_smokegrenade");
                    }
                    if (Config.TaserFlash)
                    {
                        GiveNamedItem("weapon_flashbang");
                        //                        GivePlayerItemWrapper(player, "weapon_flashbang", !player.BlockSwitch);
                    }
                    GiveNamedItem(levelWeapon.FullName);
                }
                player.UseWeapon(3);
            }
            else
            {
                // LEVEL WEAPON PRIMARY/SECONDARY
                /* Give new weapon */
                GiveNamedItem(levelWeapon.FullName);
                if (Config.FastSwitchOnLevelUp && !GGVariables.Instance.WeaponsSkipFastSwitch.Contains(levelWeapon.FullName))
                {
                    var weapon = playerPawn.WeaponServices?.ActiveWeapon?.Value;
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

            if (levelWeapon.Slot == 4)  // grenades slot
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
            if (Config.ExtraNade)
            {
                /* Do not give them another nade if they already have one */
                if (!HasWeapon(player, "weapon_hegrenade"))
                {
                    TryGiveNamedItem(player, "weapon_hegrenade", "GiveExtraNade");
                    /*                    GivePlayerItemWrapper(player, "weapon_hegrenade", Plugin.Config.BlockWeaponSwitchOnNade);
                      Here about the switching lock. Let's put it aside for now
                                        if (!blockWeapSwitch) {
                                            UTIL_UseWeapon(client, g_WeaponIdHegrenade);
                                            UTIL_FastSwitchWithCheck(client, newWeapon, true, g_WeaponIdHegrenade);
                                        } */
                }
            }
        }
        private void GiveExtraTaser(CCSPlayerController player)
        {
            if (HasWeapon(player, "weapon_taser"))
            {
                if (TryGetPlayerPawn(player, out var pawn) && pawn.WeaponServices != null)
                {
                    foreach (var clientWeapon in pawn.WeaponServices.MyWeapons)
                    {
                        if (clientWeapon is { IsValid: true, Value.IsValid: true })
                        {
                            if (clientWeapon.Value.DesignerName.Equals("weapon_taser"))
                            {
                                clientWeapon.Value.Clip1 = 1;
                                Utilities.SetStateChanged(clientWeapon.Value, "CBasePlayerWeapon", "m_iClip1");
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                TryGiveNamedItem(player, "weapon_taser", "GiveExtraTaser");
            }
        }
        private void GiveExtraMolotov(CCSPlayerController player, int weapon_index)
        {
            if (!HasWeapon(player, "weapon_molotov"))
            {
                RemoveGrenades(player);
                TryGiveNamedItem(player, "weapon_molotov", "GiveExtraMolotov");
                if (Config.MolotovBonusSmoke)
                {
                    TryGiveNamedItem(player, "weapon_smokegrenade", "GiveExtraMolotov");
                }
                if (Config.MolotovBonusFlash)
                {
                    TryGiveNamedItem(player, "weapon_flashbang", "GiveExtraMolotov");
                }
            }
            /*            else
                        {
                            Logger.LogInformation($"Can't give GiveExtraMolotov to {player.PlayerName} for kill by another weapon, he has the molotov already");
                        } */
        }
        private void ReloadActiveWeapon(CCSPlayerController player)
        {
            if (!TryGetPlayerPawn(player, out var playerPawn))
                return;

            var weapon = playerPawn.WeaponServices?.ActiveWeapon?.Value?.As<CCSWeaponBaseGun>();
            if (weapon == null || !weapon.IsValid)
                return;

            var weaponData = weapon.VData;
            if (weaponData == null)
                return;

            weapon.Clip1 = weaponData.MaxClip1 + 1;// "+1" is needed because ammo is refilling before last shot is counted
            Utilities.SetStateChanged(weapon.As<CCSWeaponBase>(), "CBasePlayerWeapon", "m_pReserveAmmo");
        }
        private void CheckForFriendlyFire(GGPlayer player)
        {
            if (!Config.AutoFriendlyFire)
            {
                return;
            }
            var pState = player.State;
            if (pState.HasFlag(PlayerStates.GrenadeLevel) && player.LevelWeapon != null && player.LevelWeapon.LevelIndex != SpecialWeapon.HegrenadeLevelIndex)
            {
                player.State &= ~PlayerStates.GrenadeLevel;

                if (--GGVariables.Instance.PlayerOnGrenade < 1)
                {
                    GGVariables.Instance.PlayerOnGrenade = 0;
                    if (Config.FriendlyFireOnOff)
                    {
                        ChangeFriendlyFire(false);
                    }
                    else
                    {
                        ChangeFriendlyFire(true);
                    }
                }
                return;
            }
            if ((!pState.HasFlag(PlayerStates.GrenadeLevel)) && player.LevelWeapon != null && player.LevelWeapon.LevelIndex == SpecialWeapon.HegrenadeLevelIndex)
            {
                GGVariables.Instance.PlayerOnGrenade++;
                player.State |= PlayerStates.GrenadeLevel;

                if (GGVariables.Instance.Mp_friendlyfire == null)
                {
                    Console.WriteLine("GGVariables.Instance.mp_friendlyfire == null");
                    return;
                }

                if (!GGVariables.Instance.Mp_friendlyfire.GetPrimitiveValue<bool>())
                {
                    if (Config.FriendlyFireOnOff)
                    {
                        ChangeFriendlyFire(true);
                    }
                    else
                    {
                        ChangeFriendlyFire(false);
                    }
                }
                return;
            }
        }
        private void UpdatePlayerScoreLevel(GGPlayer player)
        {
            if (warmupInitialized)
            {
                return;
            }
            if (Config.LevelsInScoreboard > 0)
            {
                int level = (int)player.Level;
                int slot = player.Slot;
                long connectionId = player.ConnectionId;
                player.CancelScoreUpdateTimer();
                player.ScoreUpdateTimer = AddTimer(0.5f, () =>
                {
                    if (!playerManager.TryGetCurrentPlayer(slot, connectionId, out var currentPlayer) || currentPlayer == null)
                    {
                        return;
                    }

                    currentPlayer.ScoreUpdateTimer = null;
                    var pc = Utilities.GetPlayerFromSlot(slot);
                    if (pc != null && IsValidPlayer(pc) && pc.ActionTrackingServices != null)
                    {
                        if (Config.LevelsInScoreboard == 1)
                        {
                            pc.ActionTrackingServices.MatchStats.Kills = level;
                        }
                        else if (Config.LevelsInScoreboard == 2)
                        {
                            pc.ActionTrackingServices.MatchStats.Deaths = level;
                        }
                        else if (Config.LevelsInScoreboard == 3)
                        {
                            pc.ActionTrackingServices.MatchStats.Assists = level;
                        }
                        else if (Config.LevelsInScoreboard == 4)
                        {
                            pc.Score = level;
                        }

                        if (Config.LevelsInScoreboard == 4)
                        {
                            Utilities.SetStateChanged(pc, "CCSPlayerController", "m_iScore");
                        }
                        else
                        {
                            Utilities.SetStateChanged(pc, "CCSPlayerController", "m_pActionTrackingServices");
                        }
                    }
                }, TimerFlags.STOP_ON_MAPCHANGE);
            }
        }
        private void FreezeAllPlayers()
        {
            if (GGVariables.Instance.Mp_friendlyfire != null)
            {
                GGVariables.Instance.Mp_friendlyfire.SetValue(false);
            }

            var playerEntities = GetValidPlayersWithBots();
            //            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.Connected && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var playerController in playerEntities)
                {
                    FreezePlayer(playerController);
                }
            }
        }
        private void FreezePlayer(CCSPlayerController player)
        {
            if (!TryGetPlayerPawn(player, out var pawn) || player.TeamNum <= 1)
            {
                return;
            }

            pawn.MoveType = MoveType_t.MOVETYPE_NONE;
            Schema.SetSchemaValue(pawn.Handle, "CBaseEntity", "m_nActualMoveType", 0);
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
            TryRemoveWeapons(player, "FreezePlayer");
            TryGiveNamedItem(player, "weapon_knife", "FreezePlayer");
        }
        private void CheckForTripleLevel(GGPlayer client)
        {
            client.CurrentLevelPerRoundTriple++;
            if (Config.MultiLevelBonus && client.CurrentLevelPerRoundTriple == Config.MultiLevelAmount)
            {
                //                Server.PrintToChatAll(Localizer["player.leveled", client.PlayerName, Config.MultiLevelAmount]);
                var playerEntities = GetValidPlayers();
                //                Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.Connected && !p.IsBot && !p.IsHLTV);
                if (playerEntities != null && playerEntities.Any())
                {
                    foreach (var pc in playerEntities)
                    {
                        var pl = playerManager.FindBySlot(pc.Slot, "CheckForTripleLevel");
                        if (pl != null)
                        {
                            pc.PrintToChat(pl.Translate("player.leveled", client.PlayerName, Config.MultiLevelAmount));
                        }
                    }
                }
                StartTripleEffects(client);
                client.CancelTripleEffectsTimer();
                int slot = client.Slot;
                long connectionId = client.ConnectionId;
                client.TripleEffectsTimer = AddTimer(10.0f, () =>
                {
                    if (playerManager.TryGetCurrentPlayer(slot, connectionId, out var currentPlayer) && currentPlayer != null)
                    {
                        currentPlayer.TripleEffectsTimer = null;
                        StopTripleEffects(currentPlayer);
                    }
                }, TimerFlags.STOP_ON_MAPCHANGE);
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
            if (player == null || player.TripleEffects || !playerManager.IsCurrentPlayer(player.Slot, player))
                return;
            var playerController = Utilities.GetPlayerFromSlot(player.Slot);
            if (playerController == null)
                return;
            player.TripleEffects = true;
            player.TripleGodModeApplied = false;
            player.TripleGravityApplied = false;
            player.TripleSpeedApplied = false;
            if (TryGetPlayerPawn(playerController, out var pawn))
            {
                if (Config.MultiLevelBonusGodMode)
                {
                    pawn.TakesDamage = false;
                    player.TripleGodModeApplied = true;
                }
                if (Config.MultiLevelBonusGravity != 1)
                {
                    pawn.GravityScale = Config.MultiLevelBonusGravity;
                    player.TripleGravityApplied = true;
                }
                if (Config.MultiLevelBonusSpeed != 1)
                {
                    pawn.VelocityModifier = Config.MultiLevelBonusSpeed;
                    player.TripleSpeedApplied = true;
                }
                var soundData = soundMapper.GetSoundValue("MultiLevel");

                if (soundData != null && soundData.HasValue && !string.IsNullOrEmpty(soundData.Value.SoundValue))
                {
                    RecipientFilter filter = [];
                    filter.AddAllPlayers();
                    pawn.EmitSound(soundData.Value.SoundValue, filter, 0.8f);
                    //                    PlaySound(null!, Config.MultiLevelSound);
                }
                else
                {
                    Logger.LogError("No sound data for MultiLevel sound");
                }
            }
        }
        /*        public void PlaySoundDelayed(float delay, CCSPlayerController player, string str)
                {
                    AddTimer(delay, () => PlaySound(player, str));
                } */
        public void PlayConfiguredSound(string soundKey, float delay = 0.0f, CCSPlayerController player = null!)
        {
            var soundData = soundMapper.GetSoundValue(soundKey);

            if (soundData == null || !soundData.HasValue)
            {
                Logger.LogError($"SoundPlayer: No sound value found for key '{soundKey}'. Cannot play sound.");
                return;
            }
            if (Config.UseSoundEvents)
            {
                if (string.IsNullOrEmpty(soundData.Value.SoundValue))
                {
                    //                    Logger.LogError($"SoundPlayer: The sound value for key '{soundKey}' is null or empty.");
                    return;
                }
                if (delay > 0.0f)
                {
                    AddTimer(delay, () => PlaySoundEvent(soundData.Value.SoundValue, player), TimerFlags.STOP_ON_MAPCHANGE);
                }
                else
                {
                    PlaySoundEvent(soundData.Value.SoundValue, player);
                }
            }
            else
            {
                if (delay > 0.0f)
                {
                    if (soundData.Value.IsRandom && soundData.Value.SoundList != null && soundData.Value.SoundList.Count > 0)
                        AddTimer(delay, () => PlayRandomSound(soundData.Value.SoundList, player), TimerFlags.STOP_ON_MAPCHANGE);
                    else
                        AddTimer(delay, () => PlaySoundFile(player, soundData.Value.SoundValue), TimerFlags.STOP_ON_MAPCHANGE);
                }
                else
                {
                    if (soundData.Value.IsRandom && soundData.Value.SoundList != null && soundData.Value.SoundList.Count > 0)
                        PlayRandomSound(soundData.Value.SoundList, player);
                    else
                        PlaySoundFile(player, soundData.Value.SoundValue);
                }
            }
        }
        public void PlaySoundEvent(string str, CCSPlayerController playerController)
        {
            if (string.IsNullOrEmpty(str))
            {
                Logger.LogError("SoundPlayer: The sound string is null or empty.");
                return;
            }
            // ************************************************ set volumes for sounds
            RecipientFilter filter = [];
            if (playerController != null)
            {
                if (!IsValidPlayer(playerController))
                    return;
                if (!playerController.IsBot)
                {
                    filter = [playerController];
                    var player = playerManager.FindBySlot(playerController.Slot, "PlaySoundEvent");
                    if (player != null && player.Music && TryGetPlayerPawn(playerController, out _))
                    {
                        //                        playerController.EmitSound(str, filter, 0.8f);
                        playerController.EmitSound(str, filter);
                    }
                }
            }
            else
            {
                var playerEntities = GetValidPlayers();
                if (playerEntities != null && playerEntities.Any())
                {
                    foreach (var pc in playerEntities)
                    {
                        filter = [pc];
                        var player = playerManager.FindBySlot(pc.Slot, "PlaySoundEvent");
                        if (player != null && player.Music)
                            pc.EmitSound(str, filter);
                    }
                }
            }
        }
        public void PlaySoundFile(CCSPlayerController playerController, string str)
        {
            //RecipientFilter filter = [Player];
            //Server.NextFrame(() => Player.EmitSound("sound26", filter, 1f));
            if (playerController != null)
            {
                if (!IsValidPlayer(playerController))
                    return;
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
        /*        public void PlayRandomSoundDelayed(float delay, List<string> soundList)
                {
                    AddTimer(delay, () => PlayRandomSound(soundList));
                } */
        public void PlayRandomSound(List<string> soundList, CCSPlayerController pc = null!)
        {
            if (soundList == null || soundList.Count == 0)
            {
                Logger.LogError("The sound list is empty or null.");
                return;
            }
            int index = random.Next(soundList.Count);
            if (pc != null)
            {
                if (!IsValidPlayer(pc) || pc.IsBot)
                    return;

                var targetPlayer = playerManager.FindBySlot(pc.Slot, "PlayRandomSound");
                if (targetPlayer != null && targetPlayer.Music)
                    pc.ExecuteClientCommand("play " + soundList[index]);
                return;
            }

            var playerEntities = GetValidPlayers();
            //            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.Connected && !p.IsBot && !p.IsHLTV);
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
        // ************************ Проверить почему ForLeaderLevel играет MolotovKill и KnifeInfo !!!!!!!!!!!!
        public void PlaySoundForLeaderLevel(int slot = -1)
        {
            if (slot < 0)
            {
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
                PlayConfiguredSound("NadeInfo", 0.7f);
                //                PlaySoundDelayed(1.0f, null!, Config.NadeInfoSound);
                return;
            }
            if (wep != null && wep.LevelIndex == SpecialWeapon.KnifeLevelIndex)
            {
                Logger.LogInformation($"[GunGame] ******* PlaySoundForLeaderLevel: Knife for player {player.PlayerName}");
                PlayConfiguredSound("KnifeInfo", 0.7f);
                //                PlaySoundDelayed(2.0f, null!, Config.KnifeInfoSound);
                return;
            }
        }
        private void StopTripleEffects(GGPlayer player)
        {
            if (player == null || !player.TripleEffects || !playerManager.IsCurrentPlayer(player.Slot, player))
            {
                return;
            }
            player.CancelTripleEffectsTimer();
            player.CurrentLevelPerRoundTriple = 0;
            player.TripleEffects = false;
            var playerController = Utilities.GetPlayerFromSlot(player.Slot);
            if (playerController == null)
                return;
            if (TryGetPlayerPawn(playerController, out var pawn))
            {
                if (player.TripleGodModeApplied)
                {
                    pawn.TakesDamage = true;
                }
                if (player.TripleGravityApplied)
                {
                    pawn.GravityScale = 1.0f;
                }
                if (player.TripleSpeedApplied)
                {
                    pawn.VelocityModifier = 1.0f;
                }
            }
            player.TripleGodModeApplied = false;
            player.TripleGravityApplied = false;
            player.TripleSpeedApplied = false;
            if (Config.MultiLevelEffect)
            {
                StopEffectClient(playerController);
            }
        }
        private static void StopEffectClient(CCSPlayerController playerController)
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
        private bool HasWeapon(CCSPlayerController playerController, string weapon)
        {
            bool found = false;
            if (TryGetPlayerPawn(playerController, out var pawn) && pawn.WeaponServices != null)
            {
                foreach (var clientWeapon in pawn.WeaponServices.MyWeapons)
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
        private void RemoveGrenades(CCSPlayerController playerController)
        {
            if (!TryGetPlayerPawn(playerController, out var playerPawn))
                return;
            var weapons = playerPawn.WeaponServices;

            if (weapons == null)
                return;

            foreach (var weapon in weapons.MyWeapons)
            {
                if (weapon is not { IsValid: true, Value.IsValid: true }) continue;

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
            return weapon.Contains("knife") || weapon.Contains("bayonet");
        }
        private void RecalculateLeader(int slot, int oldLevel, int newLevel = 0)
        {
            if (newLevel == oldLevel)
            {
                return;
            }
            if (newLevel < oldLevel)
            {
                if (GGVariables.Instance.CurrentLeader.Slot < 0)
                {
                    return;
                }
                if (slot == GGVariables.Instance.CurrentLeader.Slot)
                {
                    // was the leader
                    var newLeader = playerManager.FindLeader();
                    if (newLeader != null)
                    {
                        GGVariables.Instance.CurrentLeader.SetLeader(newLeader.Slot, (int)newLeader.Level);
                        if (newLeader.Slot != slot)
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
            if (GGVariables.Instance.CurrentLeader.Slot == slot) // still leading
            {
                PlaySoundForLeaderLevel(slot);
                return;
            }
            if (newLevel <= GGVariables.Instance.CurrentLeader.Level) // CurrentLeader != client
            {
                // not leading
                return;
            }
            if (newLevel > GGVariables.Instance.CurrentLeader.Level)
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
        private void ChangeFriendlyFire(bool Status)
        {
            if (GGVariables.Instance.Mp_friendlyfire == null)
                return;
            GGVariables.Instance.Mp_friendlyfire.Public = true; // set FCVAR_NOTIFY
            GGVariables.Instance.Mp_friendlyfire.SetValue(Status);
            string text;
            if (Status)
            {
                text = "friendlyfire.on";
            }
            else
            {
                text = "friendlyfire.off";
            }
            //            Server.PrintToChatAll(Localizer["friendlyfire.on"]);
            var playerEntities = GetValidPlayers();
            //            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.Connected && !p.IsBot && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var pc in playerEntities)
                {
                    var pl = playerManager.FindBySlot(pc.Slot);
                    if (pl != null)
                    {
                        pc.PrintToCenter(pl.Translate(text));
                    }
                }
            }
            PlayConfiguredSound("FriendlyFireInfo");
            //            PlaySound(null!, Config.FriendlyFireInfoSound);
        }
        private void Timer_HandicapUpdate()
        {
            if (warmupInitialized || Config.HandicapMode == 0)
            {
                LogHandicapDebug(
                    $"Periodic check skipped: warmupInitialised={warmupInitialized}, mode={Config.HandicapMode}.");
                return;
            }

            // get very minimum level
            int minimum = GetHandicapMinimumLevel(Config.HandicapSkipBots);
            if (minimum == -1)
            {
                LogHandicapDebug("Periodic check stopped: no minimum player level was found.");
                return;
            }
            // get handicap level for players above very minimum level
            int level = GetHandicapLevel(-1, minimum);
            LogHandicapDebug(
                $"Periodic check: mode={Config.HandicapMode}, minimumLevel={minimum}, targetLevel={level}, " +
                $"topRankHandicap={Config.TopRankHandicap}, handicapTopRank={Config.HandicapTopRank}, " +
                $"topRankWinsThreshold={HandicapTopWins}, statsEnabled={GGVariables.Instance.StatsEnabled}.");
            if (level <= minimum)
            {
                LogHandicapDebug(
                    $"Periodic check stopped: target level {level} is not above minimum level {minimum}.");
                return;
            }
            var playerEntities = GetValidPlayersWithBots();
            //            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.Connected && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var playerController in playerEntities)
                {
                    var player = playerManager.FindBySlot(playerController.Slot, "Timer_HandicapUpdate");
                    if (player == null || playerController.TeamNum <= 0)
                    {
                        continue;
                    }

                    LogHandicapDebug(
                        $"Periodic candidate: player={player.PlayerName}, slot={player.Slot}, bot={playerController.IsBot}, " +
                        $"team={playerController.TeamNum}, level={player.Level}, minimumLevel={minimum}, " +
                        $"wins={player.PlayerWins}, winsLoaded={IsPlayerWinsLoaded(player)}.");

                    if (player.Level == minimum)
                    {
                        if (Config.HandicapSkipBots && playerController.IsBot)
                        {
                            LogHandicapDebug(
                                $"Periodic decision: player={player.PlayerName}, result=skip, reason=bot.");
                            continue;
                        }
                        if (ShouldSkipHandicapForTopRank(player, "periodic"))
                        {
                            continue;
                        }
                        uint previousLevel = player.Level;
                        player.SetLevel(level);
                        player.CurrentKillsPerWeap = 0;
                        LogHandicapDebug(
                            $"Periodic decision: player={player.PlayerName}, result=apply, " +
                            $"previousLevel={previousLevel}, newLevel={player.Level}.");
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
                        if (Config.TurboMode && TryGetAlivePlayerPawn(playerController, out _))
                        {
                            GiveNextWeapon(player.Slot);
                        }
                        UpdatePlayerScoreLevel(player);
                    }
                }
            }
        }
        private int GetHandicapMinimumLevel(bool skipBots = false, int aboveLevel = -1, int skipClient = -1)
        {
            int minimum = -1;
            int level = 0;
            var playerEntities = GetValidPlayersWithBots();
            //            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.Connected && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var playerController in playerEntities)
                {
                    if (playerController.TeamNum > 0 && (Config.HandicapUseSpectators || playerController.TeamNum > 1))
                    {
                        if ((skipBots && playerController.IsBot) || (skipClient == playerController.Slot))
                        {
                            continue;
                        }
                        var player = playerManager.FindBySlot(playerController.Slot, "GetHandicapMinimumLevel");
                        if (player != null)
                        {
                            level = (int)player.Level;
                            if (aboveLevel >= level)
                            {
                                continue;
                            }
                            if ((minimum == -1) || (level < minimum))
                            {
                                minimum = level;
                            }
                        }

                    }
                }
            }
            return minimum;
        }
        private int GetHandicapLevel(int skipClient = -1, int aboveLevel = -1)
        {
            int level = 0;
            if (Config.HandicapMode == 1)
            {
                level = GetAverageLevel(Config.HandicapSkipBots, aboveLevel, skipClient);
            }
            else if (Config.HandicapMode == 2)
            {
                level = GetHandicapMinimumLevel(Config.HandicapSkipBots, aboveLevel, skipClient);
            }
            if (level == -1)
            {
                return 0;
            }
            level -= Config.HandicapLevelSubstract;
            if (Config.MaxHandicapLevel > 0 && Config.MaxHandicapLevel < level)
            {
                level = Config.MaxHandicapLevel;
            }
            if (level < 1)
            {
                return 0;
            }
            return level;
        }
        private int GetAverageLevel(bool skipBots = false, int aboveLevel = -1, int skipClient = -1)
        {
            int count = 0, level = 0, tmpLevel;
            var playerEntities = GetValidPlayersWithBots();
            //            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.Connected && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var playerController in playerEntities)
                {
                    if (playerController.TeamNum > 0 && (Config.HandicapUseSpectators || playerController.TeamNum > 1))
                    {
                        if ((skipBots && playerController.IsBot) || (skipClient == playerController.Slot))
                        {
                            continue;
                        }
                        var player = playerManager.FindBySlot(playerController.Slot, "GetAverageLevel");
                        if (player != null)
                        {
                            tmpLevel = (int)player.Level;
                            if (aboveLevel >= tmpLevel)
                            {
                                continue;
                            }
                            level += tmpLevel;
                            count++;
                        }
                    }
                }
            }
            if (count == 0)
            {
                return -1;
            }
            double average = level / count;
            return (int)Math.Floor(average); ;
        }
        private bool SetHandicapForClient(GGPlayer player, int first = 0) // if it’s the first time, then 1, so that toprank is not taken into account)
        {

            if (Config.HandicapTimesPerMap > 0)
            {
                if (!PlayerHandicapTimes.TryGetValue(player.SavedSteamID, out int handicapTimes))
                {
                    handicapTimes = 0;
                }

                if (handicapTimes >= Config.HandicapTimesPerMap)
                {
                    return false;
                }

                handicapTimes++;
                PlayerHandicapTimes[player.SavedSteamID] = handicapTimes;
            }

            return GiveHandicapLevel(player, first);
        }
        private bool GiveHandicapLevel(GGPlayer player, int first = 0)
        {
            if (Config.HandicapMode == 0)
            {
                LogHandicapDebug(
                    $"Join decision: player={player.PlayerName}, result=skip, reason=handicap-disabled.");
                return false;
            }

            if (ShouldSkipHandicapForTopRank(player, "join"))
            {
                return false;
            }

            int level = GetHandicapLevel(player.Slot);
            LogHandicapDebug(
                $"Join calculation: player={player.PlayerName}, slot={player.Slot}, currentLevel={player.Level}, " +
                $"targetLevel={level}, first={first}.");
            if (player.Level < level)
            {
                Logger.LogInformation($"Give Handicap level to {player.PlayerName} ({player.Slot}), up from {player.Level} to {level}");
                uint previousLevel = player.Level;
                player.SetLevel(level);
                player.CurrentKillsPerWeap = 0;
                UpdatePlayerScoreLevel(player);
                LogHandicapDebug(
                    $"Join decision: player={player.PlayerName}, result=apply, " +
                    $"previousLevel={previousLevel}, newLevel={player.Level}.");
            }
            else
            {
                LogHandicapDebug(
                    $"Join decision: player={player.PlayerName}, result=no-change, " +
                    $"currentLevel={player.Level}, targetLevel={level}.");
            }
            return true;
        }
        private bool ShouldSkipHandicapForTopRank(GGPlayer player, string source)
        {
            bool winsLoaded = IsPlayerWinsLoaded(player);
            bool statsEnabled = GGVariables.Instance.StatsEnabled;
            bool thresholdReady = Config.HandicapTopRank == 0 || HandicapTopWins > 0;
            bool isTopRank = winsLoaded && thresholdReady && IsPlayerInTopRank(player);

            string reason = "top-rank-handicap-enabled";
            bool skip = false;

            if (player.IsBot)
            {
                reason = "bot";
            }
            else if (Config.TopRankHandicap || Config.HandicapTopRank == 0)
            {
                reason = Config.TopRankHandicap
                    ? "top-rank-handicap-enabled"
                    : "top-rank-limit-disabled";
            }
            else if (!statsEnabled)
            {
                skip = true;
                reason = "statistics-not-ready";
            }
            else if (!winsLoaded)
            {
                skip = true;
                reason = "player-wins-not-loaded";
            }
            else if (!thresholdReady)
            {
                skip = true;
                reason = "top-rank-threshold-not-ready";
            }
            else if (isTopRank)
            {
                skip = true;
                reason = "player-in-top-rank";
            }
            else
            {
                reason = "player-outside-top-rank";
            }

            LogHandicapDebug(
                $"{source} top-rank decision: player={player.PlayerName}, slot={player.Slot}, result={(skip ? "skip" : "allow")}, " +
                $"reason={reason}, topRankHandicap={Config.TopRankHandicap}, handicapTopRank={Config.HandicapTopRank}, " +
                $"statsEnabled={statsEnabled}, winsLoaded={winsLoaded}, wins={player.PlayerWins}, " +
                $"topRankWinsThreshold={HandicapTopWins}, thresholdReady={thresholdReady}, isTopRank={isTopRank}.");

            return skip;
        }
        private void LogHandicapDebug(string message)
        {
            if (Config.HandicapDebugLog)
            {
                Logger.LogInformation($"[HANDICAP DEBUG] {message}");
            }
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
        private int GetCustomKillPerLevel(int level)
        {
            if (GGVariables.Instance.CustomKillsPerLevel.TryGetValue(level, out int kills))
            {
                return kills;
            }
            return Config.MinKillsPerLevel;
        }
        private void ClientSuicide(GGPlayer player, int loose) // how many levels to "loose"
        {
            int oldLevel = (int)player.Level;
            int newLevel = ChangeLevel(player, -loose);
            if (oldLevel == newLevel)
            {
                return;
            }
            string text;
            if (loose > 1)
            {
                text = "suiside.levels";
            }
            else
            {
                text = "suiside.alevel";
            }
            var playerEntities = GetValidPlayers();
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var pc in playerEntities)
                {
                    var pl = playerManager.FindBySlot(pc.Slot, "ClientSuicide");
                    if (pl != null)
                    {
                        pc.PrintToCenter(pl.Translate(text, player.PlayerName, loose));
                    }
                }
            }
            PrintLeaderToChat(player, oldLevel, newLevel);
        }
        private void PrintLeaderToChat(GGPlayer player, int oldLevel, int newLevel)
        {
            if (GGVariables.Instance.CurrentLeader.Slot != player.Slot || newLevel <= oldLevel)
            {
                return;
            }
            // newLevel > oldLevel
            if (GGVariables.Instance.CurrentLeader.Slot == player.Slot)
            {
                // say leading on level X
                var levelWeapon = player.LevelWeapon;
                if (Config.ShowLeaderWeapon && levelWeapon != null && levelWeapon.Index > 0)
                {
                    var pe = GetValidPlayers();
                    //                    Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.Connected && !p.IsBot && !p.IsHLTV);
                    if (pe != null && pe.Any())
                    {
                        foreach (var pc in pe)
                        {
                            var pl = playerManager.FindBySlot(pc.Slot, "PrintLeaderToChat1");
                            if (pl != null)
                            {
                                pc.PrintToCenter(pl.Translate("leading.onweapon", player.PlayerName, levelWeapon.Name));
                            }
                        }
                    }
                }
                else
                {
                    //                    Server.PrintToChatAll(Localizer["leading.onlevel", player.PlayerName, newLevel]);
                    var pe = GetValidPlayers();
                    //                    Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.Connected && !p.IsBot && !p.IsHLTV);
                    if (pe != null && pe.Any())
                    {
                        foreach (var pc in pe)
                        {
                            var pl = playerManager.FindBySlot(pc.Slot, "PrintLeaderToChat2");
                            if (pl != null)
                            {
                                pc.PrintToCenter(pl.Translate("leading.onlevel", player.PlayerName, newLevel));
                            }
                        }
                    }
                }
                return;
            }
            // CurrentLeader != client
            if (newLevel < GGVariables.Instance.CurrentLeader.Level)
            {
                var playerController = Utilities.GetPlayerFromSlot(player.Slot);
                if (playerController != null && IsValidHuman(playerController))
                {
                    // say how much to the lead
                    playerController.PrintToChat(player.Translate("levels.behind", GGVariables.Instance.CurrentLeader.Level - newLevel));
                }
                return;
            }
            // new level == leader level
            // say tied to the lead on level X
            var playerEntities = GetValidPlayers();
            //            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.Connected && !p.IsBot && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var pc in playerEntities)
                {
                    var pl = playerManager.FindBySlot(pc.Slot, "PrintLeaderToChat3");
                    if (pl != null)
                    {
                        pc.PrintToCenter(pl.Translate("tiedwith.leader", player.PlayerName, newLevel));
                    }
                }
            }
        }
        public int ChangeLevel(GGPlayer player, int difference, bool KnifeSteal = false, CCSPlayerController victimPc = null!)
        {
            if (difference == 0 || !GGVariables.Instance.IsActive || warmupInitialized || GGVariables.Instance.GameWinner != null)
            {
                return (int)player.Level;
            }

            int oldLevel = (int)player.Level;
            int Level = oldLevel + difference;

            if (Level < 1)
            {
                Level = 1;
            }
            if ((!Config.BotCanWin) && player.IsBot && (Level > GGVariables.Instance.WeaponOrderCount))
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
            int victim = -1;
            string playerName = player.PlayerName;
            string counterpartName = "";
            if (victimPc != null && victimPc.IsValid)
            {
                victim = victimPc.Slot;
                counterpartName = victimPc.PlayerName;
            }

            bool accept = true;
            if (player != null)
            {
                int playerSlot = player.Slot;
                try
                {
                    accept = CoreAPI.RaiseLevelChangeEvent(playerSlot, Level, difference, KnifeSteal, Level == GGVariables.Instance.WeaponOrderCount, knife, victim);
                }
                catch (Exception ex)
                {
                    //                    accept = false;
                    Server.NextFrame(() =>
                    {
                        Logger.LogError($"[GunGame API ERROR] RaiseLevelChangeEvent returned exception: {ex.Message}: pl - {playerName}, count - {counterpartName}");
                    });
                }
                if (!accept)
                {
                    Server.NextFrame(() =>
                    {
                        if (difference > 0)
                            Logger.LogInformation($"******** {playerName} killed {counterpartName} - changeLevel not accepted");
                        else
                            Logger.LogInformation($"******** changeLevel to {playerName} killed by {counterpartName} -  not accepted");
                    });
                    return oldLevel;
                }

                if (!GGVariables.Instance.IsVotingCalled && Level > (GGVariables.Instance.WeaponOrderCount - Config.VoteLevelLessWeaponCount))
                {
                    GGVariables.Instance.IsVotingCalled = true;
                    Server.ExecuteCommand("exec " + GGVariables.Instance.ActiveConfigFolder + "/gungame.mapvote.cfg");
                }

                if (Config.DisableRtvLevel > 0 && !GGVariables.Instance.IsCalledDisableRtv && Level >= Config.DisableRtvLevel)
                {
                    GGVariables.Instance.IsCalledDisableRtv = true;
                    Server.ExecuteCommand("exec " + GGVariables.Instance.ActiveConfigFolder + "/gungame.disable_rtv.cfg");
                }

                if (Config.EnableFriendlyFireLevel > 0 && !GGVariables.Instance.IsCalledEnableFriendlyFire && Level >= Config.EnableFriendlyFireLevel)
                {
                    GGVariables.Instance.IsCalledEnableFriendlyFire = true;
                    if (Config.FriendlyFireOnOff)
                    {
                        ChangeFriendlyFire(true);
                    }
                    else
                    {
                        ChangeFriendlyFire(false);
                    }
                }

                if (Level > GGVariables.Instance.WeaponOrderCount)
                {
                    /* Winner Winner Winner */
                    GGVariables.Instance.GameWinner = new(player);
                    LooserName = "";
                    if (victimPc != null && victimPc.IsValid)
                    {
                        LooserName = victimPc.PlayerName;
                    }
                    //                Logger.LogInformation($"Winner {player.PlayerName}");
                    if (Config.WinnerMessage > 0)
                    {
                        int winnerTeam = player.GetTeam();
                        if (Config.WinnerMessage == 1)
                        {
                            var playerEntities = GetValidPlayers();
                            if (playerEntities != null && playerEntities.Any())
                            {
                                foreach (var playerController in playerEntities)
                                {
                                    var pl = playerManager.FindBySlot(playerController.Slot);
                                    if (pl != null)
                                    {
                                        string text = (winnerTeam == 2 ? " \x02" : " \x0C") + pl.Translate("winner.is", GGVariables.Instance.GameWinner.Name) + (winnerTeam == 2 ? " \x0C" : " \x02") + pl.Translate("looser.is", LooserName);
                                        playerController.PrintToChat(text);
                                    }
                                }
                            }
                        }
                        else if (Config.WinnerMessage == 2 || Config.WinnerMessage == 3)
                        {
                            string fontColour;
                            if (winnerTeam == 2)
                            {
                                fontColour = "#FF5959";
                            }
                            else if (winnerTeam == 3)
                            {
                                fontColour = "#00BFFF";
                            }
                            else
                            {
                                fontColour = "#FFFFFF";
                            }
                            WinnerMessage[0] += fontColour;
                            if (winnerHintTick != null)
                            {
                                RemoveListener(winnerHintTick);
                            }
                            winnerHintTick = new(OnTickHandle);
                            RegisterListener(winnerHintTick);
                            float showTime = Config.EndGameDelay - 5;
                            if (showTime < 5)
                                showTime = 5;
                            StopTimer(ref winnerHintTimer);
                            winnerHintTimer = AddTimer(showTime, () =>
                            {
                                if (winnerHintTick != null)
                                {
                                    RemoveListener(winnerHintTick);
                                    winnerHintTick = null;
                                }
                                winnerHintTimer = null;
                            }, TimerFlags.STOP_ON_MAPCHANGE);
                        }
                        else if (Config.WinnerMessage == 4)
                        {
                            var playerEntities = GetValidPlayers();
                            if (playerEntities != null && playerEntities.Any())
                            {
                                foreach (var playerController in playerEntities)
                                {
                                    string part1, part2;
                                    using (new WithTemporaryCulture(playerController.GetLanguage()))
                                    {
                                        part1 = Localizer["winner.is", GGVariables.Instance.GameWinner.Name];
                                        part2 = Localizer["looser.is", LooserName];
                                    }
                                    DisplayHint(playerController, part1 + " " + part2);
                                }
                            }
                        }
                    }
                    try
                    {
                        CoreAPI.RaiseWinnerEvent(playerSlot, victim);
                    }
                    catch (Exception ex)
                    {
                        Server.NextFrame(() =>
                        {
                            Logger.LogError($"[GunGame API ERROR] RaiseWinnerEvent returned exception: {ex.Message}");
                        });
                    }
                    if (!(Config.DontAddWinsOnBot && victimPc != null && victimPc.IsValid && victimPc.IsBot))
                    {
                        SavePlayerWins(player);
                    }

                    if (Config.WinnerFreezePlayers)
                    {
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
                    PlayConfiguredSound("Winner", 1.7f);
                    //                    PlayRandomSoundDelayed(1.7f, Config.WinnerSound);

                    if (Config.AlltalkOnWin)
                    {
                        var sv_full_alltalk = ConVar.Find("sv_full_alltalk");
                        sv_full_alltalk?.SetValue(true);
                    }
                    return oldLevel;
                }

                // Client got new level
                player.SetLevel(Level);
                RecalculateLeader(player.Slot, oldLevel, Level);
                if (KnifeSteal && Config.KnifeProRecalcPoints && (oldLevel != Level))
                {
                    player.CurrentKillsPerWeap = player.CurrentKillsPerWeap * GetCustomKillPerLevel(Level) / GetCustomKillPerLevel(oldLevel);
                }
                else
                {
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
                    if (difference < 0)
                    {
                        PlayConfiguredSound("LevelDown", 0.0f, pc);
                        //                        PlaySound(pc, Config.LevelDownSound);
                    }
                    else
                    {
                        if (KnifeSteal)
                        {
                            PlayConfiguredSound("LevelStealUp", 0.0f, pc);
                            //                            PlaySound(pc, Config.LevelStealUpSound);
                        }
                        else
                        {
                            PlayConfiguredSound("LevelUp", 0.0f, pc);
                            //                            PlaySound(pc, Config.LevelUpSound);
                        }
                    }
                }
                //            UpdatePlayerScoreDelayed(player);

                return Level;
            }
            else
            {
                Logger.LogInformation($"******** killer invalid - changeLevel not accepted");
                return oldLevel;
            }
        }
        public void SavePlayerWins(GGPlayer player)
        {
            //            Logger.LogInformation($"{player.PlayerName} won, wins was {player.PlayerWins}");
            if (player.PlayerWins < 0)
            {
                Logger.LogError($"{player.PlayerName} win, but his PlayerWins is less than 0, so data need to be updated");
            }
            if (player.SavedSteamID == 0)
            {
                Logger.LogError($"{player.PlayerName} slot {player.Slot} win, but his SteamID is 0, so data can't be saved");
                return;
            }
            var stats = statsManager;
            if (stats != null)
                dbQueue?.EnqueueOperation(() => stats.SavePlayerWin(player));
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
            //            LogConnections = false;
            if (Config.EndGameDelay > 0)
            {
                StopTimer(ref emergencyTimer);
                emergencyTimer = AddTimer(Config.EndGameDelay + 25, () =>
                {
                    Server.NextFrame(() =>
                    {
                        Logger.LogWarning("Try to emergency end game");
                        EndMultiplayerGameNormal();
                    });
                }, TimerFlags.STOP_ON_MAPCHANGE);
                Console.WriteLine($"Call EndMultiplayerGame in {Config.EndGameDelay} seconds");
                //******************************************************
                Logger.LogInformation($"Call EndMultiplayerGame in {Config.EndGameDelay} seconds");
                endGameCount = 0;
                StopTimer(ref endGameTimer);
                endGameTimer = AddTimer(1.0f, EndMultiplayerGame, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);

                //                AddTimer(Config.EndGameDelay, EndMultiplayerGame, TimerFlags.STOP_ON_MAPCHANGE);
            }
            else
            {
                endGameCount = (int)Config.EndGameDelay;
                Logger.LogInformation($"Call EndMultiplayerGame now");
                EndMultiplayerGame();
            }
        }
        private void EndMultiplayerGame()
        {
            //******************************************************
            //            Logger.LogInformation($"EndMultiplayerGame in {endGameCount} seconds");
            if (++endGameCount < Config.EndGameDelay)
            {
                var seconds = Config.EndGameDelay - endGameCount;
                if (seconds < 6)
                {
                    var playerEntities = GetValidPlayers();
                    //                    Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.Connected && !p.IsBot && !p.IsHLTV);
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
            //******************************************************
            //            Logger.LogInformation($"EndMultiplayerGame: kill endGameTimer");
            var timerToKill = endGameTimer;
            Server.NextFrame(() =>
            {
                if (timerToKill != null)
                {
                    try
                    {
                        timerToKill.Kill();
                    }
                    catch (System.Exception)
                    {

                    }
                }
            });
            endGameTimer = null;

            if (Config.EndGameSilent)
            {
                Logger.LogInformation($"Call EndMultiplayerGameSilent now");
                EndMultiplayerGameSilent();
            }
            else
            {
                Logger.LogInformation($"Call EndMultiplayerGameNormal now");
                EndMultiplayerGameNormal();
            }
        }
        private void EndMultiplayerGameSilent()
        {
            Console.WriteLine("EndMultiplayerGameSilent");
            Logger.LogInformation("EndMultiplayerGameSilent");
            /*            var gameEnd = NativeAPI.CreateEvent("game_end", true);
                        NativeAPI.SetEventInt(gameEnd,"winner",2);
                        NativeAPI.FireEvent(gameEnd, false); */
            Server.ExecuteCommand("exec " + GGVariables.Instance.ActiveConfigFolder + "/gungame.gameend.cfg");
        }
        private void EndMultiplayerGameNormal()
        {
            Logger.LogInformation("EndMultiplayerGameNormal");
            Server.ExecuteCommand("exec " + GGVariables.Instance.ActiveConfigFolder + "/gungame.gameend.cfg");
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
                if (GGVariables.Instance.GameWinner != null && (CsTeam)GGVariables.Instance.GameWinner.TeamNum == CsTeam.Terrorist)
                {
                    gRules.TerminateRound(0.1f, RoundEndReason.TerroristsWin);
                }
                else
                {
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
                LogHandicapDebug("Top Rank threshold load skipped: stats manager is unavailable.");
                return;
            }
            int TotalWinners = await statsManager.GetNumberOfWinners();
            if (TotalWinners < 0)
            {
                HandicapTopWins = 0;
                LogHandicapDebug("Top Rank threshold is not ready because the statistics database is unavailable.");
                return;
            }
            if (Config.HandicapTopRank == 0)
            {
                HandicapTopWins = 0;
                LogHandicapDebug("Top Rank threshold disabled: HandicapTopRank=0.");
                return;
            }
            if (Config.HandicapTopRank >= TotalWinners)
            {
                HandicapTopWins = 1; // Handicap top wins = 1 (handicap top rank is more then total winners
                LogHandicapDebug(
                    $"Top Rank threshold loaded: totalWinners={TotalWinners}, handicapTopRank={Config.HandicapTopRank}, " +
                    $"winsThreshold={HandicapTopWins}.");
                return;
            }
            HandicapTopWins = await statsManager.GetWinsOfLowestTopPlayer(Config.HandicapTopRank);
            LogHandicapDebug(
                $"Top Rank threshold loaded: totalWinners={TotalWinners}, handicapTopRank={Config.HandicapTopRank}, " +
                $"winsThreshold={HandicapTopWins}.");
        }
        private static bool IsClientInTeam(CCSPlayerController player)
        {
            if (player.TeamNum == 2 || player.TeamNum == 3)
            {
                return true;
            }
            else
            {
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
            string wname = "Winner";
            if (GGVariables.Instance.GameWinner != null)
            {
                wname = GGVariables.Instance.GameWinner.Name;
            }
            var playerEntities = GetValidPlayers();
            //            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.Connected && !p.IsBot && !p.IsHLTV);
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var playerController in playerEntities)
                {
                    var pl = playerManager.FindBySlot(playerController.Slot);
                    if (pl != null)
                    {
                        if (Config.WinnerMessage == 2)              //print to Centre
                        {
                            playerController.PrintToCenter(pl.Translate("winner.is", wname) + "    " + pl.Translate("looser.is", LooserName));
                        }
                        else if (Config.WinnerMessage == 3)                                       //==3 print to CentreHtml
                        {
                            string text = WinnerMessage[0] + WinnerMessage[1] + pl.Translate("winner.is", wname) + WinnerMessage[2] + pl.Translate("looser.is", LooserName) + WinnerMessage[3];
                            playerController.PrintToCenterHtml(text);
                        }
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
            //            Utilities.GetPlayers().Where(p => p != null && p.IsValid && p.Connected == PlayerConnectedState.Connected && !p.IsBot && !p.IsHLTV);
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
                        Server.NextFrame(() =>
                        {
                            if (playerController != null && playerController.IsValid && playerController.Connected == PlayerConnectedState.Connected)
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
                Dictionary<string, int> TopPlayers = await statsManager.GetTopPlayers(Config.HandicapTopRank);
                Server.NextFrame(() =>
                {
                    if (playerController != null && playerController.IsValid && playerController.Connected == PlayerConnectedState.Connected)
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
        [RequiresPermissions("@css/root")]
        public async void OnDBResetCommand(CCSPlayerController? playerController, CommandInfo command)
        {
            CCSPlayerController? pc = null;
            if (playerController != null && playerController.IsValid)
            {
                pc = playerController;
            }
            if (statsManager == null)
            {
                Logger.LogError("OnDBResetCommand statsManager null, can't reset stats");
                if (pc != null)
                {
                    var player = playerManager.FindBySlot(pc.Slot, "OnDBResetCommand");
                    if (player != null)
                    {
                        pc.PrintToChat(player.Translate("database.error"));
                    }
                }
                return;
            }
            await statsManager.ResetStats();
            StatsLoadRank();
            Server.NextFrame(() =>
            {
                if (pc != null && pc.IsValid && pc.Connected == PlayerConnectedState.Connected)
                {
                    pc.PrintToChat("Stats reseted");
                }
                Logger.LogInformation("Stats reseted by " + (pc != null && pc.IsValid ? pc.PlayerName : "Console") + ".");
            });
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
                        if (playerController != null && playerController.IsValid && playerController.Connected == PlayerConnectedState.Connected)
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
            if (!Config.IsPluginEnabled)
            {
                Logger.LogWarning("GunGame mode is disabled in the configuration and cannot be enabled at runtime.");
                return;
            }

            runtimeGameEnabled = true;
            Server.ExecuteCommand("sv_cheats 1; endround; sv_cheats 0;");
            RestartGame(false);
        }

        [ConsoleCommand("gg_disable", "Disable GunGame")]
        [RequiresPermissions("@css/rcon")]
        public void OnDisableCommand(CCSPlayerController? playerController, CommandInfo command)
        {
            runtimeGameEnabled = false;
            GGVariables.Instance.IsActive = false;
            StopTimer(ref HandicapUpdateTimer);
            StopTimer(ref _infoTimer);
            foreach (var player in playerManager.GetPlayers())
            {
                StopTripleEffects(player);
            }
            Server.ExecuteCommand("sv_cheats 1; endround; sv_cheats 0;");
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
                    if (playerController != null && playerController.IsValid && playerController.Connected == PlayerConnectedState.Connected)
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
                {
                    Logger.LogWarning("Set RespawnRule 3 instead of 4 because no DM spawns on the current map");
                    intValue = 3;
                }
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
            if (pc is null || !pc.IsValid)
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
                            if (pc != null && pc.IsValid && pc.Connected == PlayerConnectedState.Connected)
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
        public void Respawn(CCSPlayerController player, bool spawnpoint = true)
        {
            if (!IsRespawnServiceActive || !IsValidPlayer(player))
            {
                return;
            }

            if ((Config.RespawnByPlugin == 1 && player.TeamNum != (int)CsTeam.Terrorist)
                || (Config.RespawnByPlugin == 2 && player.TeamNum != (int)CsTeam.CounterTerrorist)
                || (player.TeamNum != (int)CsTeam.Terrorist && player.TeamNum != (int)CsTeam.CounterTerrorist))
            {
                return;
            }

            var ggPlayer = playerManager.FindBySlot(player.Slot, "Respawn");
            if (ggPlayer == null || SkipSpawn.Contains(player.Slot))
            {
                return;
            }

            bool accept;
            try
            {
                accept = CoreAPI.RaiseRespawnPlayerEvent(player.Slot);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[GunGame API ERROR] RaiseRespawnPlayerEvent returned exception: {ex.Message}: slot - {player.Slot}");
                return;
            }

            if (!accept)
            {
                return;
            }

            ggPlayer.TeamNum = player.TeamNum;
            ggPlayer.CancelRespawnTimers();
            uint respawnSequence = ggPlayer.RespawnSequence;
            int playerSlot = ggPlayer.Slot;
            long connectionId = ggPlayer.ConnectionId;
            ggPlayer.RespawnTimer = AddTimer(1.0f, () =>
            {
                if (!playerManager.TryGetCurrentPlayer(playerSlot, connectionId, out var currentPlayer)
                    || currentPlayer == null
                    || currentPlayer.RespawnSequence != respawnSequence
                    || !IsRespawnServiceActive)
                {
                    return;
                }

                currentPlayer.RespawnTimer = null;
                var currentController = Utilities.GetPlayerFromSlot(playerSlot);
                if (currentController == null || !IsValidPlayer(currentController)
                    || currentController.TeamNum < (int)CsTeam.Terrorist
                    || SkipSpawn.Contains(playerSlot)
                    || !TryGetPlayerPawn(currentController, out var pawn)
                    || pawn.LifeState == (byte)LifeState_t.LIFE_ALIVE)
                {
                    return;
                }

                if (!spawnpoint)
                {
                    if (Config.RespawnByPlugin == 5)
                    {
                        skipRandomNavSpawnOnce.Add(playerSlot);
                    }
                    PlayerRespawn(currentPlayer, currentController, respawnSequence, null);
                    return;
                }

                if (Config.RespawnByPlugin == 5)
                {
                    PlayerRespawn(currentPlayer, currentController, respawnSequence, null);
                    return;
                }

                var spawn = GetSuitableSpawnPoint(playerSlot, currentController.TeamNum, Config.SpawnDistance);
                if (spawn != null)
                {
                    PlayerRespawn(currentPlayer, currentController, respawnSequence, spawn);
                    return;
                }

                currentPlayer.RespawnRetryTimer = AddTimer(0.5f, () =>
                {
                    if (!playerManager.TryGetCurrentPlayer(playerSlot, connectionId, out var retryPlayer)
                        || retryPlayer == null
                        || retryPlayer.RespawnSequence != respawnSequence
                        || !IsRespawnServiceActive)
                    {
                        return;
                    }

                    retryPlayer.RespawnRetryTimer = null;
                    var retryController = Utilities.GetPlayerFromSlot(playerSlot);
                    if (retryController == null || !IsValidPlayer(retryController)
                        || retryController.TeamNum < (int)CsTeam.Terrorist
                        || SkipSpawn.Contains(playerSlot)
                        || !TryGetPlayerPawn(retryController, out var retryPawn)
                        || retryPawn.LifeState == (byte)LifeState_t.LIFE_ALIVE)
                    {
                        return;
                    }

                    PlayerRespawn(retryPlayer, retryController, respawnSequence,
                        GetSuitableSpawnPoint(playerSlot, retryController.TeamNum, Config.SpawnDistance));
                }, TimerFlags.STOP_ON_MAPCHANGE);
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }
        // Prevent duplicate respawns until CS2 emits the corresponding spawn event.
        private void PlayerRespawn(GGPlayer player, CCSPlayerController playerController, uint respawnSequence, SpawnInfo? spawn)
        {
            int slot = player.Slot;
            SkipSpawn.Add(slot);
            playerController.Respawn();
            AddTimer(0.8f, () =>
            {
                if (playerManager.TryGetCurrentPlayer(slot, player.ConnectionId, out var currentPlayer)
                    && currentPlayer != null
                    && currentPlayer.RespawnSequence == respawnSequence)
                {
                    SkipSpawn.Remove(slot);
                }
            }, TimerFlags.STOP_ON_MAPCHANGE);
            if (spawn != null)
            {
                playerTeleport(slot, player.ConnectionId, spawn);
                FreeSpawnPointWithDelay(spawn.Position);
            }
        }
        private void playerTeleport(int slot, long connectionId, SpawnInfo spawn)
        {
            Server.NextFrame(() =>
            {
                if (!playerManager.TryGetCurrentPlayer(slot, connectionId, out _))
                    return;

                var playerController = Utilities.GetPlayerFromSlot(slot);
                if (playerController != null && IsValidPlayer(playerController) && TryGetPlayerPawn(playerController, out var pawn))
                {
                    pawn.Teleport(spawn.Position, spawn.Rotation, new Vector(0, 0, 0));
                }
            });
        }
        private void Shuffle<T>(IList<T> list)
        {
            //            Random rng = new Random();
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = random.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }
        private void LoadRandomNavSpawnAreas()
        {
            randomNavSpawnAreas.Clear();
            randomNavSpawnLoadAttempted = true;

            try
            {
                foreach (var area in CCSNavArea.GetAllNavAreas())
                {
                    if (area.Area2D < 4.0f || area.Normal.Z < 0.5f)
                    {
                        continue;
                    }

                    randomNavSpawnAreas.Add(area);
                }

                if (randomNavSpawnAreas.Count == 0 && !randomNavSpawnFallbackWarningLogged)
                {
                    Logger.LogWarning("[SPAWN] RespawnByPlugin 5: no usable NavMesh areas found. Falling back to map spawn entities.");
                    randomNavSpawnFallbackWarningLogged = true;
                }
            }
            catch (Exception ex)
            {
                if (!randomNavSpawnFallbackWarningLogged)
                {
                    Logger.LogWarning($"[SPAWN] RespawnByPlugin 5: failed to read NavMesh ({ex.Message}). Falling back to map spawn entities.");
                    randomNavSpawnFallbackWarningLogged = true;
                }
            }
        }

        private void ScheduleRandomNavSpawn(int slot)
        {
            AddTimer(RandomNavSpawnApplyDelay, () =>
            {
                if (Config.RespawnByPlugin != 5 || !IsRespawnServiceActive)
                {
                    skipRandomNavSpawnOnce.Remove(slot);
                    return;
                }

                if (skipRandomNavSpawnOnce.Remove(slot))
                {
                    return;
                }

                var playerController = Utilities.GetPlayerFromSlot(slot);
                if (playerController == null || !IsValidPlayer(playerController) || !IsClientInTeam(playerController)
                    || !TryGetPlayerPawn(playerController, out var pawn)
                    || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                {
                    return;
                }

                var spawn = GetRandomNavSpawnPoint(slot, playerController.TeamNum, pawn, Config.SpawnDistance);
                if (spawn == null)
                {
                    if (!randomNavSpawnFailureWarningLogged)
                    {
                        Logger.LogWarning($"[SPAWN] RespawnByPlugin 5: no safe random spawn found for {playerController.PlayerName} ({slot}); keeping the engine spawn. Further failures on this map will not be logged.");
                        randomNavSpawnFailureWarningLogged = true;
                    }
                    return;
                }

                try
                {
                    pawn.Teleport(spawn.Position, spawn.Rotation, new Vector(0, 0, 0));
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[SPAWN] RespawnByPlugin 5: Teleport failed for {playerController.PlayerName} ({slot}): {ex.Message}");
                    FreeSpawnPointWithDelay(spawn.Position);
                    return;
                }

                VerifyRandomNavSpawn(slot, spawn, retry: false);
                FreeSpawnPointWithDelay(spawn.Position);
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void VerifyRandomNavSpawn(int slot, SpawnInfo spawn, bool retry)
        {
            AddTimer(RandomNavSpawnVerifyDelay, () =>
            {
                if (Config.RespawnByPlugin != 5 || !IsRespawnServiceActive)
                {
                    return;
                }

                var playerController = Utilities.GetPlayerFromSlot(slot);
                if (playerController == null || !IsValidPlayer(playerController) || !IsClientInTeam(playerController)
                    || !TryGetPlayerPawn(playerController, out var pawn)
                    || pawn.LifeState != (byte)LifeState_t.LIFE_ALIVE
                    || pawn.AbsOrigin == null)
                {
                    return;
                }

                var actual = pawn.AbsOrigin;
                double dx = actual.X - spawn.Position.X;
                double dy = actual.Y - spawn.Position.Y;
                double dz = actual.Z - spawn.Position.Z;
                double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

                if (distance <= RandomNavSpawnVerifyTolerance)
                {
                    PlayRandomNavSpawnSound(playerController, pawn);
                    return;
                }

                if (!retry)
                {
                    Logger.LogWarning($"[SPAWN] NAV teleport was overwritten for {playerController.PlayerName} ({slot}): target={spawn.Position}, actual={actual}, distance={distance:F1}. Reapplying once.");
                    try
                    {
                        pawn.Teleport(spawn.Position, spawn.Rotation, new Vector(0, 0, 0));
                        VerifyRandomNavSpawn(slot, spawn, retry: true);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"[SPAWN] RespawnByPlugin 5: retry Teleport failed for {playerController.PlayerName} ({slot}): {ex.Message}");
                    }
                    return;
                }

                Logger.LogWarning($"[SPAWN] NAV teleport verification failed after retry for {playerController.PlayerName} ({slot}): target={spawn.Position}, actual={actual}, distance={distance:F1}");
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        private void PlayRandomNavSpawnSound(CCSPlayerController spawnedPlayer, CCSPlayerPawn spawnedPawn)
        {
            if (string.IsNullOrWhiteSpace(Config.SpawnSound))
            {
                return;
            }

            RecipientFilter recipients = [];
            bool hasRecipients = false;

            foreach (var playerController in GetValidPlayers())
            {
                if (playerController.Slot == spawnedPlayer.Slot
                    || playerController.TeamNum == spawnedPlayer.TeamNum
                    || !TryGetAlivePlayerPawn(playerController, out _))
                {
                    continue;
                }

                recipients.Add(playerController);
                hasRecipients = true;
            }

            if (!hasRecipients)
            {
                return;
            }

            try
            {
                spawnedPawn.EmitSound(Config.SpawnSound, recipients);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[SPAWN] Failed to play spawn sound '{Config.SpawnSound}': {ex.Message}");
            }
        }

        private SpawnInfo? GetRandomNavSpawnPoint(int slot, int team, CCSPlayerPawn pawn, double minDistance)
        {
            if (!randomNavSpawnLoadAttempted)
            {
                LoadRandomNavSpawnAreas();
            }

            if (randomNavSpawnAreas.Count > 0)
            {
                for (int attempt = 0; attempt < RandomNavSpawnMaxAttempts; attempt++)
                {
                    var area = randomNavSpawnAreas[random.Next(randomNavSpawnAreas.Count)];
                    var min = area.Min;
                    var max = area.Max;

                    float marginX = Math.Min(RandomNavSpawnAreaMargin, area.Width * 0.45f);
                    float marginY = Math.Min(RandomNavSpawnAreaMargin, area.Height * 0.45f);
                    float minX = min.X + marginX;
                    float maxX = max.X - marginX;
                    float minY = min.Y + marginY;
                    float maxY = max.Y - marginY;

                    float x = minX >= maxX ? area.Center.X : minX + (float)random.NextDouble() * (maxX - minX);
                    float y = minY >= maxY ? area.Center.Y : minY + (float)random.NextDouble() * (maxY - minY);
                    var navPoint = area.GetClosestPoint(new Vector(x, y, area.Center.Z));

                    var floorStart = new Vector(navPoint.X, navPoint.Y, navPoint.Z + RandomNavSpawnFloorProbeUp);
                    var floorEnd = new Vector(navPoint.X, navPoint.Y, navPoint.Z - RandomNavSpawnFloorProbeDown);
                    var floorTrace = CSTrace.TraceEndShape(
                        floorStart,
                        floorEnd,
                        ignoreEntity: pawn,
                        options: new CSTraceOptions
                        {
                            InteractsWith = Masks.PlayerSolidBrushOnly,
                            InteractsExclude = Contents.Pickup
                        });

                    if (!floorTrace.DidHit() || floorTrace.Normal.Z < 0.5f)
                    {
                        continue;
                    }

                    var hitPoint = floorTrace.HitPoint;
                    var position = new Vector(hitPoint.X, hitPoint.Y, hitPoint.Z + RandomNavSpawnFloorOffset);

                    if (IsAlivePlayerNearby(slot, position, minDistance))
                    {
                        continue;
                    }

                    var hullResult = CSTrace.TraceHullShape(
                        position,
                        new Vector(position.X, position.Y, position.Z + 0.1f),
                        new Vector(-RandomNavSpawnHullHalfWidth, -RandomNavSpawnHullHalfWidth, 0),
                        new Vector(RandomNavSpawnHullHalfWidth, RandomNavSpawnHullHalfWidth, RandomNavSpawnHullHeight),
                        ignoreEntity: pawn,
                        options: new CSTraceOptions
                        {
                            InteractsWith = Masks.PlayerSolidBrushOnly,
                            InteractsExclude = Contents.Pickup
                        });

                    if (hullResult.DidHit() || !TryReserveRandomSpawn(position, minDistance))
                    {
                        continue;
                    }

                    return new SpawnInfo(
                        position,
                        new QAngle(0, (float)(random.NextDouble() * 360.0), 0));
                }
            }

            var fallback = GetFallbackSpawnPoint(slot, team, minDistance);
            if (fallback != null && !randomNavSpawnFallbackWarningLogged)
            {
                Logger.LogWarning("[SPAWN] RespawnByPlugin 5: using map spawn entities because no safe NavMesh position was available.");
                randomNavSpawnFallbackWarningLogged = true;
            }

            return fallback;
        }

        private bool IsAlivePlayerNearby(int slot, Vector position, double minDistance)
        {
            if (minDistance <= 0)
            {
                return false;
            }

            double minDistanceSquared = minDistance * minDistance;
            foreach (var playerController in GetValidPlayersWithBots())
            {
                if (playerController.Slot == slot || !IsClientInTeam(playerController))
                {
                    continue;
                }

                if (!TryGetAlivePlayerPawn(playerController, out var playerPawn) || playerPawn.AbsOrigin == null)
                {
                    continue;
                }

                double dx = playerPawn.AbsOrigin.X - position.X;
                double dy = playerPawn.AbsOrigin.Y - position.Y;
                if ((dx * dx) + (dy * dy) < minDistanceSquared)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryReserveRandomSpawn(Vector position, double minDistance)
        {
            double minDistanceSquared = minDistance * minDistance;

            lock (spawnLock)
            {
                foreach (var reserved in usedSpawnPoints)
                {
                    double dx = reserved.X - position.X;
                    double dy = reserved.Y - position.Y;
                    if ((dx * dx) + (dy * dy) < minDistanceSquared)
                    {
                        return false;
                    }
                }

                usedSpawnPoints.Add(position);
                return true;
            }
        }

        private SpawnInfo? GetFallbackSpawnPoint(int slot, int team, double minDistance)
        {
            if (GGVariables.Instance.spawnPoints.TryGetValue(4, out var dmSpawns) && dmSpawns.Count > 0)
            {
                var dmSpawn = GetSuitableSpawnPointForType(slot, 4, minDistance, logFailure: false);
                if (dmSpawn != null)
                {
                    return dmSpawn;
                }
            }

            return GetSuitableSpawnPointForType(slot, team, minDistance, logFailure: false);
        }

        private SpawnInfo GetSuitableSpawnPoint(int slot, int team, double minDistance = 39.0)
        {
            int spawnType = Config.RespawnByPlugin == 4 ? 4 : team;
            return GetSuitableSpawnPointForType(slot, spawnType, minDistance, logFailure: true)!;
        }

        private SpawnInfo? GetSuitableSpawnPointForType(int slot, int spawnType, double minDistance, bool logFailure)
        {
            if (!GGVariables.Instance.spawnPoints.ContainsKey(spawnType))
            {
                if (logFailure)
                {
                    Logger.LogError($"SpawnPoints not ContainsKey {spawnType}");
                }
                return null;
            }

            var shuffledSpawns = new List<SpawnInfo>(GGVariables.Instance.spawnPoints[spawnType]);
            Shuffle(shuffledSpawns);

            lock (spawnLock)
            {
                foreach (var spawn in shuffledSpawns)
                {
                    if (usedSpawnPoints.Contains(spawn.Position))
                    {
                        continue;
                    }

                    if (!playerManager.IsPlayerNearby(slot, spawn.Position, minDistance) && spawn != LastSpawns[spawnType])
                    {
                        usedSpawnPoints.Add(spawn.Position);
                        LastSpawns[spawnType] = spawn;
                        return spawn;
                    }
                }
            }

            if (logFailure)
            {
                Logger.LogInformation($"No suitable spawn points for player {slot}");
            }

            return null;
        }
        public void FreeSpawnPointWithDelay(Vector position)
        {
            AddTimer(0.8f, () =>
            {
                lock (spawnLock)
                {
                    if (usedSpawnPoints.Contains(position))
                    {
                        usedSpawnPoints.Remove(position);
                        //                        Logger.LogInformation($"Spawn point at {position} freed after delay.");
                    }
                }
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }
        private void SetSpawnRules(int spawnType)
        {
            var respawn_on_death_ct = ConVar.Find("mp_respawn_on_death_ct");
            var respawn_on_death_t = ConVar.Find("mp_respawn_on_death_t");
            if (respawn_on_death_ct == null)
            {
                Logger.LogWarning("cvar respawn_on_death_ct can't be found");
            }
            if (respawn_on_death_t == null)
            {
                Logger.LogWarning("cvar respawn_on_death_t can't be found");
            }

            switch (spawnType)
            {
                case 0:
                    Config.RespawnByPlugin = 0;
                    respawn_on_death_ct?.SetValue(true);
                    respawn_on_death_t?.SetValue(true);
                    Console.WriteLine("Plugin Respawn off");
                    Logger.LogInformation("Plugin Respawn off");
                    break;
                case 1:
                    Config.RespawnByPlugin = 1;
                    respawn_on_death_ct?.SetValue(true);
                    respawn_on_death_t?.SetValue(false);
                    Console.WriteLine("Plugin Respawn T on");
                    Logger.LogInformation("Plugin Respawn T on");
                    break;
                case 2:
                    Config.RespawnByPlugin = 2;
                    respawn_on_death_ct?.SetValue(false);
                    respawn_on_death_t?.SetValue(true);
                    Console.WriteLine("Plugin Respawn CT on");
                    Logger.LogInformation("Plugin Respawn CT on");
                    break;
                case 3:
                    Config.RespawnByPlugin = 3;
                    respawn_on_death_ct?.SetValue(false);
                    respawn_on_death_t?.SetValue(false);
                    Console.WriteLine("Plugin Respawn T and CT on");
                    Logger.LogInformation("Plugin Respawn T and CT on");
                    break;
                case 4:
                    Config.RespawnByPlugin = 4;
                    respawn_on_death_ct?.SetValue(false);
                    respawn_on_death_t?.SetValue(false);
                    Console.WriteLine("Plugin Respawn DM on");
                    Logger.LogInformation("Plugin Respawn DM on");
                    break;
                case 5:
                    Config.RespawnByPlugin = 5;
                    respawn_on_death_ct?.SetValue(false);
                    respawn_on_death_t?.SetValue(false);
                    Console.WriteLine("Plugin Respawn Random NavMesh on");
                    Logger.LogInformation("Plugin Respawn Random NavMesh on");
                    break;
                default:
                    Console.WriteLine($"Error set Respawn Rules with code {spawnType}");
                    Logger.LogError($"Error set Respawn Rules with code {spawnType}");
                    break;
            }
        }
        public void DisplayHint(CCSPlayerController? playerController, string text)
        {
            if (playerController == null || !IsValidPlayer(playerController)) return;

            CCSPlayerController targetEntity = playerController;
            float time = 5.0f;
            float height = -40.0f;
            float range = -50.0f;
            bool follow = true;
            bool showOffScreen = true;
            string iconOnScreen = "icon_bulb";
            string iconOffScreen = "icon_arrow_up";
            string cmd = "use_binding";
            bool showTextAlways = false;
            Color color = Color.FromArgb(255, 255, 0, 0);

            playerController.ReplicateConVar("sv_gameinstructor_enable", "true");
            playerController.ReplicateConVar("gameinstructor_enable", "true");

            Server.NextFrame(() =>
            {
                DisplayInstructorHint(targetEntity, time, height, range, follow, showOffScreen, iconOnScreen, iconOffScreen, cmd, showTextAlways, color, text);
            });
        }
        private void DisplayInstructorHint(CCSPlayerController targetEntity, float time, float height, float range, bool follow, bool showOffScreen, string iconOnScreen, string iconOffScreen, string cmd, bool showTextAlways, Color color, string text)
        {
            CEnvInstructorHint entity = Utilities.CreateEntityByName<CEnvInstructorHint>("env_instructor_hint")!;

            if (entity == null) return;

            string buffer = targetEntity.Index.ToString();

            entity.Target = buffer;
            entity.HintTargetEntity = buffer;
            entity.Static = follow;
            entity.Timeout = (int)time;
            if (time > 0.0f) RemoveEntity(entity, time);

            // Height
            entity.IconOffset = height;

            // Range
            entity.Range = range;

            // Show off screen
            entity.NoOffscreen = showOffScreen;

            // Icons
            entity.Icon_Onscreen = iconOnScreen;
            entity.Icon_Offscreen = iconOffScreen;

            // Command binding
            entity.Binding = cmd;

            // Show text behind walls
            entity.ForceCaption = showTextAlways;

            // Text color
            entity.Color = color;

            // Text
            text = text.Replace("\n", " ");
            entity.Caption = text;

            entity.DispatchSpawn();
            entity.AcceptInput("ShowHint");
        }
        private void RemoveEntity(CEnvInstructorHint entity, float time = 0.0f)
        {
            if (time == 0.0f)
            {
                if (entity.IsValid)
                {
                    entity.AcceptInput("Kill");
                }
            }
            else if (time > 0.0f)
            {
                AddTimer(time, () =>
                {
                    if (entity.IsValid)
                    {
                        entity.AcceptInput("Kill");
                    }
                }, TimerFlags.STOP_ON_MAPCHANGE);
            }
        }
        public List<CCSPlayerController> GetValidPlayers()
        {
            return Utilities.GetPlayers().FindAll(p => p != null && p.IsValid && p.SteamID.ToString().Length == 17 && p.Connected == PlayerConnectedState.Connected && !p.IsBot && !p.IsHLTV);
        }
        public List<CCSPlayerController> GetValidPlayersWithBots()
        {
            return Utilities.GetPlayers().FindAll(p =>
            p != null && p.IsValid && p.SteamID.ToString().Length == 17 && p.Connected == PlayerConnectedState.Connected && !p.IsBot && !p.IsHLTV ||
            p != null && p.IsValid && p.Connected == PlayerConnectedState.Connected && p.IsBot && !p.IsHLTV
            );
        }
        public bool IsValidPlayer(CCSPlayerController? p)
        {
            if (p != null && p.IsValid && (p.SteamID.ToString().Length == 17 || (p.SteamID == 0 && p.IsBot)) &&
                p.Connected == PlayerConnectedState.Connected && !p.IsHLTV)
            {
                return true;
            }
            return false;
        }
        public bool IsValidHuman(CCSPlayerController? p)
        {
            if (p != null && p.IsValid && p.SteamID.ToString().Length == 17 &&
                p.Connected == PlayerConnectedState.Connected && !p.IsBot && !p.IsHLTV)
            {
                return true;
            }
            return false;
        }
    }
    public class PlayerManager
    {
        public PlayerManager(GunGame plugin)
        {
            Plugin = plugin;
        }
        private GunGame Plugin;
        private readonly object lockObject = new();
        private readonly Dictionary<int, GGPlayer> playerMap = new();
        private readonly Dictionary<ulong, GGPlayer> steamPlayerMap = new();
        public GGPlayer? InitPlayer(int slot)
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
            return player;
        }
        public GGPlayer? CreatePlayerBySlot(int slot)
        {
            if (slot < 0 || slot > Models.Constants.MaxPlayers)
                return null;
            GGPlayer player;
            lock (lockObject)
            {
                if (!playerMap.TryGetValue(slot, out var existingPlayer))
                {
                    player = new GGPlayer(slot, Plugin);
                    playerMap.Add(slot, player);
                }
                else
                {
                    player = existingPlayer;
                }
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
            lock (lockObject)
            {
                if (playerMap.TryGetValue(slot, out GGPlayer? player))
                {
                    return player;
                }
            }

            var pc = Utilities.GetPlayerFromSlot(slot);
            if (pc != null && pc.IsValid)
            {
                Plugin.Logger.LogWarning($"[GUNGAME] Recreating missing player slot {slot} from {name}.");
                return CreatePlayerBySlot(slot);
            }

            return null;
        }
        public bool PlayerExists(int slot)
        {
            return playerMap.TryGetValue(slot, out GGPlayer? player);
        }
        public bool IsCurrentPlayer(int slot, GGPlayer player)
        {
            lock (lockObject)
            {
                return playerMap.TryGetValue(slot, out GGPlayer? currentPlayer) && ReferenceEquals(currentPlayer, player);
            }
        }
        public bool TryGetCurrentPlayer(int slot, long connectionId, out GGPlayer? player)
        {
            lock (lockObject)
            {
                if (playerMap.TryGetValue(slot, out var currentPlayer) && currentPlayer.ConnectionId == connectionId)
                {
                    player = currentPlayer;
                    return true;
                }
            }

            player = null;
            return false;
        }
        public void RegisterSteamId(GGPlayer player, ulong previousSteamId)
        {
            lock (lockObject)
            {
                if (previousSteamId != 0 && steamPlayerMap.TryGetValue(previousSteamId, out var existing) && ReferenceEquals(existing, player))
                {
                    steamPlayerMap.Remove(previousSteamId);
                }

                if (player.SavedSteamID != 0)
                {
                    steamPlayerMap[player.SavedSteamID] = player;
                }
            }
        }
        public bool TryGetBySteamId(ulong steamId, out GGPlayer? player)
        {
            lock (lockObject)
            {
                if (steamId != 0 && steamPlayerMap.TryGetValue(steamId, out var currentPlayer))
                {
                    player = currentPlayer;
                    return true;
                }
            }

            player = null;
            return false;
        }
        public List<GGPlayer> GetPlayers()
        {
            lock (lockObject)
            {
                return playerMap.Values.ToList();
            }
        }
        public GGPlayer? FindLeader()
        {
            GGPlayer? leader = null;
            uint leaderLevel = 0;
            lock (lockObject)
            {
                foreach (var player in playerMap.Values)
                {
                    if (player.IsInActiveTeam && player.Level > leaderLevel)
                    {
                        leaderLevel = player.Level;
                        leader = player;
                    }
                }
            }
            return leader;
        }
        public void ForgetPlayer(int slot)
        {
            lock (lockObject)
            {
                if (playerMap.TryGetValue(slot, out GGPlayer? player))
                {
                    playerMap.Remove(slot);
                    if (player.SavedSteamID != 0 && steamPlayerMap.TryGetValue(player.SavedSteamID, out var currentPlayer) && ReferenceEquals(currentPlayer, player))
                    {
                        steamPlayerMap.Remove(player.SavedSteamID);
                    }
                    player.CancelTimers();
                }
            }
        }
        public void Clear()
        {
            List<GGPlayer> players;
            lock (lockObject)
            {
                players = playerMap.Values.ToList();
                playerMap.Clear();
                steamPlayerMap.Clear();
            }

            foreach (var player in players)
            {
                player.CancelTimers();
            }
        }
        public bool IsPlayerNearby(int slot, Vector spawn, double minDistance = 39.0)
        {
            if (playerMap.Count == 1)
                return false;
            double minD = 10000.0;
            double dist;

            foreach (var player in playerMap)
            {
                if (player.Value.Slot == slot)
                    continue;
                var pc = Utilities.GetPlayerFromSlot(player.Value.Slot);
                if (pc != null && Plugin.IsValidPlayer(pc))
                {
                    if (Plugin.TryGetPlayerPawn(pc, out var playerPawn) &&
                        playerPawn.AbsOrigin != null)
                    {
                        /*                        if (IsPlayerNearEntity(spawn, playerPawn.AbsOrigin, minDistance))
                                                {
                                                    return true;
                                                }*/
                        /*                        if (playerPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                                                {
                                                    continue;
                                                } */
                        dist = PlayerDistance(spawn, playerPawn.AbsOrigin);
                        if (dist < minD)
                        {
                            minD = dist;
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
            return squaredDistance <= (minDistance * minDistance);
        }
        private static double PlayerDistance(Vector entity, Vector player)
        {
            // Calculate the squared distance to avoid the square root for performance reasons
            double Distance = Math.Sqrt((player.X - entity.X) * (player.X - entity.X) +
                                    (player.Y - entity.Y) * (player.Y - entity.Y));

            // Compare squared distances (since sqrt is monotonic, the comparison is equivalent)
            return Distance;
        }
        public void ClearRoundsPerLevel()
        {
            foreach (var player in playerMap)
            {
                player.Value.LevelsPerRound = 0;
            }
        }
    }
    public class GGPlayer
    {
        private static long nextConnectionId;
        private readonly object lockObject = new();
        private readonly GunGame Plugin;
        private ulong statsLoadSteamId;
        private bool statsLoadRequested;
        public GGPlayer(int slot, GunGame plugin)
        {
            Plugin = plugin;
            Slot = slot;
            Level = 1;
            PlayerName = "";
            Index = -1;
            SavedSteamID = 0;
            IdAttempts = 0;
            LevelWeapon = GGVariables.Instance.weaponsList.FirstOrDefault(w => w.Level == 1) ?? new();
            Angles = new QAngle();
            Origin = new Vector();
            PlayerWins = -1;
            PlayerPlace = -1;
            IP = "";
            Culture = null!;
            Music = true;
            LevelRestore = false;
            PutInServer = false;
            ConnectionId = Interlocked.Increment(ref nextConnectionId);
        }
        public void UpdatePlayerController(CCSPlayerController playerController)
        {
            Index = (int)playerController.Index;
            PlayerName = playerController.PlayerName;
            SavedSteamID = playerController.SteamID;
            IsBot = playerController.IsBot;
            TeamNum = playerController.TeamNum;
        }
        public string PlayerName { get; private set; }
        public uint Level { get; private set; } = 1;
        public Weapon LevelWeapon { get; private set; }
        public int Index { get; private set; }
        public int Slot { get; private set; }
        public long ConnectionId { get; }
        public bool IsBot { get; set; } = false;
        public int TeamNum { get; set; }
        public bool IsInActiveTeam => TeamNum == (int)CsTeam.Terrorist || TeamNum == (int)CsTeam.CounterTerrorist;
        public int NumberOfNades { get; set; } = 0;
        public bool BlockFastSwitchOnChange { get; set; } = true;
        public bool BlockSwitch { get; set; } = false;
        public int CurrentKillsPerWeap { get; set; } = 0;
        public int CurrentLevelPerRound { get; set; } = 0;
        public int CurrentLevelPerRoundTriple { get; set; } = 0;
        public bool TeamChange { get; set; } = false;
        public bool TripleEffects { get; set; } = false;
        public bool TripleGodModeApplied { get; set; }
        public bool TripleGravityApplied { get; set; }
        public bool TripleSpeedApplied { get; set; }
        public ulong SavedSteamID { get; set; }
        public bool LevelRestore { get; set; } = false;
        public int LevelsPerRound { get; set; } = 0;
        public bool PutInServer { get; set; } = false;
        public int IdAttempts { get; set; }
        public CounterStrikeSharp.API.Modules.Timers.Timer? AuthorisationRetryTimer { get; set; }
        public CounterStrikeSharp.API.Modules.Timers.Timer? RespawnTimer { get; set; }
        public CounterStrikeSharp.API.Modules.Timers.Timer? RespawnRetryTimer { get; set; }
        public CounterStrikeSharp.API.Modules.Timers.Timer? TripleEffectsTimer { get; set; }
        public CounterStrikeSharp.API.Modules.Timers.Timer? ScoreUpdateTimer { get; set; }
        public uint RespawnSequence { get; set; }
        public QAngle? Angles { get; set; }
        public Vector? Origin { get; set; }
        public int AfkCount { get; set; } = 0;
        public PlayerStates State { get; set; }
        public int PlayerWins { get; set; }
        public int PlayerPlace { get; set; }
        public string IP { get; set; }
        public CultureInfo Culture { get; set; }
        public bool Music { get; set; }
        public void CancelTimers()
        {
            AuthorisationRetryTimer = CancelTimer(AuthorisationRetryTimer);
            RespawnTimer = CancelTimer(RespawnTimer);
            RespawnRetryTimer = CancelTimer(RespawnRetryTimer);
            TripleEffectsTimer = CancelTimer(TripleEffectsTimer);
            ScoreUpdateTimer = CancelTimer(ScoreUpdateTimer);
        }
        public void CancelRespawnTimers()
        {
            RespawnSequence++;
            RespawnTimer = CancelTimer(RespawnTimer);
            RespawnRetryTimer = CancelTimer(RespawnRetryTimer);
        }
        public void CancelTripleEffectsTimer()
        {
            TripleEffectsTimer = CancelTimer(TripleEffectsTimer);
        }
        public void CancelScoreUpdateTimer()
        {
            ScoreUpdateTimer = CancelTimer(ScoreUpdateTimer);
        }
        private static CounterStrikeSharp.API.Modules.Timers.Timer? CancelTimer(CounterStrikeSharp.API.Modules.Timers.Timer? timer)
        {
            if (timer == null)
            {
                return null;
            }

            try
            {
                timer.Kill();
            }
            catch (Exception)
            {
            }

            return null;
        }
        public bool TryQueueStatsLoad(ulong steamId)
        {
            if (steamId == 0)
            {
                return false;
            }

            if (statsLoadSteamId != steamId)
            {
                statsLoadSteamId = steamId;
                statsLoadRequested = false;
            }

            if (statsLoadRequested)
            {
                return false;
            }

            statsLoadRequested = true;
            return true;
        }
        public void ResetStatsLoadRequest(ulong steamId)
        {
            if (statsLoadSteamId == steamId)
            {
                statsLoadRequested = false;
            }
        }
        public void SetLevel(int setLevel)
        {
            if (setLevel > GGVariables.Instance.WeaponOrderCount || setLevel < 0)
                return;
            lock (lockObject)
            {
                this.Level = (uint)setLevel;
                if (setLevel > 0)
                    LevelWeapon = GGVariables.Instance.weaponsList.FirstOrDefault(w => w.Level == Level) ?? new();
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
            if (playerController != null && Plugin.TryGetAlivePlayerPawn(playerController, out _))
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
            NativeAPI.IssueClientCommand((int)this.Slot, $"slot{slot}");
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
            if (!Plugin.playerManager.IsCurrentPlayer(Slot, this) || pl == null || !Plugin.IsValidHuman(pl) || pl.AuthorizedSteamID == null || pl.AuthorizedSteamID.SteamId64 != SavedSteamID)
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
        public void ResetPlayer()
        {
            CancelRespawnTimers();
            AfkCount = 0;
            CurrentKillsPerWeap = 0;
            CurrentLevelPerRound = 0;
            CurrentLevelPerRoundTriple = 0;
            NumberOfNades = 0;
            TeamChange = false;
            State &= ~(PlayerStates.GrenadeLevel | PlayerStates.KnifeElite);
            SetLevel(1);
        }
    }
    public class SoundMapper
    {
        private readonly GunGame _plugin;
        private GGConfig? _currentConfig; // Instance field, not static
        private Dictionary<string, string>? _soundMap; // Simple dictionary, not Lazy
        private readonly object _initLock = new object(); // Keep the lock for thread safety

        public SoundMapper(GunGame plugin)
        {
            _plugin = plugin;
        }

        public readonly record struct SoundInfo(string SoundValue, bool IsRandom, List<string>? SoundList = null);

        public void Initialize(GGConfig loadedConfig)
        {
            lock (_initLock)
            {
                _currentConfig = loadedConfig ?? throw new ArgumentNullException(nameof(loadedConfig));

                // Create a new dictionary instance directly from the new config.
                // This replaces the old map entirely.
                _soundMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "MultiKill", _currentConfig.MultiKillSound ?? "" },
                    { "KnifeInfo",  _currentConfig.KnifeInfoSound  ?? ""  },
                    { "MultiLevel",   _currentConfig.MultiLevelSound   ?? ""   },
                    { "FriendlyFireInfo", _currentConfig.FriendlyFireInfoSound ?? "" },
                    { "WarmupTimer",  _currentConfig.WarmupTimerSound  ?? ""  },
                    { "LevelDown", _currentConfig.LevelDownSound ?? "" },
                    { "LevelUp", _currentConfig.LevelUpSound ?? "" },
                    { "LevelStealUp", _currentConfig.LevelStealUpSound ?? "" },
                    { "MolotovKill", _currentConfig.MolotovKillSound ?? "" },
                    { "NadeInfo", _currentConfig.NadeInfoSound ?? "" }
                };

                _plugin.Logger.LogInformation("[GUNGAME] ************ SoundMapper re-initialized and sound map rebuilt.");
            }
        }

        public SoundInfo? GetSoundValue(string soundIdentifierKey)
        {
            // Check the instance fields.
            if (_currentConfig == null || _soundMap == null)
            {
                _plugin.Logger.LogError("Error: SoundMapper.GetSoundValue called before Initialize().");
                return null;
            }
            if (string.IsNullOrEmpty(soundIdentifierKey))
            {
                _plugin.Logger.LogError("Error: SoundMapper.GetSoundValue called with an empty or null soundIdentifierKey.");
                return null;
            }

            // Use the dictionary directly. There's no more Lazy initialization.
            if (_soundMap.TryGetValue(soundIdentifierKey, out string? soundValue))
            {
                if (string.IsNullOrEmpty(soundValue))
                {
                    _plugin.Logger.LogWarning($"Warning: SoundMapper.GetSoundValue called with key '{soundIdentifierKey}' but no sound value is configured.");
                    return null;
                }
                return new SoundInfo(soundValue, false);
            }
            string soundFileToPlay = null!;
            List<string>? soundFileList = null!;
            switch (soundIdentifierKey)
            {
                case "Winner":
                    if (_currentConfig.UseSoundEvents)
                        soundFileToPlay = _currentConfig.WinnerSoundEvent;
                    else
                        soundFileList = _currentConfig.WinnerSound;
                    break;
                case "TeamKill":
                    if (_currentConfig.UseSoundEvents)
                        soundFileToPlay = _currentConfig.TeamKillSoundEvent;
                    else
                        soundFileList = _currentConfig.TeamKillSound;
                    break;
                case "KnifeSteal":
                    if (_currentConfig.UseSoundEvents)
                        soundFileToPlay = _currentConfig.KnifeStealSoundEvent;
                    else
                        soundFileList = _currentConfig.KnifeStealSound;
                    break;
                default:
                    _plugin.Logger.LogWarning($"Warning: SoundMapper.GetSoundValue called with unknown key '{soundIdentifierKey}'.");
                    return null; // Return null if the key is not found in the map
            }
            //            _plugin.Logger.LogInformation($"SoundMapper.GetSoundValue found sound '{soundFileToPlay}' for key '{soundIdentifierKey}'.");
            return new SoundInfo(soundFileToPlay, true, soundFileList);
        }
    }
}
// Colors Available = "{default} {white} {darkred} {green} {lightyellow}" "{lightblue} {olive} {lime} {red} {lightpurple}"
//"{purple} {grey} {yellow} {gold} {silver}" "{blue} {darkblue} {bluegrey} {magenta} {lightred}" "{orange}"
