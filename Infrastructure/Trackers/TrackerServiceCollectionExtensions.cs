using JacRed.Infrastructure.Trackers.Anidub;
using JacRed.Infrastructure.Trackers.Aniliberty;
using JacRed.Infrastructure.Trackers.AnimeLayer;
using JacRed.Infrastructure.Trackers.Baibako;
using JacRed.Infrastructure.Trackers.Bitru;
using JacRed.Infrastructure.Trackers.Kinozal;
using JacRed.Infrastructure.Trackers.Knaben;
using JacRed.Infrastructure.Trackers.Lostfilm;
using JacRed.Infrastructure.Trackers.Mazepa;
using JacRed.Infrastructure.Trackers.Megapeer;
using JacRed.Infrastructure.Trackers.NNMClub;
using JacRed.Infrastructure.Trackers.Rutor;
using JacRed.Infrastructure.Trackers.Rutracker;
using JacRed.Infrastructure.Trackers.Selezen;
using JacRed.Infrastructure.Trackers.Toloka;
using JacRed.Infrastructure.Trackers.TorrentBy;
using Microsoft.Extensions.DependencyInjection;

namespace JacRed.Infrastructure.Trackers
{
    public static class TrackerServiceCollectionExtensions
    {
        public static IServiceCollection AddJacRedTrackers(this IServiceCollection services)
        {
            services.AddSingleton<KnabenSyncService>();
            services.AddSingleton<AnimeLayerSyncService>();
            services.AddSingleton<AnilibertySyncService>();
            services.AddSingleton<LostfilmSyncService>();
            services.AddSingleton<RutrackerSyncService>();
            services.AddSingleton<BitruApiSyncService>();
            services.AddSingleton<TorrentBySyncService>();
            services.AddSingleton<MegapeerSyncService>();
            services.AddSingleton<BaibakoSyncService>();
            services.AddSingleton<AnidubSyncService>();
            services.AddSingleton<SelezenSyncService>();
            services.AddSingleton<MazepaSyncService>();
            services.AddSingleton<RutorSyncService>();
            services.AddSingleton<NNMClubSyncService>();
            services.AddSingleton<KinozalSyncService>();
            services.AddSingleton<TolokaSyncService>();
            return services;
        }
    }
}
