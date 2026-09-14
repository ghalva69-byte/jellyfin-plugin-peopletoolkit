# Jellyfin Plugin: People Toolkit

A toolkit for managing **People** (actors, cast, tagged individuals...) on any
Jellyfin item — including **Photos**, which don't natively support People at
all in Jellyfin, and Videos.

> ⚠️ **Compatibility**: This plugin targets Jellyfin **10.11.x**. It has not
> been tested against Jellyfin 12.0+, which introduced a new database schema.
> Since this plugin writes directly to internal database tables (see
> [How it works](#how-it-works) below), compatibility with 12.0+ is **not
> guaranteed**. Do not install on a 12.0+ server unless you've verified it
> works.

## Features

The plugin adds a **People Toolkit** entry to your Jellyfin dashboard sidebar
(under the same area as Extensions), with the following tabs:

- **People on photo** — search for a Photo, view/edit its People, or browse
  Photos that have no People assigned yet.
- **People on video** — same as above, for Movies, Episodes, Videos and Music
  Videos.
- **Create** — create a new Person directly, without needing to attach them
  to an item first.
- **Rename** — rename a Person everywhere in the library at once (all
  photos/videos where they appear are updated), including moving their
  cached image.
- **Delete** — permanently delete a Person, everywhere, including their
  cached image.
- **Orphans** — find and clean up:
  - People with no link to any item.
  - Leftover Person entities with no corresponding record at all (can
    happen after bugs, manual database edits, or interrupted operations).
- **Gallery** — browse every Person known to Jellyfin, with their photo and
  name.

On the People chips shown for a photo/video, you can also:
- Hover a chip and click the **×** to remove that person.
- **Drag and drop** chips to reorder people.

## Why this plugin exists

Jellyfin's built-in People system works well for Movies, Episodes, etc., but
**Photos do not support People at all** — the option simply isn't there.
This plugin works around that restriction by writing directly to Jellyfin's
internal People repository instead of going through the normal
`BaseItem.SupportsPeople`-gated API, so you can tag people in your family
photo library the same way you'd tag actors in a movie.

## How it works

Jellyfin normally handles People through `ILibraryManager.UpdatePeople(item,
people)`, which silently does nothing for items where `SupportsPeople` is
`false` (like Photos). To work around this, People Toolkit:

- Writes People assignments directly via the lower-level
  `IPeopleRepository`, bypassing the `SupportsPeople` check.
- Manually replicates the steps Jellyfin's own internal code performs to
  register a Person as a real, searchable library item (including
  correctly computing its internal id, which — depending on your server's
  `EnableNormalizedItemByNameIds` setting — is **not** the same computation
  as the public `ILibraryManager.GetNewItemId` API alone performs).
- Manages the on-disk cached image folder (`metadata/People/<Letter>/<Name>/`)
  directly when renaming or deleting a Person, since that folder is not
  tracked anywhere in the database.

Because of this, the plugin is more tightly coupled to Jellyfin's internal
data model than a typical plugin — which is also why compatibility isn't
guaranteed across major Jellyfin versions (see the compatibility warning
above).

## Installation

### Option A — Plugin repository (once published)

1. Dashboard → Plugins → Repositories → **+**
2. Add repository URL:
   `https://raw.githubusercontent.com/ghalva69-byte/jellyfin-plugin-peopletoolkit/manifest-release/manifest.json`
3. Go to Catalog, find **People Toolkit**, install.
4. Restart Jellyfin.

### Option B — Manual install

1. Download the latest release zip from the
   [Releases page](https://github.com/ghalva69-byte/jellyfin-plugin-peopletoolkit/releases).
2. Extract it into your Jellyfin plugins folder, so you end up with:
   ```
   plugins/
     Jellyfin.Plugin.PeopleToolkit_<version>/
       Jellyfin.Plugin.PeopleToolkit.dll
       meta.json
   ```
3. Restart Jellyfin.

### Option C — Build from source

Requires the .NET 9 SDK.

```bash
git clone https://github.com/ghalva69-byte/jellyfin-plugin-peopletoolkit.git
cd jellyfin-plugin-peopletoolkit
dotnet build --configuration Release
```

The compiled `.dll` will be in
`Jellyfin.Plugin.PeopleToolkit/bin/Release/net9.0/`.

## Disclaimer

This plugin writes directly to Jellyfin's internal database tables and
manipulates files on disk. While it's been tested thoroughly on a personal
Jellyfin instance, **back up your Jellyfin database before installing**, as
with any plugin that operates at this level.

## License

GPLv3 — see [LICENSE](LICENSE).
