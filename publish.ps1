$ErrorActionPreference = 'Stop'
dotnet publish "$PSScriptRoot\NexaTextGrab.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -o "$PSScriptRoot\publish"
