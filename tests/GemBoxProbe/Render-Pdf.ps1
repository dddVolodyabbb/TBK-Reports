param([Parameter(Mandatory=$true)][string]$PdfPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
[Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime] | Out-Null
[Windows.Storage.Streams.IRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime] | Out-Null
[Windows.Data.Pdf.PdfDocument,Windows.Data.Pdf,ContentType=WindowsRuntime] | Out-Null
[Windows.Data.Pdf.PdfPageRenderOptions,Windows.Data.Pdf,ContentType=WindowsRuntime] | Out-Null
$genericAwait = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and
    $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
} | Select-Object -First 1
$actionAwait = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and !$_.IsGenericMethod -and $_.GetParameters().Count -eq 1
} | Select-Object -First 1
function Await-Result($Operation, [Type]$ResultType) {
    $task = $genericAwait.MakeGenericMethod($ResultType).Invoke($null, @($Operation))
    $task.GetAwaiter().GetResult()
}
$absolute = (Resolve-Path -LiteralPath $PdfPath).Path
$file = Await-Result ([Windows.Storage.StorageFile]::GetFileFromPathAsync($absolute)) ([Windows.Storage.StorageFile])
$pdf = Await-Result ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
Write-Output "$([IO.Path]::GetFileName($absolute)): $($pdf.PageCount) pages"
for ($index = 0; $index -lt $pdf.PageCount; $index++) {
    $page = $pdf.GetPage($index)
    $stream = [Windows.Storage.Streams.InMemoryRandomAccessStream]::new()
    try {
        $options = [Windows.Data.Pdf.PdfPageRenderOptions]::new()
        $options.DestinationWidth = 1800
        $action = $page.RenderToStreamAsync($stream, $options)
        $task = $actionAwait.Invoke($null, @($action))
        $task.GetAwaiter().GetResult() | Out-Null
        $stream.Seek(0)
        $readStream = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($stream)
        $png = [IO.Path]::ChangeExtension($absolute, "page-$($index + 1).png")
        $destination = [IO.File]::Create($png)
        try { $readStream.CopyTo($destination) } finally { $destination.Dispose(); $readStream.Dispose() }
        Write-Output $png
    } finally { $page.Dispose(); $stream.Dispose() }
}
