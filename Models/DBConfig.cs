using System.Text.Json.Serialization;

namespace GunGame.Models
{
    public class DBConfig
    {
        [JsonPropertyName("DatabaseType")]
        public string DatabaseType { get; set; } = "";
        [JsonPropertyName("DatabaseFilePath")]
        public string DatabaseFilePath { get; set; } = "";
        [JsonPropertyName("GeoDatabaseFilePath")]
        public string GeoDatabaseFilePath { get; set; } = "";
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
