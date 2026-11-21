$root = $PSScriptRoot
$assets = Join-Path $root 'assets\ui'
$desktop = [Environment]::GetFolderPath('Desktop')
$languageName = -join ([char]0x044F, [char]0x0437, [char]0x044B, [char]0x043A)
$languageImage = Get-ChildItem -LiteralPath $desktop -Filter '*.jpg' -Force | Where-Object { $_.BaseName -notmatch '^[0-9a-f]{32}$' -and $_.BaseName -notlike 'photo_*' } | Select-Object -First 1
if ($languageImage) { Move-Item -LiteralPath $languageImage.FullName -Destination (Join-Path $assets 'language-source.jpg') -Force }
$target = Join-Path $root 'dist\Reze-Nova.exe'
$shortcutPath = Join-Path $desktop ($languageName + '.lnk')
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcutPath)
$link.TargetPath = $target
$link.Arguments = 'ui'
$link.WorkingDirectory = (Join-Path $root 'dist')
$link.Description = 'Reze Nova network center'
$link.IconLocation = "$target,0"
$link.Save()
Get-Item -LiteralPath $shortcutPath | Select-Object Name, FullName, Length
