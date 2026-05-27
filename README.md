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

## Build
From the project root, run:

```powershell
dotnet build -c Release
```

Building from source requires the .NET 8 SDK.

## Usage
- Hold the configured modifier key and scroll the mouse wheel to change volume.
- Hold the configured modifier key and click the middle mouse button to toggle mute.
- Right-click the tray icon to change settings or exit.

The default modifier key is Left Alt.

If you enable `Start with Windows` from a portable build, extract WheelVolume to its final folder first. Windows stores the exact executable path.

## Publish

Portable release, with the .NET runtime included:

```powershell
dotnet publish .\WheelVolume\WheelVolume.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\release\WheelVolume-portable-win-x64
```

Normal release, requiring the .NET 8 Windows Desktop Runtime:

```powershell
dotnet publish .\WheelVolume\WheelVolume.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\release\WheelVolume-win-x64
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
