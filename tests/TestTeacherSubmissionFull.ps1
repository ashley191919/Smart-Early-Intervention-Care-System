param([string]$DevelopmentUrl = 'http://127.0.0.1:5190', [string]$ProductionUrl = 'http://127.0.0.1:5191')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
function Check($condition, $label) { if (!$condition) { throw "FAIL: $label" }; Write-Host "PASS: $label" }
function Create() {
    $body = New-Object System.Net.Http.StringContent '{}', ([Text.Encoding]::UTF8), 'application/json'
    $r = $client.PostAsync("$DevelopmentUrl/api/dev/teacher-grants", $body).GetAwaiter().GetResult()
    Check ($r.IsSuccessStatusCode) 'full API creates grant'
    $script:lastGrant = $r.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    return $script:lastGrant.TeacherFormUrl
}
function Submit($url, $body) {
    $content = New-Object System.Net.Http.StringContent $body, ([Text.Encoding]::UTF8), 'application/x-www-form-urlencoded'
    return $client.PostAsync("$DevelopmentUrl$url", $content).GetAwaiter().GetResult()
}
function Revoke($id) {
    return $client.PostAsync("$DevelopmentUrl/api/dev/teacher-grants/$id/revoke", $null).GetAwaiter().GetResult()
}
function Audit($id, $limit = 200) {
    $r = $client.GetAsync("$DevelopmentUrl/api/dev/audit-logs?grantId=$id&limit=$limit").GetAwaiter().GetResult()
    Check ($r.IsSuccessStatusCode -and $r.Headers.CacheControl.NoStore) 'full API audit query and no-store'
    return ($r.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json)
}
try {
    $url = Create
    $get = $client.GetAsync("$DevelopmentUrl$url").GetAwaiter().GetResult()
    Check ($get.IsSuccessStatusCode -and $get.Headers.CacheControl.NoStore) 'full API form and no-store'
    foreach ($body in @('question1=yes', 'question1=bad&question2=no', 'question1=yes&question1=no&question2=no')) {
        Check ((Submit $url $body).StatusCode -eq 400) 'full API rejects missing/illegal/duplicate answer'
        Check ($client.GetAsync("$DevelopmentUrl$url").GetAwaiter().GetResult().IsSuccessStatusCode) 'validation preserves grant'
    }
    $sent = Submit $url 'question1=yes&question2=sometimes&caseId=other&taskId=other'
    Check ($sent.IsSuccessStatusCode -and $sent.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains('已成功送出，此連結已失效')) 'full API legal submission'
    Check ($client.GetAsync("$DevelopmentUrl$url").GetAwaiter().GetResult().StatusCode -eq 404) 'full API used GET rejected'
    Check ((Submit $url 'question1=yes&question2=no').StatusCode -eq 404) 'full API used POST rejected'
    $used = Revoke $script:lastGrant.grantId
    Check ($used.StatusCode -eq 409 -and ($used.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json).status -eq 'USED') 'full API USED revocation conflict'
    $logs = Audit $script:lastGrant.grantId
    Check (@($logs.events | Where-Object { $_.action -eq 'TeacherGrant.Create' -and $_.actorType -eq 'DevelopmentTestOperator' }).Count -eq 1 -and @($logs.events | Where-Object { $_.action -eq 'TeacherResponse.Submit' -and $_.result -eq 'Success' -and $_.actorType -eq 'TeacherGrantBearer' }).Count -eq 1 -and @($logs.events | Where-Object { $_.result -eq 'Rejected:USED' }).Count -eq 1) 'full API create/submit/USED rejection events'
    Check ($logs.notice.Contains('尚非正式持久化稽核') -and @($logs.events | Where-Object { !$_.eventId -or !$_.occurredAtUtc -or !$_.requestCorrelationId -or $_.resourceId -ne $script:lastGrant.grantId }).Count -eq 0) 'full API event fields and memory notice'
    $limited = Audit $script:lastGrant.grantId 1
    Check (@($limited.events).Count -eq 1) 'full API audit limit applies after GrantId filter'
    $logJson = $logs | ConvertTo-Json -Depth 6
    $token = $url.Split('=')[1]
    Check (!$logJson.Contains($token) -and !$logJson.Contains($url) -and !$logJson.Contains('sometimes') -and !$logJson.Contains('dev-case-001')) 'full API events exclude token, URL, answers and case'
    $url = Create
    $pending = @(1..20 | ForEach-Object {
        $body = New-Object System.Net.Http.StringContent 'question1=yes&question2=no', ([Text.Encoding]::UTF8), 'application/x-www-form-urlencoded'
        $client.PostAsync("$DevelopmentUrl$url", $body)
    })
    $results = @($pending | ForEach-Object { $_.GetAwaiter().GetResult() })
    Check (@($results | Where-Object { $_.StatusCode -eq 200 }).Count -eq 1 -and @($results | Where-Object { $_.StatusCode -eq 404 }).Count -eq 19) 'full API 20 concurrent requests: one success'
    $logs = Audit $script:lastGrant.grantId
    Check (@($logs.events | Where-Object { $_.action -eq 'TeacherResponse.Submit' -and $_.result -eq 'Success' }).Count -eq 1) 'full API parallel submissions emit one success event'
    $url = Create
    Check (!$script:lastGrant.PSObject.Properties['expiresAtUtc']) 'full API response has no expiration'
    $page = $client.GetAsync("$DevelopmentUrl$url").GetAwaiter().GetResult()
    Check ($page.IsSuccessStatusCode -and $page.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains('填答不限時')) 'full API explains no time limit'
    Start-Sleep -Milliseconds 1200
    Check ((Submit $url 'question1=yes&question2=no').IsSuccessStatusCode) 'full API delayed submission succeeds'
    $url = Create
    $id = $script:lastGrant.grantId
    Check ($client.GetAsync("$DevelopmentUrl$url").GetAwaiter().GetResult().IsSuccessStatusCode) 'full API opens before revocation'
    $revoked = Revoke $id
    $first = $revoked.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $data = $first | ConvertFrom-Json
    Check ($revoked.StatusCode -eq 200 -and $revoked.Headers.CacheControl.NoStore -and $data.status -eq 'REVOKED' -and $data.revokedAtUtc -and $data.grantId -eq $id) 'full API revocation status, timestamp and no-store'
    Check ($client.GetAsync("$DevelopmentUrl$url").GetAwaiter().GetResult().StatusCode -eq 404) 'full API revoked GET rejected'
    Check ((Submit $url 'question1=yes&question2=no').StatusCode -eq 404) 'full API revoked opened form submission rejected'
    $repeat = Revoke $id
    Check ($repeat.StatusCode -eq 200 -and $repeat.Content.ReadAsStringAsync().GetAwaiter().GetResult() -eq $first) 'full API repeat revocation is identical'
    $logs = Audit $id
    Check (@($logs.events | Where-Object { $_.action -eq 'TeacherGrant.Revoke' -and $_.result -eq 'Success' }).Count -eq 1 -and @($logs.events | Where-Object { $_.result -eq 'Rejected:REVOKED' }).Count -eq 1) 'full API first revoke only and revoked rejection event'
    Check ((Revoke ([Guid]::NewGuid())).StatusCode -eq 404) 'full API unknown ID is 404'
    Check ((Revoke 'not-a-guid').StatusCode -eq 400) 'full API malformed ID is 400 in Development'
    foreach ($iteration in 1..20) {
        $url = Create
        $id = $script:lastGrant.grantId
        $body = New-Object System.Net.Http.StringContent 'question1=yes&question2=no', ([Text.Encoding]::UTF8), 'application/x-www-form-urlencoded'
        # Alternate request launch order; either terminal result is valid.
        if ($iteration % 2 -eq 0) {
            $submitTask = $client.PostAsync("$DevelopmentUrl$url", $body)
            $revokeTask = $client.PostAsync("$DevelopmentUrl/api/dev/teacher-grants/$id/revoke", $null)
        } else {
            $revokeTask = $client.PostAsync("$DevelopmentUrl/api/dev/teacher-grants/$id/revoke", $null)
            $submitTask = $client.PostAsync("$DevelopmentUrl$url", $body)
        }
        $r = $revokeTask.GetAwaiter().GetResult()
        $s = $submitTask.GetAwaiter().GetResult()
        $state = ($r.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json).status
        Check (($r.StatusCode -eq 200 -and $s.StatusCode -eq 404 -and $state -eq 'REVOKED') -or ($r.StatusCode -eq 409 -and $s.StatusCode -eq 200 -and $state -eq 'USED')) 'full API concurrent revoke/submit consistent outcome'
    }
    foreach ($id in @([Guid]::NewGuid().ToString(), 'not-a-guid')) {
        $body = New-Object System.Net.Http.StringContent '{bad', ([Text.Encoding]::UTF8), 'application/json'
        $r = $client.PostAsync("$ProductionUrl/api/dev/teacher-grants/$id/revoke", $body).GetAwaiter().GetResult()
        Check ($r.StatusCode -eq 404 -and $r.Headers.CacheControl.NoStore) 'full API Production revoke malformed route/body is uncached 404'
    }
    foreach ($query in @('', '?grantId=bad&limit=invalid')) {
        $r = $client.GetAsync("$ProductionUrl/api/dev/audit-logs$query").GetAwaiter().GetResult()
        Check ($r.StatusCode -eq 404 -and $r.Headers.CacheControl.NoStore) 'full API Production audit query including malformed query disabled'
    }
    foreach ($query in @('limit=0', 'limit=201', 'grantId=bad')) {
        Check ($client.GetAsync("$DevelopmentUrl/api/dev/audit-logs?$query").GetAwaiter().GetResult().StatusCode -eq 400) 'full API audit query validation'
    }
    foreach ($path in @('/teacher/test-form', $url)) {
        Check ($client.GetAsync("$ProductionUrl$path").GetAwaiter().GetResult().StatusCode -eq 404) 'full API Production GET disabled'
        $body = New-Object System.Net.Http.StringContent 'question1=yes&question2=no', ([Text.Encoding]::UTF8), 'application/x-www-form-urlencoded'
        Check ($client.PostAsync("$ProductionUrl$path", $body).GetAwaiter().GetResult().StatusCode -eq 404) 'full API Production submission disabled'
    }
    $bad = New-Object System.Net.Http.StringContent '{bad', ([Text.Encoding]::UTF8), 'application/json'
    Check ($client.PostAsync("$ProductionUrl/api/dev/teacher-grants", $bad).GetAwaiter().GetResult().StatusCode -eq 404) 'full API Production malformed creation disabled'
} finally { $client.Dispose() }
