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
    /// The ItemAdded hook only catches new arrivals. Run this task once after configuring rules to
    /// tag everything already present in the watched libraries.
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
            get { return "AutoTaggerApplyExisting"; }
        }

        /// <inheritdoc />
        public string Description
        {
            get { return "Tags items already present in the watched libraries."; }
        }

        /// <inheritdoc />
        public string Category
        {
            get { return "Auto Tagger"; }
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

            var types = Tagger.GetTaggableTypeNames(configuration);
            var tagged = 0;
            var examined = 0;

            for (var folderIndex = 0; folderIndex < watched.Count; folderIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var folder = watched[folderIndex];

                var items = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    Parent = folder,
                    Recursive = true,
                    IsVirtualItem = false,
                    IncludeItemTypes = types
                });

                var itemList = items ?? new BaseItem[0];

                _logger.Info("Auto Tagger scanning {0} ({1} items)", folder.Name, itemList.Length);

                for (var i = 0; i < itemList.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    examined++;

                    try
                    {
                        // These items are already in the library, so their metadata has been
                        // fetched and locking the Tags field costs nothing the providers were
                        // going to add.
                        if (_tagger.Apply(itemList[i], true))
                        {
                            tagged++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException("Failed to tag {0}", ex, itemList[i].Name);
                    }

                    // Progress is reported per-library so one big library does not stall the bar.
                    var withinFolder = itemList.Length == 0 ? 1d : (double)(i + 1) / itemList.Length;
                    progress.Report(100 * (folderIndex + withinFolder) / watched.Count);
                }

                progress.Report(100 * (double)(folderIndex + 1) / watched.Count);
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
        /// <remarks>
        /// Jellyfin can query each library directly by its GUID, because that is what the rule
        /// stores. Emby's rules may hold either id form or only a name, so the libraries are
        /// enumerated and matched with the same <c>Tagger.MatchesLibrary</c> the live handler uses,
        /// which keeps the two from ever disagreeing about which rule covers which library.
        /// </remarks>
        private List<Folder> GetWatchedFolders(PluginOptions configuration)
        {
            return GetLibraryFolders()
                .Where(folder => configuration.Rules.Any(rule =>
                    rule != null
                    && rule.Tags != null
                    && rule.Tags.Length > 0
                    && Tagger.MatchesLibrary(folder, rule)))
                .ToList();
        }

        /// <summary>
        /// Enumerates the server's libraries as folder items.
        /// </summary>
        /// <returns>The collection folders.</returns>
        /// <remarks>
        /// The user root folder's children are the libraries. Emby's Folder does not expose a
        /// Children property, so they are queried; if that comes back empty the virtual folder list
        /// is used instead and each entry resolved by id, which is the same list the configuration
        /// page is built from.
        /// </remarks>
        private List<Folder> GetLibraryFolders()
        {
            var root = _libraryManager.GetUserRootFolder();

            if (root != null)
            {
                var children = _libraryManager.GetItemList(new InternalItemsQuery { Parent = root });

                if (children != null)
                {
                    var folders = children.OfType<Folder>().ToList();
                    if (folders.Count > 0)
                    {
                        return folders;
                    }
                }
            }

            return GetLibraryFoldersFromVirtualFolders();
        }

        /// <summary>
        /// Resolves the libraries from <c>ILibraryManager.GetVirtualFolders</c>.
        /// </summary>
        /// <returns>The collection folders that could be resolved.</returns>
        private List<Folder> GetLibraryFoldersFromVirtualFolders()
        {
            var resolved = new List<Folder>();
            var virtualFolders = _libraryManager.GetVirtualFolders();

            if (virtualFolders == null)
            {
                return resolved;
            }

            foreach (var virtualFolder in virtualFolders)
            {
                BaseItem item = null;

                long internalId;
                Guid guid;

                // GetItemById throws on an empty GUID rather than returning null, so every id is
                // checked before it is used.
                if (!string.IsNullOrEmpty(virtualFolder.ItemId) && long.TryParse(virtualFolder.ItemId, out internalId) && internalId > 0)
                {
                    item = _libraryManager.GetItemById(internalId);
                }
                else if (!string.IsNullOrEmpty(virtualFolder.ItemId) && Guid.TryParse(virtualFolder.ItemId, out guid) && !guid.Equals(Guid.Empty))
                {
                    item = _libraryManager.GetItemById(guid);
                }
                else if (!string.IsNullOrEmpty(virtualFolder.Guid) && Guid.TryParse(virtualFolder.Guid, out guid) && !guid.Equals(Guid.Empty))
                {
                    item = _libraryManager.GetItemById(guid);
                }

                var folder = item as Folder;
                if (folder != null)
                {
                    resolved.Add(folder);
                }
                else
                {
                    _logger.Warn("Auto Tagger could not resolve library {0} (id {1})", virtualFolder.Name, virtualFolder.ItemId);
                }
            }

            return resolved;
        }
    }
}
