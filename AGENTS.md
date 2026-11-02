# 프로젝트 지침

- 작업 전 plan.md, checklist.md, context-notes.md와 GLOSSARY.md를 읽는다. 기능 추가 시 계획 범위를 확인한다.
- 사용자 원본 대신 복사한 시험 파일로 검증한다. 원본 수정·이동·삭제는 앱의 미리보기·확인·백업 경로를 거친다.
- 사진·영상·GPS 원자료·개인 DB·캐시·비밀정보를 커밋하거나 외부로 전송하지 않는다. XnView 파일·코드를 재사용하지 않는다.
- 기존 변경을 보존하고 요청에 필요한 최소 변경만 한다. 새 소스 첫 줄에 역할을 설명하는 한국어 주석을 둔다.
- 코드 변경 후 관련 검사를 실행하고 실제 명령·결과·미검증 범위를 보고한다. 논리적 변경 단위로 커밋한다.

## Agent skills

### Issue tracker

작업과 이슈는 로컬 Markdown으로 관리한다. docs/agents/issue-tracker.md 참조.

### Triage labels

기본 다섯 상태 이름을 사용한다. docs/agents/triage-labels.md 참조.

### Domain docs

단일 GLOSSARY.md와 필요한 설계 결정의 docs/adr/를 사용한다. docs/agents/domain.md 참조.
