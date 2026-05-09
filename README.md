# Venom Desktop

Venom Desktop is an audio-reactive Windows desktop creature. It captures system
audio, lives in a transparent desktop window, and reacts to music with liquid
body motion instead of acting like a normal equalizer.

## Project Layout

- `VenomDesktop/` - WPF desktop app.
- `docs/` - static landing page for GitHub Pages.
- `dist/` - local release builds. This directory is ignored by git.
- `run_desktop.ps1` - starts the desktop app during development.
- `stop_desktop.ps1` - stops the desktop app during development.

## Development

Build the Windows app:

```powershell
dotnet build .\VenomDesktop\VenomDesktop.csproj
```

Run it locally:

```powershell
.\run_desktop.ps1
```

Stop it:

```powershell
.\stop_desktop.ps1
```

Preview the website:

```powershell
node serve.mjs
```

Then open:

```text
http://127.0.0.1:4173/docs/
```

## Release

The public download button on the website points to:

```text
docs/downloads/VenomDesktop-win-x64.zip
```

For a more formal release flow later, move this file to GitHub Releases and
change the website link back to the latest Release asset.

## GitHub Pages

Use the `docs/` directory as the GitHub Pages source:

1. Open repository settings.
2. Go to `Pages`.
3. Set source to `Deploy from a branch`.
4. Set branch to `main` and folder to `/docs`.
4. Save.

The Buy Me a Coffee link in `docs/index.html` is currently a placeholder and
should be replaced with the real creator profile URL before publishing broadly.
