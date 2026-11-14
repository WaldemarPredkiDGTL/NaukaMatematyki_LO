$ErrorActionPreference = "Stop"
Set-Location -LiteralPath $PSScriptRoot

$console = [System.Text.Encoding]::GetEncoding(1250)
[Console]::InputEncoding = $console
[Console]::OutputEncoding = $console
$OutputEncoding = $console

function Say([string]$Message) {
    Write-Host $Message
}

function Stop-With([string]$Message) {
    Say $Message
    exit 1
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Stop-With "Nie ma programu git. Wydanie wysyła się tym samym gitem, którym robisz push."
}

Say "Sprawdzam stan repozytorium…"
git rev-parse --is-inside-work-tree *> $null
if ($LASTEXITCODE -ne 0) { Stop-With "Ten plik ma leżeć w katalogu Pracowni." }

$branch = (git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne "master") {
    Stop-With "Jesteś na gałęzi $branch. Wydanie idzie z master."
}

$changes = @(git status --porcelain | Where-Object { $_ -notmatch '^\?\?' })
if ($changes.Count -gt 0) {
    Say "Są niezatwierdzone zmiany. Najpierw zrób commit, inaczej wydanie ich nie zawiera."
    Say ($changes -join [Environment]::NewLine)
    exit 1
}

Say "Pobieram informacje z GitHuba…"
git fetch origin master --tags --quiet
if ($LASTEXITCODE -ne 0) { Stop-With "Nie udało się połączyć z GitHubem. Sprawdź internet i spróbuj jeszcze raz." }

$counts = (git rev-list --left-right --count "origin/master...HEAD").Trim() -split "\s+"
$behind = [int]$counts[0]
$ahead = [int]$counts[1]
if ($behind -gt 0) {
    Stop-With "Na GitHubie są nowsze commity. Najpierw zrób pull w master."
}

$latest = $null
foreach ($tag in @(git tag -l "v*")) {
    if ($tag -match "^v(\d+)\.(\d+)\.(\d+)$") {
        $parsed = [version]"$($Matches[1]).$($Matches[2]).$($Matches[3])"
        if ($null -eq $latest -or $parsed -gt $latest) { $latest = $parsed }
    }
}

if ($null -eq $latest) {
    $next = [version]"1.0.0"
    Say "Nie ma jeszcze żadnego wydania."
}
else {
    $next = [version]::new($latest.Major, $latest.Minor, $latest.Build + 1)
    Say "Ostatnie wydanie: v$latest"
}

Say ""
Say "Enter wypuszcza $next."
Say "Możesz wpisać inny numer, na przykład 1.1.0."
Say "Wpisz n, aby anulować."
$answer = Read-Host "Wersja"
if ($answer -match "^(n|nie)$") {
    Say "Anulowane."
    exit 0
}

$chosenText = if ([string]::IsNullOrWhiteSpace($answer)) { $next.ToString() } else { $answer.Trim().TrimStart("v", "V") }
if ($chosenText -notmatch "^\d+\.\d+\.\d+$") {
    Stop-With "Numer ma mieć postać 1.0.3."
}

$chosen = [version]$chosenText
if ($null -ne $latest -and $chosen -le $latest) {
    Stop-With "Numer musi być wyższy niż $latest."
}

$tagName = "v$chosen"
git rev-parse -q --verify "refs/tags/$tagName" *> $null
if ($LASTEXITCODE -eq 0) {
    Stop-With "Tag $tagName już istnieje."
}

if ($ahead -gt 0) {
    Say "Wysyłam commity na master…"
    git push origin master
    if ($LASTEXITCODE -ne 0) { Stop-With "Push master nie wyszedł." }
}

Say "Oznaczam $tagName i wysyłam. GitHub buduje wydanie."
git tag $tagName
if ($LASTEXITCODE -ne 0) { Stop-With "Nie udało się utworzyć tagu." }

git push origin $tagName
if ($LASTEXITCODE -ne 0) {
    git tag -d $tagName *> $null
    Stop-With "Tag nie doszedł na GitHub. Lokalny tag został zdjęty, możesz spróbować jeszcze raz."
}

$releaseUrl = "https://api.github.com/repos/WaldemarPredkiDGTL/NaukaMatematyki_LO/releases/tags/$tagName"
Say ""
Say "Tag poszedł. Czekam, aż GitHub opublikuje wydanie. To zwykle kilka minut."
$ready = $false
for ($try = 1; $try -le 40; $try++) {
    Start-Sleep -Seconds 15
    try {
        $release = Invoke-RestMethod -Uri $releaseUrl -TimeoutSec 20
        if ($release.tag_name -eq $tagName) {
            $ready = $true
            break
        }
    }
    catch {
        Say "Jeszcze trwa… ($try z 40)"
    }
}

if ($ready) {
    Say ""
    Say "Gotowe. Wydanie $tagName jest na GitHubie."
    Say "Uruchom zainstalowany program. Zapyta, czy pobrać tę wersję i wystartować ponownie."
    exit 0
}

Say ""
Say "Tag $tagName jest na GitHubie, ale wydanie nie pojawiło się w ciągu 10 minut."
Say "Wtedy wejdź w Actions i otwórz ostatnie Wydanie, żeby zobaczyć, co stanęło."
exit 1
