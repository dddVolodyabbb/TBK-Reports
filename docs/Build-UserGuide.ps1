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
foreach ($line in $lines) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    if ($line -eq '---') {
        $insideContents = $false
        [void]$paragraphs.Append('<w:p><w:r><w:br w:type="page"/></w:r></w:p>')
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
$parts = @{
    '[Content_Types].xml' = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/><Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/></Types>'
    '_rels/.rels' = '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>'
    'word/_rels/document.xml.rels' = '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/></Relationships>'
    'word/styles.xml' = '<?xml version="1.0" encoding="UTF-8"?><w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:cs="Calibri"/><w:sz w:val="22"/><w:lang w:val="ru-RU"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after="110" w:line="264" w:lineRule="auto"/><w:widowControl/></w:pPr></w:pPrDefault></w:docDefaults><w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style><w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/><w:spacing w:before="200" w:after="200"/></w:pPr><w:rPr><w:b/><w:color w:val="17365D"/><w:sz w:val="48"/></w:rPr></w:style><w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/><w:spacing w:before="180" w:after="130"/><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/><w:color w:val="17365D"/><w:sz w:val="30"/></w:rPr></w:style><w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/><w:spacing w:before="130" w:after="90"/><w:outlineLvl w:val="1"/></w:pPr><w:rPr><w:b/><w:color w:val="17365D"/><w:sz w:val="24"/></w:rPr></w:style><w:style w:type="paragraph" w:styleId="ListParagraph"><w:name w:val="List Paragraph"/><w:basedOn w:val="Normal"/><w:pPr><w:ind w:left="210" w:hanging="210"/><w:spacing w:after="70"/></w:pPr></w:style></w:styles>'
    'word/footer1.xml' = '<?xml version="1.0" encoding="UTF-8"?><w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:p><w:pPr><w:jc w:val="right"/></w:pPr><w:r><w:rPr><w:color w:val="687787"/><w:sz w:val="18"/></w:rPr><w:t xml:space="preserve">' + [System.Security.SecurityElement]::Escape($FooterLabel) + '   |   </w:t></w:r><w:fldSimple w:instr="PAGE"/></w:p></w:ftr>'
    'word/document.xml' = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:body>' + $paragraphs.ToString() + '<w:sectPr><w:footerReference w:type="default" r:id="rId2"/><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1000" w:right="1134" w:bottom="1000" w:left="1134" w:header="400" w:footer="450"/></w:sectPr></w:body></w:document>'
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
} finally { $archive.Dispose(); $stream.Dispose() }
$check = [System.IO.Compression.ZipFile]::OpenRead($destination)
try {
    foreach ($entry in $check.Entries) {
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { $null = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    Write-Output "DOCX verified: $($check.Entries.Count) XML parts; $((Get-Item -LiteralPath $destination).Length) bytes."
} finally { $check.Dispose() }
