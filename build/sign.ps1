# 代码签名:用自签证书给 exe 签名,并把证书加进本机受信任发布者(消除"未知发布者"提示)
param(
    [Parameter(Mandatory = $true)][string[]]$Files
)
$ErrorActionPreference = 'Stop'

$subject = 'CN=ChargeDock'

# 1) 找或建自签代码签名证书(存 CurrentUser\My,含私钥)
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -FriendlyName 'ChargeDock Code Signing' -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(10)
    Write-Host ("created cert: " + $cert.Thumbprint)
}

# 2) 把证书加入本机受信任的根 + 受信任发布者(CurrentUser,无需管理员)
foreach ($storeName in @('Root', 'TrustedPublisher')) {
    $exists = Get-ChildItem ("Cert:\CurrentUser\" + $storeName) | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
    if (-not $exists) {
        $tmp = Join-Path $env:TEMP 'chargedock-cert.cer'
        Export-Certificate -Cert $cert -FilePath $tmp -Force | Out-Null
        Import-Certificate -FilePath $tmp -CertStoreLocation ("Cert:\CurrentUser\" + $storeName) | Out-Null
        Remove-Item $tmp -Force
        Write-Host ("cert added to " + $storeName)
    }
}

# 3) 逐个签名(自签证书有效期 10 年,不依赖时间戳服务器)
foreach ($f in $Files) {
    $full = (Resolve-Path $f).Path
    Write-Host ("signing " + $full)
    Set-AuthenticodeSignature -FilePath $full -Certificate $cert -HashAlgorithm SHA256 | Out-Null
    $sig = Get-AuthenticodeSignature $full
    Write-Host ("  status = " + $sig.Status + "   publisher = " + $sig.SignerCertificate.Subject)
}
