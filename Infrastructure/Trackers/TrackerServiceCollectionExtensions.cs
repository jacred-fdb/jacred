using JacRed.Infrastructure.Trackers.Anidub;
using JacRed.Infrastructure.Trackers.Aniliberty;
using JacRed.Infrastructure.Trackers.Anifilm;
using JacRed.Infrastructure.Trackers.Anibelka;
using JacRed.Infrastructure.Trackers.Anistar;
using JacRed.Infrastructure.Trackers.AnimeLayer;
using JacRed.Infrastructure.Trackers.Baibako;
using JacRed.Infrastructure.Trackers.Bitru;
using JacRed.Infrastructure.Trackers.Kinozal;
using JacRed.Infrastructure.Trackers.Knaben;
using JacRed.Infrastructure.Trackers.Korsars;
using JacRed.Infrastructure.Trackers.Leproduction;
using JacRed.Infrastructure.Trackers.Lostfilm;
using JacRed.Infrastructure.Trackers.Mazepa;
using JacRed.Infrastructure.Trackers.Megapeer;
using JacRed.Infrastructure.Trackers.NNMClub;
using JacRed.Infrastructure.Trackers.Rudub;
using JacRed.Infrastructure.Trackers.SubsPlease;
using JacRed.Infrastructure.Trackers.Rutor;
using JacRed.Infrastructure.Trackers.Rutracker;
using JacRed.Infrastructure.Trackers.Selezen;
using JacRed.Infrastructure.Trackers.Toloka;
using JacRed.Infrastructure.Trackers.TorrentBy;
using JacRed.Infrastructure.Trackers.Ultradox;
using JacRed.Infrastructure.Trackers.Viruseproject;
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
            services.AddParseAllStarter<RutrackerSyncService>();
            services.AddSingleton<BitruApiSyncService>();
            services.AddParseAllStarter<TorrentBySyncService>();
            services.AddParseAllStarter<MegapeerSyncService>();
            services.AddSingleton<BaibakoSyncService>();
            services.AddSingleton<RudubSyncService>();
            services.AddSingleton<SubsPleaseSyncService>();
            services.AddSingleton<AnidubSyncService>();
            services.AddSingleton<AnistarSyncService>();
            services.AddParseAllStarter<AnibelkaSyncService>();
            services.AddSingleton<AnifilmSyncService>();
            services.AddSingleton<LeproductionSyncService>();
            services.AddSingleton<ViruseprojectSyncService>();
            services.AddParseAllStarter<KorsarsSyncService>();
            services.AddParseAllStarter<UltradoxSyncService>();
            services.AddSingleton<SelezenSyncService>();
            services.AddSingleton<MazepaSyncService>();
            services.AddParseAllStarter<RutorSyncService>();
            services.AddParseAllStarter<NNMClubSyncService>();
            services.AddParseAllStarter<KinozalSyncService>();
            services.AddParseAllStarter<TolokaSyncService>();
            return services;
        }

        static void AddParseAllStarter<T>(this IServiceCollection services) where T : class, IParseAllStarter
        {
            services.AddSingleton<T>();
            services.AddSingleton<IParseAllStarter>(sp => sp.GetRequiredService<T>());
        }
    }
}
