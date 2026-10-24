#requires -Version 7
# Sinh snapshot-sample.pdf: tài liệu mẫu 3 trang, chữ ASCII, không có dữ liệu cá nhân.
# Dùng cho ảnh chụp PR (scripts/snapshot.ps1 -Open). Chạy lại script này để tạo lại file.
param([string]$Out = (Join-Path $PSScriptRoot 'snapshot-sample.pdf'), [int]$PageCount = 3)

$objects = [Collections.Generic.List[string]]::new()
$objects.Add('<< /Type /Catalog /Pages 2 0 R >>')
$kids = (1..$PageCount | ForEach-Object { "$(2 + $_) 0 R" }) -join ' '
$objects.Add("<< /Type /Pages /Kids [$kids] /Count $PageCount >>")
$fontId = 3 + 2 * $PageCount
foreach ($page in 1..$PageCount) {
    $contentId = 2 + $PageCount + $page
    $objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 $fontId 0 R >> >> /Contents $contentId 0 R >>")
}
foreach ($page in 1..$PageCount) {
    $ops = [Text.StringBuilder]::new()
    [void]$ops.Append("BT /F1 28 Tf 72 760 Td (Tri Thu - sample document) Tj ET`n")
    [void]$ops.Append("BT /F1 14 Tf 72 720 Td (Page $page of $PageCount) Tj ET`n")
    [void]$ops.Append("BT /F1 12 Tf 72 690 Td (Generated for UI screenshots by scripts/snapshot.ps1. No personal data.) Tj ET`n")
    for ($line = 1; $line -le 24; $line++) {
        $y = 650 - 22 * $line
        [void]$ops.Append("BT /F1 11 Tf 72 $y Td (Line $line on page ${page}: lorem ipsum dolor sit amet, consectetur adipiscing elit.) Tj ET`n")
    }
    $stream = $ops.ToString()
    $objects.Add("<< /Length $($stream.Length) >>`nstream`n$($stream)endstream")
}
$objects.Add('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>')

$pdf = [Text.StringBuilder]::new("%PDF-1.4`n")
$offsets = foreach ($i in 0..($objects.Count - 1)) {
    $pdf.Length
    [void]$pdf.Append("$($i + 1) 0 obj`n$($objects[$i])`nendobj`n")
}
$xref = $pdf.Length
[void]$pdf.Append("xref`n0 $($objects.Count + 1)`n0000000000 65535 f `n")
foreach ($offset in $offsets) { [void]$pdf.Append("{0:D10} 00000 n `n" -f $offset) }
[void]$pdf.Append("trailer`n<< /Size $($objects.Count + 1) /Root 1 0 R >>`nstartxref`n$xref`n%%EOF`n")
[IO.File]::WriteAllText($Out, $pdf.ToString(), [Text.Encoding]::ASCII)
Write-Output $Out
