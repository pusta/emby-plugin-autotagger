using MediaBrowser.Model.Plugins;

namespace AutoTagger.Configuration
{
    /// <summary>
    /// Plugin configuration. Persisted by Emby as AutoTagger.xml in the server's plugin
    /// configuration directory, via XmlSerializer.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
        /// </summary>
        public PluginConfiguration()
        {
            Rules = new LibraryTagRule[0];
            TagEpisodesAndSeasons = false;
            LockTags = false;
        }

        /// <summary>
        /// Gets or sets the watched libraries and their tags. Libraries with no tags are not stored.
        /// </summary>
        public LibraryTagRule[] Rules { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether seasons and episodes are tagged individually in
        /// addition to their series. This writes one row per episode, so it is off by default.
        /// </summary>
        public bool TagEpisodesAndSeasons { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the Tags field is locked after tagging, so that a
        /// metadata refresh with "replace existing metadata" cannot clear it. Off by default: a
        /// locked field is skipped entirely by the metadata providers, so the item keeps the
        /// configured tags and gains no others, and it cannot be edited from the web UI until it is
        /// unlocked. Leaving it off is safe because the configured tags are re-applied after any
        /// refresh that replaces them — see <c>AutoTagEntryPoint.OnItemUpdated</c>.
        /// </summary>
        /// <remarks>
        /// The lock is only ever taken once the metadata providers have finished with an item.
        /// Taking it on the ItemAdded path would pre-empt the first refresh and cost the item every
        /// tag its metadata source would have supplied.
        /// </remarks>
        public bool LockTags { get; set; }
    }
}
