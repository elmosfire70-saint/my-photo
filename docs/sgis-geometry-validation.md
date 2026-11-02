# SGIS 공간 검증

앱과 분리된 개발 검증 도구다. 사용자 원본 사진·GPS·DB를 읽거나 수정하지 않는다. 기존 `audit_sgis_package.py`가 고정 SHA-256·CRC·속성 검사를 통과시킨 공식 ZIP만 사용한다. 전체 원본 도형을 계산하므로 실행에 수 분이 걸릴 수 있으며 앱의 검색 성능 시험과 다르다.

## 재현

```powershell
python -m venv runtime/geo-audit
runtime/geo-audit/Scripts/python.exe -m pip install --only-binary=:all: -r tools/geo-audit-requirements.txt
runtime/geo-audit/Scripts/python.exe tools/check_sgis_geometry.py .cache/korea-boundaries/sgis-20250630.zip
```

stdout은 JSON 진단이며 stderr는 진행 상황이다. exit 0은 검사에서 도형 이상·면적 차이가 없음을, exit 2는 도형 이상 또는 계층/겹침 차이 발견을 뜻한다. 예외로 종료하면 검사 자체 실패이므로 JSON 결과나 분류 완료로 해석하지 않는다. 원본을 자동 보정하지 않으며, 단 하나의 점이라도 여러 구역에 해당하거나 경계 위이면 확정 분류를 하지 않는다.

## 검사 범위

- 모든 시도·시군구 도형의 비어 있음·유효성·유한 좌표·양의 면적 검사.
- PRJ 기반 WGS84 변환. 경도/위도 축 순서를 고정하고 ballpark 변환을 허용하지 않는다. PROJ 그리드 네트워크를 명시적으로 끄며 외부로 좌표를 보내지 않는다.
- 투영 원점 경도 127.5도/위도 38도가 x=1,000,000m/y=2,000,000m가 되는지 검사. 대표 합성 지점은 왕복 변환 오차도 검사한다. 왕복 성공은 실측 GPS 정확도 보장이 아니다.
- 서울·나주·세종·제주·울릉도의 대표 합성 지점, 바다·도쿄의 비할당 검사. 위치의 정답은 명시한 시험 지점과 고정 SGIS 코드이며 모든 지리적 지점의 정답을 보장하지 않는다.
- 합성 폴리곤으로 공유 경계·외곽 경계·구멍·겹침을 검사. 실제 GPS의 측위 오차나 경계 주변 완충 영역은 아직 모델링하지 않았다.
- 공간 트리로 후보를 좁혀 같은 계층의 실제 겹침 면적을 계산한다. 시군구를 코드별 상위 시도로 합쳐 시도 경계와의 부족/초과 면적을 각각 계산한다. 면적 단위는 투영 평면의 제곱미터이며 측지 면적이 아니다.

전국 해안선·국경의 법적 정확성을 검증하는 독립 기준 데이터는 없다. 계층 내부의 포함/합집합 일치와 대표 지점 검사를 외부 진실 검증으로 바꿔 말하지 않는다. 사진의 GPSMapDatum 확인과 비-WGS84 처리도 앱 적용 전 필요하다. XLSX 코드집 대조, 재배포 조건 후속 확인, 앱 분류기·지역 검색·지도는 별도 단계다.

## 실행 결과

`runtime/geo-audit/Scripts/python.exe -B tools/check_sgis_geometry.py .cache/korea-boundaries/sgis-20250630.zip`의 전체 검사 결과는 **exit 2**였다. 무효 도형 0개, 같은 계층 간 양의 면적 겹침 0쌍이며, 투영 원점·왕복 좌표·대표 지점 7개와 합성 경계/구멍/겹침 검사는 통과했다. 전체 결과는 제외된 `test-artifacts/sgis-geometry.json`에 저장했다.

| 시도 코드 | 시도에서 시군구 합계를 뺀 면적(㎡) | 시군구 합계에서 시도를 뺀 면적(㎡) |
| --- | ---: | ---: |
| 32 강원 | 0.000006084575 | 0.000981610188 |
| 36 전남 | 0.002234854900 | 0.000034563662 |
| 37 경북 | 0.000117705074 | 0 |
| 38 경남 | 0.003730200663 | 0.002309672948 |

나머지 13개 시도는 두 방향 면적 차이가 0이었다. 이는 차집합의 **면적** 측정이며 선/점 차이까지 없음을 증명하지 않는다. 극미세 차이는 부동소수점 위상 연산의 수치 오차 가능성이 있으나, 이 검사만으로 수치 오차라고 확정하거나 실측 누락 면적이라고 단정하지 않는다. 허용오차로 숨기거나 원본 도형을 수정하지 않았다. 엄격한 완전 면적 일치 조건이 불충족되므로 `accepted=false`를 유지한다.

PRJ의 자동 식별은 ESRI:102080이었다. 변환은 `Inverse of KGD2002 to WGS 84 (1)`을 포함하며 PROJ가 보고한 operation accuracy는 1m다. 이는 원본 사진 GPS 오차나 위경도/경계의 1m 정확성 보장이 아니다. PROJ 네트워크 비활성 상태도 결과에 기록했다.

실행 버전은 Shapely 2.2.0/GEOS 3.14.1, pyproj 3.7.2/PROJ 9.5.1, PyShp 3.1.6이었다.

첫 실행은 진행 로그 없이 장시간 계산되어 자체 검사만 중단했다. 진행/주기 스택 진단을 켠 재실행은 끝까지 완료됐고, 병목은 GEOS 합집합/차집합 연산이었다. 스택 진단 타이머는 최종 도구에서 제거하고 진행 로그만 유지했다. 뒤에 추가한 PyShp 링 오류 거부 조건은 동일 ZIP 전체 17/252개를 별도 재읽기하여 통과시켰다. 경계/겹침/외부 판정의 독립 소규모 검사도 통과했다. 앱·사용자 DB는 변경하지 않았으므로 WPF 검사는 실행하지 않았다.

다음 채택 단계에서는 시군구 판정을 기준으로 상위 시도 코드를 결정하는 일관된 정책과 경계 주변 불확실성 처리를 검토한다. 이를 이미 구현했다고 표시하지 않는다. XLSX 코드집 대조와 출처·조건 확인도 계속 미완료다.

## 공식 API 근거

- [pyproj Transformer](https://pyproj4.github.io/pyproj/stable/api/transformer.html). 축 순서·always_xy·allow_ballpark를 확인했다.
- [pyproj Network](https://pyproj4.github.io/pyproj/stable/api/network.html). 네트워크 그리드 다운로드를 명시적으로 비활성화한다.
- [Shapely covers](https://shapely.readthedocs.io/en/stable/reference/shapely.covers.html). 내부/경계와 외부를 구분하는 판정에 사용한다.
- [Shapely is_valid_reason](https://shapely.readthedocs.io/en/stable/reference/shapely.is_valid_reason.html). 원본 도형이 유효하지 않을 때 원인을 그대로 기록한다.

버전은 `tools/geo-audit-requirements.txt`에 고정한다. 검증 환경의 Shapely(BSD)·pyproj(MIT)·PyShp(MIT)와 부속 네이티브 도구는 앱 배포물에 포함하지 않는다. 추후 앱에 넣을 때는 별도의 라이선스/번들 결정을 해야 한다.
