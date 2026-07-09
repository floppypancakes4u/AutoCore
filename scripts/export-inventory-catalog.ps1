param(
    [string]$GamePath = "C:\Program Files (x86)\NetDevil\Auto Assault",
    [string]$OutputPath = "",
    [int]$Port = 8080,
    [switch]$Serve
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$output = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $repoRoot "tools\inventory-catalog\inventory-items.json"
} else {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
}

Write-Host "Exporting inventory catalog..." -ForegroundColor Cyan
Write-Host "  Game path: $GamePath" -ForegroundColor Gray
Write-Host "  Output:    $output" -ForegroundColor Gray

Push-Location $repoRoot
try {
    $args = @(
        "run",
        "--project", "src\AutoCore.Dev\AutoCore.Dev.csproj",
        "--",
        "export-inventory-catalog",
        "--game-path", $GamePath,
        "--output", $output
    )

    dotnet @args
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}

if ($Serve) {
    $catalogDir = Split-Path $output -Parent
    Write-Host ""
    Write-Host "Serving catalog at http://localhost:$Port/" -ForegroundColor Green
    Write-Host "Press Ctrl+C to stop." -ForegroundColor Gray
    Push-Location $catalogDir
    try {
        python -m http.server $Port
    }
    finally {
        Pop-Location
    }
}
