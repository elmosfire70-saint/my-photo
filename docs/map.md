# 선택 사진 지도

상단 오른쪽에 OpenStreetMap 지도를 표시한다. GPS가 있는 사진을 선택하면 원본 좌표에 빨간 핀이 나타난다. 휠 또는 +/−로 확대하고 드래그로 이동한다. GPS 없는 사진, 동영상, 읽기 오류 항목으로 바꾸면 이전 핀을 지운다. 측지기준 누락은 참고 위치로 표시하며 명시적 비-WGS84 좌표는 표시하지 않는다. 행정구역 자동 분류와 지역 검색 완료를 의미하지 않는다.

별도 브라우저 런타임이나 패키지 없이 WPF Canvas와 HttpClient를 사용한다. 온라인 지도 체크를 끄면 이미 저장한 타일만 사용한다. 설정은 앱 재시작 시 온라인 기본값으로 돌아간다. 지도 서버에는 현재 화면의 타일 번호가 전송되므로 대략적인 열람 지역과 접속 IP가 드러날 수 있다. 사진·파일명·DB·원본 GPS 수치를 요청 본문이나 URL 매개변수로 전송하지 않는다.

타일은 `%LOCALAPPDATA%\MyPhoto\map-tiles-v1`에 저장한다. HTTP 캐시 만료·ETag·Last-Modified·조건부 요청·no-store를 처리하며, 만료 지시가 없으면 7일을 적용한다. 현재 화면 밖 미리 받기나 오프라인 지역 다운로드는 하지 않는다. [OSM 공식 타일 정책](https://operations.osmfoundation.org/policies/tiles/)에 맞춰 앱 식별과 지도 출처를 표시한다. 공개 서버는 가용성 보장이 없으며 연결 실패 시 사진 탐색은 계속된다.

현재 제공자 URL은 고정이며 제공자 변경 UI와 전체 캐시 용량 제한은 후속 범위다. 실제 마우스 드래그·네트워크 장시간 장애·고해상도/DPI 환경은 추가 수동 확인이 필요하다. 사진 전체 화면과 슬라이드쇼에는 이 지도를 넣지 않는다.

## 재현 검사

저장소 루트에서 `E:\tools\dotnet10\dotnet.exe`를 사용한다. 각 출력 디렉터리는 새 경로여야 한다.

```powershell
dotnet build src/PhotoLibrary.Desktop -c Release --no-restore
dotnet src/PhotoLibrary.Desktop/bin/Release/net10.0-windows/PhotoLibrary.Desktop.dll --map-test test-artifacts/map-check-new
dotnet src/PhotoLibrary.Desktop/bin/Release/net10.0-windows/PhotoLibrary.Desktop.dll --map-live-test test-artifacts/map-live-new
```

합성 지도 검사는 가짜 HTTP와 별도 캐시로 핀 초기화, 확대, 늦은 응답 취소, 좌표 변환, 캐시/304/no-store, 잘못된 응답, 원본 불변을 확인한다. 합성 화면을 실제 지도 증거로 사용하지 않는다. 실제 지도 검사는 사용자 사진이나 GPS 없이 한국 기본 화면 한 번만 요청한다. 두 검사 모두 통과했으며 `map-ui-01/map-synthetic.png`와 `map-live-01/map-live.png`를 시각 확인했다. 기존 UI·카탈로그·슬라이드쇼 회귀검사도 통과했다.
