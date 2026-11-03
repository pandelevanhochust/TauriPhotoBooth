param (
    [Parameter(Mandatory=$true)][string]$ImagePath,
    [Parameter(Mandatory=$false)][string]$PrinterName = "",
    [Parameter(Mandatory=$false)][int]$Copies = 1,
    [Parameter(Mandatory=$false)][double]$Scale = 0.98,
    [Parameter(Mandatory=$false)][int]$OffsetX = 0,
    [Parameter(Mandatory=$false)][int]$OffsetY = 0
)

Write-Host "[PrintSpooler] Starting print job for: $ImagePath"
Write-Host "[PrintSpooler] Printer: '$PrinterName', Copies: $Copies, Scale: $Scale"

if (-not (Test-Path $ImagePath)) {
    Write-Error "File not found: $ImagePath"
    exit 1
}

Add-Type -AssemblyName System.Drawing

$printDoc = New-Object System.Drawing.Printing.PrintDocument
if ($PrinterName -and $PrinterName.Trim() -ne "") {
    $printDoc.PrinterSettings.PrinterName = $PrinterName
}

$printDoc.PrinterSettings.Copies = [short]$Copies

$printDoc.add_PrintPage({
    param($sender, $e)
    try {
        $img = [System.Drawing.Image]::FromFile($ImagePath)
        $destWidth = [int]($e.PageBounds.Width * $Scale)
        $destHeight = [int]($e.PageBounds.Height * $Scale)
        $destX = [int](($e.PageBounds.Width - $destWidth) / 2) + $OffsetX
        $destY = [int](($e.PageBounds.Height - $destHeight) / 2) + $OffsetY
        
        $destRect = New-Object System.Drawing.Rectangle($destX, $destY, $destWidth, $destHeight)
        $e.Graphics.DrawImage($img, $destRect)
        $img.Dispose()
    }
    catch {
        Write-Error "Error during graphics render: $_"
    }
})

try {
    $printDoc.Print()
    Write-Host "[PrintSpooler] Print job successfully submitted to Windows Spooler."
    exit 0
}
catch {
    Write-Error "Spooler submission failed: $_"
    exit 2
}
finally {
    $printDoc.Dispose()
}
