using Newtonsoft.Json;

namespace GunGame.Models
{
    public class WeaponOrderSettings
    {
        [JsonProperty("WeaponOrder")]
        public Dictionary<int, string> WeaponOrder { get; set; }

        [JsonProperty("MultipleKillsPerLevel")]
        public Dictionary<int, int> MultipleKillsPerLevel { get; set; } = new Dictionary<int, int>();

        [JsonProperty("RandomWeaponReserveLevels")]
        public string RandomWeaponReserveLevels { get; set; } = "";

        [JsonProperty("RandomWeaponOrder")]
        public bool RandomWeaponOrder { get; set; }

        // Constructor to ensure non-nullable initialization
        public WeaponOrderSettings()
        {
            WeaponOrder = new Dictionary<int, string>();
        }
    }
}
