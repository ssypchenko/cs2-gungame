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
using GunGame.Models;

namespace GunGame.Online
{
    public class OnlineManager
	{
        private GunGame Plugin;
        private MySqlConnection _mysqlConn = null!;
        public DBConfig config = new();
        public bool OnlineReportEnable = false;
        public bool SavingPlayer = false;
		public OnlineManager(DBConfig dBConfig, GunGame plugin)
		{
                Plugin = plugin;
                config = dBConfig;
                InitializeDatabaseConnection();
		}
        private void InitializeDatabaseConnection()
        {
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
                Console.WriteLine("[GunGame_Online] Connection to database established successfully.");

                // Remember to close the connection after testing
                _mysqlConn.Close();

                OnlineReportEnable = true;
            } 
            catch (Exception ex)
            {
                Console.WriteLine($"[GunGame_Online - FATAL] InitializeDatabaseConnection: Database connection error: {ex.Message}, working without online reports");
            }
        }
        public async Task SavePlayerData(GGPlayer player, string team)
		{
            if (!OnlineReportEnable)
                return;
            int attempts = 0;
            while (SavingPlayer && attempts < 10)
            {
                attempts++;
                await Task.Delay(200);
            }
            if (SavingPlayer)
            {
                Console.WriteLine($"[GunGame_Online - FATAL] SavePlayerData ******* Waiting too long to save");
                return;
            }

            if (player == null || player.SavedSteamID == 0) return;
            SavingPlayer = true;
			string safePlayerName = System.Net.WebUtility.HtmlEncode(player.PlayerName);


            var sql = "INSERT INTO `realtime_stats` (`id`, `nickname`, `level`, `team`) " +
            "VALUES (@id, @PlayerName, @level, @team) " +
            "ON DUPLICATE KEY UPDATE `nickname` = @PlayerName, `level` = @level, `team` = @team;";
            try
            {
                await _mysqlConn.OpenAsync();
                using (var command = new MySqlCommand(sql, _mysqlConn))
                {
                    command.Parameters.AddWithValue("@id", player.SavedSteamID);
                    command.Parameters.AddWithValue("@PlayerName", safePlayerName);
                    command.Parameters.AddWithValue("@level", player.Level);
                    command.Parameters.AddWithValue("@team", team);

                    await command.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GunGame_Online - FATAL] SavePlayerData ******* An error occurred: {ex.Message}");
                SavingPlayer = false;
            }
            finally
            {
                await _mysqlConn.CloseAsync();
            }
            SavingPlayer = false;
		}
        public async Task RemovePlayerData(GGPlayer player)
        {
            if (!OnlineReportEnable)
                return;
            int attempts = 0;
            while (SavingPlayer && attempts < 10)
            {
                attempts++;
                await Task.Delay(200);
            }
            if (SavingPlayer)
            {
                Console.WriteLine($"[GunGame_Online - FATAL] RemovePlayerData ******* Waiting too long to save");
                return;
            }
            SavingPlayer = true;
            string query = "DELETE FROM `realtime_stats` WHERE `id` = @authid;";
            try
            {
                await _mysqlConn.OpenAsync();

                using (var command = new MySqlCommand(query, _mysqlConn))
                {
                    command.Parameters.AddWithValue("@authid", player.SavedSteamID);
                        await command.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GunGame_Online - FATAL]******* RemovePlayerData: An error occurred: {ex.Message}");
            }
            finally
            {
                await _mysqlConn.CloseAsync();
            }
            SavingPlayer = false;
        }
        public async Task ClearAllPlayerData(string mapname)
        {
            if (!OnlineReportEnable)
                return;
            string query = "DELETE FROM `realtime_stats` WHERE 1;";
            try
            {
                await _mysqlConn.OpenAsync();

                using (var command = new MySqlCommand(query, _mysqlConn))
                {
                    await command.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GunGame_Online - FATAL]******* ClearAllPlayerData: An error occurred: {ex.Message}");
            }
            query = "INSERT INTO `realtime_stats` (`id`, `nickname`, `level`, `team`) " +
            "VALUES (@id, @PlayerName, @level, @team);";
            try
            {
                using (var command = new MySqlCommand(query, _mysqlConn))
                {
                    command.Parameters.AddWithValue("@id", "0");
                    command.Parameters.AddWithValue("@PlayerName", mapname);
                    command.Parameters.AddWithValue("@level", 0);
                    command.Parameters.AddWithValue("@team", "map");

                    await command.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GunGame_Online - FATAL] SaveMapName ******* An error occurred: {ex.Message}");
            }
            finally
            {
                await _mysqlConn.CloseAsync();
            }
        }           
    }
}
