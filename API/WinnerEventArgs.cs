namespace GunGame.API
{
    public class WinnerEventArgs : EventArgs
    {
        public int Winner { get; }
        public int Looser { get; }

        public WinnerEventArgs(int winner, int looser)
        {
            Winner = winner;
            Looser = looser;
        }
    }
}
