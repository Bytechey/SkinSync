[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$VersionTag = "",

    [Parameter(Mandatory = $false)]
    [string]$OutputDir = "build/release-assets"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$projPath = Join-Path $repoRoot "SkinSync.csproj"
$pkgPrefix = "SkinSync"
$pluginDir = "SkinSync"
$expectedNamespace = "SkinSyncMod"

if (-not (Test-Path -LiteralPath $projPath)) {
    throw "未找到项目文件：'$projPath'"
}

[xml]$projXml = Get-Content -LiteralPath $projPath -Raw -Encoding UTF8
$versionNode = $projXml.SelectSingleNode("/Project/PropertyGroup/Version[normalize-space() != '']")
if ($null -eq $versionNode) {
    throw "无法从 '$projPath' 读取非空的 <Version>。"
}
$modVersion = $versionNode.InnerText.Trim()

$asmNode = $projXml.SelectSingleNode("/Project/PropertyGroup/AssemblyName[normalize-space() != '']")
if ($null -eq $asmNode) {
    throw "无法从 '$projPath' 读取非空的 <AssemblyName>。"
}
$assemblyName = $asmNode.InnerText.Trim()

if ([string]::IsNullOrWhiteSpace($VersionTag)) {
    $VersionTag = "v$modVersion"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "build\release-assets"
} elseif ([System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
} else {
    $OutputDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}

# 使用原生 PEReader + MetadataReader 校验 PE 元数据，不加载运行期依赖也不锁磁盘文件
if (-not ("SkinSyncPeMetadataVerifier" -as [type])) {
    Add-Type -ReferencedAssemblies @("System.Reflection.Metadata", "System.Collections.Immutable") -TypeDefinition @"
using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

public static class SkinSyncPeMetadataVerifier
{
    private const int DisableOptimizationsFlag = 0x0100;

    public static string Verify(string dllPath, string expectedNamespace, string expectedVersion, bool expectDisableOptimizations)
    {
        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException("未找到编译产物。", dllPath);
        }

        using var fs = File.OpenRead(dllPath);
        using var peReader = new PEReader(fs);
        if (!peReader.HasMetadata)
        {
            throw new InvalidOperationException("PE 文件不包含 CLI 元数据：" + dllPath);
        }

        MetadataReader mr = peReader.GetMetadataReader();

        bool foundType = false;
        bool foundField = false;
        string actualVersion = null;

        foreach (TypeDefinitionHandle tdh in mr.TypeDefinitions)
        {
            TypeDefinition td = mr.GetTypeDefinition(tdh);
            if (mr.GetString(td.Name) == "BuildInfo" && mr.GetString(td.Namespace) == expectedNamespace)
            {
                foundType = true;
                foreach (FieldDefinitionHandle fdh in td.GetFields())
                {
                    FieldDefinition fd = mr.GetFieldDefinition(fdh);
                    if (mr.GetString(fd.Name) == "Version")
                    {
                        foundField = true;
                        ConstantHandle ch = fd.GetDefaultValue();
                        if (ch.IsNil)
                        {
                            throw new InvalidOperationException(expectedNamespace + ".BuildInfo.Version 不是编译期常量字段 (" + dllPath + ")。");
                        }
                        Constant constant = mr.GetConstant(ch);
                        BlobReader cbr = mr.GetBlobReader(constant.Value);
                        actualVersion = cbr.ReadConstant(constant.TypeCode) as string;
                        break;
                    }
                }
                break;
            }
        }

        if (!foundType || !foundField)
        {
            throw new InvalidOperationException("未在元数据中找到 " + expectedNamespace + ".BuildInfo.Version 常量字段 (" + dllPath + ")。");
        }
        if (!string.Equals(actualVersion, expectedVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("BuildInfo.Version 不匹配 (" + dllPath + ")：实际='" + actualVersion + "'，期望='" + expectedVersion + "'。");
        }

        AssemblyDefinition asmDef = mr.GetAssemblyDefinition();
        bool foundDebuggable = false;
        bool hasDisableOptimizations = false;
        int rawModes = 0;

        foreach (CustomAttributeHandle cah in asmDef.GetCustomAttributes())
        {
            CustomAttribute ca = mr.GetCustomAttribute(cah);
            if (!TryGetAttributeTypeAndParamCount(mr, ca.Constructor, out string attrNs, out string attrName, out int paramCount))
            {
                continue;
            }
            if (attrNs != "System.Diagnostics" || attrName != "DebuggableAttribute")
            {
                continue;
            }

            foundDebuggable = true;
            BlobReader vbr = mr.GetBlobReader(ca.Value);
            ushort prolog = vbr.ReadUInt16();
            if (prolog != 0x0001)
            {
                throw new InvalidOperationException("DebuggableAttribute prolog 异常：0x" + prolog.ToString("X4") + " (" + dllPath + ")。");
            }

            if (paramCount == 1)
            {
                rawModes = vbr.ReadInt32();
                hasDisableOptimizations = (rawModes & DisableOptimizationsFlag) != 0;
            }
            else if (paramCount == 2)
            {
                bool track = vbr.ReadBoolean();
                bool disableOpt = vbr.ReadBoolean();
                rawModes = (track ? 0x01 : 0) | (disableOpt ? DisableOptimizationsFlag : 0);
                hasDisableOptimizations = disableOpt;
            }
            else
            {
                throw new InvalidOperationException("未知的 DebuggableAttribute 构造函数参数数量：" + paramCount + " (" + dllPath + ")。");
            }
            break;
        }

        if (!foundDebuggable)
        {
            throw new InvalidOperationException("AssemblyDefinition 缺少 DebuggableAttribute (" + dllPath + ")。");
        }

        if (hasDisableOptimizations != expectDisableOptimizations)
        {
            throw new InvalidOperationException(
                "DebuggableAttribute.DisableOptimizations 校验失败 (" + dllPath + ")：实际=" + hasDisableOptimizations
                + " (Modes=0x" + rawModes.ToString("X") + ")，期望=" + expectDisableOptimizations + "。");
        }

        return "BuildInfo.Version=" + actualVersion + ", DebuggingModes=0x" + rawModes.ToString("X") + ", DisableOptimizations=" + hasDisableOptimizations;
    }

    private static bool TryGetAttributeTypeAndParamCount(
        MetadataReader mr,
        EntityHandle ctorHandle,
        out string typeNamespace,
        out string typeName,
        out int paramCount)
    {
        typeNamespace = null;
        typeName = null;
        paramCount = 0;

        if (ctorHandle.Kind == HandleKind.MemberReference)
        {
            MemberReference memberRef = mr.GetMemberReference((MemberReferenceHandle)ctorHandle);
            BlobReader sigReader = mr.GetBlobReader(memberRef.Signature);
            sigReader.ReadSignatureHeader();
            paramCount = sigReader.ReadCompressedInteger();

            EntityHandle parent = memberRef.Parent;
            if (parent.Kind == HandleKind.TypeReference)
            {
                TypeReference tr = mr.GetTypeReference((TypeReferenceHandle)parent);
                typeNamespace = mr.GetString(tr.Namespace);
                typeName = mr.GetString(tr.Name);
                return true;
            }
            if (parent.Kind == HandleKind.TypeDefinition)
            {
                TypeDefinition td = mr.GetTypeDefinition((TypeDefinitionHandle)parent);
                typeNamespace = mr.GetString(td.Namespace);
                typeName = mr.GetString(td.Name);
                return true;
            }
        }
        else if (ctorHandle.Kind == HandleKind.MethodDefinition)
        {
            MethodDefinition methodDef = mr.GetMethodDefinition((MethodDefinitionHandle)ctorHandle);
            BlobReader sigReader = mr.GetBlobReader(methodDef.Signature);
            sigReader.ReadSignatureHeader();
            paramCount = sigReader.ReadCompressedInteger();

            TypeDefinition td = mr.GetTypeDefinition(methodDef.GetDeclaringType());
            typeNamespace = mr.GetString(td.Namespace);
            typeName = mr.GetString(td.Name);
            return true;
        }

        return false;
    }
}
"@
}

function New-PluginZipArchive {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string[]]$SourceFiles,
        [Parameter(Mandatory = $true)][string]$PluginSubDir,
        [Parameter(Mandatory = $true)][int]$ExpectedCount
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }

    # 显式使用正斜杠写入 ZIP 条目名，确保 Windows 与 Linux 解压器均识别标准目录结构
    $fs = [System.IO.File]::Open($ZipPath, [System.IO.FileMode]::CreateNew)
    try {
        $zip = [System.IO.Compression.ZipArchive]::new($fs, [System.IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($filePath in $SourceFiles) {
                if (-not (Test-Path -LiteralPath $filePath)) {
                    throw "打包缺失源文件：'$filePath'"
                }
                $leafName = Split-Path -Leaf $filePath
                $entryName = "BepInEx/plugins/$PluginSubDir/$leafName"
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $zip,
                    $filePath,
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal
                ) | Out-Null
            }
        } finally {
            $zip.Dispose()
        }
    } finally {
        $fs.Dispose()
    }

    $verifyZip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        if ($verifyZip.Entries.Count -ne $ExpectedCount) {
            throw "ZIP 条目数异常 ($ZipPath)：实际=$($verifyZip.Entries.Count), 期望=$ExpectedCount"
        }
        foreach ($entry in $verifyZip.Entries) {
            if ($entry.FullName.Contains("\")) {
                throw "ZIP 条目包含反斜杠路径 ($ZipPath)：'$($entry.FullName)'"
            }
            if (-not $entry.FullName.StartsWith("BepInEx/plugins/$PluginSubDir/", [System.StringComparison]::Ordinal)) {
                throw "ZIP 条目路径前缀异常 ($ZipPath)：'$($entry.FullName)'"
            }
            if ($entry.Length -le 0) {
                throw "ZIP 条目内容为空 ($ZipPath)：'$($entry.FullName)'"
            }
        }
    } finally {
        $verifyZip.Dispose()
    }
}

Write-Host ">>> 正在还原依赖 ($projPath) ..." -ForegroundColor Cyan
& dotnet restore $projPath --nologo -v minimal | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore 失败，退出码：$LASTEXITCODE"
}

foreach ($cfg in @("Release", "Debug")) {
    Write-Host ">>> 正在构建 $cfg (VersionTag=$VersionTag, Version=$modVersion, AssemblyName=$assemblyName) ..." -ForegroundColor Cyan
    & dotnet build $projPath -c $cfg --no-incremental --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build -c $cfg 失败，退出码：$LASTEXITCODE"
    }
}

$releaseDll = Join-Path $repoRoot "bin\Release\$assemblyName.dll"
$debugDll = Join-Path $repoRoot "bin\Debug\$assemblyName.dll"
$debugPdb = Join-Path $repoRoot "bin\Debug\$assemblyName.pdb"

if (-not (Test-Path -LiteralPath $debugPdb)) {
    throw "Debug 构建未生成调试符号文件：'$debugPdb'"
}
$pdbSize = (Get-Item -LiteralPath $debugPdb).Length
if ($pdbSize -le 0) {
    throw "Debug 调试符号文件为空：'$debugPdb'"
}

$relMetaSummary = [SkinSyncPeMetadataVerifier]::Verify($releaseDll, $expectedNamespace, $modVersion, $false)
Write-Host "    [PE 校验通过] Release ($assemblyName.dll): $relMetaSummary" -ForegroundColor DarkGreen

$dbgMetaSummary = [SkinSyncPeMetadataVerifier]::Verify($debugDll, $expectedNamespace, $modVersion, $true)
Write-Host "    [PE 校验通过] Debug ($assemblyName.dll): $dbgMetaSummary, PdbBytes=$pdbSize" -ForegroundColor DarkGreen

$docFiles = @()
foreach ($docCandidate in @("README.md", "README.en.md", "changes.md", "changes.en.md")) {
    $docFullPath = Join-Path $repoRoot $docCandidate
    if (Test-Path -LiteralPath $docFullPath) {
        $docFiles += $docFullPath
    }
}

if (Test-Path -LiteralPath $OutputDir) {
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$releaseZipName = "$pkgPrefix-$VersionTag.zip"
$debugZipName = "$pkgPrefix-$VersionTag+debug.zip"
$singleReleaseDllName = "$assemblyName.dll"
$singleDebugDllName = "$assemblyName+debug.dll"

$releaseZipPath = Join-Path $OutputDir $releaseZipName
$debugZipPath = Join-Path $OutputDir $debugZipName
$singleReleaseDllPath = Join-Path $OutputDir $singleReleaseDllName
$singleDebugDllPath = Join-Path $OutputDir $singleDebugDllName

$releaseSources = @($releaseDll) + $docFiles
$debugSources = @($debugDll, $debugPdb) + $docFiles

New-PluginZipArchive -ZipPath $releaseZipPath -SourceFiles $releaseSources -PluginSubDir $pluginDir -ExpectedCount $releaseSources.Count
New-PluginZipArchive -ZipPath $debugZipPath -SourceFiles $debugSources -PluginSubDir $pluginDir -ExpectedCount $debugSources.Count

Copy-Item -LiteralPath $releaseDll -Destination $singleReleaseDllPath -Force
Copy-Item -LiteralPath $debugDll -Destination $singleDebugDllPath -Force

$assetNames = @(
    $releaseZipName,
    $debugZipName,
    $singleReleaseDllName,
    $singleDebugDllName
)

$hashLines = foreach ($name in $assetNames) {
    $filePath = Join-Path $OutputDir $name
    if (-not (Test-Path -LiteralPath $filePath) -or (Get-Item -LiteralPath $filePath).Length -le 0) {
        throw "发布资产缺失或为空：'$filePath'"
    }
    $sha256 = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$sha256  $name"
}

$sumsPath = Join-Path $OutputDir "SHA256SUMS.txt"
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($sumsPath, (($hashLines -join "`n") + "`n"), $utf8NoBom)

Write-Host ">>> 打包完成，5 项标准发布资产已输出至: $OutputDir" -ForegroundColor Green
$hashLines | ForEach-Object { Write-Host "    $_" }
