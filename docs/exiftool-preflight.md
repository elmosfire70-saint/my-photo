# ExifTool 후속 검증

2026-11-02에 공식 소스 실행으로 ExifTool 검증을 추가했다. 사용자 사진과 XnView 파일은 사용하지 않았고, 기존 시험이 만든 합성 JPEG의 복사본만 수정했다.

## 고정 도구와 출처

- [공식 저장소 13.60 태그](https://github.com/exiftool/exiftool/tree/13.60). Git 커밋 `980409314fddbd83d9092b5c160fafab7f67fd52`.
- 실행한 `exiftool` 스크립트 SHA-256 `08E8783689898D6A57D075D13732B5F9E5CA8760A4454750CC3E1E2774E97CBB`. 이 값은 로컬 체크아웃 바이트의 해시이며 별도 서명 검증은 아니다.
- 기존 `C:/Program Files/Git/usr/bin/perl.exe`, Perl v5.42.2. 새 Perl·코덱·시스템 설정을 설치/변경하지 않았다.
- 공식 README는 Perl과 같은 Artistic 또는 GPL 조건을 명시하며, 저장소 LICENSE 파일은 GPL-3.0 전문이다. 개발 PC 내부 시험만 했으며 ExifTool·Perl·코덱을 묶은 실행본 재배포 조건 검토는 완료하지 않았다.

## 재현

저장소 루트에서 실행한다. clone 대상과 시험 출력 폴더는 새 경로여야 한다. 이미 내려받았다면 다시 clone하지 않고 커밋을 확인한다. Perl·스크립트 경로는 신뢰할 수 있는 로컬 경로를 명시해야 한다. 임의 다운로드 스크립트를 넘기지 않는다.

```powershell
git clone --depth 1 --branch 13.60 https://github.com/exiftool/exiftool.git runtime/exiftool-source-13.60
git -C runtime/exiftool-source-13.60 rev-parse HEAD
E:/tools/dotnet10/dotnet.exe restore tools/MediaPreflight --locked-mode --force --no-http-cache
E:/tools/dotnet10/dotnet.exe build tools/MediaPreflight -c Release --no-restore
E:/tools/dotnet10/dotnet.exe run --project tools/MediaPreflight -c Release --no-build -- test-artifacts/exiftool-final-01 'C:/Program Files/Git/usr/bin/perl.exe' runtime/exiftool-source-13.60/exiftool
```

출력 경로 하나만 전달하는 기존 모드는 계속 실행되지만 ExifTool은 `UNVERIFIED`로 표시한다. 소스/외부 실행 파일/샘플/출력/개인정보는 Git에 포함하지 않는다.

## 실제 결과

- 미구현 상태의 `exiftool-red-01`은 NotImplementedException으로 실패했다.
- 기본 샌드박스의 `exiftool-green-01`은 MSYS 공유 객체 접근 거부 `0xC0000022`로 실패했다. 일반 실행 환경의 `exiftool-green-02`와 최종 `exiftool-final-01`은 exit 0으로 통과했다. 앱 관리자 권한 요구 사항으로 해석하지 않는다.
- 합성 이미지 7종의 기존 미리보기/복원 검사를 유지했다. HEIC는 여전히 `UNVERIFIED`다.
- 외부 프로세스는 shell 없이 ArgumentList로 실행한다. 기본 ExifTool 사용자 설정을 비활성화하고 UTF-8 파일명을 지정한다. stdout/stderr를 동시에 읽고, 오류 종료나 stderr가 있으면 실패 처리한다. 30초 제한으로 하위 프로세스를 종료하지만 시간 초과 분기를 강제 실행한 시험은 아직 없다.
- `exiftool-시험 복사본.jpg`에서 촬영일과 남위 32.5도·서경 126.25도의 태그/방향을 JSON으로 재읽어 비교했다. 시간대 없는 시각은 그대로 비교하고 UTC로 해석하지 않았다.
- 전체 출력 폴더도 `test-artifacts/exiftool-한글 경로-01`로 바꾸어 재실행했고 exit 0이었다.
- 태그 수정 후 크기·RGB 디코딩 픽셀과 ExifTool의 `ImageDataHash` SHA-256이 수정 전과 같았다. 메타데이터를 제외한 주 이미지 데이터에 대한 시험이며 전체 EXIF/XMP/IPTC·썸네일·색상 프로필 보존까지 입증하지 않는다.
- 별도 백업을 `finally`에서 복원하고 파일 전체 SHA-256과 수정하지 않은 합성 원본 해시가 같은지 확인했다. 프로세스 강제 종료·공간 부족·디스크 고장 시험은 아니다.
- 없는 입력 파일의 ExifTool 오류 종료를 감지하는 검사도 통과했다.
- `restore --locked-mode --force --no-http-cache`가 경고 없이 성공했다. 이후 `build -c Release --no-restore`는 경고 0개·오류 0개였다. 이전 NU1900 연결 오류는 이번 재조회에서 해소됐다. 이것이 알려지지 않은 취약성까지 없다는 보장은 아니다.

## 남은 범위

실제 HEIC/HEIF·RAW·MP4/MOV의 읽기/미리보기, 전체 태그 보존과 장애 복구, 배포 라이선스 검토는 남아 있다. 공식 소스의 축소 회귀시험 파일도 실제 카메라/아이폰 원본 검증을 대신하지 않는다. 이 코드는 시험 전용이며 앱의 원본 적용 기능은 아직 구현하지 않았다.
