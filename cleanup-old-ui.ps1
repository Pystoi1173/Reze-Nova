$current = Get-Process -Name 'Reze-Nova-v2' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id
Get-Process -Name 'Reze-Nova' -ErrorAction SilentlyContinue | Where-Object { $current -notcontains $_.Id } | Stop-Process -Force
