$ErrorActionPreference = "Stop"

$exe = Join-Path $PSScriptRoot "VenomDesktop\bin\Debug\net9.0-windows\VenomDesktop.exe"
if (-not (Test-Path $exe)) {
    dotnet build (Join-Path $PSScriptRoot "VenomDesktop\VenomDesktop.csproj")
}

Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
