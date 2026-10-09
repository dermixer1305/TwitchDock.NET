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
$names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach($scope in $matrix.scopes) {
    if($scope -notmatch '^[a-z]+(?::[a-z_]+){1,2}$') { throw "Invalid scope syntax: $scope" }
    $name = ($scope -split '[:_]' | ForEach-Object { $_.Substring(0,1).ToUpperInvariant() + $_.Substring(1) }) -join ''
    if(!$names.Add($name)) { throw "Duplicate constant: $name" }
    $lines.Add(('    public const string {0} = "{1}";' -f $name, $scope))
}
$lines.Add('}')
Set-Content (Join-Path $root 'src/TwitchDock.Core/TwitchScopes.cs') ($lines -join "`n") -Encoding utf8
Write-Output "Generated $($matrix.scopes.Count) scope constants."
