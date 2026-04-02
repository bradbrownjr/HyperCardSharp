$f = [IO.File]::ReadAllBytes('samples\BeavisEmulatorV2.sit')
$numFiles = [int]$f[4]*256 + [int]$f[5]
Write-Host "numFiles=$numFiles"
$pos = 22
for($i=0; $i -lt $numFiles; $i++){
  if($pos + 112 -gt $f.Length){ Write-Host "EOF at entry $i"; break }
  $e = $f[$pos..($pos+111)]
  $rsrcMethod = $e[0]; $dataMethod = $e[1]
  $nameLen = [Math]::Min($e[2], 63)
  $name = [System.Text.Encoding]::ASCII.GetString($e[3..($nameLen+2)])
  $ft = [System.Text.Encoding]::ASCII.GetString($e[0x42..0x45])
  $rsrcLen = [int]$e[0x54]*16777216 + [int]$e[0x55]*65536 + [int]$e[0x56]*256 + [int]$e[0x57]
  $dataLen = [int]$e[0x58]*16777216 + [int]$e[0x59]*65536 + [int]$e[0x5A]*256 + [int]$e[0x5B]
  $rsrcComp = [int]$e[0x5C]*16777216 + [int]$e[0x5D]*65536 + [int]$e[0x5E]*256 + [int]$e[0x5F]
  $dataComp = [int]$e[0x60]*16777216 + [int]$e[0x61]*65536 + [int]$e[0x62]*256 + [int]$e[0x63]
  Write-Host "Entry $i: name='$name' type='$ft' rsrcMethod=$rsrcMethod dataMethod=$dataMethod rsrcLen=$rsrcLen dataLen=$dataLen rsrcComp=$rsrcComp dataComp=$dataComp"
  $pos += 112 + $rsrcComp + $dataComp
}
