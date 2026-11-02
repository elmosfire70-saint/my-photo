# 읽기 전용 사진 DB 수집

아래는 최초 수집 단계의 기록이다. HEIC/HEIF 및 영상의 현재 지원/메타데이터 범위는 [아이폰 미디어 후속 작업](iphone-media.md)을 따른다.

Windows용 개발 단계의 명령행 모듈이다. 폴더를 명시적으로 지정해야 하며 사용자 폴더를 자동 검색하지 않는다. GUI·미리보기·원본 쓰기 기능은 아직 없다. 이번 실행에는 자체 생성한 합성 자료만 사용했다.

## 재현

저장소 루트에서 실행한다. 시험 출력은 기존에 없는 test-artifacts 하위 경로여야 한다.

```powershell
E:/tools/dotnet10/dotnet.exe restore src/PhotoLibrary.Index --locked-mode
E:/tools/dotnet10/dotnet.exe build src/PhotoLibrary.Index -c Release --no-restore
E:/tools/dotnet10/dotnet.exe run --project src/PhotoLibrary.Index -c Release --no-build -- --self-test test-artifacts/catalog-final-02
E:/tools/dotnet10/dotnet.exe run --project src/PhotoLibrary.Index -c Release --no-build -- list test-artifacts/catalog-final-02/catalog.sqlite 2020-01-02 2020-01-02
```

사용자가 선택한 실제 폴더에는 다음 형식을 사용할 수 있다. 이번 개발 시험에서는 실제 사진 폴더에 이 명령을 실행하지 않았다.

```powershell
E:/tools/dotnet10/dotnet.exe run --project src/PhotoLibrary.Index -c Release --no-build -- scan 'SOURCE_FOLDER' 'data/catalog.sqlite'
E:/tools/dotnet10/dotnet.exe run --project src/PhotoLibrary.Index -c Release --no-build -- list 'data/catalog.sqlite'
```

DB는 원본 폴더 밖에 두어야 한다. 잘못된 DB를 덮어쓰지 않도록 application_id와 schema version을 검사한다. 폴더/DB 경로의 링크·정션은 거부하고 폴더 안의 링크·정션은 따라가지 않는다. 조회는 읽기 전용 연결이며 기본 200건, API 최대 1000건이다. 명령행에는 아직 다음 페이지 기능이 없다. 날짜 범위의 양끝 날짜를 포함한다.

## 저장과 재개

- JPEG/PNG/BMP/GIF/TIFF/WebP/AVIF의 헤더·메타데이터만 읽는다. 전체 픽셀의 정상 디코딩이나 모든 변종 지원을 보장하지 않는다.
- 루트·상대 경로·크기·UTC 수정시각, 폭/높이, EXIF 값 JSON, Magick.NET이 반환한 프로필의 Base64를 저장한다. GPS 배열은 분자/분모를 보존한다. 알려지지 않은 제조사 정보나 ExifTool의 모든 태그 추출을 완료한 것은 아니다.
- EXIF DateTimeOriginal이 유효하면 원래 달력 시각을 사용하고 OffsetTimeOriginal은 별도로 보존한다. 시간대가 없다고 UTC로 바꾸지 않는다. 없거나 잘못된 촬영일은 파일 수정 UTC로 대체하고 `file-modified-utc-estimated`로 표시한다. 두 날짜 출처는 같은 UTC 축으로 정규화된 값이 아니며 조회는 각 기록의 달력 날짜 기준이다.
- HEIC/HEIF/CR2/CR3/NEF/ARW/DNG/MP4/MOV는 `UNVERIFIED_FORMAT`, 헤더 손상은 `INVALID_IMAGE`, 이미지 읽기 실패/변경은 `READ_FAILED_OR_CHANGED`로 기록한다. 그 밖의 확장자는 무시한다.
- 각 행을 단일 SQL 문장으로 커밋한다. Ctrl+C는 파일 처리 사이/저장 전에 반영하며 재실행 시 크기·수정시각이 같은 정상 항목을 건너뛴다. 오류는 재시도한다. 크기·수정시각을 보존한 내용 변경은 감지하지 못한다. 고비용 헤더 처리 도중의 즉시 취소/타임아웃은 아직 없다.
- 입력 루트가 없으면 중단하고 기록은 지우지 않는다. 수집 도중 폴더 접근/파일 상태 조회 자체가 실패해도 진행을 중단할 수 있다. 삭제·연결 해제 판별, 볼륨 ID/드라이브 문자 재연결, 겹치는 등록 폴더의 동일 파일 통합은 후속 범위다.
- WINPATH 정렬 규칙은 모듈이 등록한 OrdinalIgnoreCase다. 외부 SQLite 도구로 이 DB를 수정하지 않는다. 스키마 마이그레이션·일일 백업·사용자 보정·동시 수집·성능 최적화는 아직 구현하지 않았다.

## 검증 기록

미구현 self-test가 실패한 뒤 실행 가능한 합성 검사를 추가했다. 날짜 상한 없는 조회 오류와 검사 자체의 파일 순서 가정을 수정하고 재실행했다. 정상 항목 커밋 후 취소/재개, 정상 파일 건너뛰기, 오류 재시도, 손상/미검증 구분, 날짜 범위, 촬영일 출처·오프셋·GPS 프로필, 조회 제한, 한글/공백/비ASCII 대소문자 경로, 원본 바이트 불변, 변경 파일 갱신, 잘못된 DB 위치·다른 DB 거부, 오프라인 기록 유지가 통과했다. 강제 프로세스 종료/디스크 장애나 실제 100만 건 검사는 아니다.

`run --project src/PhotoLibrary.Index -c Release --no-restore -- --self-test test-artifacts/catalog-final-02`는 exit 0으로 통과했다. 읽기 전용 속성의 합성 JPEG도 수집했다. 같은 DB에 명령행 scan을 재실행하면 Written=2, Skipped=3, Errors=2였고, 2020-01-02 날짜 조회는 정확히 1행이었다. `build src/PhotoLibrary.Index -c Release --no-restore`는 경고 0개·오류 0개, `git diff --check`도 통과했다. 링크/정션 차단은 코드 검토만 했으며 실제 링크를 생성하는 시험은 하지 않았다.

2026-11-02에 Microsoft.Data.Sqlite 10.0.14와 Magick.NET 15.0.0으로 복원했고 packages.lock.json에 전이 의존성과 contentHash를 고정했다. Microsoft 패키지 nuspec의 MIT 및 SQLitePCLRaw bundle의 Apache-2.0 표기를 확인했으며 전체 배포 고지 검토는 여전히 별도 작업이다.

[Microsoft의 SQLite 트랜잭션 설명](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions)과 [디렉터리 열거 API](https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.enumeratefiles?view=net-10.0)를 참고했다. 성능/일관성 주장은 위 합성 검사 범위를 넘지 않는다.

DB·출력 JSON에는 경로·GPS 등 개인정보가 들어갈 수 있으므로 공개 저장소나 로그에 올리지 않는다. test-artifacts/data/DB/빌드 출력은 Git에서 제외한다.
