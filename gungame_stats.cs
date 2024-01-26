using System.Data;
using System.Text;
using Microsoft.Data.Sqlite;
using Dapper;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using MySqlConnector;
using GunGame.Models;
using GunGame.Variables;
using Microsoft.Extensions.Logging;

namespace GunGame.Stats
{
    public class StatsManager
	{
        private GunGame Plugin;
        private SqliteConnection _sqliteConn = null!;
        private MySqlConnection _mysqlConn = null!;
        public DBConfig config = null!;
        public DatabaseType DatabaseType { get; set; }
        public bool SavingPlayer = false;
        public bool UnsuccessfulSave = false;
		public StatsManager(DBConfig dBConfig, GunGame plugin)
		{
            Plugin = plugin;
            config = dBConfig;
            if (config != null)
            {
                if (config.DatabaseType.Trim().ToLower() == "mysql") {
                    DatabaseType = DatabaseType.MySQL;
                } else if (config.DatabaseType.Trim().ToLower() == "sqlite") {
                    DatabaseType = DatabaseType.SQLite;
                }
                else
                {
                    Console.WriteLine("[GunGame_Stats] ******* StatsManager: Invalid database type specified in config file. Please check the config file and try again. Correct values are MySQL or SQLite");
                    Plugin.Logger.LogInformation("[GunGame_Stats] ******* StatsManager: Invalid database type specified in config file. Please check the config file and try again. Correct values are MySQL or SQLite");
                }
                InitializeDatabaseConnection();
            }
            else
            {
                Console.WriteLine($"[GunGame_Stats] ******* StatsManager: Failed to load database configuration from");
                Plugin.Logger.LogInformation($"[GunGame_Stats] ******* StatsManager: Failed to load database configuration from");
            } 
		}
        private void InitializeDatabaseConnection()
        {
            if (DatabaseType == DatabaseType.SQLite)
            {
                try
                {
                    string dbFilePath = Server.GameDirectory + config.DatabaseFilePath;
                    _sqliteConn =
                        new SqliteConnection(
                            $"Data Source={dbFilePath}");
                    _sqliteConn.Open();
                    _sqliteConn.Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] InitializeDatabaseConnection: SQLite Database connection error: {ex.Message}");
                    return;
                }
            }
            else if (DatabaseType == DatabaseType.MySQL)
            {
                if (config.DatabaseHost.Length < 1 || config.DatabaseName.Length < 1 || config.DatabaseUser.Length < 1)
                {
                    Console.WriteLine("[GunGame_Stats] InitializeDatabaseConnection: Error in DataBase config. DatabaseHost, DatabaseName and DatabaseUser should be set. Continue without GunGame statistics");
                    return;
                }
                try
                {
                    var builder = new MySqlConnector.MySqlConnectionStringBuilder
                    {
                        Server = config.DatabaseHost,
                        Database = config.DatabaseName,
                        UserID = config.DatabaseUser,
                        Password = config.DatabasePassword,
                        Port = (uint)config.DatabasePort,
                    };
                    _mysqlConn = new MySqlConnection(builder.ConnectionString);
                    _mysqlConn.Open();
                    _mysqlConn.Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] InitializeDatabaseConnection: MySQL Database connection error: {ex.Message}");
                    return;

                }
            }
            else
            {
                Console.WriteLine("[GunGame_Stats] InitializeDatabaseConnection: Invalid database specified, using SQLite.");
                try
                {
                    string dbFilePath = Server.GameDirectory + config.DatabaseFilePath;
                    _sqliteConn = new SqliteConnection($"Data Source={config.DatabaseFilePath}");
                    _sqliteConn.Open();
                    _sqliteConn.Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] InitializeDatabaseConnection: Backup SQLite Database connection error: {ex.Message}");
                    return;
                }
                DatabaseType = DatabaseType.SQLite;
            }

            try
            {
                if (DatabaseType == DatabaseType.SQLite) {
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
                else if (DatabaseType == DatabaseType.MySQL)
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
                GGVariables.Instance.StatsEnabled = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GunGame_Stats - FATAL] Unable to connect to database! Continue without GunGame statistics. {ex.Message}");
            }
        }
        public async Task SavePlayerWin(GGPlayer player)
		{
            if (player == null) return;

			string safePlayerName = System.Net.WebUtility.HtmlEncode(player.PlayerName);
            DateTime now = DateTime.Now;
            bool playerExists = false;
            string query = "SELECT `wins`, `sound` FROM `gungame_playerdata` WHERE `authid` = @authid;";
            int wins = 0;
            var playerController = Utilities.GetPlayerFromSlot(player.Slot);
            if (playerController == null || playerController.SteamID == 0)
            {
                Server.NextFrame(() => {
                    player.SavedWins(false, 1); // problem with SteamID
                });
                return;
            }
			if (DatabaseType == DatabaseType.SQLite)
            {
                try 
                {
                    await _sqliteConn.OpenAsync();
                    
                    var command = new SqliteCommand(query, _sqliteConn);
                    command.Parameters.AddWithValue("@authid", playerController.SteamID);
                    var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        wins = reader.GetInt32("wins");
                        playerExists = true;
                    }

                    if (playerExists)
                    {
                        player.SetWins(++wins);
                    }
                    else
                    {
                        player.SetWins(1);
                    }
                    
                    query = "INSERT INTO `gungame_playerdata` (`wins`, `authid`, `name`, `timestamp`) " +
                    "VALUES (@wins, @SavedSteamID, @PlayerName, CURRENT_TIMESTAMP) " +
                    "ON CONFLICT(`authid`) " +
                    "DO UPDATE SET `name` = @PlayerName, `wins` = @wins, `timestamp` = CURRENT_TIMESTAMP;";
                    
                    command = new SqliteCommand(query, _sqliteConn);
                    command.Parameters.AddWithValue("@wins", player.PlayerWins);
                    command.Parameters.AddWithValue("@SavedSteamID", player.SavedSteamID);
                    command.Parameters.AddWithValue("@PlayerName", safePlayerName);
                    await command.ExecuteNonQueryAsync();
                    Server.NextFrame(() => {
                        player.SavedWins(true, 0);
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] SavePlayerWin ******* An error occurred: {ex.Message}");
                }
                finally
                {
                    await _sqliteConn.CloseAsync();
                }
            }
			else if (DatabaseType == DatabaseType.MySQL)
            {
                try
                {
                    await _mysqlConn.OpenAsync();
                    var command = new MySqlCommand(query, _mysqlConn);
                    command.Parameters.AddWithValue("@authid", player.SavedSteamID);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            wins = reader.GetInt32("wins");
                            playerExists = true;
                        }
                    }

                    if (playerExists)
                    {
                        player.SetWins(++wins);
                    }
                    else
                    {
                        player.SetWins(1);
                    }

                    query = "INSERT INTO `gungame_playerdata` (`wins`, `authid`, `name`, `timestamp`) " +
                    "VALUES (@wins, @SavedSteamID, @PlayerName, @now) " +
                    "ON DUPLICATE KEY UPDATE `name` = @PlayerName, `wins` = @wins, `timestamp` = @now;";
                
                    using (command = new MySqlCommand(query, _mysqlConn))
                    {
                        command.Parameters.AddWithValue("@wins", player.PlayerWins);
                        command.Parameters.AddWithValue("@SavedSteamID", player.SavedSteamID);
                        command.Parameters.AddWithValue("@PlayerName", safePlayerName);
                        command.Parameters.AddWithValue("@now", now);
                        await command.ExecuteNonQueryAsync();
                        Server.NextFrame(() => {
                            player.SavedWins(true, 0);
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] SavePlayerWin ******* An error occurred: {ex.Message}");
                }
                finally
                {
                    await _mysqlConn.CloseAsync();
                }
            }
		}
        public async Task GetPlayerWins(GGPlayer player)
        {
//            Plugin.Logger.LogInformation($"[GunGame_Stats] Getting player wins for {player.PlayerName}");
            int attempts = 0;
            while (SavingPlayer && attempts < 20)
            {
                attempts++;
                await Task.Delay(200);
            }
            if (SavingPlayer)
            {
                Plugin.Logger.LogInformation($"[GunGame_Stats - FATAL] GetPlayerWins ******* Waiting too long to work");
                if (!UnsuccessfulSave)
                {
                    UnsuccessfulSave = true;
                    Server.NextFrame(() =>
                    {
                        Plugin.AddTimer(1.0f, async () => {
                            await GetPlayerWins(player);
                        });
                    });
                    return;
                }
            }
            SavingPlayer = true;
            UnsuccessfulSave = false;
            int wins = 0;
            int sound = 1;

            string query = "SELECT `wins`, `sound` FROM `gungame_playerdata` WHERE `authid` = @authid;";

            bool playerExists = false;
            if (DatabaseType == DatabaseType.SQLite)
            {
                try
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

                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    if (rowsAffected != 1)
                    {
                        Console.WriteLine($"[GunGame_Stats - FATAL] ******* GetPlayerWins: Error with Database update");
                    }
                }
                catch (Exception ex)
                {
                    SavingPlayer = false;
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogInformation($"[GunGame_Stats - FATAL]******* GetPlayerWins: An error occurred: {ex.Message}");
                    });
                    
                }
                finally
                {
                    await _sqliteConn.CloseAsync();
                }
            }
            else if (DatabaseType == DatabaseType.MySQL)
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
                    SavingPlayer = false;
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogInformation($"[GunGame_Stats - FATAL]******* GetPlayerWins: An error occurred: {ex.Message}");
                    });
                }
                finally
                {
                    await _mysqlConn.CloseAsync();
                }
            }
            SavingPlayer = false;
            Server.NextFrame(() =>
            {
                player.PlayerWins = wins;
                player.Music = sound == 1;
//                Plugin.Logger.LogInformation($"[GunGame_Stats]******* GetPlayerWins: {player.PlayerName} wins {player.PlayerWins}, sound {sound}");
            });
        }
        public async Task<int> GetPlayerRank(string authid)
        {
            int rank = 0;

            if (DatabaseType == DatabaseType.SQLite)
            {
                string query = @"
                SELECT rank FROM (
                    SELECT 
                        authid, 
                        RANK() OVER (ORDER BY wins DESC) as rank 
                    FROM `gungame_playerdata`
                ) as ranked_players
                WHERE authid = @authid;";
                try
                {
                    await _sqliteConn.OpenAsync();
                    var command = new SqliteCommand(query, _sqliteConn);
                    command.Parameters.AddWithValue("@authid", authid);
                
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
                finally
                {
                    await _sqliteConn.CloseAsync();
                }
            }
            else if (DatabaseType == DatabaseType.MySQL)
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

            if (DatabaseType == DatabaseType.SQLite)
            {
                try
                {
                    await _sqliteConn.OpenAsync();
                    var command = new SqliteCommand(query, _sqliteConn);
                
                    var result = await command.ExecuteScalarAsync();
                    if (result != null)
                    {
                        count = Convert.ToInt32(result);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] GetNumberOfWinners ******* An error occurred: {ex.Message}");
                }
                finally
                {
                    await _sqliteConn.CloseAsync();
                }
            }
            else if (DatabaseType == DatabaseType.MySQL)
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
                    Console.WriteLine($"[GunGame_Stats - FATAL] GetNumberOfWinners: ******** An error occurred: {ex.Message}");
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
                LIMIT 1 OFFSET @HandicapTopRank;";

            if (DatabaseType == DatabaseType.SQLite)
            {
                try
                {
                    await _sqliteConn.OpenAsync();
                    var command = new SqliteCommand(query, _sqliteConn);
                    command.Parameters.AddWithValue("@HandicapTopRank", HandicapTopRank-1);
                
                    var result = await command.ExecuteScalarAsync();
                    if (result != null)
                    {
                        wins = Convert.ToInt32(result);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] GetWinsOfLowestTopPlayer ******* An error occurred: {ex.Message}");
                }
                finally
                {
                    await _sqliteConn.CloseAsync();
                }
            }
            else if (DatabaseType == DatabaseType.MySQL)
            {
                try
                {
                    await _mysqlConn.OpenAsync();

                    using (var command = new MySqlCommand(query, _mysqlConn))
                    {
                        command.Parameters.AddWithValue("@HandicapTopRank", HandicapTopRank-1);

                        var result = await command.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            wins = Convert.ToInt32(result);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] GetWinsOfLowestTopPlayer ******* An error occurred: {ex.Message}");
                }
                finally
                {
                    await _mysqlConn.CloseAsync();
                }
            }
            return wins;
        }
        public async Task<Dictionary<string, int>> GetTopPlayers(int HandicapTopRank = 19)
        {
            var topPlayers = new Dictionary<string, int>();
            string query = @"
                SELECT `name`, `wins` FROM `gungame_playerdata`
                ORDER BY `wins` DESC
                LIMIT @HandicapTopRank;";

            if (DatabaseType == DatabaseType.SQLite)
            {
                try
                {
                    await _sqliteConn.OpenAsync();
                    var command = new SqliteCommand(query, _sqliteConn);
                    command.Parameters.AddWithValue("@HandicapTopRank", HandicapTopRank);
                
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
                    Console.WriteLine($"[GunGame_Stats - FATAL] GetTopPlayers ******* An error occurred: {ex.Message}");
                }
                finally
                {
                    await _sqliteConn.CloseAsync();
                }
            }
            else if (DatabaseType == DatabaseType.MySQL)
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
                    Console.WriteLine($"[GunGame_Stats - FATAL] GetTopPlayers ******* An error occurred: {ex.Message}");
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

            if (DatabaseType == DatabaseType.SQLite)
            {
                try
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
                
                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    if (rowsAffected != 1)
                    {
                        Console.WriteLine($"[GunGame_Stats - FATAL] ToggleSound ********* Error with Database update");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] ToggleSound ******* An error occurred: {ex.Message}");
                }
                finally
                {
                    await _sqliteConn.CloseAsync();
                }
            }
            else if (DatabaseType == DatabaseType.MySQL)
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
                    Console.WriteLine($"[GunGame_Stats - FATAL] ToggleSound ******* An error occurred: {ex.Message}");
                }
                finally
                {
                    await _mysqlConn.CloseAsync();
                }
            }

            if (sound >= 0)
            {
                Server.NextFrame(() =>
                {
                    player.SetSound(sound == 0);
                });
            }
        }
        public async Task ResetStats()
        {
            string updateQuery = "UPDATE `gungame_playerdata` SET `wins` = 0;";

            if (DatabaseType == DatabaseType.SQLite)
            {
                try
                {
                    await _sqliteConn.OpenAsync();
                    var command = new SqliteCommand(updateQuery, _sqliteConn);
                
                    await command.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] ResetStats ******* An error occurred: {ex.Message}");
                }
                finally
                {
                    await _sqliteConn.CloseAsync();
                }
            }
            else if (DatabaseType == DatabaseType.MySQL)
            {
                await _mysqlConn.OpenAsync();
                var command = new MySqlCommand(updateQuery, _mysqlConn);
                try
                {
                    await command.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] ResetStats ******* An error occurred: {ex.Message}");
                }
                finally
                {
                    await _mysqlConn.CloseAsync();
                }
            }
        }
    }
    public enum DatabaseType
    {
        SQLite,
        MySQL
    }
}
