# 공개 HEIC와 합성 H.264·HEVC 영상을 전용 시험 폴더에 생성합니다.
param([Parameter(Mandatory)][string]$Ffmpeg, [Parameter(Mandatory)][string]$Output)
$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($Output)
$allowed = [IO.Path]::GetFullPath('test-artifacts') + [IO.Path]::DirectorySeparatorChar
if (-not $target.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $target)) {
    throw 'test-artifacts 하위의 새 폴더를 지정하세요.'
}
for ($parent = [IO.Directory]::GetParent($target); $null -ne $parent; $parent = $parent.Parent) {
    if ($parent.Exists -and ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '링크/정션을 거치지 않습니다.' }
}
if (-not (Test-Path -LiteralPath $Ffmpeg -PathType Leaf)) { throw '신뢰할 수 있는 FFmpeg 실행 파일을 지정하세요.' }
New-Item -ItemType Directory -Path $target | Out-Null
Invoke-WebRequest 'https://raw.githubusercontent.com/strukturag/libheif/master/examples/example.heic' -OutFile (Join-Path $target 'example.heic')
if ((Get-FileHash -LiteralPath (Join-Path $target 'example.heic')).Hash -ne '7F8B363E4936C0666A25F64F3A92FDA10BD8E5453BE4592530B65A55DD98F3F2') {
    throw '공개 HEIC 파일이 기록한 SHA-256과 다릅니다. 실행/검증을 중단합니다.'
}
& $Ffmpeg -hide_banner -loglevel error -n -f lavfi -i 'testsrc2=size=320x180:rate=24' -f lavfi -i 'sine=frequency=440:sample_rate=48000' -t 8 -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -movflags +faststart (Join-Path $target 'h264.mp4')
if ($LASTEXITCODE -ne 0) { throw 'H.264 합성 실패' }
& $Ffmpeg -hide_banner -loglevel error -n -f lavfi -i 'testsrc2=size=320x180:rate=24' -f lavfi -i 'sine=frequency=440:sample_rate=48000' -t 8 -c:v libx265 -preset ultrafast -x265-params 'log-level=error:pools=2:frame-threads=2' -pix_fmt yuv420p10le -tag:v hvc1 -c:a aac (Join-Path $target 'hevc10.mov')
if ($LASTEXITCODE -ne 0) { throw 'HEVC 합성 실패' }
& $Ffmpeg -hide_banner -loglevel error -n -i (Join-Path $target 'hevc10.mov') -c copy -tag:v hvc1 (Join-Path $target 'hevc10.mp4')
if ($LASTEXITCODE -ne 0) { throw 'HEVC MP4 합성 실패' }
Get-FileHash -LiteralPath (Join-Path $target 'example.heic'), (Join-Path $target 'h264.mp4'), (Join-Path $target 'hevc10.mov'), (Join-Path $target 'hevc10.mp4')
