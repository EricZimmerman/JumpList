# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

A `netstandard2.0` NuGet library that parses Windows jump lists. Its consumers are JLECmd and JumpListExplorer, so keep the public API compatible. It builds on sibling packages: OleCf (container), Lnk (LNK files), ExtensionBlocks (property stores, known folder names) and GuidMapping. Fixes to those formats belong in those repos, followed by a package bump here. Input may be corrupt or hostile.

## Commands

The SDK is pinned in `global.json` (.NET 10). The library targets `netstandard2.0`; tests target `net10.0`. Both use `LangVersion 10`. Tests use NUnit 5 + FluentAssertions 7 (pinned to `[7.0.0,8.0.0)`, since 8.x requires a commercial license).

```bash
dotnet build                                                  # builds JumpList.slnx
dotnet test                                                   # CI runs: dotnet test --configuration Release
dotnet test --filter "FullyQualifiedName~RobustnessTests"     # one fixture
dotnet test --filter "Name=Win10EntryValues"                  # one test
dotnet pack --configuration Release
```

Right after a dependency is published, `dotnet restore --no-http-cache` may be needed before NuGet's version index shows it.

## Architecture

- **`JumpList`** (static): `LoadAutoJumplist`/`LoadCustomJumplist` and the static `AppIdList`. The loaders must not recreate `AppIdList`; that used to discard AppIds added with `LoadAppListFromFile`, which is how JLECmd's `--appIds` works.
- **`AppIdList`**: built-in `Resources/AppIDs.txt` (`"APPID"|"Description"`). It is split on `\n` with `\r` trimmed, so line endings don't matter. Lookups are case-insensitive; a missing id gives "Unknown AppId".
- **Automatic (`Automatic/`)**: `AutomaticDestination` opens the OLE container (OleCf), parses the `DestList` stream (`DestList`, `DestListHeader`, `DestListEntry`) and, for each entry, the LNK in the stream named with the entry number in hex. `DestListPropertyStore` is a 4-byte count followed by a property sheet.
  - DestList entry layout: version 1 (Windows 7/8) is 114 bytes plus the path, with the path size at 112. Version 2+ (Windows 10/11) is 130 bytes plus the path (size at 128), then an Int32 size and that many bytes of property store.
  - `CreatedOn`/`MacAddress` come from the file droid only when it is a time-based (version 1) GUID. Otherwise they are `DestListEntry.NoCreationTime` (1582-10-15) and `NoMacAddress` (all zeros), which JLECmd blanks. Keep these markers.
- **Custom (`Custom/`)**: `CustomDestination` splits the file at each footer `0xBABFFBAB`. The first chunk is the 12 byte file header (version, number of categories, unknown) plus a category; later chunks are just a category. `Entry` is one category: type 0 (name length, UTF-16 name, entry count), 1 (known category id: 1 frequent, 2 recent) or 2 (tasks: entry count). Entries are a 16 byte class id followed by the LNK data. LNKs are found by searching for the LNK header plus class id, and each LNK ends before the next entry's class id. Chunks of 30 bytes or less (known categories) produce no `Entry`. `Unknown0`/`Rank`/`Unknown2` are kept for compatibility (raw bytes 0-11 of the chunk). `CategoryType` (= `HeaderType`) and `Name` are the real fields.

### Error model
- `JumpListParseException` (`Offset`, `Field`) for unusable input: empty, too small, or no custom destinations footer. `OleCfParseException` comes from OleCf for a bad container. Both derive from `Exception`, and the messages ("Empty file", "Invalid signature!", ...) are unchanged.
- Everything else goes to `ParseErrors` (on `AutomaticDestination`, `DestList`, `DestListEntry`, `CustomDestination`, `Entry`), and parsing continues: a DestList entry past the end of the stream stops the DestList; a bad LNK stream gives `Lnk == null`; a bad property store gives `Sps == null`.

## Tests

- `TestMain`: real jump lists in `JumpList.Test/TestFiles/<OS>/` plus `TestFiles/dtformats/`, samples from libyal/dtformats (Apache-2.0, see the README there). dtformats' own tests expect 7, 2 and 9 entries for them.
- `AppIdTests`: built-in count, case-insensitive lookups, AppIds from a file surviving loads. Non-parallelizable, because `AppIdList` is static.
- `RobustnessTests`: in-memory DestLists (versions 1/3/4) and multi-category custom destinations built from the sample LNKs, plus damaged samples and random corruption. Add regression tests here.
- `CorpusTests`: every jump list in a local collection that is not in the repo: the `Jump lists and lnks` folder in `Corpus/` next to the repository (or directly next to it), or `JUMPLIST_CORPUS`. Skipped when it is missing (as in CI); never copy its files into the repo. Known oddities: `Tom/.../9b9cdc69c1c24e2b` is truncated (needs OleCf 2026.10.1+, which returns the partial streams), the `(ExtraDirectories)` sample's header pinned count doesn't match its entries, and 4 empty custom destinations. About 1,300 LNK streams in automatic destinations have data after the LNK's terminal block; that is in the files, and Lnk reports it in the LNK's `ParseErrors`.

## Versioning and release

- MinVer sets the version from the git tag (bare tags like `2026.10.0`); there is no `<Version>` in the csproj.
- To release: push to `master`, then `git tag <ver> && git push origin <ver> && gh release create <ver> --verify-tag --generate-notes`.
- `.github/workflows/publish.yml` (identical to GuidMapping's) packs, validates and tests on PRs and pushes to `master`. When a GitHub Release is published, it pushes to NuGet.org using trusted publishing (`NuGet/login` with the `NUGET_USER` repo variable).
- Dependabot (`.github/dependabot.yml`) opens monthly PRs for GitHub Actions and NuGet updates.
