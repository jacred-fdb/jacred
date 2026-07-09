namespace JacRed.Infrastructure.Security
{
    public enum JacRedAccessPolicy
    {
        /// <summary>Whitelisted paths — no apikey/devkey (health, sync, static shell, swagger).</summary>
        Public,

        /// <summary>Search and API paths — apikey enforced when configured in init.yaml.</summary>
        ApiKeyWhenConfigured,

        /// <summary>/api/v1.0/config — LAN or devkey (same-host proxy alone is not enough).</summary>
        ConfigApi,

        /// <summary>/dev/, /cron/, /jsondb — LAN or devkey (same-host proxy alone is not enough).</summary>
        DevAdmin
    }
}
