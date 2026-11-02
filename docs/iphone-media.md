# 아이폰 사진·영상 지원

2026-11-02 공식 NuGet 피드의 최신 안정 패키지를 확인하고 고정했다. 사진 보기와 영상 재생을 추가한 개발 버전이며 일반 배포·모든 아이폰 파일 호환성을 보장하는 제품 인증은 아니다.

## 사용

실행 중인 앱을 닫고 바탕화면의 내 사진함 실행.cmd로 다시 실행한다. 폴더를 선택하거나 다시 수집하면 예전에 미검증이던 HEIC/HEIF·MOV/MP4 항목을 다시 처리한다. 원본을 변환·수정하지 않으며 Windows HEVC 코덱팩/VLC 프로그램을 시스템에 설치하지 않는다.

- HEIC/HEIF 사진은 기존 이미지와 같이 미리보기·확대·스크롤/이동을 지원한다. 입력 5천만 픽셀/긴 변 2400px 미리보기 한도는 유지한다.
- MOV/MP4는 선택 시 로컬 파일만 최대 5초간 분석하고 자동 재생하지 않는다. 재생/계속, 일시정지, 정지, 탐색, 볼륨을 제공한다. 선택을 바꾸거나 닫으면 이전 재생을 정지한다.
- 영상 촬영일/GPS는 아직 추출하지 않는다. 목록 날짜는 **파일 수정 UTC·추정**이며 원본 촬영일로 표시하지 않는다. 영상 원본 태그 JSON에는 이 미추출 상태를 기록한다. 손상 영상은 선택 시 오류를 표시하며 형식이 MP4/MOV라는 이유만으로 정상 영상이라고 보장하지 않는다.
- Live Photo 사진/영상 연결, 실제 아이폰 ProRAW·모든 HEIC 변종·4K/60fps·HDR/Dolby Vision 색상·gain map·장시간 재생·모든 하드웨어 호환성은 별도 검증이 필요하다.

## 엔진·라이선스·해시

- [Magick.NET 15.0.0](https://github.com/dlemstra/Magick.NET/releases/tag/15.0.0). 배포 Notice에 libheif 1.23.6/libde265 1.1.3이 포함돼 있다. HEIC 인코더 없음과 디코더 없음은 다르며 이번에 실제 파일 읽기를 검증했다.
- [LibVLCSharp.WPF 3.10.2](https://www.nuget.org/packages/LibVLCSharp.WPF/3.10.2)와 [VideoLAN.LibVLC.Windows 3.0.24](https://www.nuget.org/packages/VideoLAN.LibVLC.Windows/3.0.24). 앱 폴더의 libvlc/win-x64를 명시해 로드하며 설치된 전역 VLC를 사용하지 않는다. 실제 엔진 버전도 검사한다.
- WPF nupkg SHA-256 `CE90B276CB78D5572B3E1053F9BF41BAC3D18FDF5851332F9E0C2C057932341F`.
- Windows native nupkg SHA-256 `3DA3F37EE2B2B10E5C788D830A3B513658C36521EA1B0F966ED86EB69F41EF3C`.
- NuGet 잠금 파일에 전이 의존성과 contentHash를 보관했다. LibVLCSharp/Windows 패키지에는 LGPL-2.1 계열 표기가 있다. Magick.NET의 Apache-2.0 표기와 libheif/libde265 등 내장 구성요소의 별도 조건을 구분한다. 모든 플러그인의 배포 조건을 일괄 보장하지 않으며 설치본 재배포 전 전체 고지 검토가 남아 있다.

## 재현

저장소 루트에서 실행한다. 출력 폴더는 새 test-artifacts 하위 경로여야 한다.

```powershell
E:/tools/dotnet10/dotnet.exe restore src/PhotoLibrary.Desktop --locked-mode
E:/tools/dotnet10/dotnet.exe build src/PhotoLibrary.Desktop -c Release --no-restore
./tools/New-IphoneFixtures.ps1 -Ffmpeg 'PATH_TO_TRUSTED_FFMPEG.exe' -Output test-artifacts/iphone-media-final
E:/tools/dotnet10/dotnet.exe src/PhotoLibrary.Desktop/bin/Release/net10.0-windows/PhotoLibrary.Desktop.dll --iphone-test test-artifacts/iphone-final-local test-artifacts/iphone-media-final
```

FFmpeg는 시험 파일 생성에만 필요하다. 앱 실행에는 필요 없다. 이번에는 [FFmpeg 공식 다운로드](https://ffmpeg.org/download.html)가 안내하는 [Gyan Windows 빌드](https://www.gyan.dev/ffmpeg/builds/)의 8.1.2 essentials ZIP을 별도 runtime에만 풀었고 게시 SHA-256 `DB580001CAA24AC104C8CB856CD113A87B0A443F7BDF47D8C12B1D740584A2EC`과 비교했다. 시스템 PATH/코덱/보안 설정은 변경하지 않았다. 시험 생성기는 신뢰하는 로컬 FFmpeg 경로를 명시해야 한다.

공개 HEIC는 [libheif examples/example.heic](https://github.com/strukturag/libheif/blob/master/examples/example.heic)다. SHA-256 `7F8B363E4936C0666A25F64F3A92FDA10BD8E5453BE4592530B65A55DD98F3F2`가 다르면 생성 스크립트가 중단한다. 동영상은 320×180/24fps/8초 합성 무늬와 AAC 시험음으로 생성했고, 앱 검사는 음소거로 수행했다. HEVC Main10/yuv420p10le/hvc1은 ffprobe로 확인했다. HDR 태그를 넣으려던 중간 파일도 HDR 색상 검증 완료로 취급하지 않았다.

## 검증과 한계

- 공개 HEIC/HEIF 복사본의 1280×854 메타데이터와 WPF 픽셀 미리보기 확인.
- H.264/AAC MP4, HEVC Main10 MOV/MP4에서 실제 영상/음성 디코딩 통계, PNG 프레임 저장/크기, 일시정지·탐색·비동기 볼륨 반영·재개 후 6초 이후 시간/추가 디코딩·정지 확인.
- 손상 영상 오류, 사진 선택 시 영상 중단, 재생 중 창 종료, 읽기 전용 시험 원본 전체의 SHA-256 불변 확인.
- 기존 WPF `--smoke-test test-artifacts/desktop-iphone-regression-01`과 수집 `--self-test test-artifacts/catalog-iphone-regression-01`은 exit 0. 빌드는 경고/오류 0개.
- 최종 앱 로컬 엔진 검사 `--iphone-test test-artifacts/iphone-final-local test-artifacts/iphone-media-final`도 exit 0이었다. Blob IO 오류 분류 보완 후 기존 WPF/수집 검사를 각각 desktop-iphone-final/catalog-iphone-final 새 출력으로 다시 통과했다.
- HEIC 스트림은 제한된 샌드박스의 임시 파일 접근에서 Permission denied로 실패했고 같은 파일을 일반 실행 환경에서 처리하면 통과했다. 정상 TEMP 쓰기 권한이 필요하며 관리자 권한 필수라는 뜻은 아니다.
- 일부 GPU/snapshot/정지 과정에 converter·picture allocation·thumbnail clip 로그가 발생했다. 재생 진행·추가 프레임·정상 PNG 저장은 통과했지만 발생 시점/장시간 GPU 안정성은 완전히 분리 검증하지 못했다. 실제 4K/HDR 성능·색상 정확도 보장은 하지 않는다. 스피커의 실제 음질도 음소거 검사 범위 밖이다.

사진·영상·runtime·캐시·DB·시험 출력은 Git에 포함하지 않는다. 앱은 URL 입력 재생을 제공하지 않으며 VLC 메타데이터 네트워크 요청과 자동 자막 검색을 비활성화했다. 사용자 파일을 외부로 업로드하는 코드나 원본 쓰기 기능은 추가하지 않았다.
