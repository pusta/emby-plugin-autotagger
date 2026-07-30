using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoTagger.Configuration;
using AutoTagger.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace AutoTagger.ScheduledTasks
{
    /// <summary>
    /// Applies configured tags to items that are already in a watched library.
    /// </summary>
    /// <remarks>
    /// Emby's IScheduledTask differs from Jellyfin's in two ways that matter: the execute method is
    /// named Execute (not ExecuteAsync) and takes its arguments in the opposite order, and
    /// GetDefaultTriggers is part of the interface itself. Returning an empty trigger list makes this
    /// a manual, run-on-demand task, which is what you want for a backfill.
    /// </remarks>
    public class ApplyTagsTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;
        private readonly Tagger _tagger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplyTagsTask"/> class.
        /// </summary>
        /// <param name="libraryManager">The library manager, supplied by Emby.</param>
        /// <param name="logManager">The log manager, supplied by Emby.</param>
        public ApplyTagsTask(ILibraryManager libraryManager, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger("AutoTagger");
            _tagger = new Tagger(libraryManager, logManager);
        }

        /// <inheritdoc />
        public string Name
        {
            get { return "Apply auto-tags to existing items"; }
        }

        /// <inheritdoc />
        public string Key
        {
            get { return "AutoTaggerApplyTags"; }
        }

        /// <inheritdoc />
        public string Description
        {
            get { return "Applies the tags configured in Auto Tagger to items already present in each watched library."; }
        }

        /// <inheritdoc />
        public string Category
        {
            get { return "Library"; }
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Manual only. New items are handled live by AutoTagEntryPoint.
            return new List<TaskTriggerInfo>();
        }

        /// <inheritdoc />
        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var configuration = Plugin.Instance == null ? null : Plugin.Instance.Configuration;

            if (configuration == null || configuration.Rules == null || configuration.Rules.Length == 0)
            {
                _logger.Info("Auto Tagger has no configured rules; nothing to do");
                progress.Report(100);
                return Task.FromResult(true);
            }

            var watched = GetWatchedFolders(configuration);

            if (watched.Count == 0)
            {
                _logger.Info("Auto Tagger found no libraries matching its configured rules");
                progress.Report(100);
                return Task.FromResult(true);
            }

            var tagged = 0;
            var examined = 0;
            var folderIndex = 0;

            foreach (var folder in watched)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var items = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    Parent = folder,
                    Recursive = true,
                    IsVirtualItem = false
                });

                var itemList = items == null ? new List<BaseItem>() : items.ToList();

                _logger.Info("Auto Tagger scanning {0} ({1} items)", folder.Name, itemList.Count);

                for (var i = 0; i < itemList.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    examined++;

                    try
                    {
                        if (_tagger.TagItem(itemList[i]))
                        {
                            tagged++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException("Failed to tag {0}", ex, itemList[i].Name);
                    }

                    if (itemList.Count > 0)
                    {
                        var withinFolder = (double)(i + 1) / itemList.Count;
                        progress.Report(100 * (folderIndex + withinFolder) / watched.Count);
                    }
                }

                folderIndex++;
                progress.Report(100 * (double)folderIndex / watched.Count);
            }

            _logger.Info("Auto Tagger examined {0} items and updated {1}", examined, tagged);
            progress.Report(100);

            return Task.FromResult(true);
        }

        /// <summary>
        /// Gets the library root folders that at least one configured rule refers to.
        /// </summary>
        /// <param name="configuration">The plugin configuration.</param>
        /// <returns>The matching collection folders.</returns>
        private List<Folder> GetWatchedFolders(PluginConfiguration configuration)
        {
            // The user root folder's children are the libraries, which is how Emby itself
            // enumerates collection folders internally.
            var root = _libraryManager.GetUserRootFolder();

            if (root == null)
            {
                return new List<Folder>();
            }

            return root.Children
                .OfType<Folder>()
                .Where(folder => configuration.Rules.Any(rule =>
                    rule != null
                    && rule.Tags != null
                    && rule.Tags.Length > 0
                    && Tagger.MatchesLibrary(folder, rule)))
                .ToList();
        }
    }
}
