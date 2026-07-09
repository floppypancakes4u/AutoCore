param(
    [string]$CoverageFile = (Get-ChildItem -Path "$PSScriptRoot\..\TestResults" -Recurse -Filter coverage.cobertura.xml -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName,
    [double]$MinimumRate = 80.0
)

if ([string]::IsNullOrWhiteSpace($CoverageFile) -or -not (Test-Path $CoverageFile)) {
    Write-Error "Coverage file not found. Run dotnet test with --collect:`"XPlat Code Coverage`" first."
    exit 1
}

[xml]$xml = Get-Content $CoverageFile
$game = $xml.coverage.packages.package | Where-Object { $_.name -eq 'AutoCore.Game' }

$scopePatterns = @(
    '*\Inventory\InventoryCommandService.cs',
    '*\Inventory\InventoryRuntime.cs',
    '*\Inventory\InventoryCoidCounter.cs',
    '*\Inventory\CharacterInventoryItem.cs',
    '*\Inventory\InventoryCommandResult.cs',
    '*\Inventory\InventoryManager.cs',
    '*\Inventory\InventoryItemExporter.cs',
    '*\Packets\Sector\InventoryAddItemResponsePacket.cs',
    '*\Entities\ClonedObjectBase.cs',
    '*\Chat\ChatCommandService.cs'
)

$scopeClassNames = @(
    'AutoCore.Game.Inventory.InventoryCommandService',
    'AutoCore.Game.Inventory.InventoryRuntime',
    'AutoCore.Game.Inventory.InventoryCoidCounter',
    'AutoCore.Game.Inventory.CharacterInventoryItem',
    'AutoCore.Game.Inventory.InventoryCommandResult',
    'AutoCore.Game.Inventory.InventoryManager',
    'AutoCore.Game.Inventory.InventoryItemExporter',
    'AutoCore.Game.Packets.Sector.InventoryAddItemResponsePacket',
    'AutoCore.Game.Entities.ClonedObjectBase',
    'AutoCore.Game.Chat.ChatCommandService'
)

$scoped = @($game.classes.class | Where-Object {
    $class = $_
    $class.name -notmatch '[<>]' -and (
        ($scopeClassNames -contains $class.name) -or
        (($scopePatterns | Where-Object { $class.filename -like $_ }).Count -gt 0)
    )
})

$lines = @($scoped | ForEach-Object { $_.lines.line })
$covered = @($lines | Where-Object { [int]$_.hits -gt 0 })
$rate = if ($lines.Count -gt 0) { [math]::Round(100.0 * $covered.Count / $lines.Count, 2) } else { 0 }

Write-Output "Coverage file: $CoverageFile"
Write-Output "Item-stacks scoped classes: $($scoped.Count)"
Write-Output "Lines valid: $($lines.Count)"
Write-Output "Lines covered: $($covered.Count)"
Write-Output "Item-stacks line coverage: $rate%"

$perFile = @($scoped | Group-Object filename | ForEach-Object {
    $fileLines = @($_.Group | ForEach-Object { $_.lines.line })
    $fileCovered = @($fileLines | Where-Object { [int]$_.hits -gt 0 }).Count
    [PSCustomObject]@{
        File = $_.Name
        Lines = $fileLines.Count
        Covered = $fileCovered
        Rate = if ($fileLines.Count -gt 0) { [math]::Round(100.0 * $fileCovered / $fileLines.Count, 1) } else { 100 }
    }
} | Sort-Object Rate)

Write-Output 'Per-file breakdown:'
$perFile | ForEach-Object { Write-Output ("  {0}: {1}% ({2}/{3})" -f $_.File, $_.Rate, $_.Covered, $_.Lines) }

if ($rate -lt $MinimumRate) {
    Write-Error "Item-stacks coverage $rate% is below minimum $MinimumRate%."
    exit 1
}

Write-Output "Item-stacks coverage gate passed ($rate% >= $MinimumRate%)."
exit 0
