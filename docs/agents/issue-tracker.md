# 로컬 이슈 관리

GitHub는 원격 Git 저장소로 사용하며 작업 이슈는 로컬 Markdown으로 관리한다.

- 기능별 디렉터리는 `.scratch/<feature>/`다.
- 기능 명세는 해당 디렉터리의 `spec.md`다. 전체 구현 계획의 원본은 루트 plan.md이며 중복 복사하지 않고 링크한다.
- 개별 이슈는 `issues/01-<slug>.md`부터 번호를 붙여 하나씩 만든다.
- 이슈 앞부분에 `Status:`를 기록한다. 상태 이름은 triage-labels.md를 따른다.
- 논의는 파일 아래 `## Comments`에 추가한다.
- ‘이슈 게시’ 요청은 로컬 파일 작성을 뜻한다. 별도 요청 없이 GitHub Issues/라벨을 생성하지 않는다.
