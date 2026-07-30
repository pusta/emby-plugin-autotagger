# Jellyfin → Emby porting notes

What changed between `jellyfin-plugin-autotagger` and this project, and what to check first when it
does not compile.

## Verify these first

Ranked by how likely each is to need a fix. Every one is a single line in a single place.

| # | Call | Where | Risk | If it fails |
| --- | --- | --- | --- | --- |
| 1 | `MetadataFields.Tags` | `Tagger.TagItem` | Medium | Emby's enum is `MetadataFields` (plural); Jellyfin renamed it to `MetadataField`. If the compiler cannot find it, try the singular, or drop the locking feature by unchecking *Lock the Tags field*. |
| 2 | `item.UpdateToRepository(ItemUpdateType.MetadataEdit)` | `Tagger.SaveItem` | Medium | Emby publishes several overloads. Alternatives are named in the method's own doc comment: add a `BaseItem parent` argument, or call `_libraryManager.UpdateItem(item, item.GetParent(), ItemUpdateType.MetadataEdit)`. This is the only write call in the project, on purpose. |
| 3 | `folder.InternalId` | `Tagger.MatchesLibrary` | Medium | If `InternalId` is not exposed on your build, delete that clause. The GUID comparison and the name fallback still work. |
| 4 | `IServerEntryPoint.Run()` returning `void` | `AutoTagEntryPoint` | Low-medium | Some Emby builds expose `Task RunAsync()` instead. Change the signature and return `Task.FromResult(true)`. |
| 5 | `InternalItemsQuery.Parent` | `ApplyTagsTask.Execute` | Low | If absent, use `ParentIds` or `AncestorIds` with the folder's id. |
| 6 | `ApiClient.getVirtualFolders()` | `configPage.html` | Low | Already has a fallback to `Library/VirtualFolders` via `getJSON`. If both fail, check the browser console. |

If the library list loads but nothing ever gets tagged, the likeliest cause is #3 — a rule storing an
id in a form that never matches. The name fallback should cover it; if the library was renamed after
the rule was saved, re-save the configuration page.

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

### Configuration page

Same general shape — an embedded HTML resource resolved by
`"<RootNamespace>.Configuration.configPage.html"` — but the wrapper markup differs. Emby pages are a
full HTML document with a `data-role="page"` div carrying
`class="page type-interior pluginConfigurationPage"` and a `data-require` attribute listing the Emby
web components used (`emby-input`, `emby-button`, `emby-checkbox`). The `ApiClient` and `Dashboard`
globals are broadly the same, and jQuery is available in Emby's dashboard, though this page uses
vanilla DOM APIs so it does not depend on that.

`load()` is called both from the `pageshow` event and directly, because depending on how Emby injects
the page the event may already have fired before the inline script runs.

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
| The eight template workflows | Replaced by one `build.yml`. The Jellyfin template's workflows were built around jprm packaging and repository publishing, neither of which applies. |
| `artifacts/` | jprm's output directory, which had to be created by hand before building. |

### Analyzer and style settings

The Jellyfin template imposed `TreatWarningsAsErrors`, `AnalysisMode=AllEnabledByDefault`, StyleCop,
`GenerateDocumentationFile`, one class per file (SA1402), and XML docs on every public member. None of
that carries over — Emby has no template and no house style, and enabling those settings against a
non-annotated closed-source API surface produces noise rather than signal. XML documentation comments
were kept anyway, because they are genuinely useful here for recording *why* a call is written the way
it is.

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
