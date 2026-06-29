Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$DPMM = 12                      # Labelary 12dpmm = Zebra 300dpi 근사
function mm([double]$v){ [int][math]::Round($v * $DPMM) }

# 한글 포함 텍스트 → 1-bit 비트맵 → ZPL ^GFA
function Text-ZPL([string]$text,[string]$fontName,[double]$fsMm,[bool]$bold){
  $px = [int][math]::Round($fsMm * $DPMM)
  if($px -lt 6){$px=6}
  $fstyle = if($bold){[System.Drawing.FontStyle]::Bold}else{[System.Drawing.FontStyle]::Regular}
  $font = New-Object System.Drawing.Font($fontName,$px,$fstyle,[System.Drawing.GraphicsUnit]::Pixel)
  $tmp = New-Object System.Drawing.Bitmap 2,2
  $g0 = [System.Drawing.Graphics]::FromImage($tmp)
  $sz = $g0.MeasureString($text,$font)
  $w = [int][math]::Ceiling($sz.Width); $h = [int][math]::Ceiling($sz.Height)
  if($w -lt 1){$w=1}; if($h -lt 1){$h=1}
  $g0.Dispose(); $tmp.Dispose()
  $bmp = New-Object System.Drawing.Bitmap $w,$h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.Clear([System.Drawing.Color]::White)
  $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::SingleBitPerPixelGridFit
  $g.DrawString($text,$font,[System.Drawing.Brushes]::Black,0,0)
  $g.Dispose()
  $rowBytes = [int][math]::Ceiling($w/8.0)
  $sb = New-Object System.Text.StringBuilder
  for($y=0;$y -lt $h;$y++){
    for($bx=0;$bx -lt $rowBytes;$bx++){
      $byte=0
      for($bit=0;$bit -lt 8;$bit++){
        $x=$bx*8+$bit
        if($x -lt $w){
          $p=$bmp.GetPixel($x,$y)
          if((($p.R+$p.G+$p.B)/3) -lt 128){ $byte = $byte -bor (0x80 -shr $bit) }
        }
      }
      [void]$sb.Append($byte.ToString('X2'))
    }
  }
  $bmp.Dispose()
  $total=$rowBytes*$h
  return @{ w=$w; h=$h; zpl="^GFA,$total,$total,$rowBytes,$($sb.ToString())" }
}

# 한글 폰트 선택(Pretendard 있으면 그것, 없으면 맑은고딕)
$fams = (New-Object System.Drawing.Text.InstalledFontCollection).Families | ForEach-Object { $_.Name }
$KFONT = if($fams -contains 'Pretendard'){'Pretendard'} elseif($fams -contains 'Pretendard Variable'){'Pretendard Variable'} else {'Malgun Gothic'}
Write-Host "한글폰트: $KFONT"

# --- 라벨 조립 (우리 레이아웃 mm 좌표) ---
$zpl = "^XA`n^CI28`n^PW$(mm 60)`n^LL$(mm 10)`n^LH0,0`n"
function place([double]$x,[double]$y,$t){ "^FO$(mm $x),$(mm $y)$($t.zpl)^FS`n" }

$zpl += place 0.6 0.3 (Text-ZPL '(주)D102' $KFONT 1.9 $false)
$zpl += place 0.6 2.2 (Text-ZPL 'F-볼륨하트언발체인' $KFONT 1.9 $false)
$zpl += place 0.6 4.0 (Text-ZPL '1,830,000' $KFONT 2.6 $true)
# 바코드: 네이티브 Code128
$zpl += "^FO$(mm 0.6),$(mm 5.9)^BY2,2.0,$(mm 2.9)`n^BCN,$(mm 2.9),N,N,N`n^FD2606RL^FS`n"
$zpl += place 0.6 9.0 (Text-ZPL 'LT  2606RL' $KFONT 2.0 $false)
# 패널 B
$zpl += place 22.6 0.5 (Text-ZPL '2606RL' $KFONT 2.1 $false)
$zpl += place 22.6 2.5 (Text-ZPL '18K (17)' $KFONT 1.9 $false)
$zpl += place 22.6 4.4 (Text-ZPL '4.36 g' $KFONT 1.9 $false)
$zpl += place 22.6 6.3 (Text-ZPL '(주)D102  FASHION' $KFONT 1.8 $false)
$zpl += place 22.6 8.2 (Text-ZPL 'F-볼륨하트언발체인/백심9932' $KFONT 1.8 $false)
# 패널 C (구분선)
$zpl += "^FO$(mm 42),0^GB1,$(mm 10),1^FS`n"
$zpl += "^FO$(mm 22),0^GB1,$(mm 10),1^FS`n"
$zpl += place 42.6 1.0 (Text-ZPL '주얼리전산의 리더 지앤샵' $KFONT 1.7 $false)
$zpl += place 42.6 8.4 (Text-ZPL 'www.honsu114.com' $KFONT 1.7 $false)
$zpl += "^XZ"

$out = "$PSScriptRoot\zpl_label.txt"
[IO.File]::WriteAllText($out,$zpl,[Text.Encoding]::UTF8)
Write-Host "ZPL bytes: $($zpl.Length)"

# Labelary로 렌더(프린터 없이 검증) — 60x10mm = 2.36x0.39 in
$png = "$PSScriptRoot\zpl_label.png"
Invoke-WebRequest -Uri "http://api.labelary.com/v1/printers/12dpmm/labels/2.36x0.39/0/" -Method Post -Body $zpl -ContentType "application/x-www-form-urlencoded" -OutFile $png
Write-Host "PNG: $((Get-Item $png).Length) bytes -> $png"
