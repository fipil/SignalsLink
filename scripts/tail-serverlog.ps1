<#
    Follows the server log across restarts.

    The game archives the old log and starts a new one on every launch, so Get-Content -Wait dies
    the moment you restart. This notices the new file and carries on.

        powershell -NoProfile -File scripts\tail-serverlog.ps1                     # everything
        powershell -NoProfile -File scripts\tail-serverlog.ps1 -Pattern "anchor at"
        powershell -NoProfile -File scripts\tail-serverlog.ps1 -Pattern "SignalsLink|dock@"
#>
param(
    [string]$Log = "C:\VintageStory\1.22Data\Logs\server-main.log",

    # Everything. "." drops blank lines and nothing else.
    [string]$Pattern = ".",

    # Ours, picked out in cyan among the rest.
    [string]$Highlight = "SignalsLink|dock@|anchor at"
)

Write-Host "Following $Log" -ForegroundColor DarkGray
Write-Host "Pattern: $Pattern" -ForegroundColor DarkGray

# The first line of the log, which is what tells one run's file from the next. NOT the creation
# time: NTFS hands a recreated file its predecessor's, so a fresh log looks days old.
function Get-Head([string]$path) {
    try {
        $stream = [System.IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
        try {
            $buffer = New-Object byte[] 256
            $read = $stream.Read($buffer, 0, $buffer.Length)
            return [System.Text.Encoding]::UTF8.GetString($buffer, 0, $read)
        }
        finally { $stream.Close() }
    }
    catch { return $null }
}

$offset = 0
$head = $null

while ($true) {
    if (-not (Test-Path $Log)) {
        # Between the archive and the new file there is a moment with nothing there.
        Start-Sleep -Milliseconds 500
        continue
    }

    $info = Get-Item $Log
    $now = Get-Head $Log

    # A new run: the log begins differently, or it is shorter than what we have already read.
    if (($head -ne $null -and $now -ne $head) -or $info.Length -lt $offset) {
        $offset = 0
        Write-Host ""
        Write-Host "--- new log ---" -ForegroundColor Yellow
    }

    $head = $now

    if ($info.Length -gt $offset) {
        # ReadWrite sharing: the game holds this file open for writing the whole time.
        $stream = [System.IO.File]::Open($Log, 'Open', 'Read', 'ReadWrite')

        try {
            $null = $stream.Seek($offset, 'Begin')

            $reader = New-Object System.IO.StreamReader($stream)
            $text = $reader.ReadToEnd()
            $offset = $stream.Length
        }
        finally {
            $stream.Close()
        }

        foreach ($line in ($text -split "`r?`n")) {
            if ($line -match $Pattern) {
                # Errors and warnings keep their colour even when they are ours - that a line is
                # a complaint matters more than whose it is.
                if ($line -match '\[Error\]') { Write-Host $line -ForegroundColor Red }
                elseif ($line -match '\[Warning\]') { Write-Host $line -ForegroundColor Yellow }
                elseif ($Highlight -and $line -match $Highlight) { Write-Host $line -ForegroundColor Cyan }
                else { Write-Host $line -ForegroundColor DarkGray }
            }
        }
    }

    Start-Sleep -Milliseconds 300
}
