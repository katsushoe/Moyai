param([Parameter(Mandatory)][string]$PublishDirectory)
$ErrorActionPreference='Stop'
$run=Join-Path ([IO.Path]::GetTempPath()) ('Moyai-ServiceCli-'+[Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory $run)
$cli=Join-Path $PublishDirectory 'moyaictl.exe'
$mcp=Join-Path $PublishDirectory 'Moyai.Mcp.exe'
$config=Join-Path $run 'moyai.json'
$probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
$probe.Start();$port=$probe.LocalEndpoint.Port;$probe.Stop()
@{databasePath='state.db';serverUrl="http://127.0.0.1:$port";requestTimeoutSeconds=5}|ConvertTo-Json|Set-Content $config
$checks=[Collections.Generic.List[object]]::new()
function Invoke-Cli([string[]]$Arguments,[int]$Expected=0) {
 $si=[Diagnostics.ProcessStartInfo]::new($cli)
 $si.UseShellExecute=$false;$si.CreateNoWindow=$true;$si.RedirectStandardOutput=$true;$si.RedirectStandardError=$true
 foreach($arg in ($Arguments+@('--config',$config))){$si.ArgumentList.Add($arg)}
 $si.Environment['MOYAI_DB_PATH']=Join-Path $run 'must-not-exist.db'
 $si.Environment['MOYAI_MCP_URL']='invalid'
 $p=[Diagnostics.Process]::Start($si)
 $out=$p.StandardOutput.ReadToEndAsync();$err=$p.StandardError.ReadToEndAsync()
 if(-not $p.WaitForExit(20000)){$p.Kill();throw 'CLI timed out'}
 $o=$out.GetAwaiter().GetResult();$e=$err.GetAwaiter().GetResult()
 if($p.ExitCode -ne $Expected){throw "$($Arguments[0]) expected $Expected, got $($p.ExitCode): $o $e"}
 $result=if($Expected -eq 0){$o|ConvertFrom-Json}else{$e|ConvertFrom-Json}
 if($Expected -eq 1 -and ($result.ok -ne $false -or -not $result.error)){throw 'Invalid error contract'}
 $checks.Add([pscustomobject]@{command=$Arguments[0];exit=$p.ExitCode;pass=$true})
 $p.Dispose();return $result
}
function Assert($condition,[string]$message){if(-not $condition){throw $message}}
$hostInfo=[Diagnostics.ProcessStartInfo]::new($mcp)
$hostInfo.UseShellExecute=$false;$hostInfo.CreateNoWindow=$true
$hostInfo.RedirectStandardOutput=$true;$hostInfo.RedirectStandardError=$true
$hostInfo.ArgumentList.Add('--config');$hostInfo.ArgumentList.Add($config)
$hostInfo.Environment['MOYAI_DB_PATH']=Join-Path $run 'wrong.db'
$hostInfo.Environment['MOYAI_MCP_URL']='invalid'
$hostInfo.Environment['ASPNETCORE_URLS']='http://0.0.0.0:1'
$hostProcess=[Diagnostics.Process]::Start($hostInfo)
$hostOut=$hostProcess.StandardOutput.ReadToEndAsync();$hostErr=$hostProcess.StandardError.ReadToEndAsync()
$actor=@('--actor-type','agent','--actor-name','validation')
try {
 for($i=0;$i -lt 50;$i++){
  if($hostProcess.HasExited){throw 'MCP exited before readiness'}
  try { $response=Invoke-WebRequest "http://127.0.0.1:$port/mcp" -Method Post -ContentType 'application/json' -Headers @{Accept='application/json, text/event-stream'} -Body '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"get_version","arguments":{}}}' -TimeoutSec 2;break }
  catch {if($i -eq 49){throw};Start-Sleep -Milliseconds 200}
 }
 $version=Invoke-Cli @('version');Assert ($version.name -eq 'Moyai') 'Version response'
 $commands=Invoke-Cli @('commands');Assert (@($commands).Count -gt 50) 'Tool discovery'
 $empty=Invoke-Cli @('project-list');Assert (@($empty).Count -eq 0) 'Isolated DB not empty'
 $project=Invoke-Cli (@('project-create','--name','CliValidation','--source-path',$run,'--install-path',(Join-Path $run 'install'),'--repository-url','https://example.invalid/test.git','--repository-provider','Githubie','--build-provider','csharp','--deploy-mode','Local')+$actor)
 $read=Invoke-Cli @('project-get','--name','clivalidation');Assert ($read.name -eq 'CliValidation') 'Case-insensitive read'
 $list=Invoke-Cli @('project-list');Assert (@($list).Count -eq 1) 'Project not persisted'
 $item=Invoke-Cli (@('work-item-create','--project','CliValidation','--type','Issue','--title','Original')+$actor)
 $key=$item.key;Assert (-not [string]::IsNullOrEmpty($key)) 'Missing key'
 $updated=Invoke-Cli (@('work-item-update','--project','CliValidation','--key',$key,'--title','Updated needle','--priority','High','--expected-revision',[string]$item.revision)+$actor)
 Assert ($updated.revision -gt $item.revision) 'Revision did not advance'
 $null=Invoke-Cli (@('work-item-update','--project','CliValidation','--key',$key,'--title','Stale','--priority','Low','--expected-revision',[string]$item.revision)+$actor) 1
 $read=Invoke-Cli @('work-item-get','--project','CliValidation','--key',$key);Assert ($read.title -eq 'Updated needle') 'Stale update changed item'
 $null=Invoke-Cli (@('comment-add','--project','CliValidation','--key',$key,'--body','Comment needle')+$actor)
 $comments=Invoke-Cli @('comment-list','--project','CliValidation','--key',$key);Assert (@($comments).Count -eq 1) 'Comment missing'
 $search=Invoke-Cli @('item-search','--project','CliValidation','--query','needle');Assert (($search|ConvertTo-Json -Depth 10) -match 'Updated needle') 'Search failed'
 $null=Invoke-Cli @('work-item-history','--project','CliValidation','--key',$key)
 $null=Invoke-Cli @('project-overview','--project','CliValidation')
 $null=Invoke-Cli @('project-changes-since','--project','CliValidation','--since','2020-01-01T00:00:00Z')
 $null=Invoke-Cli (@('work-item-set-deleted','--project','CliValidation','--key',$key,'--expected-revision',[string]$updated.revision,'--deleted','true')+$actor)
 $items=Invoke-Cli @('work-item-list','--project','CliValidation');Assert (@($items).Count -eq 0) 'Deleted item visible'
 $items=Invoke-Cli @('work-item-list','--project','CliValidation','--include-deleted');Assert (@($items).Count -eq 1) 'Deleted item lost'
 $null=Invoke-Cli @('project-get','--name','Missing') 1
 $null=Invoke-Cli @('project-get','--invalid','true') 1
 $null=Invoke-Cli @('project-get') 1
 $null=Invoke-Cli @('unknown-command') 1
 $null=Invoke-Cli @('build-list','--project','CliValidation')
 $null=Invoke-Cli @('release-list','--project','CliValidation')
 $null=Invoke-Cli @('deploy-list','--project','CliValidation')
 $token=Invoke-Cli (@('token-issue','--audience','testprovider','--scopes','read,write')+$actor)
 Assert ($token.audience -eq 'testprovider' -and @($token.scopes).Count -eq 2) 'Token issue/scopes failed'
 $rotated=Invoke-Cli (@('token-rotate','--audience','testprovider','--scopes','read')+$actor)
 Assert ($rotated.token -ne $token.token) 'Token rotation failed'
 $revoked=Invoke-Cli (@('token-revoke','--audience','testprovider')+$actor);Assert ($revoked -eq $true) 'Token revoke failed'
 $null=Invoke-Cli (@('token-cleanup')+$actor)
 $hash=(Get-FileHash $config).Hash
 $null=Invoke-Cli @('config-init');Assert ((Get-FileHash $config).Hash -eq $hash) 'Config was overwritten'
 $savedConfig=$config;$config=Join-Path $run 'initial/moyai.json'
 $null=Invoke-Cli @('config-init');Assert (Test-Path $config) 'Initial config not generated'
 $initial=Get-Content $config -Raw|ConvertFrom-Json
 Assert ($initial.databasePath -eq '../data/moyai.db') 'Initial database setting changed'
 $config=$savedConfig
 Assert (-not (Test-Path (Join-Path $run 'must-not-exist.db'))) 'CLI accessed database'
 Assert (-not (Test-Path (Join-Path $run 'wrong.db'))) 'Host read environment configuration'
 $hostProcess.Kill();$hostProcess.WaitForExit()
 $status=Invoke-Cli @('service','status')
 Assert ($status.name -eq 'Moyai') 'Service status did not use SCM'
 $null=Invoke-Cli @('service') 1
 $null=Invoke-Cli @('service','invalid') 1
 $null=Invoke-Cli @('service','status','unexpected') 1
 $null=Invoke-Cli @('service-status') 1
 $null=Invoke-Cli @('version') 1
 $null=Invoke-Cli @('project-list') 1
 Write-Output "PASS: $($checks.Count) service-connected CLI cases. Results: $run"
} finally {
 if(-not $hostProcess.HasExited){$hostProcess.Kill();$hostProcess.WaitForExit()}
 $hostOut.GetAwaiter().GetResult()|Set-Content (Join-Path $run 'host.stdout.log')
 $hostErr.GetAwaiter().GetResult()|Set-Content (Join-Path $run 'host.stderr.log')
 $checks|ConvertTo-Json|Set-Content (Join-Path $run 'results.json')
 $hostProcess.Dispose()
}
