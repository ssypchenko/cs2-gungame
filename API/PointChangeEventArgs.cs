namespace GunGame.API
{
    public class PointChangeEventArgs : EventArgs
    {
        public int Killer { get; }
        public int Kills { get; }
        public bool Result { get; set; }

        public PointChangeEventArgs(int killer, int kills)
        {
            Killer = killer;
            Kills = kills;
            Result = true;
        }
    }
}
