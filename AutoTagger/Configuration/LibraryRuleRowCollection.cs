using System.Collections.Generic;
using Emby.Web.GenericEdit;
using MediaBrowser.Model.GenericEdit;

namespace AutoTagger.Configuration
{
    /// <summary>
    /// The library rows shown on the settings page.
    /// </summary>
    /// <remarks>
    /// This exists instead of Emby's own <see cref="EditableObjectCollection"/> for one reason: the
    /// element type. <c>EditableObjectCollection</c> is a <c>List&lt;EditableObjectBase&gt;</c>, and
    /// <c>EditableObjectBase</c> is abstract, so nothing can reconstruct one from JSON — there is no
    /// concrete type to instantiate. The settings page is posted back as JSON and deserialized by
    /// <c>EditableObjectBase.DeserializeFromJsonString</c>, which does
    /// <c>serializer.DeserializeFromString(json, GetType()) as IEditableObject</c>; when that fails,
    /// the <c>as</c> yields null and the caller dereferences it. That is the
    /// "Object reference not set to an instance of an object" a save used to produce. The same
    /// applies at start-up, where the options store reloads the file through
    /// <c>DeserializeFromJsonStream</c>.
    /// <para>
    /// Deriving from <c>List&lt;LibraryRuleRow&gt;</c> gives the serializer a concrete element type,
    /// while implementing <see cref="IEditableObjectCollection"/> — which asks only for
    /// <c>IEnumerable&lt;IEditableObject&gt;</c> — keeps the editor rendering one section per row.
    /// Neither of those two methods is overridable, so the fix has to be in the shape of the data.
    /// </para>
    /// </remarks>
    public class LibraryRuleRowCollection : List<LibraryRuleRow>, IEditableObjectCollection
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LibraryRuleRowCollection"/> class.
        /// </summary>
        public LibraryRuleRowCollection()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LibraryRuleRowCollection"/> class.
        /// </summary>
        /// <param name="rows">The rows to start with.</param>
        public LibraryRuleRowCollection(IEnumerable<LibraryRuleRow> rows)
            : base(rows)
        {
        }

        /// <inheritdoc />
        IEnumerator<IEditableObject> IEnumerable<IEditableObject>.GetEnumerator()
        {
            foreach (var row in this)
            {
                yield return row;
            }
        }
    }
}
