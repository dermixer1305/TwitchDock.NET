[CmdletBinding()]
param([switch]$RequireComplete)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$matrix = Get-Content (Join-Path $root 'docs/api/coverage.json') -Raw | ConvertFrom-Json
$valid = @('inventoried', 'partial', 'complete', 'excluded', 'needs-review')
$incomplete = 0
foreach ($collection in @('helix','eventsub')) {
    $items = @($matrix.$collection)
    if (($items.id | Sort-Object -Unique).Count -ne $items.Count) { throw "Duplicate $collection IDs." }
    foreach ($item in $items) {
        if ($item.status -notin $valid) { throw "Invalid status: $($item.id)" }
        if ($item.source -notlike 'https://dev.twitch.tv/*') { throw "Missing official source: $($item.id)" }
        foreach ($evidence in $item.evidence) {
            $full = [IO.Path]::GetFullPath((Join-Path $root $evidence))
            if (!$full.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $full -PathType Leaf)) { throw "Invalid evidence path: $evidence" }
        }
        if ($item.status -eq 'complete') {
            if ($item.availabilityReview -eq 'pending' -or ($collection -eq 'helix' -and $item.authorizationReview -eq 'pending')) { throw "Unreviewed API marked complete: $($item.id)" }
            foreach ($prefix in @('src/','tests/','docs/')) {
                if (!@($item.evidence | Where-Object { $_.StartsWith($prefix) }).Count) { throw "Missing $prefix evidence for $($item.id)" }
            }
        }
        if ($item.status -eq 'excluded' -and !$item.exclusionReason) { throw "Excluded API lacks a reason: $($item.id)" }
        if ($item.status -notin @('complete','excluded')) { $incomplete++ }
    }
    $counts = $items | Group-Object status | ForEach-Object { "$($_.Name)=$($_.Count)" }
    Write-Output "$collection ($($items.Count)): $($counts -join ', ')"
}
if ($RequireComplete -and $incomplete -gt 0) { throw "$incomplete API entries have not met the definition of done. A stable release is blocked." }
