using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Dapper;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using MySqlConnector;
using GunGame;
using GunGame.Variables;
using Microsoft.Extensions.Logging;

namespace GunGame.Stats
{
    public class StatsManager : BasePlugin
	{
        public override string ModuleName => "GunGame_Stats";
        public override string ModuleVersion => "v1.0.3";
        private SqliteConnection _sqliteConn = null!;
        private MySqlConnection _mysqlConn = null!;
        public DBConfig config = null!;
        public DatabaseType databaseType { get; set; }
        public bool StatsEnable = false;
		public StatsManager()
		{
            string configFile = Server.GameDirectory + "/csgo/cfg/gungame-db.json";
            config = LoadDBConfig(configFile);
            if (config != null)
            {
                if (config.DatabaseType.Trim().ToLower() == "mysql") {
                    databaseType = DatabaseType.MySQL;
                } else if (config.DatabaseType.Trim().ToLower() == "sqlite") {
                    databaseType = DatabaseType.SQLite;
                }
                else
                {
                    Console.WriteLine("[GunGame_Stats] ******* StatsManager: Invalid database type specified in config file. Please check the config file and try again. Correct values are MySQL or SQLite");
                }
                InitializeDatabaseConnection();
            }
            else
            {
                Console.WriteLine($"[GunGame_Stats] ******* StatsManager: Failed to load database configuration from {configFile}");
            } 
		}
        public DBConfig LoadDBConfig(string configFile)
        {
            if (!File.Exists(configFile))
            {
                CreateDefaultConfigFile(configFile);
            }
            try
            {
                string jsonString = File.ReadAllText(configFile);
                if (string.IsNullOrEmpty(jsonString))
                {
                    Console.WriteLine("[GunGame_Stats] ****** LoadDBConfig: Error loading DataBase config. csgo/cfg/gungame-db.json is wrong or empty. Continue without GunGame statistics");
                    return null!;
                }
                return System.Text.Json.JsonSerializer.Deserialize<DBConfig>(jsonString)!;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GunGame_Stats] ******* LoadDBConfig: Error reading or deserializing gungame-db.json file: {ex.Message}. Continue without GunGame statistics");
                return null!;
            }
        }
        private void CreateDefaultConfigFile(string configFile)
        {
            DBConfig defaultConfig = new DBConfig
            {
                DatabaseType = "SQLite",
                DatabaseFilePath = "/csgo/cfg/gungame-db.sqlite",
                DatabaseHost = "your_mysql_host",
                DatabaseName = "your_mysql_database",
                DatabaseUser = "your_mysql_username",
                DatabasePassword = "your_mysql_password",
                DatabasePort = 3306,
                Comment = "use SQLite or MySQL as Database Type"
            };

            string defaultConfigJson = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFile, defaultConfigJson);
        }
        private void InitializeDatabaseConnection()
        {
            try
            {
                if (databaseType == DatabaseType.SQLite)
                {
                    string dbFilePath = Server.GameDirectory + config.DatabaseFilePath;
                    _sqliteConn =
                        new SqliteConnection(
                            $"Data Source={dbFilePath}");
                    if (_sqliteConn == null)
                    {
                        Console.WriteLine("[GunGame_Stats]******* InitializeDatabaseConnection: Error _sqliteConn = null");
                        return;
                    }
                }
                else if (databaseType == DatabaseType.MySQL)
                {
                    if (config.DatabaseHost.Length < 1 || config.DatabaseName.Length < 1 || config.DatabaseUser.Length < 1)
                    {
                        Console.WriteLine("[GunGame_Stats] InitializeDatabaseConnection: Error in DataBase config. DatabaseHost, DatabaseName and DatabaseUser should be set. Continue without GunGame statistics");
                        return;
                    }
                    var builder = new MySqlConnector.MySqlConnectionStringBuilder
                    {
                        Server = config.DatabaseHost,
                        Database = config.DatabaseName,
                        UserID = config.DatabaseUser,
                        Password = config.DatabasePassword,
                        Port = (uint)config.DatabasePort,
                    };
                    _mysqlConn = new MySqlConnection(builder.ConnectionString);
                    if (_mysqlConn == null)
                    {
                        Console.WriteLine("[GunGame_Stats]******* InitializeDatabaseConnection: Error _mysqlConn = null");
                        return;
                    }
                }
                else
                {
                    Console.WriteLine("[GunGame_Stats] InitializeDatabaseConnection: Invalid database specified, using SQLite.");
                    string dbFilePath = Server.GameDirectory + config.DatabaseFilePath;
                    _sqliteConn =
                        new SqliteConnection(
                            $"Data Source={config.DatabaseFilePath}");
                    databaseType = DatabaseType.SQLite;
                }
            } 
            catch (Exception ex)
            {
                Console.WriteLine($"[GunGame_Stats - FATAL] InitializeDatabaseConnection: Database connection error: {ex.Message}");
            }

            try
            {
                if (databaseType == DatabaseType.SQLite) {
                    _sqliteConn.Execute(@"CREATE TABLE IF NOT EXISTS gungame_playerdata (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        wins INTEGER NOT NULL DEFAULT 0,
                        authid TEXT NOT NULL DEFAULT '0',
                        name TEXT NOT NULL DEFAULT '0',
                        timestamp TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        sound INTEGER NOT NULL DEFAULT 1,
                        UNIQUE (authid)
                    );");
                    _sqliteConn.Close();
                }
                else if (databaseType == DatabaseType.MySQL)
                {
                    _mysqlConn.Execute(@"CREATE TABLE IF NOT EXISTS `gungame_playerdata` (
                        `id` INT NOT NULL AUTO_INCREMENT,
                        `wins` INT NOT NULL DEFAULT '0',
                        `authid` VARCHAR(64) NOT NULL DEFAULT '0',
                        `name` VARCHAR(128) NOT NULL DEFAULT '0',
                        `timestamp` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        `sound` INT NOT NULL DEFAULT '1',
                        PRIMARY KEY (`id`),
                        UNIQUE KEY `authid_unique` (`authid`)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;");
                    _mysqlConn.Close();
                }
                StatsEnable = true;
                GGVariables.Instance.StatsEnabled = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GunGame_Stats - FATAL] Unable to connect to database! Continue without GunGame statistics. {ex.Message}");
            }
        }
        public async Task SavePlayerData(GGPlayer player)
		{
            if (player == null) return;

			string safePlayerName = System.Net.WebUtility.HtmlEncode(player.PlayerName);
            DateTime now = DateTime.Now;

			if (databaseType == DatabaseType.SQLite)
            {
                var sql = "INSERT INTO `gungame_playerdata` (`wins`, `authid`, `name`, `timestamp`) " +
                "VALUES (@wins, @SavedSteamID, @PlayerName, CURRENT_TIMESTAMP) " +
                "ON CONFLICT(`authid`) " +
                "DO UPDATE SET `name` = @PlayerName, `wins` = @wins, `timestamp` = CURRENT_TIMESTAMP;";
                await _sqliteConn.OpenAsync();
                var command = new SqliteCommand(sql, _sqliteConn);
                command.Parameters.AddWithValue("@wins", player.PlayerWins);
                command.Parameters.AddWithValue("@SavedSteamID", player.SavedSteamID);
                command.Parameters.AddWithValue("@PlayerName", safePlayerName);
                try
                {
                    await command.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL]******* An error occurred: {ex.Message}");
                }
                await _sqliteConn.CloseAsync();
            }
			else if (databaseType == DatabaseType.MySQL)
            {
                var sql = "INSERT INTO `gungame_playerdata` (`wins`, `authid`, `name`, `timestamp`) " +
                "VALUES (@wins, @SavedSteamID, @PlayerName, @now) " +
                "ON DUPLICATE KEY UPDATE `name` = @PlayerName, `wins` = @wins, `timestamp` = @now;";
                try
                {
                    await _mysqlConn.OpenAsync();
                    using (var command = new MySqlCommand(sql, _mysqlConn))
                    {
                        command.Parameters.AddWithValue("@wins", player.PlayerWins);
                        command.Parameters.AddWithValue("@SavedSteamID", player.SavedSteamID);
                        command.Parameters.AddWithValue("@PlayerName", safePlayerName);
                        command.Parameters.AddWithValue("@now", now);

                        await command.ExecuteNonQueryAsync();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL]******* An error occurred: {ex.Message}");
                }
                finally
                {
                    await _mysqlConn.CloseAsync();
                }
            }
		}
        public async Task GetPlayerWins(GGPlayer player)
        {
            int wins = 0;
            int sound = 1;

            string query = "SELECT `wins`, `sound` FROM `gungame_playerdata` WHERE `authid` = @authid;";

            bool playerExists = false;
            if (databaseType == DatabaseType.SQLite)
            {
                await _sqliteConn.OpenAsync();
                var command = new SqliteCommand(query, _sqliteConn);
                command.Parameters.AddWithValue("@authid", player.SavedSteamID);
                var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    wins = reader.GetInt32("wins");
                    sound = reader.GetInt32("sound");
                    playerExists = true;
                }
                string insertQuery = "INSERT INTO `gungame_playerdata` (`authid`, `name`, `timestamp`) VALUES (@authid, @name, CURRENT_TIMESTAMP);";
                string updateQuery = "UPDATE `gungame_playerdata` SET `name` = @name, `timestamp` = CURRENT_TIMESTAMP WHERE `authid` = @authid;";
                string sql = playerExists ? updateQuery : insertQuery;
                command = new SqliteCommand(sql, _sqliteConn);
                command.Parameters.AddWithValue("@authid", player.SavedSteamID);
                command.Parameters.AddWithValue("@name", player.PlayerName);
                try
                {
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    if (rowsAffected != 1)
                    {
                        Console.WriteLine($"[GunGame_Stats - FATAL] ******* GetPlayerWins: Error with Database update");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL]******* GetPlayerWins: An error occurred: {ex.Message}");
                }
                await _sqliteConn.CloseAsync();
            }
            else if (databaseType == DatabaseType.MySQL)
            {
                try
                {
                    await _mysqlConn.OpenAsync();

                    using (var command = new MySqlCommand(query, _mysqlConn))
                    {
                        command.Parameters.AddWithValue("@authid", player.SavedSteamID);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                wins = reader.GetInt32("wins");
                                sound = reader.GetInt32("sound");
                                playerExists = true;
                            }
                        }
                    }

                    string sql = playerExists
                        ? "UPDATE `gungame_playerdata` SET `name` = @name, `timestamp` = NOW() WHERE `authid` = @authid"
                        : "INSERT INTO `gungame_playerdata` (`authid`, `name`, `timestamp`) VALUES (@authid, @name, NOW())";

                    using (var command = new MySqlCommand(sql, _mysqlConn))
                    {
                        command.Parameters.AddWithValue("@authid", player.SavedSteamID);
                        command.Parameters.AddWithValue("@name", player.PlayerName);
                        int rowsAffected = await command.ExecuteNonQueryAsync();
                        if (rowsAffected != 1)
                        {
                            Console.WriteLine($"[GunGame_Stats - FATAL] ****** GetPlayerWins: Error with Database update");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL]******* GetPlayerWins: An error occurred: {ex.Message}");
                }
                finally
                {
                    await _mysqlConn.CloseAsync();
                }
            }
            player.PlayerWins = wins;
            player.Music = sound == 1;            
        }
        public async Task<int> GetPlayerRank(string authid)
        {
            int rank = 0;

            if (databaseType == DatabaseType.SQLite)
            {
                string query = @"
                SELECT rank FROM (
                    SELECT 
                        authid, 
                        RANK() OVER (ORDER BY wins DESC) as rank 
                    FROM `gungame_playerdata`
                ) as ranked_players
                WHERE authid = @authid;";
                await _sqliteConn.OpenAsync();
                var command = new SqliteCommand(query, _sqliteConn);
                command.Parameters.AddWithValue("@authid", authid);
                try
                {
                    var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        rank = reader.GetInt32("rank");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL]******* GetPlayerRank: An error occurred: {ex.Message}");
                } 
                await _sqliteConn.CloseAsync();
            }
            else if (databaseType == DatabaseType.MySQL)
            {
                string query = @"SELECT 
                    (SELECT COUNT(*) FROM `gungame_playerdata` as g2 WHERE g2.wins > g1.wins) + 1 as rank
                    FROM `gungame_playerdata` as g1
                    WHERE authid = @authid;";
                try
                {
                    await _mysqlConn.OpenAsync();

                    using (var command = new MySqlCommand(query, _mysqlConn))
                    {
                        command.Parameters.AddWithValue("@authid", authid);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                rank = reader.GetInt32("rank");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL]******* GetPlayerRank: An error occurred: {ex.Message}");
                }
                finally
                {
                    await _mysqlConn.CloseAsync();
                }
            }
            return rank;
        }
        public async Task<int> GetNumberOfWinners()
        {
            int count = 0;
            string query = "SELECT COUNT(*) FROM `gungame_playerdata` WHERE `wins` > 0;";

            if (databaseType == DatabaseType.SQLite)
            {
                await _sqliteConn.OpenAsync();
                var command = new SqliteCommand(query, _sqliteConn);
                try
                {
                    var result = await command.ExecuteScalarAsync();
                    if (result != null)
                    {
                        count = Convert.ToInt32(result);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL]******* An error occurred: {ex.Message}");
                } 
                await _sqliteConn.CloseAsync();
            }
            else if (databaseType == DatabaseType.MySQL)
            {
                try
                {
                    await _mysqlConn.OpenAsync();

                    using (var command = new MySqlCommand(query, _mysqlConn))
                    {
                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            count = Convert.ToInt32(result);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An error occurred: {ex.Message}");
                }
                finally
                {
                    await _mysqlConn.CloseAsync();
                }
            }
            return count;
        }
        public async Task<int> GetWinsOfLowestTopPlayer(int HandicapTopRank)
        {
            int wins = 0;
            string query = @"
                SELECT `wins` FROM `gungame_playerdata`
                ORDER BY `wins` DESC
                LIMIT 1 OFFSET @HandicapTopRank - 1;";

            if (databaseType == DatabaseType.SQLite)
            {
                await _sqliteConn.OpenAsync();
                var command = new SqliteCommand(query, _sqliteConn);
                command.Parameters.AddWithValue("@HandicapTopRank", HandicapTopRank);
                try
                {
                    var result = await command.ExecuteScalarAsync();
                    if (result != null)
                    {
                        wins = Convert.ToInt32(result);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL]******* An error occurred: {ex.Message}");
                }
                await _sqliteConn.CloseAsync();
            }
            else if (databaseType == DatabaseType.MySQL)
            {
                try
                {
                    await _mysqlConn.OpenAsync();

                    using (var command = new MySqlCommand(query, _mysqlConn))
                    {
                        command.Parameters.AddWithValue("@HandicapTopRank", HandicapTopRank);

                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            wins = Convert.ToInt32(result);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An error occurred: {ex.Message}");
                }
                finally
                {
                    await _mysqlConn.CloseAsync();
                }
            }
            return wins;
        }
        public async Task<Dictionary<string, int>> GetTopPlayers(int HandicapTopRank = 21)
        {
            var topPlayers = new Dictionary<string, int>();
            string query = @"
                SELECT `name`, `wins` FROM `gungame_playerdata`
                ORDER BY `wins` DESC
                LIMIT @HandicapTopRank;";

            if (databaseType == DatabaseType.SQLite)
            {
                await _sqliteConn.OpenAsync();
                var command = new SqliteCommand(query, _sqliteConn);
                command.Parameters.AddWithValue("@HandicapTopRank", HandicapTopRank);
                try
                {
                    var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        string name = reader.GetString("name");
                        int wins = reader.GetInt32("wins");
                        Console.WriteLine($"{name} {wins}");
                        if (wins != 0)
                            topPlayers[name] = wins;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL]******* An error occurred: {ex.Message}");
                }
                await _sqliteConn.CloseAsync();
            }
            else if (databaseType == DatabaseType.MySQL)
            {
                try
                {
                    await _mysqlConn.OpenAsync();

                    using (var command = new MySqlCommand(query, _mysqlConn))
                    {
                        command.Parameters.AddWithValue("@HandicapTopRank", HandicapTopRank);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                string name = reader.GetString(reader.GetOrdinal("name"));
                                int wins = reader.GetInt32(reader.GetOrdinal("wins"));
                                if (wins != 0)
                                {
                                    topPlayers[name] = wins;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An error occurred: {ex.Message}");
                }
                finally
                {
                    await _mysqlConn.CloseAsync();
                }
            }
            return topPlayers;
        }
        public async Task ToggleSound(GGPlayer player)
        {
            int sound = -1;

            string query = "SELECT `sound` FROM `gungame_playerdata` WHERE `authid` = @authid;";
            string updateQuery = "UPDATE `gungame_playerdata` SET `sound` = @sound WHERE `authid` = @authid;";

            if (databaseType == DatabaseType.SQLite)
            {
                await _sqliteConn.OpenAsync();
                var command = new SqliteCommand(query, _sqliteConn);
                command.Parameters.AddWithValue("@authid", player.SavedSteamID);
                var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    sound = reader.GetInt32("sound");
                }
                
                command = new SqliteCommand(updateQuery, _sqliteConn);
                command.Parameters.AddWithValue("@authid", player.SavedSteamID);
                command.Parameters.AddWithValue("@sound", sound == 0 ? 1 : 0);
                try
                {
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    if (rowsAffected != 1)
                    {
                        Console.WriteLine($"[GunGame_Stats - FATAL] Error with Database update");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL]******* An error occurred: {ex.Message}");
                }
                await _sqliteConn.CloseAsync();
            }
            else if (databaseType == DatabaseType.MySQL)
            {
                try
                {
                    await _mysqlConn.OpenAsync();
                    using (var command = new MySqlCommand(query, _mysqlConn))
                    {
                        command.Parameters.AddWithValue("@authid", player.SavedSteamID);

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                sound = reader.GetInt32(reader.GetOrdinal("sound"));
                            }
                        }
                    }
                    using (var command = new MySqlCommand(updateQuery, _mysqlConn))
                    {
                        command.Parameters.AddWithValue("@authid", player.SavedSteamID);
                        command.Parameters.AddWithValue("@sound", sound == 0 ? 1 : 0);
                        await command.ExecuteNonQueryAsync();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An error occurred: {ex.Message}");
                }
                finally
                {
                    await _mysqlConn.CloseAsync();
                }
            }
        }
        public async Task ResetStats()
        {

            string updateQuery = "UPDATE `gungame_playerdata` SET `wins` = 0;";

            if (databaseType == DatabaseType.SQLite)
            {
                await _sqliteConn.OpenAsync();
                var command = new SqliteCommand(updateQuery, _sqliteConn);
                try
                {
                    await command.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL]******* ResetStats - an error occurred: {ex.Message}");
                }
                await _sqliteConn.CloseAsync();
            }
            else if (databaseType == DatabaseType.MySQL)
            {
                await _mysqlConn.OpenAsync();
                var command = new MySqlCommand(updateQuery, _mysqlConn);
                try
                {
                    await command.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL]******* ResetStats - an error occurred: {ex.Message}");
                }
                await _mysqlConn.CloseAsync();
            }
        }
    }
    public enum DatabaseType
    {
        SQLite,
        MySQL
    }
    public class DBConfig
    {
        [JsonPropertyName("DatabaseType")]
        public string DatabaseType { get; set; } = "";
        [JsonPropertyName("DatabaseFilePath")]
        public string DatabaseFilePath { get; set; } = "";
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
}
