param (
    [Parameter(Mandatory=$false)]
    [string]$Version
)

if (-not $Version -or $Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Error "Version missing or invalid. Format must be X.Y.Z (e.g. 1.2.3)"
    exit 1
}

$FullVersion = "$Version.0"

Write-Host "Publishing app version $FullVersion..."
dotnet publish src/PdfReaderApp/PdfReaderApp.csproj -c Release -r win-x64 --self-contained true -p:Version=$FullVersion -o publish/win-x64
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed"
    exit 1
}

Write-Host "Stamping manifest version..."
$PublishManifestPath = "publish/win-x64/Package.appxmanifest"
Copy-Item -Force "Package.appxmanifest" $PublishManifestPath

$xml = [xml](Get-Content $PublishManifestPath)
$xml.Package.Identity.Version = $FullVersion
$xml.Save((Resolve-Path $PublishManifestPath).Path)

if (Test-Path "Assets") {
    Copy-Item -Recurse -Force "Assets" "publish/win-x64/"
}

Write-Host "Packaging MSIX..."
# Generate a dummy dev cert if we don't have one for testing? Ticket says:
# "Given dev cert self-signed đã cài (một lần). When cài file .msix vừa build và mở app. Then app khởi động..."
# So the script should probably use --generate-cert or require a cert? 
# Wait, winapp package has --generate-cert. Let's just create the msix without signing, or with a dummy cert?
# Actually, the user says "dev cert self-signed đã cài". This means the cert is generated elsewhere or I can use --generate-cert and --install-cert here if they want? 
# Let's generate a cert if it doesn't exist.
if (-not (Test-Path "devcert.pfx")) {
    Write-Host "Generating devcert.pfx for testing..."
    winapp cert generate --output devcert.pfx --password password --publisher "CN=Placeholder"
}

winapp package publish/win-x64 --output "PdfReaderApp_$Version.msix" --cert devcert.pfx --cert-password password
if ($LASTEXITCODE -ne 0) {
    Write-Error "MSIX Packaging failed"
    exit 1
}

Write-Host "Successfully created PdfReaderApp_$Version.msix"
