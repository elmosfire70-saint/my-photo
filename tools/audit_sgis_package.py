# 공식 SGIS 고정 ZIP의 무결성과 시도·시군구 속성 대응을 읽기 전용으로 검사한다.
import hashlib
import json
import re
import struct
import sys
import zipfile


def require(condition, message):
    if not condition:
        raise ValueError(message)


def audit(path):
    with open(path, "rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    require(digest == "f1cf0f9de453ac7eaacb273f39cee52851183372b9ddfda428a967c3a670b2c6",
            "고정한 공식 패키지 SHA-256 불일치")
    result = {"sha256": digest, "layers": {}}
    with zipfile.ZipFile(path) as package:
        require(package.testzip() is None, "ZIP CRC 불일치")
        names = package.namelist()
        require(len(names) == len(set(names)), "ZIP 항목 이름 중복")
        for layer, expected, width in (("sido", 17, 2), ("sigungu", 252, 5)):
            stem = "bnd_" + layer + "_00_2025_2Q"
            paths = [name for name in names if name.endswith("/" + stem + ".dbf")]
            require(len(paths) == 1, "DBF 경로 중복 또는 누락")
            base = paths[0][:-4]
            require(package.read(base + ".cpg").decode().strip() == "UTF-8", "예상 인코딩 불일치")
            prj = package.read(base + ".prj").decode()
            require('PROJCS["Korea_2000_Korea_Unified_Coordinate_System"' in prj
                    and 'UNIT["Meter",1.0]' in prj, "예상 투영 좌표계 불일치")
            data = package.read(base + ".dbf")
            count, header, size = struct.unpack_from("<IHH", data, 4)
            require(count == expected and len(data) >= header + count * size, "DBF 행 수/길이 불일치")
            fields = []
            offset = 32
            while data[offset] != 13:
                name = data[offset:offset + 11].rstrip(b"\0").decode("ascii")
                require(data[offset + 11] == ord("C"), "예상하지 않은 DBF 필드 타입")
                fields.append((name, data[offset + 16]))
                offset += 32
            code_key, name_key = layer.upper() + "_CD", layer.upper() + "_NM"
            require([name for name, _ in fields] == ["BASE_DATE", code_key, name_key]
                    and 1 + sum(length for _, length in fields) == size, "DBF 스키마 불일치")
            rows = {}
            for index in range(count):
                start = header + index * size
                require(data[start] == 32, "삭제 표시 DBF 행")
                row = {}
                cursor = start + 1
                for name, length in fields:
                    row[name] = data[cursor:cursor + length].decode("utf-8").strip()
                    cursor += length
                code, label = row[code_key], row[name_key]
                require(row["BASE_DATE"] == "20250630", "기준일 혼합")
                require(re.fullmatch(r"[0-9]{" + str(width) + "}", code)
                        and code not in rows and re.search("[가-힣]", label), "코드/한글명 불일치")
                rows[code] = label
            # ponytail: 헤더·색인 대응만 검사한다. 위상 검증은 GIS 엔진 도입 단계에서 수행한다.
            shx = package.read(base + ".shx")
            require(len(shx) == 100 + count * 8, "SHX 레코드 수 불일치")
            shp_info = package.getinfo(base + ".shp")
            with package.open(base + ".shp") as source:
                shp_header = source.read(100)
            for content, length in ((shx[:100], len(shx)), (shp_header, shp_info.file_size)):
                require(struct.unpack_from(">I", content)[0] == 9994
                        and struct.unpack_from(">I", content, 24)[0] * 2 == length
                        and struct.unpack_from("<II", content, 28) == (1000, 5), "SHP/SHX 헤더 불일치")
            result["layers"][layer] = {"count": count, "date": "20250630", "names": rows, "prj": prj}
        provinces = result["layers"]["sido"]["names"]
        require(all(code[:2] in provinces for code in result["layers"]["sigungu"]["names"]),
                "시군구 상위 시도 코드 누락")
        require(result["layers"]["sido"]["prj"] == result["layers"]["sigungu"]["prj"], "좌표계 혼합")
    result["limits"] = "속성·CRC·헤더 검사만 수행. 좌표 변환·위상·경계 분류·코드집 대조는 미검증."
    return result


if __name__ == "__main__":
    print(json.dumps(audit(sys.argv[1]), ensure_ascii=True, indent=2))
