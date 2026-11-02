# 미디어 도구 사전 검증 결과

2026-11-02, Windows x64, .NET SDK 10.0.402에서 합성 파일만 사용했다. 사용자 사진과 XnView 파일은 읽거나 수정하지 않았다. 이 도구는 앱이 아닌 실행 가능한 시험이다.

## 재현

프로젝트 루트에서 실행한다. SDK가 PATH에 있으면 `dotnet`으로 바꿀 수 있다. 결과 경로는 반드시 현재 작업 디렉터리의 `test-artifacts` 하위 새 디렉터리여야 한다. 기존 경로, 범위 밖 경로, 심볼릭 링크/정션을 거치는 경로면 쓰기 전에 중단한다.

```powershell
E:/tools/dotnet10/dotnet.exe restore tools/MediaPreflight --locked-mode
E:/tools/dotnet10/dotnet.exe build tools/MediaPreflight -c Release --no-restore
E:/tools/dotnet10/dotnet.exe run --project tools/MediaPreflight -c Release --no-restore -- test-artifacts/verification-01
```

시험은 새 디렉터리 안에서만 파일을 생성한다. 결과물은 Git에서 제외한다. 백업은 복사본을 수정하기 전에 생성하고, 쓰기/재읽기 실패 시에도 `finally`에서 복원한다. 마지막 SHA-256 비교는 정상 경로의 바이트 단위 복원을 확인한다. 프로세스 강제 종료·디스크 고장·공간 부족 복구를 검증한 것은 아니다.

## 확인 결과

| 시험 | 관찰 |
|---|---|
| JPEG·PNG·BMP·GIF·TIFF·WebP·AVIF | 64×48 합성 파일 인코딩/디코딩, 32×24 JPEG 미리보기, 읽기 전후 원본 해시 동일 |
| JPEG 촬영일/GPS | 고정 시험 날짜와 북위 32도·동경 126도 태그 쓰기/재읽기 일치 |
| JPEG 복원 | 별도 백업으로 복원한 복사본과 수정하지 않은 시험 원본의 SHA-256 일치 |
| 손상 JPEG | MagickException으로 거부 |
| HEIC | 합성 인코더 부재. 실제 HEIC 디코딩은 미검증 |
| 실제 카메라 RAW·동영상·아이폰 파일 | 미검증. 지원 완료로 표시하지 않음 |
| ExifTool | 공식 배포 URL이 ZIP 대신 HTML 반환. 실행하지 않았으며 버전/동작 검증 미완료 |

읽기/메타데이터 쓰기/미리보기는 각각 미구현 예외로 실패를 먼저 확인하고 구현 후 재실행했다. `metadata-red-01`, `preview-red-01`의 실패는 의도된 RED다. 최종 검사는 전체 필수 항목 통과 시 exit 0이며, HEIC 시험 자료 생성 불가는 `UNVERIFIED`로 따로 출력한다.

`build --no-restore`는 오류 0개로 성공했고 전체 시험 `verification-01`은 exit 0이었다. 잠금 복원은 성공했으나 NuGet 취약성 조회가 연결 오류 `NU1900`으로 실패했다. 취약성이 없다는 판정은 아니며, 재조회 전까지 보안 검증 미완료다.

JPEG 쓰기는 Magick.NET 재인코딩 경로다. **원본 사진의 무손실 태그 편집 기능으로 채택하지 않는다.** 원본 적용은 ExifTool 등으로 별도 검증하고 공간 확인·원자적 교체·태그 보존·장애 복구를 추가해야 한다. GPS 부호·시간대·방향·다중 프레임·색상 프로필·EXIF 전체 보존도 이번 합성 시험의 보장 범위가 아니다.

## 버전과 출처

- Magick.NET-Q8-x64 및 Magick.NET.Core 15.0.0. `packages.lock.json`에 NuGet SHA-512 contentHash를 보관하고 locked restore로 고정한다.
- [공식 소스와 라이선스](https://github.com/dlemstra/Magick.NET), [15.0.0 릴리스](https://github.com/dlemstra/Magick.NET/releases/tag/15.0.0).
- 패키지 nuspec의 소스 커밋 `79bb8700d7fe3a564f62926ab299fc2520cb2269`와 Apache-2.0 표기를 확인했다.
- Q8 x64 nupkg SHA-256 `96BF50FCBC586E8A149B467A97FB3D1BF09C0362B5D8FF828A9936B23FDC5374`.
- Core nupkg SHA-256 `9936D4EA3ED1991A6672056E8DF47B75B43C00AFA884BFF78E136CD2141798B3`.
- 패키지의 `Notice.txt`에는 ImageMagick 7.1.2-33 및 여러 내장 구성요소의 별도 라이선스가 있다. 모든 코덱이 Apache-2.0이라는 뜻은 아니다. 배포 전 전체 고지 보존과 조건 검토가 필요하다. 현재 소스 시험 외 실행본 배포는 하지 않는다.
- [ExifTool 공식 홈페이지](https://exiftool.org/)에서 Windows 13.60 배포 링크를 확인했지만 다운로드를 검증하지 못했다. 실패한 HTML의 해시를 도구 해시로 사용하지 않는다.

## 다음 경계

ExifTool 정식 파일 확보와 무손실 태그 편집, 재배포 가능한 실제 HEIC/RAW/영상 시험 자료와 각 디코더, 제3자 고지 검토가 남아 있다. DB·WPF 화면·100만 건 성능은 이번 단계에 포함하지 않는다.

## 변경 검토

`178a853...4be70dd`를 독립된 두 검토자로 확인했다. Standards에서는 지침 위반 0건, Spec에서는 출력 경로 제한 누락 1건이 발견됐다. 이후 경로 제한과 정션/링크 거부를 추가하고 정상 경로 및 금지 경로를 재검증했다.

보강 후 `run --project tools/MediaPreflight -c Release --no-restore -- test-artifacts/verification-03`은 exit 0이었다. 빌드한 DLL에 `preflight-forbidden-01`, `test-artifacts/../preflight-forbidden-02`, 기존 `test-artifacts/verification-03`을 전달하면 모두 비정상 종료로 거부했다. 범위 밖 두 경로는 생성되지 않았다. 정션 거부 분기는 코드 검토만 했으며 실제 정션 생성 시험은 하지 않았다. Spec 검토자는 경로 제한 수정도 다시 확인했다.
