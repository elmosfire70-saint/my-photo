# 고정 SGIS 경계의 좌표 변환·도형 위상과 합성 지점 분류를 오프라인 검증한다.
import io
import json
import math
import sys
import zipfile
from importlib.metadata import version

import pyproj
import shapefile
import shapely
from shapely.geometry import Point, Polygon, shape
from shapely.strtree import STRtree

from audit_sgis_package import audit, require


def classify(polygons, point):
    covered = [code for code, polygon in polygons.items() if polygon.covers(point)]
    if not covered:
        return "outside", []
    if len(covered) == 1 and polygons[covered[0]].contains(point):
        return "inside", covered
    return "boundary-or-overlap", covered


def check(path):
    pyproj.network.set_network_enabled(False)
    result = audit(path)
    print("PASS package integrity", file=sys.stderr, flush=True)
    result.pop("limits")
    result["versions"] = {name: version(name) for name in ("shapely", "pyproj", "pyshp")}
    result["versions"].update(geos=shapely.geos_version_string, proj=pyproj.proj_version_str)
    polygons = {}
    with zipfile.ZipFile(path) as package:
        for layer in ("sido", "sigungu"):
            prefix = next(name[:-4] for name in package.namelist()
                          if name.endswith("/bnd_" + layer + "_00_2025_2Q.shp"))
            streams = {ext: io.BytesIO(package.read(prefix + "." + ext)) for ext in ("shp", "shx", "dbf")}
            with shapefile.Reader(**streams, encoding="utf-8") as reader:
                polygons[layer] = {}
                for record in reader.iterShapeRecords():
                    geometry = record.shape.__geo_interface__
                    require(not any(record.shape._errors.values()), "SHP 링 해석 오류/자동 대체 감지")
                    polygons[layer][record.record[layer.upper() + "_CD"]] = shape(geometry)
            require(len(polygons[layer]) == result["layers"][layer]["count"], "도형 수 불일치")
            print("Loaded " + layer, file=sys.stderr, flush=True)
    invalid = {layer: {code: shapely.is_valid_reason(polygon) for code, polygon in items.items()
                       if polygon.is_empty or not polygon.is_valid or polygon.area <= 0
                       or not all(math.isfinite(value) for value in polygon.bounds)}
               for layer, items in polygons.items()}
    result["invalid"] = invalid
    print("Checked geometry validity", file=sys.stderr, flush=True)
    if any(invalid.values()):
        result["accepted"] = False
        result["reason"] = "Invalid source geometry; no automatic repair or spatial classification."
        return result

    crs = pyproj.CRS.from_wkt(result["layers"]["sido"].pop("prj"))
    result["layers"]["sigungu"].pop("prj")
    forward = pyproj.Transformer.from_crs(4326, crs, always_xy=True, allow_ballpark=False, only_best=True)
    reverse = pyproj.Transformer.from_crs(crs, 4326, always_xy=True, allow_ballpark=False, only_best=True)
    x, y = forward.transform(127.5, 38, errcheck=True)
    require(abs(x - 1000000) < 0.01 and abs(y - 2000000) < 0.01, "투영 원점/축 순서 불일치")
    result["projection"] = {"authority": crs.to_authority(), "origin_xy": [x, y],
                            "operation": forward.description, "accuracy_m": forward.accuracy,
                            "network": pyproj.network.is_network_enabled()}

    # 경계선·공유 경계·구멍·겹침은 최근접 또는 첫 번째 구역으로 확정하지 않는다.
    synthetic = {"a": Polygon([(0, 0), (2, 0), (2, 2), (0, 2)],
                              holes=[[(0.5, 0.5), (1, 0.5), (1, 1), (0.5, 1)]]),
                 "b": Polygon([(2, 0), (3, 0), (3, 2), (2, 2)])}
    for xy, status in (((0.25, 0.25), "inside"), ((2, 1), "boundary-or-overlap"),
                       ((0, 1), "boundary-or-overlap"), ((0.75, 0.75), "outside"), ((5, 5), "outside")):
        require(classify(synthetic, Point(*xy))[0] == status, "합성 경계/구멍 분류 실패")
    require(classify({"a": synthetic["a"], "b": synthetic["a"]}, Point(0.25, 0.25))[0]
            == "boundary-or-overlap", "겹침을 확정 구역으로 오인")
    tests = [("seoul", 126.978, 37.5665, "11", "11020"),
             ("naju", 126.7108, 35.0159, "36", "36040"),
             ("sejong", 127.289, 36.480, "29", "29010"),
             ("jeju", 126.5312, 33.4996, "39", "39010"),
             ("ulleung", 130.9057, 37.4845, "37", "37630"),
             ("sea", 125.0, 35.0, None, None), ("tokyo", 139.6917, 35.6895, None, None)]
    result["points"] = []
    for label, lon, lat, province, district in tests:
        xy = forward.transform(lon, lat, errcheck=True)
        lon2, lat2 = reverse.transform(*xy, errcheck=True)
        require(abs(lon - lon2) < 1e-8 and abs(lat - lat2) < 1e-8, "좌표 왕복 불일치")
        actual = [classify(polygons[layer], Point(*xy)) for layer in ("sido", "sigungu")]
        expected = [("inside", [code]) if code else ("outside", []) for code in (province, district)]
        require(actual == expected, f"{label} 합성 지점 분류 불일치: {actual}")
        result["points"].append({"label": label, "sido": actual[0], "sigungu": actual[1]})
    print("PASS projection and synthetic points", file=sys.stderr, flush=True)

    result["overlaps_m2"] = {}
    for layer, items in polygons.items():
        codes, geometries = list(items), list(items.values())
        tree = STRtree(geometries)
        overlaps = []
        for i, polygon in enumerate(geometries):
            for j in tree.query(polygon, predicate="intersects"):
                if j > i:
                    area = polygon.intersection(geometries[j]).area
                    if area > 0:
                        overlaps.append({"codes": [codes[i], codes[j]], "area": area})
        result["overlaps_m2"][layer] = overlaps
        print("Checked overlaps " + layer, file=sys.stderr, flush=True)
    result["hierarchy_m2"] = []
    for code, province in polygons["sido"].items():
        children = shapely.union_all([polygon for key, polygon in polygons["sigungu"].items()
                                     if key[:2] == code])
        result["hierarchy_m2"].append({"sido": code, "missing": province.difference(children).area,
                                       "outside": children.difference(province).area})
        print("Checked hierarchy " + code, file=sys.stderr, flush=True)
    # ponytail: 진단 단계이므로 면적 불일치는 임의 허용 오차로 숨기지 않는다.
    result["accepted"] = not any(result["overlaps_m2"].values()) and all(
        row["missing"] == 0 and row["outside"] == 0 for row in result["hierarchy_m2"])
    result["limits"] = "Representative synthetic points only; no claim of current legal boundaries or GPS accuracy."
    return result


if __name__ == "__main__":
    report = check(sys.argv[1])
    print(json.dumps(report, ensure_ascii=True, indent=2))
    sys.exit(0 if report["accepted"] else 2)
