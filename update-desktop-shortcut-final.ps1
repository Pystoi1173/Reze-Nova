$root = $PSScriptRoot
$desktop = [Environment]::GetFolderPath('Desktop')
$languageName = -join ([char]0x044F, [char]0x0437, [char]0x044B, [char]0x043A)
$shortcutPath = Join-Path $desktop ($languageName + '.lnk')
$target = Join-Path $root 'dist\Reze-Nova-final.exe'
$icon = Join-Path $root 'dist\assets\ui\language.ico'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcutPath)
$link.TargetPath = $target
$link.Arguments = 'ui'
$link.WorkingDirectory = (Join-Path $root 'dist')
$link.Description = 'Reze Nova network center'
$link.IconLocation = $icon + ',0'
$link.Save()
Get-Item -LiteralPath $shortcutPath | Select-Object Name, FullName, Length
