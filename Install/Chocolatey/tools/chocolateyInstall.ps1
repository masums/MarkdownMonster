$packageName = 'markdownmonster'
$fileType = 'exe'
$url = 'https://github.com/RickStrahl/MarkdownMonsterReleases/raw/master/v1.7/MarkdownMonsterSetup-1.8.4.exe'

$silentArgs = '/VERYSILENT'
$validExitCodes = @(0)

Install-ChocolateyPackage "packageName" "$fileType" "$silentArgs" "$url"  -validExitCodes  $validExitCodes  -checksum "D6EBF8AC6B9E864DD12818739E134CA5C29C4CEC04FA4CD671A2F8640F6190FA" -checksumType "sha256"
