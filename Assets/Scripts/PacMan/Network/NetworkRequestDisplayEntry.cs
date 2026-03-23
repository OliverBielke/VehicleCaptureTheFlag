namespace PacMan.Network
{
    public sealed class NetworkRequestDisplayEntry
    {
        public NetworkRequestDisplayEntry(string teamTag, string teamName, double averageWaitMs, double currentWaitMs)
        {
            TeamTag = teamTag ?? string.Empty;
            TeamName = teamName ?? string.Empty;
            AverageWaitMs = averageWaitMs;
            CurrentWaitMs = currentWaitMs;
        }

        public string TeamTag { get; }
        public string TeamName { get; }
        public double AverageWaitMs { get; }
        public double CurrentWaitMs { get; }
    }
}
