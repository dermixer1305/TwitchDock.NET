[CmdletBinding()]
param([switch]$Download)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$directory = Join-Path $root 'docs/api'
# The API and EventSub references are split into one page per resource, listed by an index page. Twitch publishes a
# Markdown copy of every page (no site chrome, no build stamps), so the importer reads those. The scopes page keeps its
# HTML table.
$site = 'https://dev.twitch.tv/docs/'
$references = [ordered]@{
    helix = 'api/reference'
    eventsub = 'eventsub/reference'
}
$scopesUrl = $site + 'authentication/scopes/'
# Stored in the manifest; fingerprints are only comparable when both runs computed them the same way.
$fingerprintFormat = 'markdown-section'
function Plain([string]$html) {
    return [System.Net.WebUtility]::HtmlDecode([regex]::Replace($html, '<[^>]+>', '')).Trim()
}
function ReadText([string]$path) {
    # Git may check the pages out with CRLF line endings; hashes and parsing must not depend on that.
    return (Get-Content -LiteralPath $path -Raw -Encoding utf8) -replace "`r`n", "`n"
}
function Sha([string]$text) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))).ToLowerInvariant()
}
function PageHash([string]$path) {
    # Every site rebuild restamps the publish dates in an HTML page head; drop them so only content changes alter the hash.
    $text = ReadText $path
    $text = [regex]::Replace($text, '<meta property="article:[a-z_]+_time" content="[^"]*">', '')
    $text = [regex]::Replace($text, '"date(?:Modified|Published)":"[^"]*"', '')
    return Sha $text
}
function IndexPath([string]$name) { Join-Path $directory "$name-reference.md" }
function PagePath([string]$name, [string]$page) { Join-Path $directory "$name/$page.md" }
function IndexEntries([string]$name) {
    $text = ReadText (IndexPath $name)
    $pattern = '\]\(' + [regex]::Escape($site + $references[$name]) + '/([^/)#]+)\.md#([^)\s]+)\)'
    $entries = @(
        foreach ($link in [regex]::Matches($text, $pattern)) {
            # The page name becomes a local file name; accept nothing but a plain slug.
            if ($link.Groups[1].Value -notmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') { throw "Unexpected $name resource page name: $($link.Groups[1].Value)" }
            [pscustomobject]@{ page = $link.Groups[1].Value; anchor = $link.Groups[2].Value }
        }
    )
    if (!$entries.Count) { throw "No $name entries found in $(IndexPath $name): upstream format may have changed." }
    return $entries
}
function Sections([string]$name) {
    # Every anchored second-level heading on a resource page is one entry, except the page's table of contents.
    $index = @(IndexEntries $name)
    $sections = @(
        foreach ($page in @($index.page | Select-Object -Unique)) {
            $path = PagePath $name $page
            if (!(Test-Path -LiteralPath $path)) { throw "Missing $path. Run with -Download." }
            $text = ReadText $path
            $headings = [regex]::Matches($text, '(?m)^## (.+?) \{#([^}\s]+)\}[ \t]*$')
            for ($i = 0; $i -lt $headings.Count; $i++) {
                $heading = $headings[$i]
                if ($heading.Groups[2].Value -eq 'contents') { continue }
                $end = if ($i + 1 -lt $headings.Count) { $headings[$i+1].Index } else { $text.Length }
                [pscustomobject]@{ page = $page; anchor = $heading.Groups[2].Value; title = $heading.Groups[1].Value.Trim(); text = $text.Substring($heading.Index, $end - $heading.Index).Trim() }
            }
        }
    )
    # A section missing from the index, or an index link without a section, means the parser no longer sees the whole reference.
    $mismatch = @(Compare-Object @($index | ForEach-Object { "$($_.page)#$($_.anchor)" }) @($sections | ForEach-Object { "$($_.page)#$($_.anchor)" }))
    if ($mismatch.Count) { throw "The $name index and its resource pages disagree: $(($mismatch | ForEach-Object { "$($_.InputObject) ($(if ($_.SideIndicator -eq '<=') { 'index only' } else { 'page only' }))" }) -join ', ')" }
    return $sections
}
function Block([string]$section, [string]$headingPattern) {
    return [regex]::Match($section, '(?ms)^### (?:' + $headingPattern + ')[ \t]*\n(.*?)(?=^#{2,3} |\z)').Groups[1].Value
}
function Fields([string]$block, [switch]$Request) {
    # A field line reads "- `parent.name` — Type[, Nullable][, **Required**]. Description", indented two spaces per level.
    $parents = [Collections.Generic.List[string]]::new()
    @(
        foreach ($line in $block -split "`n") {
            $match = [regex]::Match($line, '^( *)- `([^`]+)` — (.*)$')
            if (!$match.Success) { continue }
            $depth = [int][Math]::Floor($match.Groups[1].Value.Length / 2)
            $full = $match.Groups[2].Value
            # Nested fields repeat their parent's path; nested list items without it (possible values) are not fields.
            if ($depth -gt $parents.Count -or ($depth -gt 0 -and !$full.StartsWith($parents[$depth-1] + '.'))) { continue }
            if ($parents.Count -gt $depth) { $parents.RemoveRange($depth, $parents.Count - $depth) }
            $parents.Add($full)
            $meta = [regex]::Match($match.Groups[3].Value, '^(.*?)(?:\.(?:\s|$)|$)').Groups[1].Value
            $type = ($meta -replace ',\s*\*\*Required\*\*', '' -replace ',\s*Nullable\b', '').Trim()
            if (!$type) { throw "Missing type for field $full" }
            $field = [ordered]@{ name = if ($depth -gt 0) { $full.Substring($parents[$depth-1].Length + 1) } else { $full }; type = $type; indent = $depth * 3 }
            # The reference marks every required request field, so an unmarked request field is optional.
            if ($Request) { $field.required = if ($meta -match '\*\*Required\*\*') { 'Yes' } else { 'No' } }
            if ($meta -match '(?:^|,\s*)Nullable\b') { $field.nullable = $true }
            $field
        }
    )
}
function Shape($endpoint) {
    $parts = @($endpoint.method, $endpoint.path, (@($endpoint.scopeCandidates) | Sort-Object) -join ',')
    # JSON names are case-sensitive; the documented type names are not ("string" and "String" are the same type).
    foreach ($collection in @('query','requestBody','responseBody')) { $parts += (@($endpoint.$collection) | ForEach-Object { "$($_.name):$($_.type.ToLowerInvariant())" }) -join ';' }
    return $parts -join '|'
}
if ($Download) {
    foreach ($name in $references.Keys) {
        Invoke-WebRequest -Uri ($site + $references[$name] + '.md') -OutFile (IndexPath $name)
        $pages = @((IndexEntries $name).page | Select-Object -Unique)
        $folder = Join-Path $directory $name
        New-Item -ItemType Directory -Force $folder | Out-Null
        Get-ChildItem -LiteralPath $folder -Filter '*.md' | Where-Object { $_.BaseName -notin $pages } | Remove-Item
        foreach ($page in $pages) { Invoke-WebRequest -Uri ($site + $references[$name] + "/$page.md") -OutFile (PagePath $name $page) }
    }
    Invoke-WebRequest -Uri $scopesUrl -OutFile (Join-Path $directory 'scopes-reference.html')
}
foreach ($path in @((IndexPath 'helix'), (IndexPath 'eventsub'), (Join-Path $directory 'scopes-reference.html'))) {
    if (!(Test-Path -LiteralPath $path)) { throw "Missing $path. Run with -Download." }
}
$endpoints = @(
    foreach ($section in Sections 'helix') {
        $url = [regex]::Match((Block $section.text 'Endpoint'), '`(GET|POST|PUT|PATCH|DELETE) https://api\.twitch\.tv/helix/([^`\s]+)`')
        if (!$url.Success) { throw "No URL found for $($section.anchor)" }
        $auth = (Block $section.text 'Authorization') -replace '\[([^\]]*)\]\([^)]*\)', '$1'
        $scopes = @([regex]::Matches($auth, '\b[a-z]+:(?:[a-z]+:)?[a-z_]+\b') | ForEach-Object Value | Sort-Object -Unique)
        [ordered]@{
            id = $section.anchor
            name = $section.title
            method = $url.Groups[1].Value
            path = $url.Groups[2].Value
            source = $site + $references.helix + "/$($section.page)#$($section.anchor)"
            documentationFingerprint = Sha $section.text
            scopeCandidates = $scopes
            query = @(Fields (Block $section.text 'Query Parameters|Request Query Parameters') -Request)
            requestBody = @(Fields (Block $section.text 'Request Body') -Request)
            responseBody = @(Fields (Block $section.text 'Response Body'))
            authorizationReview = 'pending' # Scope candidates may be alternatives; never enforce all blindly.
            availabilityReview = 'pending'
            status = 'inventoried'
            evidence = @()
        }
    }
)
$events = @(
    foreach ($section in Sections 'eventsub') {
        $subscription = Block $section.text 'Subscription'
        $type = [regex]::Match($subscription, '(?m)^- Type: `([^`]+)`').Groups[1].Value
        $version = [regex]::Match($subscription, '(?m)^- Version: `([^`]+)`').Groups[1].Value
        if (!$type -or !$version) { throw "No subscription type and version found for $($section.anchor)" }
        [ordered]@{
            id = "$type@$version"
            type = $type
            version = $version
            source = $site + $references.eventsub + "/$($section.page)#$($section.anchor)"
            availabilityReview = if ($version -eq 'beta') { 'public-beta' } else { 'pending' }
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
    $comparableFingerprints = $previous.fingerprintFormat -eq $fingerprintFormat
    foreach ($collection in @('helix','eventsub')) {
        $items = if ($collection -eq 'helix') { $endpoints } else { $events }
        foreach ($item in $items) {
            $old = @($previous.$collection | Where-Object id -EQ $item.id)
            if ($old.Count -eq 1) {
                $item.status = $old[0].status
                $item.evidence = @($old[0].evidence)
                if ($old[0].availabilityReview -ne 'pending') { $item.availabilityReview = $old[0].availabilityReview }
                if ($collection -eq 'helix') { $item.authorizationReview = $old[0].authorizationReview }
                if ($collection -eq 'helix' -and $item.status -in @('complete','partial')) {
                    # After a fingerprint format change the old fingerprint says nothing; compare the parsed method, path,
                    # scopes and field names/types instead. Prose-only edits made in the same upstream change go unnoticed.
                    $changed = if ($comparableFingerprints) { $old[0].documentationFingerprint -and $old[0].documentationFingerprint -ne $item.documentationFingerprint } else { (Shape $old[0]) -cne (Shape $item) }
                    if ($changed) {
                        $item.status = 'needs-review'
                        $item.authorizationReview = 'pending'
                        $item.availabilityReview = 'pending'
                    }
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
$sourceFiles = [ordered]@{}
foreach ($name in $references.Keys) {
    $sourceFiles[$site + $references[$name] + '.md'] = IndexPath $name
    foreach ($page in @((IndexEntries $name).page | Select-Object -Unique)) { $sourceFiles[$site + $references[$name] + "/$page.md"] = PagePath $name $page }
}
$sourceFiles[$scopesUrl] = Join-Path $directory 'scopes-reference.html'
$manifest = [ordered]@{
    schemaVersion = 1
    retrievedOn = [DateTime]::UtcNow.ToString('yyyy-MM-dd')
    scope = 'Official documentation snapshot; availability and authorization require individual review.'
    fingerprintFormat = $fingerprintFormat
    sources = @($sourceFiles.Keys | ForEach-Object { [ordered]@{ url = $_; sha256 = PageHash $sourceFiles[$_] } })
    helix = $endpoints
    eventsub = $events
    scopes = $scopeNames
}
$manifest | ConvertTo-Json -Depth 12 | Set-Content $outputPath -Encoding utf8
Write-Output "Inventoried $($endpoints.Count) Helix endpoints, $($events.Count) EventSub type/version pairs, $($scopeNames.Count) scopes."
