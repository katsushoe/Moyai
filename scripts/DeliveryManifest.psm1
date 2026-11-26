Set-StrictMode -Version Latest

function Get-MoyaiDeliveryInputs {
    param([Parameter(Mandatory)][string]$RepositoryRoot)
    $repoPath=(Resolve-Path -LiteralPath $RepositoryRoot).Path.TrimEnd('\','/')
    $excluded=@('.git','.local','artifacts','bin','obj','.wix','.codex','.agents','node_modules','vendor','TestResults')
    $directories=[Collections.Generic.Stack[string]]::new()
    foreach($name in @('src','tests','tools','scripts','installer','docs','.config','.github')){
        $directory=Join-Path $repoPath $name
        if(Test-Path -LiteralPath $directory){$directories.Push($directory)}
    }
    $paths=[Collections.Generic.List[string]]::new()
    foreach($file in Get-ChildItem -LiteralPath $repoPath -File -Force){
        if($file.Name -in @('AGENTS.md','CLAUDE.md')){continue}
        if($file.Extension -notin @('.md','.props','.targets','.sln','.slnx','.json','.yml','.yaml') -and $file.Name -notin @('.editorconfig','.gitattributes','.gitignore','LICENSE')){continue}
        if($file.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked delivery input is not supported.'}
        $paths.Add($file.FullName)
    }
    while($directories.Count -gt 0){
        $directory=$directories.Pop()
        if((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked input directory is not supported.'}
        foreach($entry in Get-ChildItem -LiteralPath $directory -Force){
            if($entry.PSIsContainer){if($entry.Name -notin $excluded){$directories.Push($entry.FullName)}}
            else{
                if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked delivery input is not supported.'}
                $paths.Add($entry.FullName)
            }
        }
    }
    $ordered=$paths.ToArray();[Array]::Sort($ordered,[StringComparer]::Ordinal)
    foreach($filePath in $ordered){
        $file=Get-Item -LiteralPath $filePath
        [pscustomobject][ordered]@{path=$file.FullName.Substring($repoPath.Length+1).Replace('\','/');bytes=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
    }
}

function Test-MoyaiDeliveryInputs {
    param([Parameter(Mandatory)][string]$RepositoryRoot,[Parameter(Mandatory)]$Manifest)
    if($Manifest.schemaVersion -ne 1){throw 'Unsupported input manifest schema.'}
    $expected=@($Manifest.files);$current=@(Get-MoyaiDeliveryInputs -RepositoryRoot $RepositoryRoot)
    if($current.Count -ne $expected.Count){throw 'Delivery input membership changed.'}
    for($i=0;$i -lt $current.Count;$i++){
        if($current[$i].path -cne $expected[$i].path -or $current[$i].bytes -ne $expected[$i].bytes -or $current[$i].sha256 -cne $expected[$i].sha256){throw 'Delivery input path, size or hash changed.'}
    }
    return $current.Count
}

Export-ModuleMember -Function Get-MoyaiDeliveryInputs,Test-MoyaiDeliveryInputs
