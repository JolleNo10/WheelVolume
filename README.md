# WheelVolume

A small Windows utility that adjusts system volume using the mouse wheel.

## Features
- Adjust system volume with a modifier key and the mouse wheel
- Toggle mute with the modifier key and middle mouse button
- On-screen volume indicator (OSD)
- Optional start with Windows

## Requirements
- Windows 10 / 11
- Portable release: no .NET install required
- Normal release: .NET 8 Windows Desktop Runtime

## Downloads
- `WheelVolume-v1.0.0-portable-win-x64.zip`: portable build with the .NET runtime included
- `WheelVolume-v1.0.0-win-x64.zip`: normal build that requires the .NET 8 Windows Desktop Runtime

## Security Note
Windows may show a warning when running WheelVolume because the executable is not code-signed. If you are unsure about a download, scan it with a service such as VirusTotal or build the app yourself from the open source code in this repository.

## Build
From the project root, run:

```powershell
dotnet build -c Release
```

Building from source requires the .NET 8 SDK.

## Test

```powershell
dotnet run --project .\WheelVolume.Tests\WheelVolume.Tests.csproj -c Release
```

## Usage
- Hold the configured modifier key and scroll the mouse wheel to change volume.
- Hold the configured modifier key and click the middle mouse button to toggle mute.
- Right-click the tray icon to change settings or exit.

The default modifier key is Left Alt.

If you enable `Start with Windows` from a portable build, extract WheelVolume to its final folder first. Windows stores the exact executable path.

## Screenshots

![Volume step menu](docs/screenshots/volume-step-menu.png)

![OSD timeout menu](docs/screenshots/osd-settings-menus.png)

![OSD screen menu](docs/screenshots/osd-screen-menu.png)

![Modifier key menu](docs/screenshots/modifier-key-menu.png)

![Osd](docs/screenshots/osd.png)

## Publish

Portable release, with the .NET runtime included:

```powershell
dotnet publish .\WheelVolume\WheelVolume.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\release\WheelVolume-portable-win-x64
```

Normal release, requiring the .NET 8 Windows Desktop Runtime:

```powershell
dotnet publish .\WheelVolume\WheelVolume.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\release\WheelVolume-win-x64
```

For a different release version, override version metadata on both publish commands:

```powershell
dotnet publish .\WheelVolume\WheelVolume.csproj -c Release -r win-x64 --self-contained false -p:Version=1.0.1 -p:FileVersion=1.0.1.0 -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\release\WheelVolume-win-x64
```

## Run

After building or publishing, run the published executable:

```powershell
.\release\WheelVolume-win-x64\WheelVolume.exe
```

Or run directly from the build output for quick testing:

```powershell
.\bin\Debug\net8.0-windows\WheelVolume.exe
```

## Contributing
PRs and issues are welcome. Keep changes small and focused.

## License
No license specified. Add a `LICENSE` file if you want to set one.
