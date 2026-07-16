namespace JacRed.Models
{
    public class Evercache
    {
        public bool enable { get; set; }

        /// <summary>Hours before idle shard eviction. 0 = no time-based eviction (still capped by maxOpenWriteTask).</summary>
        public int validHour { get; set; }

        /// <summary>Hard cap on cached open shards; always enforced by CronFast when enable=true.</summary>
        public int maxOpenWriteTask { get; set; }

        public int dropCacheTake { get; set; }
    }
}
