# Emby Auto Tagger

Applies configured tags to media as it is added to selected libraries.

Intended for tag-based parental controls: map a library to a tag, then use that tag in each
user's **Allowed tags** / **Blocked tags** policy.

This is the Emby port of the Jellyfin plugin of the same name. The two are separate projects
rather than one codebase with a shared abstraction layer — see [PORTING-NOTES.md](PORTING-NOTES.md)
for what changed and why.

> **Read this before you build.** Emby Server is closed source. Every API call in this project was
> written against Emby's published plugin API and real community plugin source, but it has **not
> been compiled against Emby's assemblies**, because the reference package could not be restored in
> the environment where it was written. Expect to spend a short round of fixing signature mismatches
> on first build. [PORTING-NOTES.md](PORTING-NOTES.md) lists every call worth checking, ranked by how
> likely it is to need adjusting, and each one is a one-line fix in a known location.

## Configuration

**Dashboard → Plugins → Auto Tagger.** Every library is listed with a text field; enter
comma-separated tags, or leave a library blank to skip it. Tagging is additive — existing tags
are never removed.

New items are tagged as they arrive. To tag items already in a library, run **Apply auto-tags to
existing items** from Dashboard → Scheduled Tasks.

## Building

```bash
dotnet build AutoTagger.sln -c Release
```

Output lands at `AutoTagger/bin/Release/netstandard2.0/AutoTagger.dll`.

By default the build resolves Emby's plugin API from NuGet. To build against the assemblies from an
installed server instead — necessary for a beta, or any version without a matching package:

```bash
dotnet build AutoTagger.sln -c Release -p:EmbyRefMode=local -p:EmbyDir=/opt/emby-server/system
```

### Which API version to build against

`MediaBrowser.Server.Core` is Emby's published plugin API package. Pin it at or below your server's
version, never above:

```bash
dotnet build AutoTagger.sln -c Release -p:EmbyApiVersion=4.9.1.90
```

| Your server | Recommended `EmbyApiVersion` |
| --- | --- |
| 4.8.x | `4.8.11` (the project default) |
| 4.9.x | `4.9.1.90`, or stay on `4.8.11` |
| Beta builds | Use `EmbyRefMode=local` against the installed assemblies |

Emby is more forgiving about this than Jellyfin. A plugin built against 4.8 generally loads on a 4.9
server, whereas a Jellyfin plugin whose `Jellyfin.Controller` version does not match the server is
rejected outright as `NotSupported`. Building against a *newer* package than your server still breaks,
so when in doubt, build low.

## Installing

Copy `AutoTagger.dll` into the `plugins` folder inside Emby's **program data** directory — not the
install directory:

| Platform | Path |
| --- | --- |
| Docker | `/config/plugins/` |
| Linux | `/var/lib/emby/plugins/` |
| Windows | `%AppData%\Emby-Server\programdata\plugins\` |
| Synology | `/volume1/@appstore/EmbyServer/plugins/` |

Restart the server. Unlike Jellyfin, Emby loads plugin DLLs directly out of `plugins/` — there is no
per-plugin subfolder and no metadata file alongside the assembly.

Then configure at **Dashboard → Plugins → Auto Tagger**.

## Distribution

There is no Emby equivalent of the jprm + GitHub Pages workflow used for the Jellyfin build. Emby has
no support for third-party plugin repositories: its catalog is curated by the Emby team and populated
by submission. For personal use, distribute the DLL directly — the included GitHub Actions workflow
builds it on every push and attaches it as an artifact.

## How it works

- `AutoTagEntryPoint` is an `IServerEntryPoint` subscribed to `ILibraryManager.ItemAdded`. Emby
  discovers it automatically; there is nothing to register.
- `Tagger` resolves an item's libraries via `ILibraryManager.GetCollectionFolders`, unions the tags
  from every matching rule, and writes with `UpdateToRepository`.
- `ApplyTagsTask` is a manual scheduled task that backfills existing items.
- Library matching tries the stored id against both the internal row id and the GUID, then falls back
  to the library name. This is deliberate defensiveness about how Emby identifies libraries — see the
  porting notes.

## Things to verify on your server

**Whether episodes inherit series tags for blocking.** This is the crux of the parental control use
case and is worth testing directly rather than assuming: tag a series, log in as a restricted user,
and check whether individual episodes are hidden. If they are not, enable **Also tag seasons and
episodes** — but that writes one row per episode.

**Metadata refresh behaviour.** *Lock the Tags field* is on by default, because a refresh with
"replace existing metadata" can otherwise clear the Tags field. If you would rather manage tags by
hand later, turn it off — a locked field cannot be edited from the UI until it is unlocked.

**`ItemAdded` timing.** As on Jellyfin, tags can take a while to appear in the UI after an item is
added. On the Jellyfin build this looked like a bug and turned out to be latency. Check the server log
for `Tagged <name>` lines before concluding anything is broken.

## License

GPL-3.0, matching the Jellyfin build this was ported from.
