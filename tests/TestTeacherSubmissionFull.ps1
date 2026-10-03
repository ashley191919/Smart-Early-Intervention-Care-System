param([string]$DevelopmentUrl = 'http://127.0.0.1:5190', [string]$ProductionUrl = 'http://127.0.0.1:5191')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
function Check($condition, $label) { if (!$condition) { throw "FAIL: $label" }; Write-Host "PASS: $label" }
function Create($seconds) {
    $body = New-Object System.Net.Http.StringContent ('{"expiresInSeconds":' + $seconds + '}'), ([Text.Encoding]::UTF8), 'application/json'
    $r = $client.PostAsync("$DevelopmentUrl/api/dev/teacher-grants", $body).GetAwaiter().GetResult()
    Check ($r.IsSuccessStatusCode) 'full API creates grant'
    return ($r.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json).TeacherFormUrl
}
function Submit($url, $body) {
    $content = New-Object System.Net.Http.StringContent $body, ([Text.Encoding]::UTF8), 'application/x-www-form-urlencoded'
    return $client.PostAsync("$DevelopmentUrl$url", $content).GetAwaiter().GetResult()
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
    foreach ($path in @('/teacher/test-form', $url)) {
        Check ($client.GetAsync("$ProductionUrl$path").GetAwaiter().GetResult().StatusCode -eq 404) 'full API Production GET disabled'
        $body = New-Object System.Net.Http.StringContent 'question1=yes&question2=no', ([Text.Encoding]::UTF8), 'application/x-www-form-urlencoded'
        Check ($client.PostAsync("$ProductionUrl$path", $body).GetAwaiter().GetResult().StatusCode -eq 404) 'full API Production submission disabled'
    }
    $bad = New-Object System.Net.Http.StringContent '{bad', ([Text.Encoding]::UTF8), 'application/json'
    Check ($client.PostAsync("$ProductionUrl/api/dev/teacher-grants", $bad).GetAwaiter().GetResult().StatusCode -eq 404) 'full API Production malformed creation disabled'
} finally { $client.Dispose() }
