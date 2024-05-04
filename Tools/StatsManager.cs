using CounterStrikeSharp.API;
using Dapper;
using GunGame.Models;
using GunGame.Stats;
using GunGame.Variables;
using MaxMind.GeoIP2;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using System.Data;
using System.Globalization;
using System.Text;

namespace GunGame.Tools
{
    public class StatsManager : CustomManager
    {
        private bool _isDatabaseReady = false;
        private int checkTries = 10;
        private string _dbFilePath = "";

        private readonly DBConfig dbConfig = null!;
        private readonly MySqlConnectionStringBuilder _builder = null!;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public DatabaseType DatabaseType { get; set; }
        public bool SavingPlayer = false;
        public bool UnsuccessfulSave = false;

        public StatsManager(DBConfig dbConfig, GunGame plugin) : base(plugin)
        {
            this.dbConfig = dbConfig;
            if (this.dbConfig != null)
            {
                if (this.dbConfig.DatabaseType.Trim().ToLower() == "mysql")
                {
                    DatabaseType = DatabaseType.MySQL;
                    if (this.dbConfig.DatabaseHost.Length < 1 || this.dbConfig.DatabaseName.Length < 1 || this.dbConfig.DatabaseUser.Length < 1)
                    {
                        Console.WriteLine("[GunGame_Stats] InitializeDatabaseConnection: Error in DataBase config. DatabaseHost, DatabaseName and DatabaseUser should be set. Continue without GunGame statistics");
                        Plugin.Logger.LogInformation("[GunGame_Stats] InitializeDatabaseConnection: Error in DataBase config. DatabaseHost, DatabaseName and DatabaseUser should be set. Continue without GunGame statistics");
                        return;
                    }
                    _builder = new MySqlConnectionStringBuilder
                    {
                        Server = this.dbConfig.DatabaseHost,
                        Database = this.dbConfig.DatabaseName,
                        UserID = this.dbConfig.DatabaseUser,
                        Password = this.dbConfig.DatabasePassword,
                        Port = (uint)this.dbConfig.DatabasePort,
                    };
                }
                else if (this.dbConfig.DatabaseType.Trim().ToLower() == "sqlite")
                {
                    DatabaseType = DatabaseType.SQLite;
                    _dbFilePath = Server.GameDirectory + this.dbConfig.DatabaseFilePath;
                }
                else
                {
                    Console.WriteLine("[GunGame_Stats] ******* StatsManager: Invalid database type specified in config file. Please check the config file and try again. Correct values are MySQL or SQLite");
                    Plugin.Logger.LogInformation("[GunGame_Stats] ******* StatsManager: Invalid database type specified in config file. Please check the config file and try again. Correct values are MySQL or SQLite");
                }
                _ = InitializeDatabaseConnection();
            }
            else
            {
                Console.WriteLine($"[GunGame_Stats] ******* StatsManager: Failed to load database configuration");
                Plugin.Logger.LogInformation($"[GunGame_Stats] ******* StatsManager: Failed to load database configuration");
            }
        }
        private async Task InitializeDatabaseConnection()
        {
            if (DatabaseType == DatabaseType.MySQL)
            {
                try
                {
                    await CheckMySQLConnectionAsync();
                    if (!_isDatabaseReady)
                    {
                        if (!_cts.IsCancellationRequested && --checkTries > 0)
                        {
                            Server.NextFrame(() =>
                            {
                                Plugin.Logger.LogInformation($"[GunGame_Stats] ERROR: ********* Database is not ready, try again in 5 seconds");
                            });
                            // Schedule the next check, using a method compatible with your environment to delay execution
                            await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token); // Using Task.Delay as an example
                            _ = InitializeDatabaseConnection();
                            return;
                        }
                        else
                        {
                            Server.NextFrame(() =>
                            {
                                Plugin.Logger.LogInformation($"[GunGame_Stats] ERROR: ********* Database can't be initialised. Continue without stats");
                            });
                            return;
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("Connection attempts canceled.");
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogInformation($"[GunGame_Stats] ERROR: ********* Connection attempts canceled.");
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An error occurred during database initialization: {ex.Message}");
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogInformation($"[GunGame_Stats] ERROR: ********* An error occurred during database initialization: {ex.Message}");
                    });
                    // Optionally, reschedule the connection attempt or handle the error differently
                }
                try
                {
                    using (var _mysqlConn = new MySqlConnection(_builder.ConnectionString))
                    {
                        _mysqlConn.Execute(@"CREATE TABLE IF NOT EXISTS `gungame_playerdata` (
                            `id` INT NOT NULL AUTO_INCREMENT,
                            `wins` INT NOT NULL DEFAULT '0',
                            `authid` VARCHAR(64) NOT NULL DEFAULT '0',
                            `name` VARCHAR(128) NOT NULL DEFAULT '0',
                            `timestamp` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            `sound` INT NOT NULL DEFAULT '1',
                            `countrycode` TEXT,
                            PRIMARY KEY (`id`),
                            UNIQUE KEY `authid_unique` (`authid`)
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;");
                        _mysqlConn.Close();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to connect to the database and check if table exists: {ex.Message}");
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogInformation($"[GunGame_Stats] ERROR: ********* Failed to connect to the database and check if table exists: {ex.Message}, continue without stats.");
                    });
                    return;
                }
                Server.NextFrame(() =>
                {
                    Plugin.Logger.LogInformation($"[GunGame_Stats] Initialised MySQL Database connection for stats");
                });
            }
            else
            {
                await using (var _sqliteConn = new SqliteConnection($"Data Source={_dbFilePath}"))
                {
                    try
                    {
                        await _sqliteConn.OpenAsync();

                        var createTableCmd = @"CREATE TABLE IF NOT EXISTS gungame_playerdata (
                                                id INTEGER PRIMARY KEY AUTOINCREMENT,
                                                wins INTEGER NOT NULL DEFAULT 0,
                                                authid TEXT NOT NULL DEFAULT '0',
                                                name TEXT NOT NULL DEFAULT '0',
                                                timestamp TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                                                sound INTEGER NOT NULL DEFAULT 1,
                                                countrycode TEXT,
                                                UNIQUE (authid)
                                            );";
                        await using (var command = new SqliteCommand(createTableCmd, _sqliteConn))
                        {
                            await command.ExecuteNonQueryAsync();
                        }

                        _isDatabaseReady = true; // Indicate that the database is ready for use
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GunGame_Stats - FATAL] InitializeDatabaseConnection: SQLite Database connection error: {ex.Message}");
                        Server.NextFrame(() =>
                        {
                            Plugin.Logger.LogError($"[GunGame_Stats - FATAL] InitializeDatabaseConnection: SQLite Database connection error: {ex.Message}");
                        });
                        _isDatabaseReady = false;
                        return;
                    }
                }
                DatabaseType = DatabaseType.SQLite;
                Server.NextFrame(() =>
                {
                    Plugin.Logger.LogInformation($"[GunGame_Stats] ***** InitializeDatabaseConnection: SQLite - success");
                });
            }
            GGVariables.Instance.StatsEnabled = true;
        }
        public async Task<bool> CheckMySQLConnectionAsync()
        {
            try
            {
                using (var conn = new MySqlConnection(_builder.ConnectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new MySqlCommand("SELECT 1;", conn))
                    {
                        await cmd.ExecuteScalarAsync(); // Lightweight query
                    }
                }
                _isDatabaseReady = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect to the database: {ex.Message}");
                _isDatabaseReady = false;
            }

            return _isDatabaseReady;
        }
        public async Task SavePlayerWin(GGPlayer player)
        {
            if (!_isDatabaseReady)
            {
                Console.WriteLine("************** Database is not ready yet. Can't save Player Wins");
                return;
            }
            if (!_isDatabaseReady || player == null) return;
            string safePlayerName = System.Net.WebUtility.HtmlEncode(player.PlayerName);
            bool playerExists = false;
            string query = "SELECT `wins` FROM `gungame_playerdata` WHERE `authid` = @authid;";
            int wins = 0;
            var playerController = Utilities.GetPlayerFromSlot(player.Slot);
            if (playerController == null || !playerController.IsValid || playerController.SteamID == 0)
            {
                Server.NextFrame(() =>
                {
                    if (player != null)
                    {
                        player.SavedWins(false, 1); // problem with SteamID
                        Plugin.Logger.LogError($"[GunGame_Stats] SavePlayerWin: Problem with SteamID for player slot {player.Slot}");
                    }
                });
                return;
            }
            if (DatabaseType == DatabaseType.SQLite)
            {
                string connectionString = $"Data Source={_dbFilePath}";
                await using (var _sqliteConn = new SqliteConnection(connectionString))
                {
                    try
                    {
                        await _sqliteConn.OpenAsync();
                        await using (var checkCommand = new SqliteCommand(query, _sqliteConn))
                        {
                            checkCommand.Parameters.AddWithValue("@authid", playerController.SteamID);
                            using (var reader = await checkCommand.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    wins = reader.GetInt32(0);
                                    playerExists = true;
                                }
                            }
                        }

                        // Update wins
                        if (playerExists)
                        {
                            wins++;
                        }
                        else
                        {
                            wins = 1; // Set to 1 if the player does not exist
                        }
                        /*                        Server.NextFrame(() => {
                                                    if (player != null)
                                                        player.SetWins(wins);
                                                }); */

                        // Insert or update player data
                        string upsertQuery = @"
                            INSERT INTO gungame_playerdata (wins, authid, name, timestamp) 
                            VALUES (@wins, @SavedSteamID, @PlayerName, CURRENT_TIMESTAMP) 
                            ON CONFLICT(authid) 
                            DO UPDATE SET name = @PlayerName, wins = @wins, timestamp = CURRENT_TIMESTAMP;";

                        await using (var upsertCommand = new SqliteCommand(upsertQuery, _sqliteConn))
                        {
                            upsertCommand.Parameters.AddWithValue("@wins", wins);
                            upsertCommand.Parameters.AddWithValue("@SavedSteamID", player.SavedSteamID);
                            upsertCommand.Parameters.AddWithValue("@PlayerName", safePlayerName);
                            await upsertCommand.ExecuteNonQueryAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GunGame_Stats - FATAL] SavePlayerWin ******* An error occurred: {ex.Message}");
                        Server.NextFrame(() =>
                        {
                            Plugin.Logger.LogError($"[GunGame_Stats - FATAL] SavePlayerWin ******* An error occurred: {ex.Message}");
                        });
                    }
                }
            }
            else if (DatabaseType == DatabaseType.MySQL)
            {
                DateTime now = DateTime.Now;
                using (var _mysqlConn = new MySqlConnection(_builder.ConnectionString))
                {
                    try
                    {
                        await _mysqlConn.OpenAsync();
                        using (var command = new MySqlCommand(query, _mysqlConn))
                        {
                            command.Parameters.AddWithValue("@authid", player.SavedSteamID);
                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync() && !reader.IsDBNull(reader.GetOrdinal("wins")))
                                {
                                    wins = reader.GetInt32("wins") + 1;
                                }
                                else
                                {
                                    wins = 1; // Set wins to 1 if not found
                                }
                            }
                        }

                        // Update or insert the player data
                        string upsertQuery = @"
                            INSERT INTO `gungame_playerdata` (`wins`, `authid`, `name`, `timestamp`) 
                            VALUES (@wins, @SavedSteamID, @PlayerName, @now)
                            ON DUPLICATE KEY UPDATE `name` = @PlayerName, `wins` = @wins, `timestamp` = @now;";

                        using (var upsertCommand = new MySqlCommand(upsertQuery, _mysqlConn))
                        {
                            upsertCommand.Parameters.AddWithValue("@wins", wins);
                            upsertCommand.Parameters.AddWithValue("@SavedSteamID", player.SavedSteamID);
                            upsertCommand.Parameters.AddWithValue("@PlayerName", safePlayerName);
                            upsertCommand.Parameters.AddWithValue("@now", now);
                            await upsertCommand.ExecuteNonQueryAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GunGame_Stats - FATAL] SavePlayerWin ******* An error occurred: {ex.Message}");
                        Server.NextFrame(() =>
                        {
                            Plugin.Logger.LogError($"[GunGame_Stats - FATAL] SavePlayerWin ******* An error occurred: {ex.Message}");
                        });
                    }
                }

            }
            if (wins != 0)
            {
                Console.WriteLine($"[GunGame_Stats] Saved player wins for {player.PlayerName} - {wins}");
                Server.NextFrame(() =>
                {
                    if (player != null)
                    {
                        player.SetWins(wins);
                        player.SavedWins(true, 0);
                        Plugin.Logger.LogInformation($"[GunGame_Stats] Saved player wins for {player.PlayerName} - {wins}");
                    }
                });
            }
            else
            {
                Console.WriteLine($"[GunGame_Stats] ***** Can't saved player wins for {player.PlayerName} - {player.PlayerWins}");
                Server.NextFrame(() =>
                {
                    if (player != null)
                    {
                        Plugin.Logger.LogInformation($"[GunGame_Stats] Error saving player wins for {player.PlayerName}");
                        player.SavedWins(false, 0);
                    }
                });
            }
        }
        // Add check in main plugin if the function work
        public async Task GetPlayerWins(GGPlayer player)
        {
            if (!_isDatabaseReady)
            {
                Console.WriteLine("************** Database is not ready yet. Can't request Player Wins");
                Plugin.Logger.LogError("GetPlayerWins: Database is not ready yet. Can't request Player Wins");
                return;
            }
            /*            int attempts = 0;
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
                                return false;
                            }
                        } */
            string ipcountrycode = "";
            if (player.IP.Length > 0)
            {
                string dbFilePath = Server.GameDirectory + dbConfig.GeoDatabaseFilePath;
                if (File.Exists(dbFilePath))
                {
                    using var reader = new DatabaseReader(dbFilePath);
                    if (reader != null)
                    {
                        try
                        {
                            var response = reader.Country(player.IP);
                            if (response != null && response.Country != null && response.Country.IsoCode != null)
                            {
                                ipcountrycode = response.Country.IsoCode.ToLower();
                            }
                            else
                            {
                                Console.WriteLine($"[GunGame_Stats]***********: No response from GeoDB for ip: {player.IP}");
                                Server.NextFrame(() =>
                                {
                                    Plugin.Logger.LogError($"[GunGame_Stats] GetPlayerWins GetPlayerISOCode: No response from GeoDB for ip: {player.IP}");
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                            Server.NextFrame(() =>
                            {
                                Plugin.Logger.LogError($"[GunGame_Stats - FATAL] GetPlayerWins GetPlayerISOCode: An error occurred: {ex.Message}");
                            });
                        }
                    }
                }
            }
            if (ipcountrycode.Length == 0)
            {
                ipcountrycode = GGVariables.Instance.ServerLanguageCode;
            }
            //            SavingPlayer = true;
            //            UnsuccessfulSave = false;
            int wins = 0;
            int sound = 1;
            string countrycode = "";

            string query = "SELECT `wins`, `sound`, `countrycode` FROM `gungame_playerdata` WHERE `authid` = @authid;";

            bool playerExists = false;
            if (DatabaseType == DatabaseType.SQLite)
            {
                string connectionString = $"Data Source={_dbFilePath}";
                await using (var _sqliteConn = new SqliteConnection(connectionString))
                {
                    try
                    {
                        await _sqliteConn.OpenAsync();
                        await using (var command = new SqliteCommand(query, _sqliteConn))
                        {
                            command.Parameters.AddWithValue("@authid", player.SavedSteamID);
                            await using (var reader = await command.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    // Assuming wins, sound, and countrycode follow in this order in your SELECT statement
                                    int winsIndex = reader.GetOrdinal("wins");
                                    int soundIndex = reader.GetOrdinal("sound");
                                    int countrycodeIndex = reader.GetOrdinal("countrycode");

                                    wins = reader.GetInt32(winsIndex);
                                    sound = reader.GetInt32(soundIndex);
                                    countrycode = reader.GetString(countrycodeIndex);
                                    playerExists = true;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Server.NextFrame(() =>
                        {
                            Plugin.Logger.LogError($"[GunGame_Stats - FATAL] GetPlayerWins(1): An error occurred: {ex.Message}");
                        });
                        return;
                    }
                }
            }
            else if (DatabaseType == DatabaseType.MySQL)
            {
                MySqlConnection _mysqlConn = new(_builder.ConnectionString);
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
                                countrycode = reader.GetString("countrycode");
                                playerExists = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    //                    SavingPlayer = false;
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogError($"[GunGame_Stats - FATAL] GetPlayerWins(1): An error occurred: {ex.Message}");
                    });
                    return;
                }
            }

            if (DatabaseType == DatabaseType.SQLite)
            {
                string sql;
                if (playerExists)
                {
                    if (countrycode.Length == 0)
                    {
                        sql = "UPDATE `gungame_playerdata` SET `name` = @name, `timestamp` = CURRENT_TIMESTAMP, `countrycode` = @countrycode WHERE `authid` = @authid;";
                    }
                    else
                    {
                        sql = "UPDATE `gungame_playerdata` SET `name` = @name, `timestamp` = CURRENT_TIMESTAMP WHERE `authid` = @authid;";
                    }
                }
                else
                {
                    sql = "INSERT INTO `gungame_playerdata` (`authid`, `name`, `timestamp`, `countrycode`) VALUES (@authid, @name, CURRENT_TIMESTAMP, @countrycode);";
                }
                try
                {
                    using (var _sqliteConn = new SqliteConnection($"Data Source={_dbFilePath}"))
                    {
                        await _sqliteConn.OpenAsync();
                        using (var command = new SqliteCommand(sql, _sqliteConn))
                        {
                            command.Parameters.AddWithValue("@authid", player.SavedSteamID);
                            command.Parameters.AddWithValue("@name", player.PlayerName);
                            command.Parameters.AddWithValue("@countrycode", ipcountrycode);

                            int rowsAffected = await command.ExecuteNonQueryAsync();
                            if (rowsAffected != 1)
                            {
                                Console.WriteLine($"[GunGame_Stats - FATAL] GetPlayerWins(2): Error with Database update");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    //                    SavingPlayer = false;
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogError($"[GunGame_Stats - FATAL] GetPlayerWins(2): An error occurred: {ex.Message}");
                    });

                }
            }
            else if (DatabaseType == DatabaseType.MySQL)
            {
                string sql;
                if (playerExists)
                {
                    if (countrycode.Length == 0)
                    {
                        sql = "UPDATE `gungame_playerdata` SET `name` = @name, `timestamp` = NOW(), `countrycode` = @countrycode WHERE `authid` = @authid";
                    }
                    else
                    {
                        sql = "UPDATE `gungame_playerdata` SET `name` = @name, `timestamp` = NOW() WHERE `authid` = @authid";
                    }
                }
                else
                {
                    sql = "INSERT INTO `gungame_playerdata` (`authid`, `name`, `timestamp`, `countrycode`) VALUES (@authid, @name, NOW(), @countrycode)";
                }
                try
                {
                    using (var _mysqlConn = new MySqlConnection(_builder.ConnectionString))
                    {
                        await _mysqlConn.OpenAsync();
                        using (var command = new MySqlCommand(sql, _mysqlConn))
                        {
                            command.Parameters.AddWithValue("@authid", player.SavedSteamID);
                            command.Parameters.AddWithValue("@name", player.PlayerName);
                            command.Parameters.AddWithValue("@countrycode", ipcountrycode);
                            int rowsAffected = await command.ExecuteNonQueryAsync();
                            if (rowsAffected != 1)
                            {
                                Console.WriteLine($"[GunGame_Stats - FATAL] ****** GetPlayerWins(2): Error with Database update");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    //                    SavingPlayer = false;
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogError($"[GunGame_Stats - FATAL] GetPlayerWins(2): An error occurred: {ex.Message}");
                    });
                }
            }
            //            SavingPlayer = false;
            if (countrycode.Length == 0)
            {
                countrycode = ipcountrycode;
            }
            var tempCulture = new CultureInfo(countrycode);

            Server.NextFrame(() =>
            {
                if (player != null)
                {
                    player.PlayerWins = wins;
                    player.Music = sound == 1;
                    player.Culture = tempCulture;
                    player.SetLanguage();
                    Plugin.Logger.LogInformation($"[GunGame_Stats] {player.PlayerName} wins {player.PlayerWins}, sound {sound}");
                }
            });
        }
        public async Task<bool> UpdateLanguage(GGPlayer player)
        {
            if (!_isDatabaseReady)
            {
                Console.WriteLine("************** Database is not ready yet. Try to update language later");
                return false;
            }
            string updateQuery = "UPDATE `gungame_playerdata` SET `countrycode` = @countrycode WHERE `authid` = @authid;";
            if (DatabaseType == DatabaseType.SQLite)
            {
                try
                {
                    using (var _sqliteConn = new SqliteConnection($"Data Source={_dbFilePath}"))
                    {
                        await _sqliteConn.OpenAsync();
                        using (var command = new SqliteCommand(updateQuery, _sqliteConn))
                        {
                            command.Parameters.AddWithValue("@authid", player.SavedSteamID);
                            command.Parameters.AddWithValue("@countrycode", player.Culture.Name);

                            int rowsAffected = await command.ExecuteNonQueryAsync();
                            if (rowsAffected != 1)
                            {
                                Console.WriteLine($"[GunGame_Stats - FATAL] UpdateLanguage ********* Error with Database update");
                                return false;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] UpdateLanguage ******* An error occurred: {ex.Message}");
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogError($"[GunGame_Stats - FATAL] UpdateLanguage: An error occurred: {ex.Message}");
                    });
                    return false;
                }
            }
            else if (DatabaseType == DatabaseType.MySQL)
            {
                try
                {
                    using (var _mysqlConn = new MySqlConnection(_builder.ConnectionString))
                    {
                        await _mysqlConn.OpenAsync();
                        using (var command = new MySqlCommand(updateQuery, _mysqlConn))
                        {
                            command.Parameters.AddWithValue("@authid", player.SavedSteamID);
                            command.Parameters.AddWithValue("@countrycode", player.Culture.Name);
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] UpdateLanguage ******* An error occurred: {ex.Message}");
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogError($"[GunGame_Stats - FATAL] UpdateLanguage: An error occurred: {ex.Message}");
                    });
                    return false;
                }
            }
            return true;
        }
        public async Task<int> GetPlayerRank(string authid)
        {
            if (!_isDatabaseReady)
            {
                Console.WriteLine("************** Database is not ready yet. Try to request Rank later");
                return -1;
            }
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
                    using (var _sqliteConn = new SqliteConnection($"Data Source={_dbFilePath}"))
                    {
                        await _sqliteConn.OpenAsync();
                        using (var command = new SqliteCommand(query, _sqliteConn))
                        {
                            command.Parameters.AddWithValue("@authid", authid);
                            var reader = await command.ExecuteReaderAsync();
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
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogError($"[GunGame_Stats - FATAL] GetPlayerRank: An error occurred: {ex.Message}");
                    });
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
                    using (var _mysqlConn = new MySqlConnection(_builder.ConnectionString))
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
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL]******* GetPlayerRank: An error occurred: {ex.Message}");
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogError($"[GunGame_Stats - FATAL] GetPlayerRank: An error occurred: {ex.Message}");
                    });
                }
            }
            return rank;
        }
        // *********** add check to the main plugin if the returned result valid
        public async Task<int> GetNumberOfWinners()
        {
            if (!_isDatabaseReady)
            {
                Console.WriteLine("************** Database is not ready yet. Try to request Winners later");
                return -1;
            }
            int count = 0;
            string query = "SELECT COUNT(*) FROM `gungame_playerdata` WHERE `wins` > 0;";

            if (DatabaseType == DatabaseType.SQLite)
            {
                try
                {
                    using (var _sqliteConn = new SqliteConnection($"Data Source={_dbFilePath}"))
                    {
                        await _sqliteConn.OpenAsync();
                        using (var command = new SqliteCommand(query, _sqliteConn))
                        {
                            var result = await command.ExecuteScalarAsync();
                            if (result != null)
                            {
                                count = Convert.ToInt32(result);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] GetNumberOfWinners ******* An error occurred: {ex.Message}");
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogError($"[GunGame_Stats - FATAL] GetNumberOfWinners: An error occurred: {ex.Message}");
                    });
                }
            }
            else if (DatabaseType == DatabaseType.MySQL)
            {
                try
                {
                    using (var _mysqlConn = new MySqlConnection(_builder.ConnectionString))
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
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] GetNumberOfWinners: ******** An error occurred: {ex.Message}");
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogError($"[GunGame_Stats - FATAL] GetNumberOfWinners: An error occurred: {ex.Message}");
                    });
                }
            }
            return count;
        }
        public async Task<int> GetWinsOfLowestTopPlayer(int HandicapTopRank)
        {
            if (!_isDatabaseReady)
            {
                Console.WriteLine("*********** Database is not ready yet. Try to request Wins later");
                return 0;
            }
            int wins = 0;
            string query = @"
                SELECT `wins` FROM `gungame_playerdata`
                ORDER BY `wins` DESC
                LIMIT 1 OFFSET @HandicapTopRank;";

            if (DatabaseType == DatabaseType.SQLite)
            {
                try
                {
                    using (var _sqliteConn = new SqliteConnection($"Data Source={_dbFilePath}"))
                    {
                        await _sqliteConn.OpenAsync();
                        using (var command = new SqliteCommand(query, _sqliteConn))
                        {
                            command.Parameters.AddWithValue("@HandicapTopRank", HandicapTopRank - 1);

                            var result = await command.ExecuteScalarAsync();
                            if (result != null)
                            {
                                wins = Convert.ToInt32(result);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] GetWinsOfLowestTopPlayer ******* An error occurred: {ex.Message}");
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogError($"[GunGame_Stats - FATAL] GetWinsOfLowestTopPlayer: An error occurred: {ex.Message}");
                    });
                }
            }
            else if (DatabaseType == DatabaseType.MySQL)
            {
                try
                {
                    using (var _mysqlConn = new MySqlConnection(_builder.ConnectionString))
                    {
                        await _mysqlConn.OpenAsync();
                        using (var command = new MySqlCommand(query, _mysqlConn))
                        {
                            command.Parameters.AddWithValue("@HandicapTopRank", HandicapTopRank - 1);

                            var result = await command.ExecuteScalarAsync();
                            if (result != null && result != DBNull.Value)
                            {
                                wins = Convert.ToInt32(result);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] GetWinsOfLowestTopPlayer ******* An error occurred: {ex.Message}");
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogError($"[GunGame_Stats - FATAL] GetWinsOfLowestTopPlayer: An error occurred: {ex.Message}");
                    });
                }
            }
            return wins;
        }
        public async Task<Dictionary<string, int>> GetTopPlayers(int HandicapTopRank = 19)
        {
            if (!_isDatabaseReady)
            {
                Console.WriteLine("*********** Database is not ready yet. Try to request TopPlayers later");
                return new Dictionary<string, int>();
            }
            var topPlayers = new Dictionary<string, int>();
            string query = @"
                SELECT `name`, `wins` FROM `gungame_playerdata`
                ORDER BY `wins` DESC
                LIMIT @HandicapTopRank;";

            if (DatabaseType == DatabaseType.SQLite)
            {
                try
                {
                    using (var _sqliteConn = new SqliteConnection($"Data Source={_dbFilePath}"))
                    {
                        await _sqliteConn.OpenAsync();
                        using (var command = new SqliteCommand(query, _sqliteConn))
                        {
                            command.Parameters.AddWithValue("@HandicapTopRank", HandicapTopRank);

                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    string name = reader.GetString("name");
                                    int wins = reader.GetInt32("wins");
                                    Console.WriteLine($"{name} {wins}");
                                    if (wins != 0)
                                        topPlayers[name] = wins;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] GetTopPlayers ******* An error occurred: {ex.Message}");
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogError($"[GunGame_Stats - FATAL] GetTopPlayers: An error occurred: {ex.Message}");
                    });
                }
            }
            else if (DatabaseType == DatabaseType.MySQL)
            {
                try
                {
                    using (var _mysqlConn = new MySqlConnection(_builder.ConnectionString))
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
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] GetTopPlayers ******* An error occurred: {ex.Message}");
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogError($"[GunGame_Stats - FATAL] GetTopPlayers: An error occurred: {ex.Message}");
                    });
                }
            }
            return topPlayers;
        }
        public async Task<bool> ToggleSound(GGPlayer player)
        {
            if (!_isDatabaseReady)
            {
                Console.WriteLine("********* Database is not ready yet. Try to toggle sound later");
                return false;
            }
            int sound = -1;

            string query = "SELECT `sound` FROM `gungame_playerdata` WHERE `authid` = @authid;";
            string updateQuery = "UPDATE `gungame_playerdata` SET `sound` = @sound WHERE `authid` = @authid;";

            if (DatabaseType == DatabaseType.SQLite)
            {
                await using (var _sqliteConn = new SqliteConnection($"Data Source={_dbFilePath}"))
                {
                    try
                    {
                        await _sqliteConn.OpenAsync();

                        await using (var command = new SqliteCommand(query, _sqliteConn))
                        {
                            command.Parameters.AddWithValue("@authid", player.SavedSteamID);
                            await using (var reader = await command.ExecuteReaderAsync())
                            {
                                if (await reader.ReadAsync())
                                {
                                    // Use GetOrdinal to find the column index and use it to retrieve the value
                                    int soundIndex = reader.GetOrdinal("sound");
                                    sound = reader.GetInt32(soundIndex);
                                }
                            }
                        }

                        await using (var updateCommand = new SqliteCommand(updateQuery, _sqliteConn))
                        {
                            updateCommand.Parameters.AddWithValue("@authid", player.SavedSteamID);
                            updateCommand.Parameters.AddWithValue("@sound", sound == 0 ? 1 : 0);

                            int rowsAffected = await updateCommand.ExecuteNonQueryAsync();
                            if (rowsAffected != 1)
                            {
                                Console.WriteLine($"[GunGame_Stats - FATAL] ToggleSound ********* Error with Database update");
                                Server.NextFrame(() =>
                                {
                                    Plugin.Logger.LogError($"[GunGame_Stats - FATAL] ToggleSound: Error with Database update for slot {player.Slot}");
                                });
                                return false; // Ensure the return type of your method is correct
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GunGame_Stats - FATAL] ToggleSound ******* An error occurred: {ex.Message}");
                        Server.NextFrame(() =>
                        {
                            Plugin.Logger.LogError($"[GunGame_Stats - FATAL] ToggleSound: An error occurred: {ex.Message}");
                        });
                        return false; // Ensure the return type of your method is correct
                    }
                }
            }
            else if (DatabaseType == DatabaseType.MySQL)
            {
                MySqlConnection _mysqlConn = new(_builder.ConnectionString);
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
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogError($"[GunGame_Stats - FATAL] ToggleSound: An error occurred: {ex.Message}");
                    });
                    return false;
                }
                finally
                {
                    if (_mysqlConn.State != ConnectionState.Closed)
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
            return true;
        }
        public async Task ResetStats()
        {
            if (!_isDatabaseReady)
            {
                Console.WriteLine("*********** Database is not ready yet. Try to reset stats later");
                return;
            }
            string updateQuery = "UPDATE `gungame_playerdata` SET `wins` = 0;";

            if (DatabaseType == DatabaseType.SQLite)
            {
                try
                {
                    using (var _sqliteConn = new SqliteConnection($"Data Source={_dbFilePath}"))
                    {
                        await _sqliteConn.OpenAsync();
                        using (var command = new SqliteCommand(updateQuery, _sqliteConn))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] ResetStats ******* An error occurred: {ex.Message}");
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogError($"[GunGame_Stats - FATAL] ResetStats: An error occurred: {ex.Message}");
                    });
                }
            }
            else if (DatabaseType == DatabaseType.MySQL)
            {
                try
                {
                    using (var _mysqlConn = new MySqlConnection(_builder.ConnectionString))
                    {
                        await _mysqlConn.OpenAsync();
                        using (var command = new MySqlCommand(updateQuery, _mysqlConn))
                        {
                            await command.ExecuteNonQueryAsync();
                        }
                    } // Connection is closed here when it goes out of the using block's scope
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GunGame_Stats - FATAL] ResetStats ******* An error occurred: {ex.Message}");
                    Server.NextFrame(() =>
                    {
                        Plugin.Logger.LogError($"[GunGame_Stats - FATAL] ResetStats: An error occurred: {ex.Message}");
                    });
                }
            }
        }
    }
}
