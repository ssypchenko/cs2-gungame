using System.Text.Json.Serialization;

namespace GunGame.Models
{
    public class WeaponInfo
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }
        [JsonPropertyName("slot")]
        public int Slot { get; set; }
        [JsonPropertyName("clipsize")]
        public int Clipsize { get; set; }
        [JsonPropertyName("ammotype")]
        public int AmmoType { get; set; }
        [JsonPropertyName("level_index")]
        public int LevelIndex { get; set; }
        [JsonPropertyName("fullname")]
        public string FullName { get; set; } = "";
    }
}
