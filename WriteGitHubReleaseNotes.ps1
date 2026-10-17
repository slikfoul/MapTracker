param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'packaging\manifest.json') -Raw | ConvertFrom-Json
$version = $manifest.version_number
$changelog = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'CHANGELOG.release.md'))
$section = [regex]::Match($changelog, '(?ms)^## ' + [regex]::Escape($version) + '\r?\n(?<changes>.*?)(?=^## |\z)')
if (-not $section.Success) { throw 'Current public changelog section not found.' }
$changes = $section.Groups['changes'].Value.Trim()
if (-not $changes -or $changes -match '(?im)^#{1,6}\s+(Updating|Installation)\s*$') { throw 'GitHub release notes must contain changes only.' }
if (-not $OutputPath) { $OutputPath = Join-Path $PSScriptRoot ("artifacts\github-release-$version.md") }
$fullPath = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullPath)) | Out-Null
[IO.File]::WriteAllText($fullPath, "$changes`r`n", [Text.UTF8Encoding]::new($false))
