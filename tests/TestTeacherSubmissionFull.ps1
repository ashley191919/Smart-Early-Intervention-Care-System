param([string]$DevelopmentUrl = 'http://127.0.0.1:5190', [string]$ProductionUrl = 'http://127.0.0.1:5191')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
function Check($condition, $label) { if (!$condition) { throw "FAIL: $label" }; Write-Host "PASS: $label" }
function Create($seconds) {
    $body = New-Object System.Net.Http.StringContent ('{"expiresInSeconds":' + $seconds + '}'), ([Text.Encoding]::UTF8), 'application/json'
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
try {
    $url = Create 300
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
    $url = Create 300
    $pending = @(1..20 | ForEach-Object {
        $body = New-Object System.Net.Http.StringContent 'question1=yes&question2=no', ([Text.Encoding]::UTF8), 'application/x-www-form-urlencoded'
        $client.PostAsync("$DevelopmentUrl$url", $body)
    })
    $results = @($pending | ForEach-Object { $_.GetAwaiter().GetResult() })
    Check (@($results | Where-Object { $_.StatusCode -eq 200 }).Count -eq 1 -and @($results | Where-Object { $_.StatusCode -eq 404 }).Count -eq 19) 'full API 20 concurrent requests: one success'
    $url = Create 1
    Check ($client.GetAsync("$DevelopmentUrl$url").GetAwaiter().GetResult().IsSuccessStatusCode) 'full API opens before expiry'
    Start-Sleep -Milliseconds 1200
    Check ((Submit $url 'question1=yes&question2=no').StatusCode -eq 404) 'full API rejects expiry after opening'
    $expired = Revoke $script:lastGrant.grantId
    Check ($expired.StatusCode -eq 409 -and ($expired.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json).status -eq 'EXPIRED') 'full API EXPIRED revocation conflict'
    $url = Create 300
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
    Check ((Revoke ([Guid]::NewGuid())).StatusCode -eq 404) 'full API unknown ID is 404'
    Check ((Revoke 'not-a-guid').StatusCode -eq 400) 'full API malformed ID is 400 in Development'
    foreach ($iteration in 1..20) {
        $url = Create 300
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
    foreach ($path in @('/teacher/test-form', $url)) {
        Check ($client.GetAsync("$ProductionUrl$path").GetAwaiter().GetResult().StatusCode -eq 404) 'full API Production GET disabled'
        $body = New-Object System.Net.Http.StringContent 'question1=yes&question2=no', ([Text.Encoding]::UTF8), 'application/x-www-form-urlencoded'
        Check ($client.PostAsync("$ProductionUrl$path", $body).GetAwaiter().GetResult().StatusCode -eq 404) 'full API Production submission disabled'
    }
    $bad = New-Object System.Net.Http.StringContent '{bad', ([Text.Encoding]::UTF8), 'application/json'
    Check ($client.PostAsync("$ProductionUrl/api/dev/teacher-grants", $bad).GetAwaiter().GetResult().StatusCode -eq 404) 'full API Production malformed creation disabled'
} finally { $client.Dispose() }
