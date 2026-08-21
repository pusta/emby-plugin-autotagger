using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AutoTagger.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
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

        private static PluginOptions Configuration
        {
            get
            {
                var plugin = Plugin.Instance;
                return plugin == null ? null : plugin.Configuration;
            }
        }

        /// <summary>
        /// Determines whether an item is eligible for tagging at all, independent of whether any
        /// rule matches it. Movies and series always qualify; seasons and episodes are opt-in
        /// because tagging every episode of a large series is a lot of database writes.
        /// </summary>
        /// <param name="item">The item to test.</param>
        /// <param name="configuration">The current plugin configuration.</param>
        /// <returns><c>true</c> if the item should be considered for tagging.</returns>
        /// <remarks>
        /// This is a whitelist rather than a blacklist, matching the Jellyfin build. Anything that
        /// is not one of the four types below — library roots, views, collections, music, photos,
        /// live TV — is never written to, so the plugin cannot damage item types it was never
        /// meant to touch. Missing/unaired placeholder entries are excluded as well: they have no
        /// file behind them and Emby recreates them on the next scan.
        /// </remarks>
        public static bool IsTaggable(BaseItem item, PluginOptions configuration)
        {
            if (item == null || configuration == null)
            {
                return false;
            }

            if (item.IsVirtualItem || item.LocationType == LocationType.Virtual)
            {
                return false;
            }

            if (item is Movie || item is Series)
            {
                return true;
            }

            if (item is Season || item is Episode)
            {
                return configuration.TagEpisodesAndSeasons;
            }

            return false;
        }

        /// <summary>
        /// Gets the item type names to include in a library query, matching what
        /// <see cref="IsTaggable"/> accepts.
        /// </summary>
        /// <param name="configuration">The current plugin configuration.</param>
        /// <returns>Type names for <c>InternalItemsQuery.IncludeItemTypes</c>.</returns>
        /// <remarks>
        /// Emby's query takes type names as strings, where Jellyfin's takes a BaseItemKind enum.
        /// The names are taken from the types themselves so a rename cannot silently produce a
        /// query that matches nothing.
        /// </remarks>
        public static string[] GetTaggableTypeNames(PluginOptions configuration)
        {
            var names = new List<string>
            {
                typeof(Movie).Name,
                typeof(Series).Name
            };

            if (configuration != null && configuration.TagEpisodesAndSeasons)
            {
                names.Add(typeof(Season).Name);
                names.Add(typeof(Episode).Name);
            }

            return names.ToArray();
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
        /// The Jellyfin build can simply parse the stored id as a GUID; this is the one place where
        /// the two implementations genuinely cannot share logic.
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
        /// An item present in two watched libraries receives the union of both rule sets.
        /// A rule whose exclusions match one of the item's existing tags contributes nothing;
        /// the other libraries' rules still apply.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="existingTags">The tags the item already carries, used to evaluate exclusions.</param>
        /// <returns>The tags to apply. Empty if no rule matches.</returns>
        public string[] GetConfiguredTags(BaseItem item, string[] existingTags)
        {
            var configuration = Configuration;
            if (item == null || configuration == null || configuration.Rules == null || configuration.Rules.Length == 0)
            {
                return new string[0];
            }

            var folders = _libraryManager.GetCollectionFolders(item);
            if (folders == null || folders.Length == 0)
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

                    if (IsExcluded(rule, existingTags))
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
        /// Tests whether an item's existing tags suppress a rule. An empty exclusion list
        /// never suppresses.
        /// </summary>
        /// <param name="rule">The rule to test.</param>
        /// <param name="existingTags">The tags the item already carries.</param>
        /// <returns><c>true</c> if the rule should be skipped for this item.</returns>
        private static bool IsExcluded(LibraryTagRule rule, string[] existingTags)
        {
            var exclusions = rule.ExcludeTags;
            if (exclusions == null || exclusions.Length == 0 || existingTags == null || existingTags.Length == 0)
            {
                return false;
            }

            return exclusions
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Any(tag => existingTags.Contains(tag, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Applies any missing configured tags to the item and saves it. Tagging is additive;
        /// existing tags are never removed. An item excluded by every matching rule is left
        /// untouched, including its lock state.
        /// </summary>
        /// <param name="item">The item to tag.</param>
        /// <param name="metadataSettled">
        /// Whether the metadata providers have finished with this item. Locking the Tags field
        /// stops the providers writing to it at all, so the lock must never be taken on the
        /// ItemAdded path: that fires before the first refresh and would cost the item every
        /// tag its metadata source would have supplied.
        /// </param>
        /// <returns><c>true</c> if the item was written to the repository.</returns>
        public bool Apply(BaseItem item, bool metadataSettled)
        {
            var configuration = Configuration;
            if (configuration == null || !IsTaggable(item, configuration))
            {
                return false;
            }

            // Exclusions are evaluated against the tags the item carries on entry, so the
            // order in which rules are applied cannot change the outcome.
            var existing = item.Tags ?? new string[0];

            var wanted = GetConfiguredTags(item, existing);
            if (wanted.Length == 0)
            {
                return false;
            }

            var missing = wanted
                .Where(tag => !existing.Contains(tag, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            var lockedFields = item.LockedFields ?? new MetadataFields[0];
            var needsLock = configuration.LockTags
                && metadataSettled
                && !lockedFields.Contains(MetadataFields.Tags);

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
        /// <para>
        /// The update reason matters beyond bookkeeping: this write raises ILibraryManager.ItemUpdated,
        /// and AutoTagEntryPoint ignores MetadataEdit precisely so that saving a tag cannot re-enter
        /// the handler that saved it.
        /// </para>
        /// </remarks>
        private void SaveItem(BaseItem item)
        {
            item.UpdateToRepository(ItemUpdateType.MetadataEdit);
        }
    }
}
