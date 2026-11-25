[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$matrix = Get-Content (Join-Path $root 'docs/api/coverage.json') -Raw | ConvertFrom-Json
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('// Generated from docs/api/coverage.json by tools/Update-ScopeConstants.ps1.')
$lines.Add('// Includes documented legacy scopes; a constant does not imply transport availability.')
$lines.Add('namespace TwitchDock.Core;')
$lines.Add('')
$lines.Add('public static class TwitchScopes')
$lines.Add('{')
# Scopes that left the official scope table. Their constants stay, marked [Obsolete], until a major release removes them (docs/releases.md#versioning).
$retired = [ordered]@{
    'whispers:read' = 'Twitch removed whispers:read from the scope reference in November 2026. Use user:read:whispers or user:manage:whispers.'
}
foreach($scope in $retired.Keys) {
    if($matrix.scopes -ccontains $scope) { throw "Retired scope $scope is documented again; remove it from the retired list." }
}
$names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$all = [string[]]@($matrix.scopes) + [string[]]@($retired.Keys)
[Array]::Sort($all, [StringComparer]::Ordinal)
foreach($scope in $all) {
    if($scope -notmatch '^[a-z]+(?::[a-z_]+){1,2}$') { throw "Invalid scope syntax: $scope" }
    $name = ($scope -split '[:_]' | ForEach-Object { $_.Substring(0,1).ToUpperInvariant() + $_.Substring(1) }) -join ''
    if(!$names.Add($name)) { throw "Duplicate constant: $name" }
    if($retired.Contains($scope)) { $lines.Add(('    [Obsolete("{0}")]' -f $retired[$scope])) }
    $lines.Add(('    public const string {0} = "{1}";' -f $name, $scope))
}
$lines.Add('}')
Set-Content (Join-Path $root 'src/TwitchDock.Core/TwitchScopes.cs') ($lines -join "`n") -Encoding utf8
Write-Output "Generated $($all.Count) scope constants ($($retired.Count) retired)."
