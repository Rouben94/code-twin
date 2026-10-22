#Requires -Version 5.1
<#
.SYNOPSIS
    Queries the Beckhoff Information System (InfoSys) search live and prints the hits.
.DESCRIPTION
    Submits the public search form on https://infosys.beckhoff.com/search/ and prints title, URL
    and snippet of each hit on the first result page. Nothing is cached or stored.
.EXAMPLE
    .\Search-InfoSys.ps1 -Query 'MC_MoveAbsolute'
.EXAMPLE
    .\Search-InfoSys.ps1 -Query 'Tc2_Utilities FB_FormatString' -Language de -First 5
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Query,

    [ValidateSet('en', 'de')]
    [string] $Language = 'en',

    [ValidateRange(1, 50)]
    [int] $First = 10
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$searchUrl = "https://infosys.beckhoff.com/search/default.aspx?lg=$Language"

function ConvertFrom-Html([string] $html) {
    $text = [regex]::Replace($html, '<[^>]+>', '')
    $text = [System.Net.WebUtility]::HtmlDecode($text)
    return ([regex]::Replace($text, '\s+', ' ')).Trim()
}

$session = $null
$form = Invoke-WebRequest -Uri $searchUrl -UseBasicParsing -SessionVariable session

$fields = @{}
foreach ($match in [regex]::Matches($form.Content, '<input type="hidden" name="([^"]+)" id="[^"]*" value="([^"]*)"')) {
    $fields[$match.Groups[1].Value] = [System.Net.WebUtility]::HtmlDecode($match.Groups[2].Value)
}
$fields['solr_contentText'] = $Query
$fields['button1.x'] = '1'
$fields['button1.y'] = '1'

$response = Invoke-WebRequest -Uri $searchUrl -Method Post -Body $fields -WebSession $session -UseBasicParsing

$items = [regex]::Matches(
    $response.Content,
    '<li class="search-list-item">.*?href="(?<url>[^"]+)".*?class="search-list-item-headline">(?<title>.*?)</h\d>.*?class="search-list-item-text">(?<text>.*?)</p>',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)

if ($items.Count -eq 0) {
    Write-Output "No InfoSys results for '$Query'."
    return
}

$total = [regex]::Match($response.Content, 'search-list-header">\s*[^:<]+:\s*(\d+)').Groups[1].Value
Write-Output "InfoSys results for '$Query' ($total total, showing up to $First):"
$items | Select-Object -First $First | ForEach-Object {
    Write-Output ''
    Write-Output ('- ' + (ConvertFrom-Html $_.Groups['title'].Value))
    Write-Output ('  ' + $_.Groups['url'].Value)
    Write-Output ('  ' + (ConvertFrom-Html $_.Groups['text'].Value))
}
