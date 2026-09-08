[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("editmode", "playmode")]
    [string] $Mode,

    [Parameter(Position = 1)]
    [string] $ProjectPath = (Get-Location).Path,

    [string] $UnityEditor = $env:UNITY_EDITOR,

    [string] $TestFilter,

    [string] $AssemblyNames,

    [string] $Categories,

    [int] $McpPort
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$resolvedProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$projectVersionFile = Join-Path $resolvedProjectPath "ProjectSettings\ProjectVersion.txt"

if (-not (Test-Path -LiteralPath $projectVersionFile -PathType Leaf)) {
    throw "Not a Unity project: '$resolvedProjectPath' does not contain ProjectSettings\ProjectVersion.txt."
}

$versionLine = Select-String -LiteralPath $projectVersionFile -Pattern '^m_EditorVersion:\s*(.+)$' | Select-Object -First 1
if ($null -eq $versionLine) {
    throw "Could not determine the Unity version from '$projectVersionFile'."
}

$unityVersion = $versionLine.Matches[0].Groups[1].Value.Trim()

if ($McpPort -gt 0) {
    $mcpRunner = Join-Path $resolvedProjectPath "scripts/codex/unity-mcp-test.py"
    & python $mcpRunner $Mode $resolvedProjectPath --port $McpPort --assemblies "$AssemblyNames" --filter "$TestFilter" --categories "$Categories"
    if ($LASTEXITCODE -ne 0) { throw "Unity MCP test run failed. See '$resolvedProjectPath/Logs/codex-tests'." }
    return
}

if ([string]::IsNullOrWhiteSpace($UnityEditor)) {
    $programFiles = [Environment]::GetFolderPath("ProgramFiles")
    $hubEditor = Join-Path $programFiles "Unity\Hub\Editor\$unityVersion\Editor\Unity.exe"

    if (Test-Path -LiteralPath $hubEditor -PathType Leaf) {
        $UnityEditor = $hubEditor
    }
    else {
        $unityCommand = Get-Command "Unity.exe" -ErrorAction SilentlyContinue
        if ($null -eq $unityCommand) {
            $unityCommand = Get-Command "Unity" -ErrorAction SilentlyContinue
        }

        if ($null -ne $unityCommand) {
            $UnityEditor = $unityCommand.Source
        }
    }
}

if ([string]::IsNullOrWhiteSpace($UnityEditor) -or -not (Test-Path -LiteralPath $UnityEditor -PathType Leaf)) {
    throw "Unity $unityVersion was not found. Install it with Unity Hub or set UNITY_EDITOR to Unity.exe."
}

$testPlatform = if ($Mode -eq "editmode") { "EditMode" } else { "PlayMode" }
$resultsDirectory = Join-Path $resolvedProjectPath "Logs\codex-tests"
$resultsFile = Join-Path $resultsDirectory "$Mode-results.xml"
$logFile = Join-Path $resultsDirectory "$Mode.log"

New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
Remove-Item -LiteralPath $resultsFile -Force -ErrorAction SilentlyContinue

$unityArguments = @(
    "-batchmode",
    "-nographics",
    "-projectPath", $resolvedProjectPath,
    "-runTests",
    "-testPlatform", $testPlatform,
    "-testResults", $resultsFile,
    "-logFile", $logFile
)

if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
    $unityArguments += @("-testFilter", $TestFilter)
}

if (-not [string]::IsNullOrWhiteSpace($AssemblyNames)) {
    $unityArguments += @("-assemblyNames", $AssemblyNames)
}

if (-not [string]::IsNullOrWhiteSpace($Categories)) {
    $unityArguments += @("-testCategory", $Categories)
}

Write-Host "Running $testPlatform tests with:"
Write-Host "  Unity:   $UnityEditor"
Write-Host "  Version: $unityVersion"
Write-Host "  Project: $resolvedProjectPath"
Write-Host "  Results: $resultsFile"
Write-Host "  Log:     $logFile"

# Unity is a GUI executable on Windows; direct invocation can return before it
# exits and leave LASTEXITCODE unset. Test Framework exits when tests finish.
$quotedArguments = $unityArguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }
$unityProcess = Start-Process -FilePath $UnityEditor -ArgumentList $quotedArguments -WindowStyle Hidden -PassThru
$unityProcess.WaitForExit()
$unityExitCode = $unityProcess.ExitCode

if ($unityExitCode -ne 0) {
    throw "Unity exited with code $unityExitCode. See '$logFile'."
}

if (-not (Test-Path -LiteralPath $resultsFile -PathType Leaf)) {
    throw "Unity exited successfully but did not create '$resultsFile'. See '$logFile'."
}

[xml] $testResults = Get-Content -LiteralPath $resultsFile -Raw
$testRun = $testResults.DocumentElement
$failedCount = 0

if ($null -ne $testRun -and $testRun.HasAttribute("failed")) {
    $failedCount = [int] $testRun.GetAttribute("failed")
}

if ($null -eq $testRun -or $testRun.Name -ne "test-run" -or -not $testRun.HasAttribute("total") -or [int] $testRun.GetAttribute("total") -eq 0) {
    throw "Unity did not discover any tests. See '$resultsFile' and '$logFile'."
}

if ($failedCount -gt 0 -or $testRun.GetAttribute("result") -ne "Passed") {
    throw "$failedCount $testPlatform test(s) failed. See '$resultsFile' and '$logFile'."
}

Write-Host "Unity $testPlatform tests passed."
