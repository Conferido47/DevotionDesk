# DevotionDesk

DevotionDesk is a desktop devotional study application that combines a PDF devotional library with an integrated Bible reader, allowing users to read devotionals, explore Scripture, and compare Bible translations within a focused and distraction-free workspace.

## Features

- PDF library (local) with a built-in viewer
- Bible reader with offline downloads for supported translations
- Verse compare mode (side-by-side, 2 translations)

## Build

Requirements:

- .NET SDK 8.x
- WebView2 Runtime (for the PDF viewer)

Build:

```bat
dotnet build "DevotionDesk\DevotionDesk.csproj" -c Release
```

Run:

```bat
dotnet run --project "DevotionDesk\DevotionDesk.csproj"
```

## Data Location

DevotionDesk stores local data under:

- `%LOCALAPPDATA%\DevotionDesk\`

## License

MIT (see `LICENSE`).

## Third-Party Notices

See `THIRD_PARTY_NOTICES.md`.

## Installer / Releases

When a version tag like `v0.1.0` is pushed, GitHub Actions builds:

- A portable zip (self-contained)
- A Windows installer (Inno Setup)

You can download them from the GitHub Releases page.
