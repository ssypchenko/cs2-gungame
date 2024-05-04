namespace GunGame.API
{
    public class KillEventArgs : EventArgs
    {
        public bool Result { get; set; }
        public int Killer { get; }
        public int Victim { get; }
        public string Weapon { get; }
        public bool TeamKill { get; }

        public KillEventArgs(int killer, int victim, string weapon, bool teamkill)
        {
            Killer = killer;
            Victim = victim;
            Weapon = weapon;
            TeamKill = teamkill;
            Result = true;
        }
    }
}
