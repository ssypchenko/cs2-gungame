namespace GunGame.Models
{
    public class DatabaseSettings
    {
        public DBConfig StatsDB { get; set; } = null!;
        public DBConfig OnlineDB { get; set; }  = null!;
    }
}
