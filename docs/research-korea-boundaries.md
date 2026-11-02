# 한국 시도·시군구 오프라인 경계 후보 조사

조사일은 2026-11-02다. 공식 서비스·제공자 원문과 공개 API를 확인했으며 법률 자문이나 경계 정확성 보증이 아니다. 사용자 미디어·GPS·DB는 사용하지 않았다.

## 결론

우선 검증 후보는 **공공데이터포털의 국가데이터처 SGIS 2025년 기준 경계 패키지**다. 경계·코드집을 같은 배포본에서 확보하는 방향이 가장 직접적이다. 원본 SHP·참고자료 실제 확보, 동일 기준일 확인, 코드·한글 명칭·시군구 정의·위상 검증 전에는 앱 기본 데이터로 채택하지 않는다. `최신 행정구역`이라고 표시하지 않는다.

대안은 **admdongkor 한 시점의 원본 경계를 고정하고 시도·시군구를 함께 파생**하는 것이다. geoBoundaries ADM1/ADM2를 그대로 혼합하는 것은 현재 요구에 부적합하다.

## 1. 공식 SGIS 배포본

[공식 데이터셋 메타데이터 JSON](https://www.data.go.kr/catalog/15129688/fileData.json)은 다음을 명시한다.

- 제공기관은 국가데이터처, 자료명은 `국가데이터처_SGIS 행정구역 통계 및 경계_20250630`다.
- 설명에 2024년 행정구역 통계와 **2025년 기준 경계**, CSV·SHP, 참고자료 폴더의 코드집·자료 이용안내가 명시되어 있다.
- 전국 자료이며 `license.name`은 `이용허락범위 제한 없음`이다. 메타데이터 수정일 `2026-07-23`은 경계 기준일이 아니다. `temporalCoverage`의 2024년은 통계 기간이므로 경계 연도로 사용하면 안 된다.

[공식 통계데이터센터 자료목록](https://data.kostat.go.kr/sbchome/serviceData/svcOfrDataList.do?curMenuNo=OPT_0)도 행정구역 경계 전체·시도·시군구·행정동을 **2025년 2분기** 기준으로 안내하며 신청자료별 경계파일·코드집 제공을 명시한다. 두 안내가 같은 패키지의 정확히 같은 파일을 뜻하는지는 실제 압축파일로 확인해야 한다.

[공공데이터포털 이용정책](https://www.data.go.kr/ugs/selectPortalPolicyView.do)은 제3자 권리가 있으면 정당한 이용허락을 확보해야 한다고 안내한다. 목록의 허용 표기를 확인한 사실과 압축파일 내부의 모든 조건을 확인한 사실은 구분한다. 이 조사에서는 원본 패키지·내부 이용안내·개별 SHP 속성을 아직 확인하지 못했다. SGIS 모든 파일에 일괄적으로 CC0·공공누리 1유형이 적용된다고 단정하지 않는다.

접근 기록. 목록 HTML은 웹 도구에서 `Unsupported content-type: text/markdown`, PowerShell 요청에서는 `알려진 호스트가 없습니다`였으나 위 공식 JSON은 웹 도구로 정상 열람했다. [SGIS 다운로드 화면](https://sgis.mods.go.kr/view/pss/downloadList)은 읽을 수 있는 본문이 반환되지 않았다. 로그인·신청이나 사용자 계정 변경은 수행하지 않았다.

## 2. admdongkor 고정 시점 대안

`git ls-remote https://github.com/vuski/admdongkor.git HEAD`로 확인한 커밋은 `d5d5a821c8de6cc337d47eeca604d7af76271c9b`다. 다음은 이 커밋에 고정한 제공자 문서다.

- [README](https://raw.githubusercontent.com/vuski/admdongkor/d5d5a821c8de6cc337d47eeca604d7af76271c9b/README.md)는 SGIS 원자료를 수정한 경계, `adm_nm` 한글 이름, `adm_cd` 통계분류 코드, `adm_cd2` 행정기관 코드를 설명한다. `adm_cd`의 앞 2자리/5자리로 dissolve하면 시도/시군구를 만들 수 있다고 안내한다. 이는 법정동 코드와 같은 체계라는 뜻이 아니다.
- [데이터 전용 LICENSE-DATA](https://raw.githubusercontent.com/vuski/admdongkor/d5d5a821c8de6cc337d47eeca604d7af76271c9b/LICENSE-DATA)는 가공물을 CC BY 4.0으로 제공하고 SGIS 및 vuski/admdongkor 출처 보존을 요구한다. MIT는 소스 코드에만 적용된다. SGIS 원자료가 공공누리 1유형이라는 설명은 이 가공 제공자의 주장이고, 모든 과거 원파일의 조건을 독립적으로 확인한 결과는 아니다.

실제 대안은 특정 `verYYYYMMDD` 원본 파일과 해시를 고정하여 한 번 변환하는 방식이다. 단순화 전 원본으로 시군구를 만든 뒤 그 시군구를 시도로 병합하면 계층별 출처·시점 혼합을 피할 수 있다. 단, 이는 설계 권고이며 실제 변환·위상 검증은 하지 않았다. 공식 SGIS 2025-06-30과 `ver20250701`을 같은 시점으로 간주해서도 안 된다.

README는 일부 실제 경계 불일치를 인정한다. 또한 기본 라이브러리는 import 시 인덱스를 자동 다운로드하므로 그대로 앱 런타임에 도입하면 오프라인·고정 버전 요구와 충돌한다. 도서 위치를 육지 가까이 옮긴 표시용 변형본도 있으므로 GPS 분류에는 그런 파일을 선택하지 않아야 한다. 구역 개수·공식 코드집 대응·일반구 포함 여부·출장소/세종/제주 예외를 실제 데이터로 검증해야 한다.

## 3. geoBoundaries 기존 후보의 한계

| 계층 | 공식 API 표기 | 확인한 한계 |
| --- | --- | --- |
| ADM1 | [공식 API](https://www.geoboundaries.org/api/current/gbOpen/KOR/ADM1/). 2021년, Natural Earth, 17개, Public Domain. | 아래 ADM2와 기준 연도·원천이 다르다. |
| ADM2 | [공식 API](https://www.geoboundaries.org/api/current/gbOpen/KOR/ADM2/). 2020년, geoBoundaries/citypopulation.de, 229개, CC BY 3.0 표기. | 아래 고정 다운로드 실측은 228개다. API 메타데이터와 불일치한다. |

고정 다운로드 커밋은 `9469f09592ced973a3448cf66b6100b741b64c0d`다. 로컬 캐시에 확보된 [ADM1 원문](https://raw.githubusercontent.com/wmgeolab/geoBoundaries/9469f09592ced973a3448cf66b6100b741b64c0d/releaseData/gbOpen/KOR/ADM1/geoBoundaries-KOR-ADM1.geojson)·[ADM2 원문](https://raw.githubusercontent.com/wmgeolab/geoBoundaries/9469f09592ced973a3448cf66b6100b741b64c0d/releaseData/gbOpen/KOR/ADM2/geoBoundaries-KOR-ADM2.geojson)을 `Get-Content -Raw | ConvertFrom-Json`으로 읽어 `features.Count`를 확인했다. ADM1은 17개, ADM2는 228개다. 첫 속성은 각각 `Gangwon`/`KR-42`, `Sokcho-si`/빈 `shapeISO`였으며 한글 이름·상위 시도 코드가 그 속성에 없다. 전체 파일의 완전성·정합성 검사 완료를 뜻하지 않는다.

추가로 전체 feature의 속성을 검사했다. 한글 shapeName은 두 계층 모두 0개, 빈 shapeISO는 ADM1 0개/ADM2 228개, 중복 shapeID는 두 계층 모두 0개였다. 중복 ID가 없다는 사실이 geometry 중복·빈틈이나 정확성을 증명하지는 않는다. 수량 불일치도 특정 구역 한 개가 누락됐다는 증명으로 해석하지 않는다.

| 파일 | SHA-256 |
| --- | --- |
| `.cache/korea-boundaries/ADM1.geojson` | `6683CD1AD991676D96493FD0AAE068215426497CCF82C8B0EB5683CAD341CDDC` |
| `.cache/korea-boundaries/ADM2.geojson` | `C367911B02F2CD7DCAE6512087D84965131971DE0C9DEF2FC5D0ED9A7392F2D8` |

[Natural Earth 원 약관](https://www.naturalearthdata.com/about/terms-of-use/)은 지도 자료의 public domain과 수정·배포 허용을 확인한다. 반면 [City Population 원 약관](https://www.citypopulation.de/en/help/termsofuse/)은 CC BY 3.0을 인구 데이터 절에 명시하고 지도·지리공간 자료는 별도 저작권 절로 다룬다. 따라서 geoBoundaries API의 라이선스 표기를 City Population 모든 지도에 확대해서 해석하면 안 된다. 이 차이가 geoBoundaries의 별도 허가 부재를 증명하는 것은 아니나, 재배포 전 고정 릴리스의 허가 근거 확인이 필요하다.

## 채택 전 필요한 최소 증거

### 공식 원본 확보 후 추가 검증

공공데이터포털 공개 다운로드 버튼의 `selectFileDataDownload.do` 응답에서 `status=true`, `atchFileId=FILE_000000003681593`, `fileDetailSn=1`을 확인했다. 공개 `check-limit.json` 응답이 `needCaptcha=false`인 것을 확인한 뒤 공식 `fileDownload.do`로 다운로드했다. SGIS 자체 다운로드의 로그인은 우회하지 않았다. 포털의 목록 확장자 CSV 표기와 달리 실제 배포물은 ZIP이다.

- 로컬 원본은 `.cache/korea-boundaries/sgis-20250630.zip`이며 269,032,521바이트다. 캐시는 Git에서 제외한다.
- SHA-256은 `F1CF0F9DE453AC7EAACB273F39CEE52851183372B9DDFDA428A967C3A670B2C6`다. 이는 확보한 파일의 식별값이며 제공자가 서명한 해시는 아니다.
- ZIP 전체 CRC 검사와 중복 경로 검사를 통과했다. 시도·시군구 DBF는 각각 **17개·252개**이며 모두 `BASE_DATE=20250630`이다. 삭제 행/중복 코드 없이 한글 명칭을 갖고, 시군구 5자리 코드 앞 2자리는 모두 시도 코드에 대응한다.
- 이 수량은 SGIS 통계경계의 수량이며 자치단체 수와 동일하다고 해석하지 않는다. 시군구 명칭에는 `창원시 의창구` 같은 일반구도 포함된다. 법정동 코드로 바꾸거나 영문 이름으로 대응하지 않는다.
- 두 계층은 모두 UTF-8 DBF이고 동일 PRJ의 `Korea_2000_Korea_Unified_Coordinate_System` 투영 좌표계, GRS80, 미터 단위다. EXIF 위경도 숫자를 그대로 대입하면 안 된다. 좌표 변환은 아직 구현하지 않았다.
- SHP/SHX 헤더와 파일 길이, Polygon 유형과 SHX 레코드 수가 DBF 행 수에 대응함을 검사했다. 이 검사는 폴리곤 유효성·겹침·빈틈·시도 포함 관계의 공간 검증이 아니다.
- 같은 ZIP의 `3. 코드집` 폴더에는 격자코드 매핑 XLSX, 제공용 코드 XLSX, 통계자료 이용안내 HWPX, 공간정보 설계서 HWPX가 있다. XLSX 셀 대조는 아직 수행하지 않았다.
- HWPX XML을 직접 읽은 공간정보 설계서는 시도·시군구 모두 2025년 6월 자료이며 BASE_DATE/코드/한글명/GEOM을 정의한다. 실제 DBF 이름 필드 길이는 25바이트여서 설계서의 VARCHAR2(50) 설명과 구분한다.
- 통계자료 이용안내는 통계값 비밀보호 처리와 통계 항목을 설명한다. 이 두 안내에서 별도 경계 재배포 허가 문구는 확인하지 못했다. 포털의 `이용허락범위 제한 없음` 표기와 함께 출처를 유지하되 앱 번들 채택은 후속 검토로 남긴다.

재현 명령은 `python tools/audit_sgis_package.py .cache/korea-boundaries/sgis-20250630.zip`이다. 외부 패키지 없이 Python 표준 라이브러리로 읽기 전용 검사하며 고정 해시가 다르면 즉시 거부한다. 이 스크립트는 다운로드나 앱 실행 시 네트워크 요청을 하지 않는다. 현재 단계는 원본 확보·속성 검증 완료이며 지도/지역 검색 기능 완료가 아니다.

- [x] SGIS 단일 배포본의 원 ZIP·내부 이용안내·코드집 파일·해시 확보. 기준일 및 확인된 이용조건/미확인 범위 기록.
- [ ] 한글 시도/시군구 이름, 문자열 코드, 상위 코드, 시군구 계층 정의 확인. 단순 영문명 수동 번역으로 대체하지 않기.
- [ ] 두 계층의 동일 시점·포함 관계, 중복/빈틈·유효 geometry·좌표계 확인.
- [ ] 일반구·동명 구역·세종·제주·주요 도서·행정구역 변경 구역 검증.
- [ ] 경계선·바다·해외를 억지로 최근접 구역에 배정하지 않는 합성 좌표 시험.
- [ ] 앱에 출처·기준 연도·변환 내역·라이선스 표시. 실제 GPS나 원본을 외부로 전송하지 않기.

이번 결과는 출처 조사 문서다. 조사 자체에서 데이터 번들·분류기는 채택하지 않았고, 실제 국내 경계 오분류율·현재 법정 경계 일치·2026년 최신성은 검증하지 않았다. 별도로 추가한 원본 GPS 표시의 범위는 [GPS 기반 검사](gps-metadata.md)를 따른다.
