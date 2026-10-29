param(
    [string]$Directory = $PSScriptRoot,
    [string]$DocumentName = 'ТБК Отчёт — руководство пользователя',
    [string]$FooterLabel = 'ТБК Отчёт · v1.4'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$source = Join-Path $Directory ($DocumentName + '.md')
$destination = Join-Path $Directory ($DocumentName + '.docx')
$lines = [System.IO.File]::ReadAllLines($source)
$headingBookmarks = @{}
$bookmarkNumber = 1
foreach ($headingLine in $lines) {
    if (-not $headingLine.StartsWith('## ')) { continue }
    $headingText = $headingLine.Substring(3)
    if ($headingText -eq 'Руководство пользователя · версия 1.4 · Windows' -or $headingText -eq 'Содержание') { continue }
    $headingBookmarks[$headingText] = "section$bookmarkNumber"
    $bookmarkNumber++
}
$paragraphs = [System.Text.StringBuilder]::new()
$insideContents = $false
$bookmarkId = 1
# Screenshots referenced as ![caption](images/file.png): embedded as pictures that fit
# the text width and a maximum height, followed by the caption.
Add-Type -AssemblyName System.Drawing
$images = [System.Collections.Generic.List[object]]::new()
$emuPerTwip = 635
$maxImageWidth = (11906 - 1134 - 1134) * $emuPerTwip
$maxImageHeight = 7200 * $emuPerTwip
foreach ($line in $lines) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    if ($line -eq '---') {
        $insideContents = $false
        [void]$paragraphs.Append('<w:p><w:r><w:br w:type="page"/></w:r></w:p>')
        continue
    }
    if ($line -match '^!\[(.*)\]\((.+)\)$') {
        $caption = $Matches[1]
        $imagePath = Join-Path $Directory $Matches[2]
        $bitmap = [System.Drawing.Image]::FromFile($imagePath)
        try { $pixelWidth = $bitmap.Width; $pixelHeight = $bitmap.Height } finally { $bitmap.Dispose() }
        $scale = [Math]::Min(1.0, [Math]::Min($maxImageWidth / ($pixelWidth * 9525.0), $maxImageHeight / ($pixelHeight * 9525.0)))
        $cx = [long]($pixelWidth * 9525 * $scale)
        $cy = [long]($pixelHeight * 9525 * $scale)
        $number = $images.Count + 1
        $images.Add([pscustomobject]@{ Path = $imagePath; Name = "image$number.png"; Id = "rIdImg$number" })
        $alt = [System.Security.SecurityElement]::Escape($caption)
        [void]$paragraphs.Append("<w:p><w:pPr><w:keepNext/><w:jc w:val=`"center`"/><w:spacing w:before=`"120`" w:after=`"40`"/></w:pPr><w:r><w:drawing><wp:inline distT=`"0`" distB=`"0`" distL=`"0`" distR=`"0`"><wp:extent cx=`"$cx`" cy=`"$cy`"/><wp:docPr id=`"$number`" name=`"Рисунок $number`" descr=`"$alt`"/><a:graphic xmlns:a=`"http://schemas.openxmlformats.org/drawingml/2006/main`"><a:graphicData uri=`"http://schemas.openxmlformats.org/drawingml/2006/picture`"><pic:pic xmlns:pic=`"http://schemas.openxmlformats.org/drawingml/2006/picture`"><pic:nvPicPr><pic:cNvPr id=`"$number`" name=`"image$number.png`"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=`"rIdImg$number`"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x=`"0`" y=`"0`"/><a:ext cx=`"$cx`" cy=`"$cy`"/></a:xfrm><a:prstGeom prst=`"rect`"><a:avLst/></a:prstGeom><a:ln w=`"6350`"><a:solidFill><a:srgbClr val=`"BFC5CC`"/></a:solidFill></a:ln></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>")
        if ($caption) {
            [void]$paragraphs.Append("<w:p><w:pPr><w:jc w:val=`"center`"/><w:spacing w:after=`"160`"/></w:pPr><w:r><w:rPr><w:i/><w:color w:val=`"5A6773`"/><w:sz w:val=`"19`"/></w:rPr><w:t xml:space=`"preserve`">$alt</w:t></w:r></w:p>")
        }
        continue
    }
    $style = 'Normal'
    $value = $line
    if ($line.StartsWith('### ')) { $style = 'Heading2'; $value = $line.Substring(4) }
    elseif ($line.StartsWith('## ')) { $style = 'Heading1'; $value = $line.Substring(3) }
    elseif ($line.StartsWith('# ')) { $style = 'Title'; $value = $line.Substring(2) }
    elseif ($line.StartsWith('- ')) { $style = 'ListParagraph'; $value = '• ' + $line.Substring(2) }
    if ($style -eq 'Heading1' -and $value -eq 'Содержание') {
        $insideContents = $true
    }
    $targetText = if ($line.StartsWith('- ')) { $line.Substring(2) } else { $value }
    $escaped = [System.Security.SecurityElement]::Escape($value)
    if ($insideContents -and $headingBookmarks.ContainsKey($targetText)) {
        $anchor = $headingBookmarks[$targetText]
        [void]$paragraphs.Append("<w:p><w:pPr><w:pStyle w:val=`"$style`"/></w:pPr><w:hyperlink w:anchor=`"$anchor`" w:history=`"1`"><w:r><w:rPr><w:color w:val=`"0563C1`"/><w:u w:val=`"single`"/></w:rPr><w:t xml:space=`"preserve`">$escaped</w:t></w:r></w:hyperlink></w:p>")
    } elseif ($style -eq 'Heading1' -and $headingBookmarks.ContainsKey($value)) {
        $anchor = $headingBookmarks[$value]
        [void]$paragraphs.Append("<w:p><w:pPr><w:pStyle w:val=`"$style`"/></w:pPr><w:bookmarkStart w:id=`"$bookmarkId`" w:name=`"$anchor`"/><w:r><w:t xml:space=`"preserve`">$escaped</w:t></w:r><w:bookmarkEnd w:id=`"$bookmarkId`"/></w:p>")
        $bookmarkId++
    } else {
        [void]$paragraphs.Append("<w:p><w:pPr><w:pStyle w:val=`"$style`"/></w:pPr><w:r><w:t xml:space=`"preserve`">$escaped</w:t></w:r></w:p>")
    }
}
$imageRelationships = ($images | ForEach-Object {
    "<Relationship Id=`"$($_.Id)`" Type=`"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image`" Target=`"media/$($_.Name)`"/>" }) -join ''
$parts = @{
    '[Content_Types].xml' = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="png" ContentType="image/png"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/><Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/></Types>'
    '_rels/.rels' = '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>'
    'word/_rels/document.xml.rels' = '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/>' + $imageRelationships + '</Relationships>'
    'word/styles.xml' = '<?xml version="1.0" encoding="UTF-8"?><w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:cs="Calibri"/><w:sz w:val="22"/><w:lang w:val="ru-RU"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after="110" w:line="264" w:lineRule="auto"/><w:widowControl/></w:pPr></w:pPrDefault></w:docDefaults><w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style><w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/><w:spacing w:before="200" w:after="200"/></w:pPr><w:rPr><w:b/><w:color w:val="17365D"/><w:sz w:val="48"/></w:rPr></w:style><w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/><w:spacing w:before="180" w:after="130"/><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/><w:color w:val="17365D"/><w:sz w:val="30"/></w:rPr></w:style><w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/><w:spacing w:before="130" w:after="90"/><w:outlineLvl w:val="1"/></w:pPr><w:rPr><w:b/><w:color w:val="17365D"/><w:sz w:val="24"/></w:rPr></w:style><w:style w:type="paragraph" w:styleId="ListParagraph"><w:name w:val="List Paragraph"/><w:basedOn w:val="Normal"/><w:pPr><w:ind w:left="210" w:hanging="210"/><w:spacing w:after="70"/></w:pPr></w:style></w:styles>'
    'word/footer1.xml' = '<?xml version="1.0" encoding="UTF-8"?><w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:pPr><w:jc w:val="right"/></w:pPr><w:r><w:rPr><w:color w:val="687787"/><w:sz w:val="18"/></w:rPr><w:t xml:space="preserve">' + [System.Security.SecurityElement]::Escape($FooterLabel) + '   |   </w:t></w:r><w:fldSimple w:instr="PAGE"/></w:p></w:ftr>'
    'word/document.xml' = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"><w:body>' + $paragraphs.ToString() + '<w:sectPr><w:footerReference w:type="default" r:id="rId2"/><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1000" w:right="1134" w:bottom="1000" w:left="1134" w:header="400" w:footer="450"/></w:sectPr></w:body></w:document>'
}
$stream = [System.IO.File]::Open($destination, [System.IO.FileMode]::Create)
$archive = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($part in $parts.GetEnumerator()) {
        $null = [xml]$part.Value
        $entry = $archive.CreateEntry($part.Key)
        $writer = [System.IO.StreamWriter]::new($entry.Open(), [System.Text.UTF8Encoding]::new($false))
        try { $writer.Write($part.Value) } finally { $writer.Dispose() }
    }
    foreach ($image in $images) {
        $entry = $archive.CreateEntry("word/media/$($image.Name)", [System.IO.Compression.CompressionLevel]::NoCompression)
        $target = $entry.Open()
        try { $bytes = [System.IO.File]::ReadAllBytes($image.Path); $target.Write($bytes, 0, $bytes.Length) } finally { $target.Dispose() }
    }
} finally { $archive.Dispose(); $stream.Dispose() }
$check = [System.IO.Compression.ZipFile]::OpenRead($destination)
try {
    foreach ($entry in $check.Entries) {
        if ($entry.FullName.StartsWith('word/media/')) { continue }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { $null = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    Write-Output "DOCX verified: $($check.Entries.Count - $images.Count) XML parts, $($images.Count) images; $((Get-Item -LiteralPath $destination).Length) bytes."
} finally { $check.Dispose() }
