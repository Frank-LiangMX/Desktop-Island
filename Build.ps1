param([string]$OutputPath=(Join-Path $PSScriptRoot '性能监视器.exe'),[string]$Probe)
$ErrorActionPreference='Stop'
$fx="$env:windir\Microsoft.NET\Framework64\v4.0.30319"
$winmd=Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\UnionMetadata\10.*\Windows.winmd" | Sort-Object { [version]$_.Directory.Name } -Descending | Select-Object -First 1
$runtime=(Get-ChildItem "$env:windir\Microsoft.NET\assembly\GAC_MSIL\System.Runtime.WindowsRuntime" -Recurse -Filter System.Runtime.WindowsRuntime.dll | Select-Object -First 1).FullName
$facades="${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades"
$refs=@('/r:System.Security.dll','/r:System.Runtime.Serialization.dll', "/r:$fx\WPF\UIAutomationClient.dll","/r:$fx\WPF\UIAutomationTypes.dll","/r:$($winmd.FullName)","/r:$runtime","/r:$facades\System.Runtime.dll","/r:$facades\System.Runtime.InteropServices.WindowsRuntime.dll","/r:$facades\System.Collections.dll","/r:$fx\WPF\PresentationFramework.dll","/r:$fx\WPF\PresentationCore.dll","/r:$fx\WPF\WindowsFormsIntegration.dll","/r:$fx\WPF\WindowsBase.dll",'/r:System.Xaml.dll','/r:System.Management.dll')
$src=@(Get-ChildItem "$PSScriptRoot\*.cs" | ForEach-Object FullName)
$mode='/target:winexe'
if($Probe){$mode='/target:exe';$src+=@('/main:Pulse.ActivityProbe',$Probe)}
& "$fx\csc.exe" /nologo $mode /platform:x64 /optimize+ "/out:$OutputPath" "/resource:$PSScriptRoot\Ranking.xaml,Ranking.xaml" @refs @src
if($LASTEXITCODE){throw 'Compilation failed'}

