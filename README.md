# DevotionDesk

DevotionDesk is a Windows WPF app for reading devotionals (PDF) and reading/comparing Bible translations.

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
