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
            LockTags = true;
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
        /// Gets or sets a value indicating whether the Tags field is locked after writing, so that a
        /// metadata refresh with "replace existing metadata" cannot clear it. A locked field cannot
        /// be edited from the web UI until it is unlocked.
        /// </summary>
        public bool LockTags { get; set; }
    }
}
