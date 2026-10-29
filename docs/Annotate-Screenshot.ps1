param(
    [Parameter(Mandatory)] [string]$Source,
    [Parameter(Mandatory)] [string]$Destination,
    # Boxes in source pixels: "x,y,width,height;x,y,width,height". Numbered 1, 2, 3… in this order.
    [string]$Boxes = '',
    # Optional crop of the source before drawing: "x,y,width,height".
    [string]$Crop = '',
    # Largest width of the saved image in pixels; larger screenshots are scaled down.
    [int]$MaxWidth = 1200,
    # Draw boxes without numbers when only one element is marked.
    [switch]$NoNumbers,
    # Thicker frames and larger badges for phone screenshots, which a guide shows narrow.
    [switch]$Phone
)
# Marks the buttons and fields a guide talks about: a red frame and a numbered red badge.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function ConvertTo-Rect([string]$text) {
    $v = $text.Split(',') | ForEach-Object { [double]$_.Trim() }
    [System.Drawing.RectangleF]::new($v[0], $v[1], $v[2], $v[3])
}

$original = [System.Drawing.Bitmap]::FromFile((Resolve-Path -LiteralPath $Source))
try {
    $cropRect = if ($Crop) { ConvertTo-Rect $Crop } else { [System.Drawing.RectangleF]::new(0, 0, $original.Width, $original.Height) }
    $scale = [Math]::Min(1.0, $MaxWidth / $cropRect.Width)
    $width = [int][Math]::Round($cropRect.Width * $scale)
    $height = [int][Math]::Round($cropRect.Height * $scale)
    $result = [System.Drawing.Bitmap]::new($width, $height)
    $g = [System.Drawing.Graphics]::FromImage($result)
    try {
        $g.SmoothingMode = 'AntiAlias'
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.TextRenderingHint = 'AntiAliasGridFit'
        $g.DrawImage($original, [System.Drawing.RectangleF]::new(0, 0, $width, $height), $cropRect, 'Pixel')

        $red = [System.Drawing.Color]::FromArgb(229, 57, 53)
        $penWidth = if ($Phone) { [Math]::Max(5.0, $width / 130.0) } else { [Math]::Max(3.0, $width / 260.0) }
        $pen = [System.Drawing.Pen]::new($red, $penWidth)
        $badgeSize = if ($Phone) { [Math]::Max(44.0, $width / 14.0) } else { [Math]::Max(26.0, $width / 26.0) }
        $font = [System.Drawing.Font]::new('Segoe UI', [float]($badgeSize * 0.52), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $format = [System.Drawing.StringFormat]::new()
        $format.Alignment = 'Center'; $format.LineAlignment = 'Center'
        $number = 1
        foreach ($boxText in ($Boxes.Split(';') | Where-Object { $_.Trim() })) {
            $box = ConvertTo-Rect $boxText
            $r = [System.Drawing.RectangleF]::new(($box.X - $cropRect.X) * $scale, ($box.Y - $cropRect.Y) * $scale, $box.Width * $scale, $box.Height * $scale)
            $r.Inflate($penWidth * 1.5, $penWidth * 1.5)
            $g.DrawRectangle($pen, $r.X, $r.Y, $r.Width, $r.Height)
            if (-not $NoNumbers) {
                # The badge sits on the frame's top-left corner, moved inside the image when needed.
                $bx = [Math]::Min([Math]::Max(0, $r.X - $badgeSize * 0.85), $width - $badgeSize)
                $by = [Math]::Min([Math]::Max(0, $r.Y - $badgeSize / 2), $height - $badgeSize)
                $badge = [System.Drawing.RectangleF]::new($bx, $by, $badgeSize, $badgeSize)
                $g.FillEllipse([System.Drawing.SolidBrush]::new($red), $badge)
                $g.DrawEllipse([System.Drawing.Pen]::new([System.Drawing.Color]::White, [float]($penWidth * 0.6)), $badge)
                $g.DrawString([string]$number, $font, [System.Drawing.Brushes]::White, $badge, $format)
            }
            $number++
        }
    } finally { $g.Dispose() }
    $directory = Split-Path -Parent ([IO.Path]::GetFullPath($Destination))
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $result.Save([IO.Path]::GetFullPath($Destination), [System.Drawing.Imaging.ImageFormat]::Png)
    $result.Dispose()
} finally { $original.Dispose() }
