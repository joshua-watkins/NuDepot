<#
.SYNOPSIS
Builds a NuDepot release: the Windows installers, the Linux archives and the NuGet packages, all with one version.

.DESCRIPTION
The version is the build's date and time in UTC, like 26.11.14.26100: the year's last two digits, the month, the day,
and the seconds since midnight divided by two. Releases built the same day share the first three parts, which are all
Windows Installer compares, so the installer replaces an installed NuDepot of the same day.

The script cleans every project's bin and obj folders first, so no assemblies from an earlier build end up in the
release, then builds and tests, and stops at the first failure. Close Visual Studio, or at least stop debugging,
before running it, since it deletes the build folders.

Everyday builds, like Visual Studio's, keep the placeholder version in Directory.Build.props.

.PARAMETER Version
The version to build, instead of the date and time, like 26.11.14.1.

.PARAMETER Output
The folder the release goes in. Default: artifacts\<version> in the repository.

.EXAMPLE
pwsh build/release.ps1
#>
param(
    [string] $Version,
    [string] $Output
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if (-not $Version)
{
    $now = [DateTime]::UtcNow
    $Version = '{0}.{1}.{2}.{3}' -f ($now.Year % 100), $now.Month, $now.Day, [int][Math]::Floor($now.TimeOfDay.TotalSeconds / 2)
}

if (-not $Output)
{
    $Output = Join-Path $root "artifacts\$Version"
}

# The files' version has four parts, like 26.11.14.26100, or 1.2.3.0 for 1.2.3.
$numbers = [Collections.Generic.List[string]]($Version.Split('-')[0].Split('.'))
while ($numbers.Count -lt 4)
{
    $numbers.Add('0')
}
$fileVersion = $numbers -join '.'

# NuGet leaves out a fourth part that's 0 in package file names.
$packageVersion = if ($numbers[3] -eq '0') { ($numbers[0..2] -join '.') + $Version.Substring($Version.Split('-')[0].Length) } else { $Version }

function Invoke-Dotnet
{
    & dotnet @args
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Assert-Version([string] $what, [string] $actual)
{
    if ($actual -ne $fileVersion)
    {
        throw "$what has version '$actual', not $fileVersion."
    }

    Write-Host "  $what`: $actual"
}

function Get-MsiProductVersion([string] $path)
{
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($path, 0))
    $view = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database, @("SELECT Value FROM Property WHERE Property='ProductVersion'"))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
    $version = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, 1)
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
    return $version
}

Push-Location $root
try
{
    if (Test-Path $Output)
    {
        throw "$Output already exists."
    }

    Write-Host "Building NuDepot $Version"

    Write-Host 'Cleaning the build folders'
    Get-ChildItem src, pub, test -Recurse -File -Include *.csproj, *.wixproj | ForEach-Object {
        foreach ($folder in 'bin', 'obj')
        {
            $path = Join-Path $_.DirectoryName $folder
            if (Test-Path $path)
            {
                Remove-Item $path -Recurse -Force
            }
        }
    }

    # Each platform's solution build makes its installer. Conflicting assembly versions (MSB3277) mean an output with
    # the wrong assemblies, so they fail the build.
    $common = @('-c', 'Release', "-p:Version=$Version", '-warnaserror:MSB3277')
    foreach ($platform in 'x64', 'ARM64')
    {
        Write-Host "Building for $platform"
        Invoke-Dotnet build NuDepot.slnx "-p:Platform=$platform" @common
        if ($platform -eq 'x64')
        {
            Write-Host 'Testing'
            Invoke-Dotnet test --solution NuDepot.slnx -c Release '-p:Platform=x64' --no-build
        }
    }

    foreach ($runtime in 'linux-x64', 'linux-arm64')
    {
        Write-Host "Publishing for $runtime"
        Invoke-Dotnet publish pub/Linux/Linux.csproj -r $runtime --self-contained -o "pub/Linux/bin/Release/publish/$runtime" @common
    }

    Write-Host 'Packing the libraries'
    foreach ($library in 'Core', 'Feed', 'Gallery')
    {
        Invoke-Dotnet pack "src/$library/$library.csproj" -o "$Output\packages" @common
    }

    Write-Host 'Checking the versions'
    foreach ($architecture in 'x64', 'arm64')
    {
        $platform = if ($architecture -eq 'arm64') { 'ARM64' } else { 'x64' }
        $msi = "pub/Windows.Setup/bin/$platform/Release/en-US/NuDepot-$architecture.msi"
        Assert-Version "NuDepot-$architecture.msi" (Get-MsiProductVersion (Resolve-Path $msi).Path)
        Assert-Version "NuDepot.exe in NuDepot-$architecture.msi" (Get-Item "pub/Windows.Setup/obj/$platform/Release/publish/Windows/NuDepot.exe").VersionInfo.FileVersion
        Assert-Version "NuDepot.dll in NuDepot-linux-$architecture.tar.gz" (Get-Item "pub/Linux/bin/Release/publish/linux-$architecture/NuDepot.dll").VersionInfo.FileVersion
    }

    foreach ($library in 'Core', 'Feed', 'Gallery')
    {
        foreach ($extension in 'nupkg', 'snupkg')
        {
            $package = "$Output\packages\NuDepot.$library.$packageVersion.$extension"
            if (-not (Test-Path $package))
            {
                throw "$package wasn't made."
            }
        }
    }

    Write-Host 'Collecting the release'
    foreach ($architecture in 'x64', 'arm64')
    {
        $platform = if ($architecture -eq 'arm64') { 'ARM64' } else { 'x64' }
        Copy-Item "pub/Windows.Setup/bin/$platform/Release/en-US/NuDepot-$architecture.msi" $Output
        Copy-Item "pub/Linux/bin/Release/NuDepot-linux-$architecture.tar.gz" $Output
    }

    Write-Host ''
    Write-Host "NuDepot $Version is in $Output"
    Get-ChildItem $Output -Recurse -File | ForEach-Object { Write-Host "  $($_.FullName.Substring($Output.Length + 1))" }
}
finally
{
    Pop-Location
}
