param([Parameter(Mandatory=$true)][string]$GamePath)
$ErrorActionPreference='Stop'
$GamePath=(Resolve-Path -LiteralPath $GamePath).Path
$root=$PSScriptRoot;$managed=Join-Path $GamePath 'Crime Simulator_Data\Managed';$core=Join-Path $GamePath 'BepInEx\core';$out=Join-Path $root 'dist'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not(Test-Path -LiteralPath $compiler)){throw 'The .NET Framework C# compiler is required'}
New-Item -ItemType Directory -Path $out -Force|Out-Null
$names=@('mscorlib.dll','netstandard.dll','System.dll','System.Core.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.UIModule.dll','UnityEngine.TextRenderingModule.dll','UnityEngine.AudioModule.dll','Assembly-CSharp.dll','Fusion.Runtime.dll','Fusion.Common.dll','Fusion.Sockets.dll','Fusion.Log.dll','AstarPathfindingProject.dll','UnityEngine.AnimationModule.dll','PackageTools.dll','Drawing.dll','UnityEngine.ScreenCaptureModule.dll','UnityEngine.UI.dll','Unity.TextMeshPro.dll','UnityEngine.ImageConversionModule.dll','UnityEngine.ParticleSystemModule.dll','Rewired_Core.dll')
$lines=@('/nologo','/target:library','/nostdlib+','/optimize+',('/out:"'+(Join-Path $out 'JujutsuMod.dll')+'"'))
foreach($name in $names){$path=Join-Path $managed $name;if(-not(Test-Path -LiteralPath $path)){throw ('Missing game assembly: '+$name)};$lines+='/reference:"'+$path+'"'}
foreach($name in @('BepInEx.dll','0Harmony.dll')){$path=Join-Path $core $name;if(-not(Test-Path -LiteralPath $path)){throw ('Install BepInEx5 first; missing '+$name)};$lines+='/reference:"'+$path+'"'}
foreach($name in @('Rules.cs','JujutsuMod.cs','SukunaFx.cs','ShrineModel.cs','FugaModel.cs','SkillHud.cs')){$lines+='"'+(Join-Path $root ('src\'+$name))+'"'}
foreach($entry in @(@('slash.png','jjk.slash.png'),@('shrine-wood.png','jjk.shrine.wood.png'),@('fuga-flame.png','jjk.fuga.flame.png'))){$lines+='/resource:"'+(Join-Path $root ('assets\'+$entry[0]))+'",'+$entry[1]}
$response=Join-Path $out 'build.rsp';[IO.File]::WriteAllLines($response,$lines,[Text.UTF8Encoding]::new($true))
& $compiler /noconfig ('@'+$response)
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& $compiler /nologo ('/out:'+(Join-Path $out 'TestRules.exe')) (Join-Path $root 'src\Rules.cs') (Join-Path $root 'src\TestRules.cs')
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& (Join-Path $out 'TestRules.exe')
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
Write-Output 'BUILD PASS: dist/JujutsuMod.dll and rule checks'
