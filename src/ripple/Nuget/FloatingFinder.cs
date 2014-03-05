using System.Collections.Generic;
using System.Linq;
using FubuCore;
using ripple.Model;

namespace ripple.Nuget
{
    public class FloatingFinder : INugetFinder
    {
        public bool Matches(Dependency dependency)
        {
            return dependency.IsFloat();
        }

        public NugetResult Find(Solution solution, Dependency dependency)
        {
            var feeds = FeedRegistry.FloatedFeedsFor(solution).ToArray();
            var result = NugetSearch.FindNuget(feeds, x =>
            {
                var feed = x.As<IFloatingFeed>();
                var nuget = feed.FindLatest(dependency);
                if (nuget != null && dependency.Mode == UpdateMode.Fixed && nuget.IsUpdateFor(dependency))
                {
                    return null;
                }

                if (nuget == null)
                {
                    return null;
                }

                var cache = FeedRegistry.CacheFor(solution);
                var cachedNuget = cache.Find(new Dependency(dependency.Name, nuget.Version, dependency.Mode));
                
                if (cachedNuget != null)
                {
                    return cachedNuget;
                }

                return nuget;
            });

            if (!result.Found)
            {
                feeds
                    .Where(x => x.IsOnline())
                    .Each(files => files.DumpLatest());
            }

            return result;
        }
    }
}