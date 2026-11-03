# 사진함 DB 수동 백업

사진 폴더 수집이 끝나면 상단 `DB 백업…`에서 별도 폴더와 새 `.sqlite` 파일 이름을 선택한다. 기존 파일은 덮어쓰지 않으므로 이름을 바꿔야 한다. 백업 중에는 폴더/DB 변경과 추가 백업을 막으며 완료 후 종료할 수 있다. 저장 성공뿐 아니라 무결성 검증까지 통과해야 완료로 표시한다.

이 파일은 사진함 메타데이터 DB의 백업이다. 원본 사진·동영상·지도 캐시는 포함하지 않는다. DB 안에 파일 경로·EXIF·GPS가 있으므로 개인 자료로 취급하고 공유 저장소나 Git에 올리지 않는다. 원본과 다른 디스크에 보관하면 원본 디스크 장애 위험을 줄일 수 있다.

단순 DB 파일 복사 대신 [Microsoft.Data.Sqlite 공식 백업 API](https://learn.microsoft.com/dotnet/standard/data/sqlite/backup)를 사용한다. WAL에 커밋된 변경도 포함한 일관된 스냅샷을 만들고 같은 대상 디렉터리의 작업 전용 임시 파일에서 `PRAGMA integrity_check`와 사진함 식별자 검사를 통과한 뒤 새 이름으로 이동한다. 링크 경로·기존 대상·원본 덮어쓰기는 거부한다. 정상적으로 처리되는 실패에서는 작업 소유 임시 파일만 제거한다. 강제 종료/전원 차단 시 숨김 임시 파일이 남을 수 있으며 다음 실행에서 임의 삭제하지 않는다.

앱은 작업 중 응답하도록 백그라운드에서 실행하지만 API 자체는 단계별 취소를 제공하지 않는다. 다른 프로그램에서 DB를 쓰는 중이라면 백업 완료를 기다려야 할 수 있다. 전체 디스크 용량 사전 예약·대용량 소요 시간·전원 차단 시험은 아직 하지 않았다. 저장 실패는 완료로 표시하지 않는다.

## 명령행과 검증

```powershell
dotnet run --project src/PhotoLibrary.Index -c Release --no-restore -- backup SOURCE.sqlite NEW_BACKUP.sqlite
dotnet run --project src/PhotoLibrary.Index -c Release --no-restore -- --self-test test-artifacts/backup-new
dotnet src/PhotoLibrary.Desktop/bin/Release/net10.0-windows/PhotoLibrary.Desktop.dll --smoke-test test-artifacts/backup-ui-new
```

복구 검증은 백업 파일을 별도 DB로 재열어 합성 항목과 원본 날짜/GPS가 동일한지 확인한다. 이 단계는 현재 DB를 자동 교체하는 복원 기능이 아니다. 일일 자동 백업·백업 위치 설정 보관·명시적 복원 화면은 후속 작업이다. 실제 사용자 DB나 원본 사진은 시험하지 않는다.

검증 결과는 Release 빌드 0경고/0오류, `--self-test test-artifacts/backup-catalog-final`, `--smoke-test test-artifacts/backup-ui-final`, `--map-test test-artifacts/backup-map-regression` 모두 exit 0이다. WAL 커밋 반영·기존 대상 보존·손상 DB 거부·CHECK 제약을 위반한 합성 DB의 무결성 실패 시 미공개/임시 정리·원본 해시와 UI 복귀를 확인했다. 화면 캡처는 `test-artifacts/backup-ui-final/window.png`다.
