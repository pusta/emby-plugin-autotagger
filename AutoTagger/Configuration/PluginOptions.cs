using System.ComponentModel;
using Emby.Web.GenericEdit;

namespace AutoTagger.Configuration
{
    /// <summary>
    /// Plugin settings, and the model the settings page is generated from.
    /// </summary>
    /// <remarks>
    /// Emby builds the settings UI from this class: one control per browsable property, labelled
    /// by <see cref="DisplayNameAttribute"/> and described by <see cref="DescriptionAttribute"/>.
    /// There is no HTML or JavaScript involved, which is the point — a hand-written configuration
    /// page depends on markup and script conventions that have changed between Emby releases,
    /// and this one cannot.
    /// <para>
    /// Replaces the earlier <c>PluginConfiguration : BasePluginConfiguration</c>. Settings are
    /// persisted by the plugin's own options store rather than as <c>AutoTagger.xml</c>, so
    /// anything saved by an earlier build is not carried over.
    /// </para>
    /// </remarks>
    public class PluginOptions : EditableOptionsBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PluginOptions"/> class.
        /// </summary>
        public PluginOptions()
        {
            Rules = new LibraryTagRule[0];
            LibraryRules = new LibraryRuleRowCollection();
            TagEpisodesAndSeasons = false;
            LockTags = false;
        }

        /// <inheritdoc />
        public override string EditorTitle
        {
            get { return "Auto Tagger"; }
        }

        /// <inheritdoc />
        public override string EditorDescription
        {
            get
            {
                return "Tags are applied to new items as they are added, and are only ever added — "
                       + "nothing already on an item is removed. To tag items already in a library, run "
                       + "\"Apply auto-tags to existing items\" from Scheduled Tasks.";
            }
        }

        /// <summary>
        /// Gets or sets the editable rows shown on the settings page, one per library.
        /// </summary>
        /// <remarks>
        /// Rebuilt from the server's library list every time the page is opened, and folded back
        /// into <see cref="Rules"/> on save. It is the editor surface, not the stored data.
        /// </remarks>
        [DisplayName("Tags by library")]
        public LibraryRuleRowCollection LibraryRules { get; set; }

        /// <summary>
        /// Gets or sets the stored rules. This is the authoritative copy that the tagger reads.
        /// </summary>
        /// <remarks>
        /// Deliberately not browsable: a plain array survives being persisted and reloaded without
        /// depending on how the edit framework round-trips a polymorphic collection, so the data
        /// the plugin actually runs on is never at the mercy of the UI layer.
        /// </remarks>
        [Browsable(false)]
        public LibraryTagRule[] Rules { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether seasons and episodes are tagged individually in
        /// addition to movies and series.
        /// </summary>
        [DisplayName("Also tag seasons and episodes")]
        [Description("Off by default, so only movies and series are tagged. Tag a series and check whether restricted users are blocked from its episodes before turning this on — it writes one database row per episode.")]
        public bool TagEpisodesAndSeasons { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the Tags field is locked after tagging.
        /// </summary>
        /// <remarks>
        /// The lock is only ever taken once the metadata providers have finished with an item.
        /// Taking it on the ItemAdded path would pre-empt the first refresh and cost the item every
        /// tag its metadata source would have supplied.
        /// </remarks>
        [DisplayName("Lock the Tags field after tagging")]
        [Description("Not recommended. A locked Tags field is skipped by the metadata providers, so the item keeps the tags configured here and gains no others, and it cannot be edited by hand until it is unlocked. Leaving this off is safe: the configured tags are re-applied after any refresh that replaces them.")]
        public bool LockTags { get; set; }
    }
}
