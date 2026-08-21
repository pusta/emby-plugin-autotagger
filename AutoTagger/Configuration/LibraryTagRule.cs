namespace AutoTagger.Configuration
{
    /// <summary>
    /// A single watched library and the tags applied to items added to it.
    /// </summary>
    public class LibraryTagRule
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LibraryTagRule"/> class.
        /// </summary>
        public LibraryTagRule()
        {
            LibraryId = string.Empty;
            LibraryName = string.Empty;
            Tags = new string[0];
            ExcludeTags = new string[0];
        }

        /// <summary>
        /// Gets or sets the library identifier as reported by /Library/VirtualFolders.
        /// </summary>
        /// <remarks>
        /// Emby's REST API exposes item ids as numeric strings (the internal row id), whereas
        /// Jellyfin exposes GUIDs. Rather than assume either form, matching is done defensively —
        /// see <c>Tagger.MatchesLibrary</c> — with the library name as a fallback.
        /// </remarks>
        public string LibraryId { get; set; }

        /// <summary>
        /// Gets or sets the library display name. Stored so the configuration page stays readable
        /// and so rules can still be matched if the id form changes between Emby versions.
        /// </summary>
        public string LibraryName { get; set; }

        /// <summary>
        /// Gets or sets the tags to apply. Comparison is case-insensitive.
        /// </summary>
        public string[] Tags { get; set; }

        /// <summary>
        /// Gets or sets the tags that suppress this rule. If an item already carries any of
        /// these tags, this library's tags are not applied to it. Comparison is case-insensitive.
        /// </summary>
        /// <remarks>
        /// Older configuration files predate this field, so XmlSerializer leaves it null on load
        /// rather than running the constructor's initializer. Every read of it is null-tolerant.
        /// </remarks>
        public string[] ExcludeTags { get; set; }
    }
}
