param(
    [Parameter(Mandatory)] [string]$Title,
    [string]$Subtitle = '',
    # Steps: array of @{ Text = 'что сделать'; Note = 'пояснение'; Image = 'путь к картинке' }
    [Parameter(Mandatory)] [object[]]$Steps,
    [Parameter(Mandatory)] [string]$Destination,
    [int]$Width = 1080
)
# Builds one tall picture with numbered steps and screenshots, to send to a messenger chat.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$pad = 48
$badge = 76
$textLeft = $pad + $badge + 24
$textWidth = $Width - $textLeft - $pad
$imageWidth = $Width - 2 * $pad
$fontTitle = [System.Drawing.Font]::new('Segoe UI', 46, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$fontSub = [System.Drawing.Font]::new('Segoe UI', 30, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$fontStep = [System.Drawing.Font]::new('Segoe UI', 38, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$fontNote = [System.Drawing.Font]::new('Segoe UI', 31, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$fontBadge = [System.Drawing.Font]::new('Segoe UI', 40, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$measure = [System.Drawing.Graphics]::FromImage([System.Drawing.Bitmap]::new(1, 1))
function Measure-Text([string]$text, $font, [int]$width) {
    if (-not $text) { return 0 }
    [int][Math]::Ceiling($measure.MeasureString($text, $font, $width).Height)
}

# First pass: layout.
$headerHeight = $pad + (Measure-Text $Title $fontTitle ($Width - 2 * $pad)) + 10 + (Measure-Text $Subtitle $fontSub ($Width - 2 * $pad)) + $pad
$y = $headerHeight + $pad
$layout = foreach ($step in $Steps) {
    $textHeight = Measure-Text $step.Text $fontStep $textWidth
    $noteHeight = Measure-Text $step.Note $fontNote $textWidth
    $top = $y
    $y += [Math]::Max($badge, $textHeight + $(if ($noteHeight) { 10 + $noteHeight } else { 0 }))
    $image = $null; $imageHeight = 0; $drawWidth = 0
    if ($step.Image) {
        $image = [System.Drawing.Image]::FromFile($step.Image)
        # Screenshots fill the width so buttons stay readable on a phone screen.
        $drawWidth = if ($step.ImageWidth) { [Math]::Min($imageWidth, [int]$step.ImageWidth) } else { $imageWidth }
        $imageHeight = [int]($image.Height * $drawWidth / $image.Width)
        $y += 24 + $imageHeight
    }
    $y += 56
    [pscustomobject]@{ Step = $step; Top = $top; TextHeight = $textHeight; Image = $image; ImageTop = $y - 56 - $imageHeight; ImageWidth = $drawWidth; ImageHeight = $imageHeight }
}
$height = $y + $pad - 24

# Second pass: drawing.
$bitmap = [System.Drawing.Bitmap]::new($Width, $height)
$g = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::White)
    $navy = [System.Drawing.Color]::FromArgb(23, 54, 93)
    $red = [System.Drawing.Color]::FromArgb(229, 57, 53)
    $g.FillRectangle([System.Drawing.SolidBrush]::new($navy), 0, 0, $Width, $headerHeight)
    $titleHeight = Measure-Text $Title $fontTitle ($Width - 2 * $pad)
    $g.DrawString($Title, $fontTitle, [System.Drawing.Brushes]::White, [System.Drawing.RectangleF]::new($pad, $pad, $Width - 2 * $pad, $titleHeight))
    if ($Subtitle) {
        $g.DrawString($Subtitle, $fontSub, [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(214, 225, 238)),
            [System.Drawing.RectangleF]::new($pad, $pad + $titleHeight + 10, $Width - 2 * $pad, $headerHeight))
    }
    $center = [System.Drawing.StringFormat]::new(); $center.Alignment = 'Center'; $center.LineAlignment = 'Center'
    $number = 1
    foreach ($item in $layout) {
        $g.FillEllipse([System.Drawing.SolidBrush]::new($red), $pad, $item.Top, $badge, $badge)
        $g.DrawString([string]$number, $fontBadge, [System.Drawing.Brushes]::White, [System.Drawing.RectangleF]::new($pad, $item.Top + 2, $badge, $badge), $center)
        $textTop = $item.Top + [Math]::Max(0, ($badge - $item.TextHeight) / 2)
        if ($item.Step.Note) { $textTop = $item.Top + 6 }
        $g.DrawString($item.Step.Text, $fontStep, [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(25, 25, 25)),
            [System.Drawing.RectangleF]::new($textLeft, $textTop, $textWidth, $item.TextHeight))
        if ($item.Step.Note) {
            $g.DrawString($item.Step.Note, $fontNote, [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(90, 103, 115)),
                [System.Drawing.RectangleF]::new($textLeft, $textTop + $item.TextHeight + 10, $textWidth, 2000))
        }
        if ($item.Image) {
            $x = [int](($Width - $item.ImageWidth) / 2)
            $g.DrawImage($item.Image, $x, $item.ImageTop, $item.ImageWidth, $item.ImageHeight)
            $g.DrawRectangle([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(191, 197, 204), 2), $x, $item.ImageTop, $item.ImageWidth, $item.ImageHeight)
            $item.Image.Dispose()
        }
        $number++
    }
} finally { $g.Dispose() }
[IO.Directory]::CreateDirectory((Split-Path -Parent ([IO.Path]::GetFullPath($Destination)))) | Out-Null
$bitmap.Save([IO.Path]::GetFullPath($Destination), [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
