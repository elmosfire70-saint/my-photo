# WPF 폴더 화면과 미리보기

이 문서는 첫 화면 단계 기록이다. 이후 추가한 HEIC/HEIF·MOV/MP4 지원은 [아이폰 미디어](iphone-media.md)를 따른다.

2026-11-02 기준 개발 버전이다. 기존 수집 모듈과 WPF 기본 컨트롤을 재사용하며 새 UI 패키지는 추가하지 않았다. 원본 수정·삭제·이동 버튼은 없다. 연월 타임라인·지도·디스크 캐시·일일 DB 백업·일반 배포는 아직 구현하지 않았다.

## 실행

프로젝트 루트의 `Start-MyPhoto.cmd`를 실행한다. 이 PC의 E:/tools/dotnet10/dotnet.exe가 있으면 사용하고, 없으면 PATH의 .NET 10 SDK가 필요하다. 개발용 실행기이므로 콘솔과 첫 빌드/패키지 복원이 나타날 수 있다. 앱 자체를 관리자 권한으로 실행할 필요는 없다.

```powershell
E:/tools/dotnet10/dotnet.exe run --project src/PhotoLibrary.Desktop -c Release
```

폴더 선택 후에만 파일을 수집하며 하위 폴더를 포함한다. DB 기본 위치는 `%LOCALAPPDATA%/MyPhoto/catalog.sqlite`이며 DB 위치 버튼으로 바꿀 수 있다. 원본 폴더 안에 DB를 만들면 기존 수집기의 보호 검증으로 거부한다. 이번 개발 검사에서는 사용자 사진이나 기본 사용자 DB에 접근하지 않고 test-artifacts에만 생성했다.

## 제공 화면

- 폴더별 200건 목록과 이전/다음 페이지, 양끝 날짜를 포함하는 날짜 필터.
- 백그라운드 수집, 약 600ms 간격으로 커밋된 목록 갱신, 취소 후 재수집. 수집 중 닫으면 취소·현재 파일 저장 처리를 기다려 닫는다. 현재 파일을 강제로 중단하지는 않는다.
- 촬영일과 추정 UTC의 구분, 오프셋·원본 EXIF JSON 일부(최대 20,000자), 손상/미검증 형식의 이유. 전체 태그 편집·보정 UI는 없다.
- 선택 이미지의 첫 프레임 미리보기. 원본은 읽기 전용 스트림으로 읽고 완료 후 닫는다. 긴 변은 최대 2400px이고 입력이 5천만 픽셀을 넘으면 거부한다.
- 미리보기 기준 10~800% 확대, 화면 맞춤, 가로/세로 스크롤, 이미지 드래그 이동과 Ctrl+휠 확대. 확대가 원본 이상의 세부 정보를 복구하는 것은 아니다.
- 미리보기 작업은 한 번에 하나만 처리하고 선택이 바뀌면 오래된 결과를 표시하지 않는다. 현재 선택 루트 밖의 경로나 링크·정션은 거부한다.

HEIC/RAW/영상은 여전히 미검증이다. 수집 헤더가 정상이어도 픽셀 디코딩이 실패하면 미리보기 오류를 표시한다. 애니메이션 재생·전체 프레임 탐색은 지원하지 않는다. 사용자 폴더/DB 대화상자, 실제 마우스 드래그/Ctrl+휠, 다중 프레임·대용량·EXIF 회전 사진과 실제 사용자 데이터는 수동/후속 검증 대상이다.

## 실행 가능한 검사

저장소 루트에서 실행한다. 출력 폴더는 새 경로여야 하며 합성 이미지 외에는 사용하지 않는다.

```powershell
E:/tools/dotnet10/dotnet.exe restore src/PhotoLibrary.Desktop --locked-mode
E:/tools/dotnet10/dotnet.exe build src/PhotoLibrary.Desktop -c Release --no-restore
E:/tools/dotnet10/dotnet.exe src/PhotoLibrary.Desktop/bin/Release/net10.0-windows/PhotoLibrary.Desktop.dll --smoke-test test-artifacts/desktop-final-01
E:/tools/dotnet10/dotnet.exe run --project src/PhotoLibrary.Index -c Release --no-restore -- --self-test test-artifacts/catalog-ui-regression-01
```

미구현 UI 검사에서 NotImplementedException을 먼저 확인했다. 이후 합성 입력 205개의 UI 검사와 기존 수집 회귀검사가 모두 exit 0으로 통과했다. WPF 빌드는 경고 0개·오류 0개였다. 200/5건 페이지, 날짜 필터, 픽셀 표시와 파일 핸들 해제, 루트 이탈 거부, 800% 확대 후 양방향 스크롤, 오류 선택 시 오래된 미리보기 폐기, 수집 취소/종료 및 모든 합성 원본의 SHA-256 불변을 확인했다.

UI 검사에서 실제 WPF 창을 렌더링해 test-artifacts/desktop-final-01/window.png와 zoom.png를 저장하고 window.png를 시각 확인했다. 이 검사는 마우스 입력을 주입한 전체 사용자 자동화가 아니라 실제 WPF 컨트롤과 연결된 처리 경로를 실행하는 통합 검사다. 폴더/DB 대화상자는 프로그램으로 우회했고 실제 사용자 사진은 읽지 않았다.

`Start-MyPhoto.cmd --smoke-test test-artifacts/desktop-launcher-01`도 exit 0으로 통과했다. 이 실행기의 자동 복원에서 NU1900 연결 경고가 있었지만, 공식 NuGet 서버에서 `restore --locked-mode --force --no-http-cache`로 재조회한 뒤 복원과 최종 빌드는 경고 없이 성공했다. 이는 알려지지 않은 모든 취약성까지 없다는 판정은 아니다.

폴더별 조회/페이지 추가에 대해 수집 검사에서도 다른 루트 제외, 다음 페이지와 음수 OFFSET 거부를 확인했다. OFFSET 페이지는 깊은 페이지에서 비용이 증가하며 100만 건 성능을 달성했다고 보장하지 않는다.

[WPF 폴더 선택 API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.openfolderdialog?view=windowsdesktop-10.0)와 [비트맵 OnLoad 캐시](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.imaging.bitmapcacheoption?view=windowsdesktop-10.0)를 참고했다. DB·사진·스크린샷은 Git에 포함하지 않는다.
