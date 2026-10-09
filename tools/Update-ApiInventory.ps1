[CmdletBinding()]
param([switch]$Download)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$directory = Join-Path $root 'docs/api'
$sources = [ordered]@{
    helix = 'https://dev.twitch.tv/docs/api/reference/'
    eventsub = 'https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/'
    'eventsub-models' = 'https://dev.twitch.tv/docs/eventsub/eventsub-reference/'
    scopes = 'https://dev.twitch.tv/docs/authentication/scopes/'
}
function Plain([string]$html) {
    return [System.Net.WebUtility]::HtmlDecode([regex]::Replace($html, '<[^>]+>', '')).Trim()
}
function Fields([string]$section, [string]$heading) {
    $headingPattern = switch ($heading) {
        'Request Query Parameters' { 'Request Query (?:Parameters?|Paramters)' }
        'Request Body' { 'Request Body(?: Parameters)?' }
        default { [regex]::Escape($heading) }
    }
    $block = [regex]::Match($section, ('(?s)<h3>' + $headingPattern + '</h3>(?:(?!<h3>).)*?(<table.*?</table>)')).Groups[1].Value
    $headers = @([regex]::Matches($block, '(?s)<th[^>]*>(.*?)</th>') | ForEach-Object { Plain $_.Groups[1].Value })
    $typeIndex = [array]::IndexOf($headers, 'Type')
    $requiredIndex = -1
    for ($column = 0; $column -lt $headers.Count; $column++) {
        if ($headers[$column] -match '^Required\s*\??$') { $requiredIndex = $column; break }
    }
    if ($block -and $typeIndex -lt 0) { throw "Missing Type column in $heading" }
    @(
        foreach ($row in [regex]::Matches($block, '(?s)<tr>(.*?)</tr>')) {
            $cells = [regex]::Matches($row.Value, '(?s)<td[^>]*>(.*?)</td>')
            if ($cells.Count -lt 2) { continue }
            $rawName = [System.Net.WebUtility]::HtmlDecode([regex]::Replace($cells[0].Groups[1].Value, '<[^>]+>', ''))
            $field = [ordered]@{ name = $rawName.Trim(); type = Plain $cells[$typeIndex].Groups[1].Value; indent = $rawName.Length - $rawName.TrimStart().Length }
            if ($requiredIndex -ge 0) { $field.required = Plain $cells[$requiredIndex].Groups[1].Value }
            $field
        }
    )
}
foreach ($name in $sources.Keys) {
    $path = Join-Path $directory "$name-reference.html"
    if ($Download) { Invoke-WebRequest -Uri $sources[$name] -OutFile $path }
    if (!(Test-Path -LiteralPath $path)) { throw "Missing $path. Run with -Download." }
}
$helix = Get-Content (Join-Path $directory 'helix-reference.html') -Raw
$headings = [regex]::Matches($helix, '<h2 id="([^"]+)">(.+?)</h2>')
$endpoints = @(
    for ($i = 0; $i -lt $headings.Count; $i++) {
        $heading = $headings[$i]
        $end = if ($i + 1 -lt $headings.Count) { $headings[$i+1].Index } else { $helix.Length }
        $section = $helix.Substring($heading.Index, $end - $heading.Index)
        $url = [regex]::Match($section, '(GET|POST|PUT|PATCH|DELETE) https://api\.twitch\.tv/helix/([^<\s]+)')
        if (!$url.Success) { throw "No URL found for $($heading.Groups[1].Value)" }
        $auth = [regex]::Match($section, '(?s)<h3>Authorization</h3>(.*?)<h3>').Groups[1].Value
        $scopes = @([regex]::Matches((Plain $auth), '\b[a-z]+:(?:[a-z]+:)?[a-z_]+\b') | ForEach-Object Value | Sort-Object -Unique)
        $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($section))).ToLowerInvariant()
        [ordered]@{
            id = $heading.Groups[1].Value
            name = Plain $heading.Groups[2].Value
            method = $url.Groups[1].Value
            path = [System.Net.WebUtility]::HtmlDecode($url.Groups[2].Value)
            source = $sources.helix + '#' + $heading.Groups[1].Value
            documentationFingerprint = $fingerprint
            scopeCandidates = $scopes
            query = @(Fields $section 'Request Query Parameters')
            requestBody = @(Fields $section 'Request Body')
            responseBody = @(Fields $section 'Response Body')
            authorizationReview = 'pending' # Scope candidates may be alternatives; never enforce all blindly.
            availabilityReview = 'pending'
            status = 'inventoried'
            evidence = @()
        }
    }
)
$eventHtml = Get-Content (Join-Path $directory 'eventsub-reference.html') -Raw
$table = [regex]::Match($eventHtml, '(?s)<h1 id="subscription-types">.*?(<table>.*?</table>)').Groups[1].Value
$events = @(
    foreach ($row in [regex]::Matches($table, '(?s)<tr>(.*?)</tr>')) {
        $cells = [regex]::Matches($row.Value, '(?s)<td>(.*?)</td>')
        if ($cells.Count -lt 3) { continue }
        $anchor = [regex]::Match($cells[0].Value, 'href="#([^"]+)"').Groups[1].Value
        [ordered]@{
            id = (Plain $cells[1].Groups[1].Value) + '@' + (Plain $cells[2].Groups[1].Value)
            type = Plain $cells[1].Groups[1].Value
            version = Plain $cells[2].Groups[1].Value
            source = $sources.eventsub + '#' + $anchor
            availabilityReview = if ((Plain $cells[2].Groups[1].Value) -eq 'beta') { 'public-beta' } else { 'pending' }
            status = 'inventoried'
            evidence = @()
        }
    }
)
$scopeHtml = Get-Content (Join-Path $directory 'scopes-reference.html') -Raw
$scopeNames = @(
    [regex]::Matches($scopeHtml, '(?s)<tr>\s*<td[^>]*>(.*?)</td>') |
        ForEach-Object { Plain $_.Groups[1].Value } |
        Where-Object { $_ -match '^[a-z]+(?::[a-z_]+){1,2}$' } |
        Sort-Object -Unique
)
if ($endpoints.Count -lt 100 -or $events.Count -lt 50 -or $scopeNames.Count -lt 30) { throw 'Unexpectedly small inventory: upstream format may have changed.' }
if (($endpoints.id | Sort-Object -Unique).Count -ne $endpoints.Count) { throw 'Duplicate endpoint IDs.' }
if (($events.id | Sort-Object -Unique).Count -ne $events.Count) { throw 'Duplicate event type/version IDs.' }
$outputPath = Join-Path $directory 'coverage.json'
if (Test-Path $outputPath) {
    $previous = Get-Content $outputPath -Raw | ConvertFrom-Json
    foreach ($collection in @('helix','eventsub')) {
        $items = if ($collection -eq 'helix') { $endpoints } else { $events }
        foreach ($item in $items) {
            $old = @($previous.$collection | Where-Object id -EQ $item.id)
            if ($old.Count -eq 1) {
                $item.status = $old[0].status
                $item.evidence = @($old[0].evidence)
                if ($old[0].availabilityReview -ne 'pending') { $item.availabilityReview = $old[0].availabilityReview }
                if ($collection -eq 'helix') { $item.authorizationReview = $old[0].authorizationReview }
                if ($collection -eq 'helix' -and $old[0].documentationFingerprint -and $old[0].documentationFingerprint -ne $item.documentationFingerprint -and $item.status -in @('complete','partial')) {
                    $item.status = 'needs-review'
                    $item.authorizationReview = 'pending'
                    $item.availabilityReview = 'pending'
                }
            }
        }
    }
    foreach ($collection in @('helix','eventsub')) {
        $items = if ($collection -eq 'helix') { $endpoints } else { $events }
        $missing = @($previous.$collection | Where-Object { $_.id -notin $items.id })
        if ($missing.Count -gt 0) { throw "Previously recorded $collection entries disappeared: $($missing.id -join ', '). Review removals manually before updating the pinned inventory." }
    }
}
$manifest = [ordered]@{
    schemaVersion = 1
    retrievedOn = [DateTime]::UtcNow.ToString('yyyy-MM-dd')
    scope = 'Official documentation snapshot; availability and authorization require individual review.'
    sources = @($sources.Keys | ForEach-Object { [ordered]@{ url = $sources[$_]; sha256 = (Get-FileHash (Join-Path $directory "$_-reference.html") -Algorithm SHA256).Hash.ToLowerInvariant() } })
    helix = $endpoints
    eventsub = $events
    scopes = $scopeNames
}
$manifest | ConvertTo-Json -Depth 12 | Set-Content $outputPath -Encoding utf8
Write-Output "Inventoried $($endpoints.Count) Helix endpoints, $($events.Count) EventSub type/version pairs, $($scopeNames.Count) scopes."
