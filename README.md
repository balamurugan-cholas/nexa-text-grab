# Nexa Text Grab

A Windows desktop utility for copying text from anything visible on screen. Press a global shortcut, drag a selection, and paste recognized text into the customer record. OCR is performed locally with Tesseract; selected screen captures are only held in memory and are never saved or uploaded.

## Run it

Open `publish\Nexa Text Grab.exe`. The first run opens the settings window and adds an icon in the notification area. Close the settings window or choose **Hide to tray** to keep the app running quietly.

Default shortcut: **Ctrl + Shift + Space**. The key can be changed in Settings if another program uses it. Right-click the tray icon to select text, open Settings, or exit.

When the overlay appears, drag around the text and release. The result is copied to the clipboard automatically. Press Escape to cancel. English, Hindi, and Tamil OCR models are included. Recognition quality depends on image sharpness, contrast, font size, and script; confirm extracted customer data before saving it.

Use **Start automatically** in Settings to launch after sign-in. Startup is registered for the current Windows user only.

## Publish

Requires the .NET 10 SDK and network access for NuGet restore. From PowerShell:

```powershell
dotnet publish .\NexaTextGrab.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o .\publish
```

Copy the complete `publish` folder to a Windows 10/11 x64 PC. Run `Nexa Text Grab.exe`. The folder includes the .NET runtime, OCR native libraries, and English, Hindi, and Tamil recognition models in `tessdata`.

## Project

- C# / .NET 10 WPF
- Global shortcut and screen capture through Win32 APIs
- Local OCR using Tesseract 5 with English, Hindi, and Tamil data
- Settings saved under `%LOCALAPPDATA%\NexaTextGrab`

This utility copies OCR output directly to the clipboard without storing it in an app log. Windows clipboard history may retain copied text if clipboard history is enabled on the PC; follow the office's customer-data handling policy.
