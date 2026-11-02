# 원본 GPS 표시와 지역 데이터 검증 기반

앱을 닫고 기존 바탕화면 실행 스크립트로 다시 실행한 뒤 사진을 선택한다. 저장된 원본 EXIF에 완전한 GPS가 있으면 오른쪽 메타데이터에서 위도·경도를 볼 수 있다. 지역명/지도/위치 검색은 아직 제공하지 않는다. 일부 GPS 태그가 없거나 값이 잘못되면 임의로 방향/좌표/지역을 추측하지 않는다.

## 처리 범위

- 기존 Magick.NET 수집기가 저장한 GPSLatitude/GPSLongitude의 3개 UInt32 유리수(도/분/초)와 N/S/E/W 방향을 검증한다. 다른 도구의 십진 GPS JSON 형식을 자동 추측해 해석하지 않는다.
- 0도와 남위/서경, 위도 ±90도/경도 ±180도의 유효 값은 처리한다. 분/초는 60 미만, 분모는 0이 아니어야 하며 범위를 넘으면 실패한다. 부호를 방향 태그 대신 음수 유리수에서 추측하지 않는다.
- 사진의 GPS 없음, 부분 누락, 잘못된 값/JSON, 파일 읽기 실패로 미확정인 상태를 구분한다. 영상은 GPS 없음으로 단정하지 않고 아직 미추출이라고 표시한다.
- 파서는 원본 EXIF JSON 문자열만 읽는다. 파일·DB 스키마·원본 태그·네트워크를 변경하지 않으며 새 패키지를 설치하지 않는다.
- 숫자는 기록된 원본 값이다. 실제 촬영지·기기 신뢰도·GPSMapDatum 등 좌표계/측지계 변환·측위 정확성은 검증하지 않았다. 소수점 6자리 표시가 그 정확도를 보장하지 않는다.

## 한국 행정구역 채택 상태

[후보 조사](research-korea-boundaries.md)에서 geoBoundaries ADM1/ADM2의 기준 연도 혼합, API/실제 개수 차이, 한글 이름/상위 코드와 재배포 근거의 추가 검증 필요를 확인했다. 그래서 해당 자료를 앱에 기본 포함하거나 이름을 수동 번역해 자동 분류하지 않았다.

다음 우선 후보는 공식 SGIS의 단일 배포본 경계·코드집이다. 원본 ZIP/이용안내와 한글 코드·같은 기준일·계층·좌표계·위상·도서/경계 사례를 검증해야 한다. 공식 메타데이터의 허용 표기만으로 모든 내부 파일 조건이 검증됐다고 간주하지 않는다. 이번 UI에도 자동 분류 대기 상태를 명시한다.

## 실행 검사

저장소 루트에서 실행한다. 출력은 새 test-artifacts 하위 경로여야 한다.

```powershell
E:/tools/dotnet10/dotnet.exe build src/PhotoLibrary.Desktop -c Release --no-restore
E:/tools/dotnet10/dotnet.exe run --project src/PhotoLibrary.Index -c Release --no-restore -- --self-test test-artifacts/gps-catalog-final
E:/tools/dotnet10/dotnet.exe src/PhotoLibrary.Desktop/bin/Release/net10.0-windows/PhotoLibrary.Desktop.dll --smoke-test test-artifacts/gps-ui-final
```

미구현 파서에서 실패를 먼저 확인했다. 이후 유리수·분수 변환, 남위/서경과 방향 패딩/대소문자, 0도·극점·날짜변경선, 누락된 방향, 분모 0·잘못된 분/초·범위·배열 길이·음수·타입·JSON 오류를 검사했다. 기존 DB/타임라인/원본 SHA-256 검사와 실제 WPF 표시 검사도 exit 0이었다. 빌드는 경고 0개·오류 0개였다.

WPF 검사에서는 합성 GPS 37.5/126.25와 분류 대기 문구를 확인했고, GPS 없는 정상 이미지와 읽기 오류 이미지도 미확정 사유를 구분했다. 실제 사용자 사진·원본 DB·기기 좌표 정확성·행정구역 일치율은 검증하지 않았다. 사진 위치를 지도/지명 서버에 전송하지 않았다.

`--iphone-test test-artifacts/iphone-gps-regression test-artifacts/iphone-media-final`도 exit 0이었다. 영상 GPS가 미추출 상태라고 표시되는지와 기존 공개 HEIC·합성 영상의 재생/종료/원본 해시를 확인했다. 이전에 기록한 GPU 변환/종료 로그는 계속 발생했으며 4K/HDR 안정성 문제를 해결했다는 결과는 아니다. gps-ui-final/window.png도 시각 확인했다.
