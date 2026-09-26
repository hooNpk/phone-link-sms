# 단일 실행파일(dist\단체문자발송.exe) 만들기 — .NET 설치 없이 더블클릭으로 실행된다.
# 내부 어셈블리 이름은 PhoneLinkSms 그대로 두고, 배포 파일 이름만 바꾼다.
$ErrorActionPreference = 'Stop'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$exeName = '단체문자발송.exe'
$dist = "$PSScriptRoot\dist"

& $dotnet test "$PSScriptRoot\PhoneLinkSms.slnx" -nologo
if ($LASTEXITCODE -ne 0) { throw '테스트 실패 — 배포 중단' }

& $dotnet publish "$PSScriptRoot\PhoneLinkSmsApp\PhoneLinkSmsApp.csproj" -c Release -o $dist -nologo
if ($LASTEXITCODE -ne 0) { throw '빌드 실패' }

Move-Item "$dist\PhoneLinkSms.exe" "$dist\$exeName" -Force
Get-Item "$dist\$exeName" | Select-Object Name, @{n='MB'; e={[math]::Round($_.Length / 1MB, 1)}}
