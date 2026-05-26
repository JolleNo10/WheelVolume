# WheelVolume

A small Windows utility that adjusts system volume using the mouse wheel.

## Features
- Adjust system volume with the mouse wheel
- On-screen volume indicator (OSD)

## Requirements
- .NET 8 SDK (or newer)
- Windows 10 / 11

## Build
From the project root, run:

```powershell
dotnet build -c Release
```

## Publish (self-contained x64)

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./bin/Release/net8.0-windows/win-x64/publish
```

## Run

After building or publishing, run the published executable:

```powershell
.\bin\Release\net8.0-windows\win-x64\publish\WheelVolume.exe
```

Or run directly from the build output for quick testing:

```powershell
.\bin\Debug\net8.0-windows\WheelVolume.exe
```

## Contributing
PRs and issues are welcome. Keep changes small and focused.

## License
No license specified. Add a `LICENSE` file if you want to set one.
