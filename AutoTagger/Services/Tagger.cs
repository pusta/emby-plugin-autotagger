using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AutoTagger.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

namespace AutoTagger.Services
{
    /// <summary>
    /// Resolves which tags apply to an item and writes them additively.
    /// </summary>
    /// <remarks>
    /// On Jellyfin this type was resolved from the host container via IPluginServiceRegistrator.
    /// Emby's automatic type discovery only constructs types that implement one of its own
    /// interfaces, and it will not resolve arbitrary plugin types as constructor dependencies,
    /// so this class is instantiated by hand where it is needed.
    /// </remarks>
    public class Tagger
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Tagger"/> class.
        /// </summary>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="logManager">The log manager. Emby hands out loggers through ILogManager
        /// rather than injecting a generic ILogger&lt;T&gt; as Jellyfin does.</param>
        public Tagger(ILibraryManager libraryManager, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger("AutoTagger");
        }

        private static PluginConfiguration Configuration
        {
            get
            {
                var plugin = Plugin.Instance;
                return plugin == null ? null : plugin.Configuration;
            }
        }

        /// <summary>
        /// Determines whether an item is a candidate for tagging at all, independent of whether any
        /// rule matches it. Used by both the live event handler and the backfill task so the two
        /// always agree on scope.
        /// </summary>
        /// <param name="item">The item to test.</param>
        /// <returns><c>true</c> if the item should be considered for tagging.</returns>
        public static bool IsTaggable(BaseItem item)
        {
            if (item == null)
            {
                return false;
            }

            // Library roots and dashboard views are not real media and must never be written to.
            if (item is ICollectionFolder || item is UserView || item is AggregateFolder)
            {
                return false;
            }

            // Missing/unaired placeholder entries.
            if (item.LocationType == LocationType.Virtual)
            {
                return false;
            }

            var configuration = Configuration;
            if (configuration == null)
            {
                return false;
            }

            if ((item is Season || item is Episode) && !configuration.TagEpisodesAndSeasons)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determines whether a configured rule refers to the given collection folder.
        /// </summary>
        /// <param name="folder">A library root folder.</param>
        /// <param name="rule">A configured rule.</param>
        /// <returns><c>true</c> if the rule applies to the folder.</returns>
        /// <remarks>
        /// Emby's REST API returns item ids as numeric strings backed by <c>InternalId</c>, while the
        /// in-process object model also carries a <c>Guid</c> id. Both forms are compared so the
        /// stored configuration works whichever one the dashboard handed us, and the library name is
        /// used as a last resort — Emby requires library names to be unique, so it is a safe key.
        /// </remarks>
        public static bool MatchesLibrary(Folder folder, LibraryTagRule rule)
        {
            if (folder == null || rule == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(rule.LibraryId))
            {
                if (string.Equals(rule.LibraryId, folder.InternalId.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rule.LibraryId, folder.Id.ToString("N"), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rule.LibraryId, folder.Id.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return !string.IsNullOrEmpty(rule.LibraryName)
                   && string.Equals(rule.LibraryName, folder.Name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the union of tags configured for every library that contains the item.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>The tags to apply. Empty if no rule matches.</returns>
        public string[] GetTagsForItem(BaseItem item)
        {
            var configuration = Configuration;
            if (item == null || configuration == null || configuration.Rules == null || configuration.Rules.Length == 0)
            {
                return new string[0];
            }

            var folders = _libraryManager.GetCollectionFolders(item);
            if (folders == null || folders.Count == 0)
            {
                return new string[0];
            }

            var tags = new List<string>();

            foreach (var folder in folders)
            {
                foreach (var rule in configuration.Rules)
                {
                    if (rule == null || rule.Tags == null || rule.Tags.Length == 0)
                    {
                        continue;
                    }

                    if (!MatchesLibrary(folder, rule))
                    {
                        continue;
                    }

                    foreach (var tag in rule.Tags)
                    {
                        if (string.IsNullOrWhiteSpace(tag))
                        {
                            continue;
                        }

                        var trimmed = tag.Trim();
                        if (!tags.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                        {
                            tags.Add(trimmed);
                        }
                    }
                }
            }

            return tags.ToArray();
        }

        /// <summary>
        /// Applies any missing configured tags to the item and saves it.
        /// </summary>
        /// <param name="item">The item to tag.</param>
        /// <returns><c>true</c> if the item was written to the repository.</returns>
        public bool TagItem(BaseItem item)
        {
            if (!IsTaggable(item))
            {
                return false;
            }

            var configuration = Configuration;
            var wanted = GetTagsForItem(item);
            if (wanted.Length == 0)
            {
                return false;
            }

            var existing = item.Tags ?? new string[0];
            var missing = wanted
                .Where(tag => !existing.Contains(tag, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            var lockedFields = item.LockedFields ?? new MetadataFields[0];
            var needsLock = configuration.LockTags && !lockedFields.Contains(MetadataFields.Tags);

            if (missing.Length == 0 && !needsLock)
            {
                return false;
            }

            if (missing.Length > 0)
            {
                item.Tags = existing.Concat(missing).ToArray();
            }

            if (needsLock)
            {
                item.LockedFields = lockedFields.Concat(new[] { MetadataFields.Tags }).ToArray();
            }

            SaveItem(item);

            if (missing.Length > 0)
            {
                _logger.Info(
                    "Tagged {0} ({1}) with [{2}]",
                    item.Name,
                    item.GetType().Name,
                    string.Join(", ", missing));
            }

            return true;
        }

        /// <summary>
        /// Writes the item back to the library repository.
        /// </summary>
        /// <param name="item">The item to save.</param>
        /// <remarks>
        /// This is the single place the project touches Emby's write API, deliberately, because it is
        /// the call most likely to need adjusting between server builds. Emby's BaseItem publishes
        /// several overloads of UpdateToRepository; if the one below is not present on your server's
        /// assemblies, try one of these instead and nothing else in the project needs to change:
        /// <code>
        /// item.UpdateToRepository(ItemUpdateType.MetadataEdit, item.GetParent());
        /// _libraryManager.UpdateItem(item, item.GetParent(), ItemUpdateType.MetadataEdit);
        /// </code>
        /// Jellyfin's equivalent was <c>UpdateToRepositoryAsync(ItemUpdateType, CancellationToken)</c>,
        /// which is why the whole call chain here is synchronous rather than async.
        /// </remarks>
        private void SaveItem(BaseItem item)
        {
            item.UpdateToRepository(ItemUpdateType.MetadataEdit);
        }
    }
}
