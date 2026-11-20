[CmdletBinding()]
param(
    [string]$PetAssets,
    [string]$CurtainGif
)

$ErrorActionPreference = 'Stop'
if (!$PetAssets -and !$CurtainGif) {
    throw '请指定 -PetAssets 或 -CurtainGif。'
}

$planned = [System.Collections.Generic.List[object]]::new()

function Add-Asset {
    param([string]$Source, [string]$Destination, [string]$Hash)

    if ($Hash -and (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash -ne $Hash) {
        throw "素材校验失败：$Source"
    }
    $planned.Add(@{ Source = $Source; Destination = $Destination })
}

if ($PetAssets) {
    $petFolder = (Resolve-Path -LiteralPath $PetAssets).Path
    $petTarget = Join-Path $PSScriptRoot 'windows/assets'
    $baseHashes = @{
        'compact-scene.png' = '67cf9f967fe6bb3370dbaa1a6501e4f5a1705417c6e220e5648f1eed3a91abe7'
        'compact-cleanplate.png' = 'd9fb6272ef3f0b79ecee602f133a3c585110eab628389c9b1955a45d6b0995ba'
        'compact-paws.png' = '663d0edec1eb9b14781c1216f22b00b8a804a02852e0e7b261a257f16817c481'
        'YierBubu.ico' = 'fdc233df0a423c45fc7d9b90c48fb232feb48179127ec02cae070efe90c5d9e1'
    }
    foreach ($file in $baseHashes.Keys) {
        Add-Asset (Join-Path $petFolder $file) (Join-Path $petTarget $file) $baseHashes[$file]
    }

    $catalogs = @{ Animations = 'animation-catalog.json'; Keyboards = 'keyboard-catalog.json' }
    foreach ($folder in $catalogs.Keys) {
        $catalog = Join-Path $PSScriptRoot ('windows/' + $catalogs[$folder])
        $targetFolder = Join-Path $petTarget $folder
        foreach ($entry in (Get-Content -LiteralPath $catalog -Raw | ConvertFrom-Json).animations) {
            if ($entry.file -notmatch '^[a-z0-9_-]+\.(gif|png)$') { throw '素材目录包含无效文件名。' }
            Add-Asset (Join-Path (Join-Path $petFolder $folder) $entry.file) (Join-Path $targetFolder $entry.file) $entry.sha256
        }
        Add-Asset $catalog (Join-Path $targetFolder 'catalog.json')
    }
}

if ($CurtainGif) {
    Add-Asset (Resolve-Path -LiteralPath $CurtainGif).Path (Join-Path $PSScriptRoot 'SimulationCurtain/Assets/desk-break-hd.gif') `
        '04c94374d87407b0b563252cd6455e94297ec885c680782a2095d8b5f96251b1'
}

foreach ($asset in $planned) {
    $parent = Split-Path -Parent $asset.Destination
    New-Item -Path $parent -ItemType Directory -Force | Out-Null
    Copy-Item -LiteralPath $asset.Source -Destination $asset.Destination -Force
}
Write-Output "已导入 $($planned.Count) 个本地资源和目录文件。"
