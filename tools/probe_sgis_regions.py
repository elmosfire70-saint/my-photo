# 고정 SGIS 시군구 경계로 좌표를 로컬 판정하고 경계·측지기준·오차 불확실성을 분리한다.
import json
import math
import sys

import pyproj
from shapely.geometry import Point, Polygon
from shapely.strtree import STRtree

from audit_sgis_package import audit, require
from check_sgis_geometry import load_boundaries


def decide(codes, polygons, tree, point, review_m):
    covered = list(tree.query(point, predicate="intersects"))
    if review_m is None:
        return "accuracy-unknown", sorted(codes[i] for i in covered)
    nearby = list(tree.query(point, predicate="dwithin", distance=review_m))
    if (len(covered) == len(nearby) == 1 and polygons[covered[0]].contains(point)
            and polygons[covered[0]].boundary.distance(point) > review_m):
        return "inside", [codes[covered[0]]]
    return ("boundary-uncertain" if nearby else "outside"), sorted(codes[i] for i in nearby)


def finite_number(value):
    if not isinstance(value, (int, float)) or isinstance(value, bool):
        return False
    try:
        return math.isfinite(value)
    except OverflowError:
        return False


class LocalRegions:
    def __init__(self, path):
        self.metadata = audit(path)
        districts = load_boundaries(path, self.metadata, ("sigungu",))["sigungu"]
        require(all(not p.is_empty and p.is_valid and p.area > 0 for p in districts.values()),
                "유효하지 않은 원본 경계")
        self.codes = tuple(sorted(districts))
        self.polygons = tuple(districts[code] for code in self.codes)
        self.tree = STRtree(self.polygons)
        pyproj.network.set_network_enabled(False)
        crs = pyproj.CRS.from_wkt(self.metadata["layers"]["sigungu"]["prj"])
        self.transformer = pyproj.Transformer.from_crs(4326, crs, always_xy=True,
                                                       allow_ballpark=False, only_best=True)
        x, y = self.transformer.transform(127.5, 38, errcheck=True)
        require(abs(x - 1000000) < 0.01 and abs(y - 2000000) < 0.01, "투영 축/원점 불일치")

    def lookup(self, longitude, latitude, datum, accuracy_m):
        require(finite_number(longitude) and -180 <= longitude <= 180
                and finite_number(latitude) and -90 <= latitude <= 90, "위경도 범위/형식 오류")
        require(accuracy_m is None or finite_number(accuracy_m) and accuracy_m >= 0,
                "측위 오차 범위/형식 오류")
        result = {"status": "datum-unverified", "match": None, "candidates": [],
                  "review_m": None, "boundary_date": "20250630"}
        if not isinstance(datum, str) or "".join(datum.upper().split()) != "WGS84":
            return result
        try:
            x, y = self.transformer.transform(longitude, latitude, errcheck=True)
        except pyproj.exceptions.ProjError:
            result["status"] = "projection-unavailable"
            return result
        projection_m = self.transformer.accuracy
        if accuracy_m is not None and finite_number(projection_m) and projection_m >= 0:
            result["review_m"] = accuracy_m + projection_m
            require(math.isfinite(result["review_m"]), "검토 폭 범위 초과")
        status, codes = decide(self.codes, self.polygons, self.tree, Point(x, y), result["review_m"])
        result["status"] = status
        for code in codes:
            province = code[:2]
            result["candidates"].append({"sido_code": province, "sigungu_code": code,
                "sido_name": self.metadata["layers"]["sido"]["names"][province],
                "sigungu_name": self.metadata["layers"]["sigungu"]["names"][code]})
        if status == "inside":
            result["match"] = result["candidates"][0]
        return result


def self_test(path):
    a = Polygon([(0, 0), (100, 0), (100, 100), (0, 100)],
                holes=[[(20, 20), (30, 20), (30, 30), (20, 30)]])
    b = Polygon([(100, 0), (200, 0), (200, 100), (100, 100)])
    shapes = (a, b)
    tree = STRtree(shapes)
    tests = [((50, 50), 2, ("inside", ["a"])),
             ((98, 50), 2, ("boundary-uncertain", ["a", "b"])),
             ((97, 50), 2, ("inside", ["a"])),
             ((100, 50), 0, ("boundary-uncertain", ["a", "b"])),
             ((-1, 50), 2, ("boundary-uncertain", ["a"])),
             ((-3, 50), 2, ("outside", [])),
             ((25, 25), 2, ("outside", [])),
             ((20.5, 25), 2, ("boundary-uncertain", ["a"])),
             ((50, 50), None, ("accuracy-unknown", ["a"]))]
    for xy, margin, expected in tests:
        require(decide(("a", "b"), shapes, tree, Point(*xy), margin) == expected, "합성 경계 판정 실패")
        require(decide(("b", "a"), shapes[::-1], STRtree(shapes[::-1]), Point(*xy), margin) == expected,
                "구역 순서에 따른 판정 차이")
    require(decide(("a", "b"), (a, a), STRtree((a, a)), Point(50, 50), 0)
            == ("boundary-uncertain", ["a", "b"]), "겹침을 확정함")
    regions = LocalRegions(path)
    examples = [(126.978, 37.5665, "11020"), (126.7108, 35.0159, "36040"),
                (127.289, 36.480, "29010"), (126.5312, 33.4996, "39010"),
                (130.9057, 37.4845, "37630")]
    for lon, lat, expected in examples:
        result = regions.lookup(lon, lat, "WGS 84", 5)
        require(result["status"] == "inside" and result["match"]["sigungu_code"] == expected
                and result["match"]["sido_code"] == expected[:2] and result["review_m"] == 6,
                "대표 지역/상위 코드/검토 폭 불일치")
    for lon, lat in ((125, 35), (139.6917, 35.6895)):
        require(regions.lookup(lon, lat, "WGS84", 5)["status"] == "outside", "바다/해외 강제 할당")
    for datum in (None, "Tokyo", "EPSG:5179"):
        result = regions.lookup(126.978, 37.5665, datum, 5)
        require(result["status"] == "datum-unverified" and result["match"] is None
                and not result["candidates"], "미확인 측지기준을 추측함")
    result = regions.lookup(126.978, 37.5665, "WGS84", None)
    require(result["status"] == "accuracy-unknown" and result["match"] is None, "오차 누락을 확정함")
    for args in ((181, 35, 5), (126, 91, 5), (True, 35, 5), (float("nan"), 35, 5),
                 (126, 35, -1), (126, 35, float("inf")), (126, 35, True),
                 (10 ** 1000, 35, 5), (126, 35, 10 ** 1000)):
        try:
            regions.lookup(args[0], args[1], "WGS84", args[2])
        except ValueError:
            continue
        raise AssertionError("잘못된 좌표/측위 오차를 허용함")
    reverse = pyproj.Transformer.from_crs(regions.transformer.target_crs, 4326, always_xy=True,
                                          allow_ballpark=False, only_best=True)
    for code in ("31051", "31052", "31053"):
        point = regions.polygons[regions.codes.index(code)].representative_point()
        lon, lat = reverse.transform(point.x, point.y, errcheck=True)
        require(regions.lookup(lon, lat, "WGS84", 0)["match"]["sigungu_code"] == code,
                "부천 일반구를 격자 집계 코드로 합침")
    require(not pyproj.network.is_network_enabled(), "좌표 변환 네트워크가 켜짐")
    return {"synthetic_cases": len(tests) * 2 + 1, "representative_regions": len(examples),
            "bucheon_districts": 3, "projection_accuracy_m": regions.transformer.accuracy,
            "boundary_date": "20250630", "network": pyproj.network.is_network_enabled(),
            "limits": "Development probe only. No app integration, source repair or GPS accuracy guarantee."}


if __name__ == "__main__":
    require(len(sys.argv) == 3 and sys.argv[1] == "--self-test", "사용법: --self-test 고정ZIP")
    print(json.dumps(self_test(sys.argv[2]), ensure_ascii=True, indent=2))
