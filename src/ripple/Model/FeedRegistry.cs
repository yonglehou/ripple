using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FubuCore;
using FubuCore.Util;
using NuGet;
using ripple.Nuget;

namespace ripple.Model
{
    public interface IFeedProvider
    {
        INugetFeed For(Feed feed);

        IEnumerable<IFloatingFeed> FloatedFeedsFor(Solution solution);
        IEnumerable<INugetFeed> FeedsFor(Solution solution);
    }

    public class FeedProvider : IFeedProvider
    {
        private readonly Cache<Feed, INugetFeed> _feeds;

        public Solution Config { get; set; }

        public FeedProvider()
        {
            _feeds = new Cache<Feed, INugetFeed>(buildFeed);
        }

        public INugetFeed For(Feed feed)
        {
            return _feeds[feed];
        }

        public IEnumerable<IFloatingFeed> FloatedFeedsFor(Solution solution)
        {
            return FeedsFor(solution).OfType<IFloatingFeed>();
        }

        public IEnumerable<INugetFeed> FeedsFor(Solution solution)
        {
            return solution.Feeds.Select(For);
        }

        private INugetFeed buildFeed(Feed feed)
        {
            if (feed.Url.StartsWith("file://"))
            {
                return buildFileSystemFeed(feed);
            }

            if (!String.IsNullOrEmpty(feed.Username) && !String.IsNullOrEmpty(feed.Password))
            {
                if (HttpClient.DefaultCredentialProvider.GetType() != NugetCredentialsProvider.Instance.GetType())
                {
                    HttpClient.DefaultCredentialProvider = NugetCredentialsProvider.Instance;
                }
                NugetCredentialsProvider.Instance.AddCredentials(feed.Url, new NetworkCredential(feed.Username, feed.Password));
            }
            var stability = DetectStability(feed);

            if (feed.Mode == UpdateMode.Fixed)
            {
                return new NugetFeed(feed.Url, stability);
            }

            return new FloatingFeed(feed.Url, stability);
        }

        // Start stability hack until https://github.com/DarthFubuMVC/ripple/issues/123
        NugetStability DetectStability(Feed feed)
        {
            switch (feed.StabilityConvention)
            {
                case Feed.FeedStabilityConventions.GitFlow:
                    return GitFlowConventionalStability();
                case Feed.FeedStabilityConventions.None:
                    return feed.Stability;
                default:
                    throw new InvalidOperationException("Unknown stability convention: " + feed.StabilityConvention);
            }
        }

        NugetStability GitFlowConventionalStability()
        {
            RippleLog.Info("GitFlow conventional stability is enabled, stability will be set according to the current branch");

            if (!BranchDetector.CanDetectBranch())
            {
                throw new InvalidOperationException("Current git branch could not be detected, this is required if GiFlow conventional stability is configured for the feed");
            }

            var branchName = BranchDetector.Current().ToLower();

            if (branchName == "master" ||
                branchName.StartsWith("hotfix-") ||
                branchName.StartsWith("release-"))
            {
                RippleLog.Info("Detected git branch: {0}, forcing stability to 'Released'".ToFormat(branchName));

                return NugetStability.ReleasedOnly;
            }
            return NugetStability.Anything;
        }

        //end hack

        private const string BranchPlaceholder = "{branch}";

        private INugetFeed buildFileSystemFeed(Feed feed)
        {
            var directory = feed.Url.Replace("file://", "");

            if (directory.Contains(BranchPlaceholder))
            {
                var branchName = BranchDetector.Current();
                directory = directory.Replace(BranchPlaceholder, branchName);

                RippleLog.Debug("Detected branch feed: {0}. Current branch is {1}. Setting directory to {2}".ToFormat(feed, branchName, directory), false);
            }

            directory = directory.ToFullPath();

            if (feed.Mode == UpdateMode.Fixed)
            {
                return new FileSystemNugetFeed(directory, feed.Stability);
            }

            return new FloatingFileSystemNugetFeed(directory, feed.Stability);
        }
    }

    public class FeedRegistry
    {
        private static IFeedProvider _provider;

        static FeedRegistry()
        {
            Reset();
        }

        public static void Stub(IFeedProvider provider)
        {
            _provider = provider;
        }

        public static void Reset()
        {
            Stub(new FeedProvider());
        }

        public static bool IsFloat(Solution solution, Dependency dependency)
        {
            return FloatedFeedsFor(solution).Any(feed => feed.FindLatest(dependency) != null);
        }

        public static INugetFeed FeedFor(Feed feed)
        {
            return _provider.For(feed);
        }

        public static INugetFeed CacheFor(Solution solution)
        {
            return solution.Cache.ToFeed().GetNugetFeed();
        }

        public static IEnumerable<IFloatingFeed> FloatedFeedsFor(Solution solution)
        {
            foreach (var feed in _provider.FloatedFeedsFor(solution))
            {
                if (feed.IsOnline())
                {
                    yield return feed;
                }
            }
        }

        public static IEnumerable<INugetFeed> FeedsFor(Solution solution)
        {
            foreach (var feed in _provider.FeedsFor(solution))
            {
                if (feed.IsOnline())
                {
                    yield return feed;
                }
            }
        }
    }
}