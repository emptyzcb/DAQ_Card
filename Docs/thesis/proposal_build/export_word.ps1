param(
    [Parameter(Mandatory = $true)][string]$InputDocx,
    [Parameter(Mandatory = $true)][string]$OutputPdf
)
$ErrorActionPreference = 'Stop'
$application = $null
$document = $null
$ownInstance = $false
$previousAlerts = $null
try {
    $application = New-Object -ComObject Word.Application
    $ownInstance = $application.Documents.Count -eq 0
    $previousAlerts = $application.DisplayAlerts
    if ($ownInstance) { $application.Visible = $false }
    $application.DisplayAlerts = 0
    $document = $application.Documents.Open($InputDocx, $false, $true)
    $null = $document.Fields.Update()
    $document.Repaginate()
    $document.ExportAsFixedFormat($OutputPdf, 17)
    Write-Output ('Pages: ' + $document.ComputeStatistics(2))
    Write-Output $OutputPdf
} finally {
    if ($null -ne $document) {
        $document.Close(0)
        $null = [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($document)
    }
    if ($null -ne $application) {
        if ($null -ne $previousAlerts) { $application.DisplayAlerts = $previousAlerts }
        if ($ownInstance) { $application.Quit() }
        $null = [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($application)
    }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}
