# 사진 전체 화면과 슬라이드쇼

지도 전체 화면이 아닌 사진 보기 기능이다. 지도는 합의한 상단 오른쪽 배치를 후속 작업으로 유지하며, 이번 변경에 실제 지도를 추가하지 않았다.

## 사용법

1. 폴더 수집과 목록 조회가 끝나면 사진을 선택한다.
2. 미리보기의 `사진 전체 화면 (F11)` 또는 F11 키, 사진 더블클릭으로 전체 화면을 연다. 자동 재생하지 않는다.
3. 자동 전환은 `슬라이드쇼` 버튼으로 시작하거나 전체 화면에서 `재생`을 누른다.
4. 간격은 기본 5초이며 1~300초 정수로 입력한다. 전체 화면에서는 `간격 적용`을 누르거나 입력란에서 Enter를 누른다. 잘못된 입력은 기존 설정을 유지한다.
5. Space는 재생/일시정지, 좌우 화살표는 이전/다음이다. 간격 입력란에서 편집 중에는 Space/좌우 키를 텍스트 편집에 사용한다.
6. Esc/F11 또는 `닫기`로 돌아온다. 원래 창의 폴더·선택·기간 입력·페이지·확대 상태는 바꾸지 않는다.

선택 위치에서 시작해 **실제로 목록에 적용된 폴더·날짜 조건**으로 이어진다. 아직 `기간 적용`을 누르지 않은 날짜 입력으로 목록과 다른 사진을 표시하지 않는다. 200건 페이지를 넘어 이동하며 모든 항목을 메모리에 모으지 않는다. 영상/미검증/오류 항목은 제외하고, 원본 연결 해제 등 읽기 실패는 건너뛴 개수로 알린다. 마지막 사진에서 멈추며 다시 재생하면 첫 사진부터 시작한다. 자동 반복은 하지 않는다.

사진은 검은 배경에 비율을 유지해 화면에 맞춘다. 파일명과 조작부는 아래에 두어 사진을 가리지 않는다. 기존 읽기 전용 디코더를 재사용하므로 긴 변 최대 2400px 미리보기와 5천만 픽셀 입력 제한을 유지한다. 원본 픽셀 해상도의 확대·영상 슬라이드쇼·HDR/4K 색상 보증 기능은 아니다.

설정 간격은 사진 디코딩 완료 후부터 대기하며, 로딩 시간만큼 실제 전환이 더 걸릴 수 있다. 로딩 중 타이머를 멈춰 겹치는 작업이 쌓이지 않게 한다. 창을 닫으면 타이머를 정지하고 늦은 디코딩 결과를 버린다. 수정/복사/정리/원본 삭제는 수행하지 않는다.

전체 화면에서 바꾼 간격은 원래 창에도 반영되지만 앱 재시작 시 기본 5초로 돌아온다. 기존 OFFSET 조회를 재사용하므로 아주 뒤쪽 페이지의 대규모 탐색 성능은 아직 보장하지 않는다. 수집/조회 중에는 시작을 막으며, 다른 프로세스의 DB 갱신까지 고정한 장기 스냅샷은 아니다.

## 검증

저장소 루트에서 다음 명령을 실행했다. 사용자 사진 대신 합성/공개 시험 자료만 사용한다.

```powershell
E:/tools/dotnet10/dotnet.exe build src/PhotoLibrary.Desktop -c Release --no-restore
E:/tools/dotnet10/dotnet.exe src/PhotoLibrary.Desktop/bin/Release/net10.0-windows/PhotoLibrary.Desktop.dll --slideshow-test test-artifacts/slideshow-ui-verified
E:/tools/dotnet10/dotnet.exe src/PhotoLibrary.Desktop/bin/Release/net10.0-windows/PhotoLibrary.Desktop.dll --smoke-test test-artifacts/slideshow-regression-ui
E:/tools/dotnet10/dotnet.exe run --project src/PhotoLibrary.Index -c Release --no-restore -- --self-test test-artifacts/slideshow-regression-catalog
```

빌드는 경고/오류 0개이며 위 검사는 모두 exit 0이었다. 새 UI 검사는 203개 합성 사진과 오류/영상/미검증 항목, 다른 기간·다른 루트로 전체 화면 비율·자동 재생 없는 진입·실제 1초/2초 타이머·일시정지·간격 오류·최대 간격 300초·페이지 경계/역방향·읽기 실패 건너뛰기·끝에서 정지/재시작·Esc·닫는 중 늦은 결과·빈 기간·원본 SHA-256 불변을 확인했다.

검사를 반복할 때는 test-artifacts 아래의 새 출력 폴더 이름을 사용한다. 기존 출력 폴더를 덮어쓰지 않는다.

`test-artifacts/slideshow-ui-final/fullscreen.png`와 기존 UI 검사 화면을 직접 확인했다. 실제 다중 모니터·실제 마우스 더블클릭·장시간 슬라이드쇼는 수동 검증으로 남긴다.

공개 HEIC/HEIF와 합성 MOV/MP4의 기존 검사도 `--iphone-test test-artifacts/slideshow-regression-iphone test-artifacts/iphone-media-final`로 실행해 exit 0이었다. 실제 디코딩/재생/원본 해시는 통과했지만 기존 D3D11 picture allocation/converter/thumbnail 로그는 계속 관찰됐으며 이번 변경에서 해결했다고 주장하지 않는다.
