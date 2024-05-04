namespace GunGame.Models
{
    public class Weapon
    {
        public int Level { get; set; } = 0;
        public string Name { get; set; } = "";
        public string FullName { get; set; } = "";
        public int Index { get; set; } = -1;
        public int LevelIndex { get; set; }  = -1;
        public int Slot { get; set; }  = -1;
        public int Ammo { get; set; }  = -1;
        public int ClipSize { get; set; }  = -1;
    }
}
