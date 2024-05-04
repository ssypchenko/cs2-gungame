namespace GunGame.API
{
    public class LevelChangeEventArgs : EventArgs
    {
        public int Killer { get; }
        public int Level { get; }
        public int Difference { get; }
        public bool KnifeSteal { get; }
        public bool LastLevel { get; }
        public bool Knife { get; }
        public int Victim { get; }
        public bool Result { get; set; }

        public LevelChangeEventArgs(int killer, int level, int difference, bool knifesteal, bool lastlevel, bool knife, int victim)
        {
            Killer = killer;
            Level = level;
            Difference = difference;
            KnifeSteal = knifesteal;
            LastLevel = lastlevel;
            Knife = knife;
            Victim = victim;
            Result = true;
        }
    }
}
