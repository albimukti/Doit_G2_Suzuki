Add-Type -AssemblyName 'System.IO.Compression.FileSystem'
$docxPath = 'c:\Users\welcome\Downloads\Do-It G2\DO-IT_G2_Ringkasan_Client.docx'
$zip = [System.IO.Compression.ZipFile]::OpenRead($docxPath)
foreach($entry in $zip.Entries) {
    if($entry.Name -eq 'document.xml') {
        $stream = $entry.Open()
        $reader = New-Object System.IO.StreamReader($stream)
        $content = $reader.ReadToEnd()
        $reader.Close()
        $stream.Close()
        # Replace XML tags with newlines for readability
        $text = $content -replace '</w:p>', "`n"
        $text = $text -replace '<[^>]+>', ''
        $text = $text -replace '^\s*$\n', '' 
        Write-Output $text.Trim()
    }
}
$zip.Dispose()
