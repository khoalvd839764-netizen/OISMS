# ==============================================================================
# Script Kiểm Thử Từng Testcase Sprint 2: FR-AUTH
# Dành cho: @Nam Nguyễn (QA/Tester)
# Cách dùng:
#   .\test-auth.ps1 -Case AUTH-01      # Chạy riêng 1 testcase
#   .\test-auth.ps1 -Case AUTH-02
#   .\test-auth.ps1 -All               # Chạy toàn bộ bộ test
# ==============================================================================

param(
    [string]$Case = "",
    [switch]$All = $false,
    [string]$BaseUrl = "http://localhost:5000"
)

# File lưu session token giữa các lần chạy
$SessionFile = "$PSScriptRoot\.test-session.json"

function Save-Session($data) {
    $existing = @{}
    if (Test-Path $SessionFile) {
        $existing = Get-Content $SessionFile -Raw | ConvertFrom-Json -AsHashtable
    }
    foreach ($k in $data.Keys) { $existing[$k] = $data[$k] }
    $existing | ConvertTo-Json | Set-Content $SessionFile -Encoding UTF8
}

function Get-Session() {
    if (Test-Path $SessionFile) {
        return (Get-Content $SessionFile -Raw | ConvertFrom-Json)
    }
    return $null
}

function Send-Api($method, $path, $bodyObj = $null, $token = $null) {
    $url = "$BaseUrl$path"
    $req = [System.Net.HttpWebRequest]::Create($url)
    $req.Method = $method
    $req.Timeout = 10000
    
    if ($token) {
        $req.Headers.Add("Authorization", "Bearer $token")
    }

    if ($bodyObj -ne $null) {
        $json = ($bodyObj | ConvertTo-Json -Compress)
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
        $req.ContentType = "application/json; charset=utf-8"
        $req.ContentLength = $bytes.Length
        $stream = $req.GetRequestStream()
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Close()
    }

    try {
        $resp = $req.GetResponse()
        $sr = New-Object System.IO.StreamReader($resp.GetResponseStream(), [System.Text.Encoding]::UTF8)
        $content = $sr.ReadToEnd()
        return @{
            StatusCode = [int]$resp.StatusCode
            Body = $content
            Success = $true
        }
    } catch [System.Net.WebException] {
        if ($_.Exception.Response) {
            $sr = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream(), [System.Text.Encoding]::UTF8)
            $content = $sr.ReadToEnd()
            return @{
                StatusCode = [int]$_.Exception.Response.StatusCode
                Body = $content
                Success = $false
            }
        }
        return @{
            StatusCode = 0
            Body = $_.Exception.Message
            Success = $false
        }
    }
}

function Print-Result($id, $name, $expected, $actual, $passed, $extra = "") {
    Write-Host "`n------------------------------------------------------------" -ForegroundColor Cyan
    Write-Host "[$id] $name" -ForegroundColor Yellow
    Write-Host "Expected HTTP : $expected"
    Write-Host "Actual HTTP   : $actual"
    if ($passed) {
        Write-Host "KẾT QUẢ       : [ PASS ]" -ForegroundColor Green
    } else {
        Write-Host "KẾT QUẢ       : [ FAIL ]" -ForegroundColor Red
    }
    if ($extra) {
        Write-Host "Chi tiết      : $extra" -ForegroundColor Gray
    }
    Write-Host "------------------------------------------------------------" -ForegroundColor Cyan
}

# ==============================================================================
# ĐỊNH NGHĨA TỪNG TESTCASE
# ==============================================================================

function Run-AUTH-01 {
    $rnd = Get-Random -Minimum 1000 -Maximum 9999
    $email = "owner_$rnd@oisms.test"
    $code = "SHOP$rnd"
    $body = @{
        tenantName  = "Cửa Hàng Test $rnd"
        tenantCode  = $code
        fullName    = "Nguyễn Văn Nam"
        email       = $email
        phoneNumber = "090123$rnd"
        password    = "Test@12345"
    }

    Write-Host "`n==> Đang gọi API: POST /api/auth/register-tenant" -ForegroundColor White
    $res = Send-Api "POST" "/api/auth/register-tenant" $body
    $passed = ($res.StatusCode -eq 201)

    if ($passed) {
        Save-Session @{
            LastEmail = $email
            LastPassword = "Test@12345"
            LastTenantCode = $code
        }
    }

    Print-Result "AUTH-01" "Đăng ký cửa hàng mới (Tenant Registration)" 201 $res.StatusCode $passed $res.Body
}

function Run-AUTH-02 {
    $session = Get-Session
    $email = if ($session.LastEmail) { $session.LastEmail } else { "owner_test@oisms.test" }
    $password = if ($session.LastPassword) { $session.LastPassword } else { "Test@12345" }

    Write-Host "`n==> Đang gọi API: POST /api/auth/login với email: $email" -ForegroundColor White
    $body = @{
        email = $email
        password = $password
    }

    $res = Send-Api "POST" "/api/auth/login" $body
    $passed = ($res.StatusCode -eq 200)

    if ($passed) {
        $json = $res.Body | ConvertFrom-Json
        Save-Session @{
            AccessToken  = $json.accessToken
            RefreshToken = $json.refreshToken
            UserId       = $json.user.id
            TenantId     = $json.user.tenantId
        }
        Write-Host "--> Đã lưu AccessToken & RefreshToken vào session!" -ForegroundColor Green
    }

    Print-Result "AUTH-02" "Đăng nhập thành công và nhận JWT" 200 $res.StatusCode $passed $res.Body
}

function Run-AUTH-03 {
    $session = Get-Session
    $email = if ($session.LastEmail) { $session.LastEmail } else { "owner_test@oisms.test" }

    Write-Host "`n==> Đang gọi API: POST /api/auth/login với password SAI" -ForegroundColor White
    $body = @{
        email = $email
        password = "WrongPassword999!"
    }

    $res = Send-Api "POST" "/api/auth/login" $body
    $passed = ($res.StatusCode -eq 401)
    Print-Result "AUTH-03" "Đăng nhập với mật khẩu sai" 401 $res.StatusCode $passed $res.Body
}

function Run-AUTH-04 {
    Write-Host "`n==> Đang gọi API: GET /api/auth/me với Token hết hạn / Giả mạo" -ForegroundColor White
    $fakeToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjE2MDAwMDAwMDB9.fakeSignature"

    $res = Send-Api "GET" "/api/auth/me" $null $fakeToken
    $passed = ($res.StatusCode -eq 401)
    Print-Result "AUTH-04" "Truy cập API khi Token hết hạn / không hợp lệ" 401 $res.StatusCode $passed $res.Body
}

function Run-AUTH-05 {
    $session = Get-Session
    $token = $session.AccessToken

    Write-Host "`n==> Đang gọi API: GET /api/auth/me với Token hợp lệ từ AUTH-02" -ForegroundColor White
    if (-not $token) {
        Write-Host "⚠️ Chưa có Token! Hãy chạy test AUTH-02 trước." -ForegroundColor Yellow
        return
    }

    $res = Send-Api "GET" "/api/auth/me" $null $token
    $passed = ($res.StatusCode -eq 200)
    Print-Result "AUTH-05" "Lấy thông tin người dùng hiện tại (GET /me)" 200 $res.StatusCode $passed $res.Body
}

function Run-AUTH-06 {
    $session = Get-Session
    $email = if ($session.LastEmail) { $session.LastEmail } else { "duplicate@test.com" }
    
    Write-Host "`n==> Đang gọi API: POST /api/auth/register-tenant với email đã tồn tại: $email" -ForegroundColor White
    $body = @{
        tenantName  = "Trùng Email Shop"
        tenantCode  = "SHOPDUP"
        fullName    = "Trùng Lặp"
        email       = $email
        phoneNumber = "0987654321"
        password    = "Test@12345"
    }

    $res = Send-Api "POST" "/api/auth/register-tenant" $body
    $passed = ($res.StatusCode -eq 409)
    Print-Result "AUTH-06" "Đăng ký Tenant với email đã tồn tại (Unique Constraint)" 409 $res.StatusCode $passed $res.Body
}

function Run-AUTH-07 {
    $session = Get-Session
    $refreshToken = $session.RefreshToken

    Write-Host "`n==> Đang gọi API: POST /api/auth/refresh-token" -ForegroundColor White
    if (-not $refreshToken) {
        Write-Host "⚠️ Chưa có RefreshToken! Hãy chạy test AUTH-02 trước." -ForegroundColor Yellow
        return
    }

    $body = @{ refreshToken = $refreshToken }
    $res = Send-Api "POST" "/api/auth/refresh-token" $body
    $passed = ($res.StatusCode -eq 200)

    if ($passed) {
        $json = $res.Body | ConvertFrom-Json
        Save-Session @{
            AccessToken  = $json.accessToken
            RefreshToken = $json.refreshToken
        }
        Write-Host "--> Đã cập nhật cặp AccessToken & RefreshToken mới!" -ForegroundColor Green
    }

    Print-Result "AUTH-07" "Làm mới Token bằng Refresh Token hợp lệ" 200 $res.StatusCode $passed $res.Body
}

function Run-AUTH-08 {
    Write-Host "`n==> Đang gọi API: POST /api/auth/refresh-token với token giả mạo" -ForegroundColor White
    $body = @{ refreshToken = "Fake-Token-Replay-Attack-12345" }
    $res = Send-Api "POST" "/api/auth/refresh-token" $body
    $passed = ($res.StatusCode -eq 401)
    Print-Result "AUTH-08" "Tấn công dùng lại Refresh Token (Replay Attack) hoặc giả mạo" 401 $res.StatusCode $passed $res.Body
}

function Run-AUTH-09 {
    $session = Get-Session
    $refreshToken = $session.RefreshToken

    Write-Host "`n==> Đang gọi API: POST /api/auth/logout" -ForegroundColor White
    $body = @{ refreshToken = $refreshToken }
    $res = Send-Api "POST" "/api/auth/logout" $body
    $passed = ($res.StatusCode -eq 200)

    Print-Result "AUTH-09" "Đăng xuất tài khoản (Thu hồi Refresh Token)" 200 $res.StatusCode $passed $res.Body
}

# ==============================================================================
# MENU ĐIỀU HƯỚNG
# ==============================================================================

if ($All) {
    Write-Host "`n============================================================" -ForegroundColor Green
    Write-Host " CHẠY TOÀN BỘ SUITE KIỂM THỬ SPRINT 2 - FR-AUTH" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Green
    Run-AUTH-01
    Run-AUTH-02
    Run-AUTH-03
    Run-AUTH-04
    Run-AUTH-05
    Run-AUTH-06
    Run-AUTH-07
    Run-AUTH-08
    Run-AUTH-09
} elseif ($Case) {
    switch ($Case.ToUpper()) {
        "AUTH-01" { Run-AUTH-01 }
        "AUTH-02" { Run-AUTH-02 }
        "AUTH-03" { Run-AUTH-03 }
        "AUTH-04" { Run-AUTH-04 }
        "AUTH-05" { Run-AUTH-05 }
        "AUTH-06" { Run-AUTH-06 }
        "AUTH-07" { Run-AUTH-07 }
        "AUTH-08" { Run-AUTH-08 }
        "AUTH-09" { Run-AUTH-09 }
        default {
            Write-Host "❌ Không tìm thấy testcase: $Case" -ForegroundColor Red
            Write-Host "Các mã khả dụng: AUTH-01 đến AUTH-09" -ForegroundColor Yellow
        }
    }
} else {
    Write-Host "`n============================================================" -ForegroundColor Cyan
    Write-Host " BỘ TOOL KIỂM THỬ TỪNG TESTCASE CHO TESTER NAM NGUYỄN" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host "Cách chạy:" -ForegroundColor Yellow
    Write-Host "  .\test-auth.ps1 -Case AUTH-01   --> Đăng ký cửa hàng mới"
    Write-Host "  .\test-auth.ps1 -Case AUTH-02   --> Đăng nhập & lưu token"
    Write-Host "  .\test-auth.ps1 -Case AUTH-03   --> Sai mật khẩu (401)"
    Write-Host "  .\test-auth.ps1 -Case AUTH-04   --> Token hết hạn (401)"
    Write-Host "  .\test-auth.ps1 -Case AUTH-05   --> Lấy profile (GET /me)"
    Write-Host "  .\test-auth.ps1 -Case AUTH-06   --> Trùng email (409)"
    Write-Host "  .\test-auth.ps1 -Case AUTH-07   --> Refresh token (200)"
    Write-Host "  .\test-auth.ps1 -Case AUTH-08   --> Refresh token giả mạo (401)"
    Write-Host "  .\test-auth.ps1 -Case AUTH-09   --> Đăng xuất / Logout (200)"
    Write-Host "  .\test-auth.ps1 -All            --> Chạy toàn bộ 9 ca kiểm thử"
    Write-Host "============================================================`n"
}
