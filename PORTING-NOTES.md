# Jellyfin → Emby porting notes

What differs between `jellyfin-plugin-autotagger` and this project, and why.

## API surface: verified

Everything below was checked against the real `MediaBrowser.Server.Core` assemblies (4.8.11 and
4.9.1.90). The project compiles clean against both. Kept as a record of the calls that were in doubt
during the port, since they are the ones most likely to move in a future Emby release.

| Call | Where | Status |
| --- | --- | --- |
| `MetadataFields.Tags` | `Tagger.Apply` | Present. Emby's enum is `MetadataFields` (plural); Jellyfin renamed it to `MetadataField`. |
| `item.UpdateToRepository(ItemUpdateType.MetadataEdit)` | `Tagger.SaveItem` | Present, along with three other overloads named in the method's doc comment. This is the only write call in the project, on purpose. |
| `folder.InternalId` | `Tagger.MatchesLibrary` | Present, `long`. The GUID and name comparisons are still checked alongside it. |
| `IServerEntryPoint.Run()` returning `void` | `AutoTagEntryPoint` | Correct — the interface declares exactly `void Run()`. |
| `InternalItemsQuery.Parent` | `ApplyTagsTask` | Present, and not a plain property: its setter assigns `ParentIds` from the folder's internal id, so `Parent` + `Recursive` really does scope the query to one library. |
| `ILibraryManager.ItemUpdated` | `AutoTagEntryPoint` | Present, with `ItemChangeEventArgs.UpdateReason` carrying the `ItemUpdateType`. |
| `ApiClient.getVirtualFolders()` | `configPage.html` | Not verifiable from a build. Falls back to `Library/VirtualFolders` via `getJSON`; if both fail, check the browser console. |

One compile-time dependency is not obvious: Emby's `ILogger` publishes a `ReadOnlyMemory<char>`
overload of every log method, which on `netstandard2.0` lives in `System.Memory`. Without that
package reference the compiler cannot resolve *any* call to `Info`/`Warn`/`Error`, not only the ones
that would bind to it. The reference is compile-time only (`ExcludeAssets="runtime"`); the server
supplies the assembly.

If the library list loads but nothing ever gets tagged, the likeliest cause is a rule storing a
library id in a form that never matches. The name fallback should cover it; if the library was
renamed after the rule was saved, re-save the configuration page.

## Structural changes

### Target framework

`net9.0` → `netstandard2.0`. Emby loads plugins as .NET Standard 2.0 assemblies regardless of the
runtime the server itself is on. This ripples outward: no collection expressions, no `Array.Empty` in
places where `new string[0]` reads more plainly across older tooling, and no file-scoped namespaces
(braced namespaces are used throughout for maximum tooling compatibility).

### Package reference

`Jellyfin.Controller` + `Jellyfin.Model` → `MediaBrowser.Server.Core`. One package instead of two, and
it pulls `MediaBrowser.Common` transitively. Version matching is a soft constraint on Emby rather than
the hard `NotSupported` rejection Jellyfin applies.

### Event listener: `IHostedService` → `IServerEntryPoint`

This is the largest single change and it runs in the opposite direction from what you might expect.
Jellyfin *removed* `IServerEntryPoint` in 10.9 in favour of `IHostedService`; Emby still uses
`IServerEntryPoint`, and it is the correct choice there. `Run()` is synchronous and is called once at
start-up; the instance stays alive for the life of the server, and `Dispose()` unsubscribes.

### Dependency injection: gone

`PluginServiceRegistrator.cs` was **deleted** and has no equivalent. Jellyfin's
`IPluginServiceRegistrator` lets a plugin add its own types to the host container. Emby instead
performs automatic type discovery: it finds classes implementing its own interfaces
(`IServerEntryPoint`, `IScheduledTask`, `IRestfulService`, …) and constructs them, resolving their
constructor arguments from its container.

The consequence is that Emby will not resolve *plugin-owned* types. `Tagger` cannot be injected, so
both `AutoTagEntryPoint` and `ApplyTagsTask` construct their own instance. `Tagger` is stateless
apart from its two injected dependencies, so two instances cost nothing.

### Logging

`Microsoft.Extensions.Logging.ILogger<T>` → `MediaBrowser.Model.Logging.ILogger`, obtained by
injecting `ILogManager` and calling `GetLogger(name)`. Emby's logger uses `Info` / `Debug` / `Warn` /
`Error` / `ErrorException` with `string.Format`-style positional placeholders (`{0}`, `{1}`), not the
named structured-logging placeholders Jellyfin uses (`{ItemName}`). Note the argument order of
`ErrorException(message, exception, params)` — the exception comes second, which is easy to get wrong.

### Scheduled task interface

Emby's `IScheduledTask` differs from Jellyfin's in three ways:

| | Jellyfin | Emby |
| --- | --- | --- |
| Execute method | `ExecuteAsync(IProgress<double>, CancellationToken)` | `Execute(CancellationToken, IProgress<double>)` |
| Default triggers | `GetDefaultTriggers()` on the same interface | same, but `TaskTriggerInfo` lives in `MediaBrowser.Model.Tasks` |
| Task key | `Key` | `Key`, plus `Category` is a free-text string rather than a localized constant |

Both the name and the argument order of the execute method changed, so this will not compile by
accident — which is the good outcome.

### Settings page: no HTML at all

The largest difference, and the one that took three attempts to get right. Jellyfin serves a
configuration page as an HTML document with an inline `<script>`. **Do not port that page to Emby.**
On Emby 4.9 a hand-written page renders behind the dashboard and garbled, and its script never runs.

Emby generates the settings UI from a C# class instead:

| | Jellyfin | Emby |
| --- | --- | --- |
| Plugin base | `BasePlugin<PluginConfiguration>` + `IHasWebPages` | `BasePluginSimpleUI<PluginOptions>` |
| Settings model | `BasePluginConfiguration` | `EditableOptionsBase` |
| UI | `configPage.html`, hand-written | Generated by the server from the model's properties |
| Labels | HTML | `[DisplayName]` / `[Description]` |
| Hiding a field | omit it from the HTML | `[Browsable(false)]` |
| Repeating rows | markup built in JS | `EditableObjectCollection` of child objects |
| Populate before display | `pageshow` handler | `OnBeforeShowUI(options)` |
| Intercept a save | form submit handler | `OnOptionsSaving(options)` |
| Config access elsewhere | `Plugin.Instance.Configuration` | `GetOptions()`, wrapped here as a public `Configuration` property |

What was tried first, so it is not tried again:

1. **Full HTML document with `data-role="page"` and an inline script** — the pre-4.x style. Renders
   behind the current page and garbled on 4.9. Note that plenty of third-party plugins still ship
   this and appear to work on older servers, so copying an arbitrary plugin is not safe.
2. **Fragment with `is="emby-scroller"` and `data-controller="__plugin/<name>"`, script in a second
   embedded resource** — the style Emby's own [Anime plugin](https://github.com/MediaBrowser/Emby.Plugins.Anime/tree/master/MediaBrowser.Plugins.Anime/Configuration)
   uses. Also failed on 4.9, with the same symptoms.
3. **`BasePluginSimpleUI`** — what the project uses now, and what
   [Emby's own documentation](https://dev.emby.media/doc/plugins/ui/index.html) recommends, in as
   many words: custom pages "got broken quite too often, either visually or sometimes even
   functionally due to breaking changes in Emby Server".

The working reference for the declarative route is [StrmAssistant](https://github.com/sjtuross/StrmAssistant/tree/HEAD/StrmAssistant/Options),
a current plugin that uses it against 4.8 and 4.9.

#### Do not use `EditableObjectCollection` for a repeating list

Emby's documentation points at `EditableObjectCollection` for "a dynamic number of child options",
and it renders correctly — but nothing can read it back. It is a `List<EditableObjectBase>`, and
`EditableObjectBase` is abstract, so the serializer has no concrete type to construct:

```
NotSupportedException: Deserialization of interface or abstract types is not supported.
Type 'Emby.Web.GenericEdit.EditableObjectBase'. Path: $.LibraryRules[0]
```

That matters because the whole options object is round-tripped through JSON twice: the settings page
posts it back to `EditableObjectBase.DeserializeFromJsonString`, and the options store reloads it at
start-up through `DeserializeFromJsonStream`. Both do
`serializer.DeserializeFromString(json, GetType()) as IEditableObject` — and when the deserialize
fails, the `as` produces null, which the caller dereferences. In the dashboard that surfaces as
**"Object reference not set to an instance of an object"** when you press Save. Neither method is
virtual in any useful sense (they satisfy an interface and cannot be overridden), so the fix has to
be in the shape of the data rather than in a hook.

`LibraryRuleRowCollection` is that fix: it derives from `List<LibraryRuleRow>` — a concrete element
type the serializer can construct — and implements `IEditableObjectCollection`, which asks only for
`IEnumerable<IEditableObject>`. The editor renders it identically, because that interface is what the
editor builder keys on.

One wrinkle to know about: the collection then implements `IEnumerable<T>` twice, so LINQ over it
needs the element type pinned (`RuleRows.ToRules` takes `IEnumerable<LibraryRuleRow>`, which resolves
it at the call site).

Two consequences worth knowing:

- **The rows are display state, not storage.** `PluginOptions.Rules` — a plain `LibraryTagRule[]`,
  `[Browsable(false)]` — is what the tagger reads. `OnBeforeShowUI` builds one row per library from
  it, and `OnOptionsSaving` folds the edited rows back. Storing the plain array rather than the
  collection keeps the plugin's actual data independent of how the edit framework round-trips the
  editor surface, and lets `RuleRows` be tested without a server. If a save ever arrives with no
  rows at all, `OnOptionsSaving` keeps the stored rules and logs an error rather than wiping them.
- **Settings do not migrate.** `BasePluginSimpleUI` persists through its own options store, not as
  the `AutoTagger.xml` that `BasePlugin<TConfiguration>` wrote. Anything saved by an earlier build
  of this plugin has to be entered again.

### Plugin icon

Jellyfin took the icon from the repository manifest (the `imagePath` / `image` asymmetry that came up
while setting up jprm). Emby reads it from the plugin itself: implement `IHasThumbImage`, return an
`ImageFormat`, and stream an embedded resource. `thumb.png` is embedded and served from `GetThumbImage()`.

### Plugin id

New GUID: `ddd2f8f0-cd06-41a9-a5c2-2b6e54160596`.

The Jellyfin build's id is `a9a34b1d-01e4-4156-8083-6cbd7dd86975`. Reusing it would have worked, since
the two never run on the same server, but a distinct id keeps the two plugins unambiguous in the
config files and logs on your bench where both servers exist. If you would rather they share one
identity, change `_id` in `Plugin.cs` — before the first install, because the id is baked into the
configuration file name and cannot be changed afterwards without orphaning the saved settings.

### Files that no longer exist

| Dropped | Reason |
| --- | --- |
| `Directory.Build.props` | Owned by jprm on the Jellyfin side. Nothing owns it here, and folding the two properties it carried into the csproj removes a file that looks hand-editable but was not. |
| `build.yaml` | jprm's manifest and single source of truth for version and metadata. Emby has no packaging manifest; version lives in the csproj. |
| `jellyfin.ruleset` | StyleCop ruleset from the Jellyfin template. |
| `PluginServiceRegistrator.cs` | No DI registration hook on Emby. |
| The eight template workflows | Dropped entirely — there is no CI in this repository. The Jellyfin template's workflows were built around jprm packaging and repository publishing, neither of which applies, and Emby has no third-party plugin repository to publish to. |
| `artifacts/` | jprm's output directory, which had to be created by hand before building. |

### Analyzer and style settings

The Jellyfin template imposed `TreatWarningsAsErrors`, `AnalysisMode=AllEnabledByDefault`, StyleCop,
`GenerateDocumentationFile`, one class per file (SA1402), and XML docs on every public member. None of
that carries over — Emby has no template and no house style, and enabling those settings against a
non-annotated closed-source API surface produces noise rather than signal. XML documentation comments
were kept anyway, because they are genuinely useful here for recording *why* a call is written the way
it is.

### Tag merge semantics: the one behavioural difference

This is the only place where the same configuration produces genuinely different behaviour on the
two servers, and it is the server's doing, not the plugin's.

Jellyfin unions provider tags with the ones already on the item. Emby's `MergeBaseItemData` does not:

```csharp
if (!lockedFields.Contains(MetadataFields.Tags))
{
    if (replaceData || target.Tags.Length == 0)
    {
        target.Tags = source.Tags;
    }
}
```

So on Emby a refresh either replaces the tag set wholesale (`replaceData`, i.e. "replace all
metadata") or writes provider tags only when the item has none at all. Two consequences:

1. A replace-all refresh discards the configured tags. The `ItemUpdated` handler is what puts them
   back, and without it tagging would silently come undone. On Jellyfin that handler is a
   nice-to-have; here it is load-bearing.
2. Because the plugin tags an item at `ItemAdded` — before its first refresh — the item usually has
   a non-empty tag set by the time the providers merge, so an ordinary refresh will not add provider
   tags even with the field unlocked. The **Lock the Tags field** option therefore costs less on
   Emby than on Jellyfin, but it still blocks hand-editing tags in the web UI, so it stays off by
   default for parity.

### Event handling and the ordering hazard

`AutoTagEntryPoint` mirrors the Jellyfin build's two-event design (`ItemAdded` for arrival,
`ItemUpdated` for after the providers have run, locking only ever on the latter). Three details are
Emby-specific:

- **Re-entrancy.** Emby raises `ItemUpdated` from inside `LibraryManager.UpdateItems`, synchronously,
  once per item in the batch. The plugin's own write goes through the same path with
  `ItemUpdateType.MetadataEdit`, which the handler's reason mask excludes — that mask is what stops
  the handler from calling itself forever, exactly as on Jellyfin.
- **Threading.** Both events are raised in-line on the scan thread. The work is pushed onto the
  thread pool, which also gets the write out from under the `UpdateItems` call that raised the event.
- **`Task.Run` without `await`.** Jellyfin's handler discards the task with `_ = Task.Run(...)`;
  the same is done here, and the lambda is synchronous because Emby's write API is.

### Exclusion tags

`LibraryTagRule.ExcludeTags` behaves identically on both platforms. The only Emby-specific concern is
that `XmlSerializer` does not run the constructor's initializer for a field that is absent from an
existing configuration file, so a rule saved by an earlier build deserializes with `ExcludeTags`
null. Every read of it is null-tolerant.

## What did not change

The interesting part of the plugin is platform-agnostic and reads almost identically in both projects:
resolve an item's collection folders, union the tags from every matching rule, diff against existing
tags, write only if something is missing, optionally lock the field. The `IsTaggable` filter is shared
between the live event handler and the backfill task in both builds, so the two can never disagree
about scope.

This is why the two-sibling-projects approach was the right call over a shared `Core` abstraction. The
shared logic is perhaps eighty lines. Everything around it — the host lifecycle, DI, logging, task
interface, and write API — differs, and an abstraction layer would have had to model all of it to
share those eighty lines.
