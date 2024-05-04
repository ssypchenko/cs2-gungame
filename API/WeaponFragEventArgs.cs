namespace GunGame.API
{
    public class WeaponFragEventArgs : EventArgs
    {
        public int Killer { get; }
        public string Weapon { get; }
        public bool Result { get; set; }

        public WeaponFragEventArgs(int killer, string weapon)
        {
            Killer = killer;
            Weapon = weapon;
            Result = true;
        }
    }
}
