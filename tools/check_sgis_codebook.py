# 같은 SGIS ZIP의 격자 매핑 코드집과 경계 DBF의 명칭·코드 차이를 읽기 전용으로 검사한다.
import io
import json
import sys
import zipfile

from openpyxl import load_workbook

from audit_sgis_package import audit, require


def compare(rows, source, provinces, layer):
    mapping = {}
    for code, label in rows:
        require(isinstance(code, int) and not isinstance(code, bool)
                and isinstance(label, str) and str(code) not in mapping, "코드집 중복/누락/타입 오류")
        mapping[str(code)] = label
    mismatches = []
    for code in sorted(source.keys() & mapping.keys()):
        expected = source[code] if layer == "sido" else provinces[code[:2]] + " " + source[code]
        if mapping[code] != expected:
            mismatches.append({"code": code, "xlsx": mapping[code], "dbf": expected})
    return {"records": len(mapping), "missing": sorted(source.keys() - mapping.keys()),
            "extra": sorted(mapping.keys() - source.keys()), "name_mismatches": mismatches}


def check(path):
    metadata = audit(path)
    results = {}
    with zipfile.ZipFile(path) as package:
        entry = next(name for name in package.namelist() if name.endswith("(adm_grid_mapping).xlsx"))
        book = load_workbook(io.BytesIO(package.read(entry)), read_only=True, data_only=False)
        try:
            require(book.sheetnames == ["시도-격자", "시군구-격자"], "예상하지 않은 코드집 시트")
            for layer, sheet in zip(("sido", "sigungu"), book):
                require(tuple(sheet.values)[1][:2] ==
                        (("시도코드", "시도명칭") if layer == "sido" else ("시군구코드", "시군구명칭")),
                        "코드집 헤더 불일치")
                rows = list(sheet.iter_rows(min_row=3, max_col=2, values_only=True))
                results[layer] = compare(rows, metadata["layers"][layer]["names"],
                                         metadata["layers"]["sido"]["names"], layer)
        finally:
            book.close()
    require(results["sido"] == {"records": 17, "missing": [], "extra": [], "name_mismatches": []},
            "시도 대조 결과 변경")
    require(results["sigungu"] == {"records": 250, "missing": ["31051", "31052", "31053"],
                                    "extra": ["31050"], "name_mismatches": []}, "시군구 대조 결과 변경")
    require(compare([(11, "다른 이름")], {"11": "서울특별시"}, {}, "sido")["name_mismatches"],
            "명칭 불일치 감지 실패")
    for rows in ([(11, "서울"), (11, "서울")], [(None, "서울")], [(True, "서울")]):
        try:
            compare(rows, {}, {}, "sido")
        except ValueError:
            continue
        raise AssertionError("잘못된 코드집 행을 허용함")
    return {"sha256": metadata["sha256"], "comparison": results, "exact_match": False,
            "policy": "Grid mapping scope differs. Use boundary DBF names/codes; do not merge Bucheon districts."}


if __name__ == "__main__":
    print(json.dumps(check(sys.argv[1]), ensure_ascii=True, indent=2))
