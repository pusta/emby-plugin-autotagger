using System.ComponentModel;
using Emby.Web.GenericEdit;

namespace AutoTagger.Configuration
{
    /// <summary>
    /// One library's row in the settings page: the library it belongs to, and the tags typed
    /// against it.
    /// </summary>
    /// <remarks>
    /// This is the editor's view of a rule, not the stored form. Emby's generic edit framework
    /// renders one section per item in an <see cref="EditableObjectCollection"/>, titled by
    /// <see cref="EditorTitle"/> — which is why the library name is carried on the row itself.
    /// The rows are rebuilt from the current library list every time the page is opened, and
    /// converted back to <see cref="LibraryTagRule"/> on save; see <c>Plugin.OnBeforeShowUI</c>
    /// and <c>Plugin.OnOptionsSaving</c>.
    /// </remarks>
    public class LibraryRuleRow : EditableOptionsBase
    {
        /// <inheritdoc />
        public override string EditorTitle
        {
            get { return string.IsNullOrEmpty(LibraryName) ? "Library" : LibraryName; }
        }

        /// <summary>
        /// Gets or sets the library id this row belongs to. Not shown: the row is titled with the
        /// library name, and the id is only meaningful to the server.
        /// </summary>
        [Browsable(false)]
        public string LibraryId { get; set; }

        /// <summary>
        /// Gets or sets the library display name. Not shown as a field because it is the row's
        /// own heading.
        /// </summary>
        [Browsable(false)]
        public string LibraryName { get; set; }

        /// <summary>
        /// Gets or sets the comma-separated tags applied to items in this library.
        /// </summary>
        [DisplayName("Tags to apply")]
        [Description("Comma-separated. Leave empty to skip this library entirely. Existing tags are never removed.")]
        public string Tags { get; set; }

        /// <summary>
        /// Gets or sets the comma-separated tags that suppress this library's rule.
        /// </summary>
        [DisplayName("Skip items already tagged")]
        [Description("Comma-separated. An item already carrying any of these tags is left alone. Leave empty to tag everything in this library.")]
        public string ExcludeTags { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="LibraryRuleRow"/> class.
        /// </summary>
        public LibraryRuleRow()
        {
            LibraryId = string.Empty;
            LibraryName = string.Empty;
            Tags = string.Empty;
            ExcludeTags = string.Empty;
        }
    }
}
