# 미디어 도구 읽기와 JPEG 복원 검증

Status: ready-for-human

사용자 확인 경계는 ../spec.md를 따른다. 라이선스·버전·다운로드 해시를 기록하고 실행 가능한 검사를 남긴다. RAW·영상·실제 사용자 파일은 검증 전 지원 완료로 표시하지 않는다.

## Comments

Magick.NET 합성 이미지/미리보기 및 JPEG 복원 시험을 구현했다. docs/media-preflight.md에 재현 명령과 제약을 기록했다. ExifTool 확보, HEIC/실물 RAW/영상 및 배포 라이선스 검토는 남아 있어 상위 단계 완료로 표시하지 않는다.

후속 시험에서는 공식 ExifTool 소스와 기존 Perl을 사용했다. 합성 JPEG 태그·이미지 데이터 보존과 복원을 통과했다. docs/exiftool-preflight.md 참조. 실제 형식별 샘플과 배포 조건은 여전히 미완료다.
