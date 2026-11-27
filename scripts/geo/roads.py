#!/usr/bin/env python3
"""Bounded WFS acquisition of BD TOPO road troncons and the road-bearing Sedan manifest.

Owner: Track R R3 group 6 (line corridors painted into generated terrain materials, WP1026).
Conventions: stdlib only; fail() style named errors; writes only under --out root;
never mutates delegate prepared sources, only copies them into the proof root.

The composed manifest adds the road KML source and one lineMaterialRules entry mapping
the road source to the "dirt" palette entry with the WP1026 width ladder
(largeur_de_chaussee, then nombre_de_voies x meters-per-lane, then nature class widths,
then a 4 m default). The "dirt" palette entry itself is introduced by a dormant
attribute-form material rule ("paletteEntry" matches no polygon feature of any source).
"""

from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import math
import os
import shutil
import subprocess
import sys
import time
import urllib.parse
from xml.sax.saxutils import escape

WFS_URL = "https://data.geopf.fr/wfs/ows"
TYPE_NAMES = "BDTOPO_V3:troncon_de_route"
STABLE_ID_FIELD = "cleabs"
SEDAN_BBOX_WGS84 = (4.9390, 49.6970, 4.9560, 49.7076)
OUTPUT_FORMAT = "application/json"
SRS_NAME = "EPSG:4326"
PAGE_SIZE = 1000
PAGE_CAP = 10
FEATURE_CAP = 20000
TIMEOUT_SECONDS = 600
RESPONSE_CAP_BYTES = 64 * 1024 * 1024
TOTAL_CAP_BYTES = 256 * 1024 * 1024
USER_AGENT = "Panopticon-geo-roads/1.0 (IGN Licence Ouverte 2.0; contact via repository)"

KEPT_ATTRIBUTES = [
    "cleabs",
    "nature",
    "importance",
    "nombre_de_voies",
    "largeur_de_chaussee",
]

ATTRIBUTION = "IGN, Licence Ouverte 2.0"
LICENCE = {
    "name": "Licence Ouverte 2.0 (Etalab)",
    "freeOfCharge": True,
    "url": "https://www.etalab.gouv.fr/wp-content/uploads/2014/05/Licence_Ouverte.pdf",
}

# Frozen v3 Sedan prepared sources copied verbatim into the proof root.
PREPARED_SOURCE_DIR = "/mnt/pit2/panopticon/geo-proof/r2-03-footprints-v3/prepared"
PREPARED_FILES = {
    "sedan-extent.kml": "generated extent KML from the frozen selection geometry",
    "sedan-dem-32631.tif": "IGN RGE ALTI 1 m prepared EPSG:32631 Float32 DEM",
    "sedan-ortho-32631.tif": "IGN BD ORTHO 20 cm prepared EPSG:32631 uint8 GeoTIFF",
    "sedan-proof-buildings.kml": "IGN BD TOPO building footprints",
}

# Class widths for nature values that carry no largeur_de_chaussee in BD TOPO V3,
# per the WP1026 proposed defaults (autoroute 20, principal 10, secondary 7, chemin 4).
CLASS_WIDTHS_METERS = {
    "Sentier": 2.0,
    "Escalier": 2.0,
    "Chemin": 4.0,
    "Route empierrée": 4.0,
    "Rond-point": 8.0,
}
DEFAULT_WIDTH_METERS = 4.0


def fail(message: str) -> "NoReturn":  # type: ignore[name-defined]
    print(f"error: {message}", file=sys.stderr)
    sys.exit(2)


def now_iso() -> str:
    """Local timestamp; agent log lines never use UTC."""
    return datetime.datetime.now().astimezone().isoformat(timespec="milliseconds")


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: str) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def check_root(out_root: str) -> None:
    """Refuse any existing non-empty root; the proof path is unique per run policy."""
    if not os.path.lexists(out_root):
        return
    if not os.path.isdir(out_root):
        fail(f"output root exists and is not a directory: {out_root}")
    entries = os.listdir(out_root)
    if entries:
        fail(f"refusing to use existing non-empty root: {out_root}")


def fetch_roads(out_root: str) -> dict:
    """Paged WFS GetFeature acquisition with strict page, sort, id, and cap enforcement."""
    provenance_dir = os.path.join(out_root, "provenance", "pages")
    os.makedirs(provenance_dir, exist_ok=True)

    lonmin, latmin, lonmax, latmax = SEDAN_BBOX_WGS84
    bbox_param = f"{lonmin:.6f},{latmin:.6f},{lonmax:.6f},{latmax:.6f},EPSG:4326"

    start_time = time.monotonic()
    start_index = 0
    seen_ids: set[str] = set()
    features: list[dict] = []
    pages: list[dict] = []
    initial_number_matched = None

    while True:
        elapsed = time.monotonic() - start_time
        if elapsed >= TIMEOUT_SECONDS:
            fail(f"roads stage exceeded timeout of {TIMEOUT_SECONDS}s")
        if len(pages) >= PAGE_CAP:
            fail(f"roads stage exceeded page cap of {PAGE_CAP} pages")

        params = [
            ("service", "WFS"),
            ("version", "2.0.0"),
            ("request", "GetFeature"),
            ("typeNames", TYPE_NAMES),
            ("outputFormat", OUTPUT_FORMAT),
            ("srsName", SRS_NAME),
            ("bbox", bbox_param),
            ("STARTINDEX", str(start_index)),
            ("COUNT", str(PAGE_SIZE)),
            ("SORTBY", STABLE_ID_FIELD),
        ]
        full_url = f"{WFS_URL}?{urllib.parse.urlencode(params)}"

        timeout_remaining = max(1.0, TIMEOUT_SECONDS - (time.monotonic() - start_time))
        argv = [
            "curl", "-fsS", "--proto", "=https",
            "--max-time", str(int(math.ceil(timeout_remaining))),
            "-H", f"User-Agent: {USER_AGENT}",
            full_url,
        ]
        run = subprocess.run(argv, capture_output=True, timeout=timeout_remaining)
        if run.returncode != 0:
            excerpt = run.stderr.decode("utf-8", "replace").strip()[-400:]
            fail(f"WFS curl request failed (rc {run.returncode}) for {full_url}: {excerpt}")

        page_data = run.stdout
        if len(page_data) > RESPONSE_CAP_BYTES:
            fail(f"WFS response exceeded per-response cap of {RESPONSE_CAP_BYTES} bytes")
        if sum(page["bytes"] for page in pages) + len(page_data) > TOTAL_CAP_BYTES:
            fail(f"WFS total acquisition exceeded total cap of {TOTAL_CAP_BYTES} bytes")

        try:
            response = json.loads(page_data.decode("utf-8"))
        except (ValueError, UnicodeDecodeError) as error:
            fail(f"WFS response unparseable or truncated: {error}")

        if not isinstance(response, dict) or not isinstance(response.get("features"), list):
            fail("WFS response is not a feature collection")

        page_features = response["features"]
        number_matched = response.get("numberMatched")
        returned = response.get("numberReturned")
        if returned is not None and returned != len(page_features):
            fail(f"WFS numberReturned ({returned}) does not match feature count ({len(page_features)})")

        if pages:
            if number_matched != initial_number_matched:
                fail(f"WFS numberMatched changed between pages: {initial_number_matched} -> {number_matched}")
        else:
            initial_number_matched = number_matched

        # Page 0: verify the server honoured SORTBY.
        if not pages and len(page_features) > 1:
            ids = [f.get("properties", {}).get(STABLE_ID_FIELD) for f in page_features]
            if ids != sorted(ids):
                fail(f"WFS server did not honor SORTBY: first page not sorted by {STABLE_ID_FIELD}")

        new_ids = 0
        for feature in page_features:
            properties = feature.get("properties")
            if not isinstance(properties, dict):
                fail("WFS feature is missing a properties object")
            cleabs = properties.get(STABLE_ID_FIELD)
            if not isinstance(cleabs, str) or not cleabs:
                fail(f"WFS feature is missing stable id {STABLE_ID_FIELD}")
            if cleabs in seen_ids:
                fail(f"duplicate feature id: {cleabs}")
            seen_ids.add(cleabs)
            new_ids += 1
            features.append(feature)

        if new_ids == 0 and page_features:
            fail("WFS page made no progress (0 new ids)")

        if len(features) > FEATURE_CAP:
            fail(f"WFS acquisition exceeded feature cap of {FEATURE_CAP} features")

        page_record = {
            "pageIndex": len(pages),
            "url": full_url,
            "sha256": sha256_bytes(page_data),
            "bytes": len(page_data),
            "featureCount": len(page_features),
            "acquiredAt": now_iso(),
            "timeStamp": response.get("timeStamp"),
        }
        pages.append(page_record)
        with open(os.path.join(provenance_dir, f"page-{page_record['pageIndex']:03d}.json"), "wb") as handle:
            handle.write(page_data)

        if not page_features:
            if initial_number_matched is not None and len(seen_ids) < initial_number_matched:
                fail(f"WFS page returned 0 features before numberMatched ({initial_number_matched}) was reached")
            break
        if initial_number_matched is not None and len(seen_ids) >= initial_number_matched:
            break
        if len(page_features) < PAGE_SIZE:
            break

        start_index += PAGE_SIZE

    print(f"acquired {len(features)} road features in {len(pages)} page(s), "
          f"numberMatched={initial_number_matched}, wall={time.monotonic() - start_time:.1f}s")
    return {
        "pages": pages,
        "featureCount": len(features),
        "numberMatched": initial_number_matched,
    }


def features_to_kml(features: list[dict]) -> str:
    """Converts WGS84 GeoJSON road features to KML placemarks in one roads folder."""
    placemarks = []
    for index, feature in enumerate(features):
        properties = feature.get("properties", {})
        cleabs = properties.get(STABLE_ID_FIELD)
        geometry = feature.get("geometry", {})
        geom_type = geometry.get("type")
        if geom_type == "LineString":
            lines = [geometry.get("coordinates", [])]
        elif geom_type == "MultiLineString":
            lines = geometry.get("coordinates", [])
        else:
            fail(f"feature {cleabs} carries unsupported geometry type: {geom_type!r}")

        data_elements = []
        for attribute in KEPT_ATTRIBUTES:
            value = properties.get(attribute)
            text = "" if value is None else str(value)
            data_elements.append(
                f'          <Data name="{escape(attribute)}"><value>{escape(text)}</value></Data>')

        line_elements = []
        for line in lines:
            coordinates = " ".join(f"{point[0]},{point[1]},{point[2]}" for point in line)
            line_elements.append(
                "          <LineString>\n"
                "            <tessellate>1</tessellate>\n"
                f"            <coordinates>{escape(coordinates)}</coordinates>\n"
                "          </LineString>")

        placemarks.append(
            f'      <Placemark id="{escape(cleabs)}">\n'
            f"        <name>{escape(cleabs)}</name>\n"
            "        <ExtendedData>\n" + "\n".join(data_elements) + "\n        </ExtendedData>\n"
            "        <MultiGeometry>\n" + "\n".join(line_elements) + "\n        </MultiGeometry>\n"
            "      </Placemark>")

        if (index + 1) % 200 == 0:
            print(f"converted {index + 1}/{len(features)} features")

    return (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        '<kml xmlns="http://www.opengis.net/kml/2.2">\n'
        f'  <Document id="sedan-proof-roads">\n'
        "    <Folder>\n"
        "      <name>roads</name>\n"
        + "\n".join(placemarks) + "\n"
        "    </Folder>\n"
        "  </Document>\n"
        "</kml>\n"
    )


def copy_prepared_sources(out_root: str) -> list[dict]:
    """Copies the frozen v3 prepared sources into the proof root; never mutates them."""
    prepared_dir = os.path.join(out_root, "prepared")
    os.makedirs(prepared_dir, exist_ok=True)
    sources = []
    for file_name, label in PREPARED_FILES.items():
        source_path = os.path.join(PREPARED_SOURCE_DIR, file_name)
        if not os.path.isfile(source_path):
            fail(f"prepared source is missing: {source_path}")
        target_path = os.path.join(prepared_dir, file_name)
        shutil.copyfile(source_path, target_path)
        sources.append({
            "file": f"prepared/{file_name}",
            "sha256": sha256_file(target_path),
            "bytes": os.path.getsize(target_path),
            "label": label,
        })
    return sources


def write_roads_kml(out_root: str, features: list[dict]) -> dict:
    kml_path = os.path.join(out_root, "prepared", "sedan-proof-roads.kml")
    kml = features_to_kml(features)
    with open(kml_path, "w", encoding="utf-8") as handle:
        handle.write(kml)
    return {
        "file": "prepared/sedan-proof-roads.kml",
        "sha256": sha256_file(kml_path),
        "bytes": os.path.getsize(kml_path),
        "label": "IGN BD TOPO road troncons (troncon_de_route line corridors)",
        "featureCount": len(features),
    }


def compose_manifest(out_root: str) -> None:
    """Writes the road-bearing Sedan manifest; paths stay under the proof root."""
    manifest = {
        "sources": [
            {
                "id": "sedan-extent",
                "kind": "kml",
                "path": "prepared/sedan-extent.kml",
                "role": "extent",
                "label": "generated extent KML: exact projected 1100 m AOI square corners (EPSG:32631)",
                "attribution": ATTRIBUTION,
            },
            {
                "id": "sedan-dem-32631",
                "kind": "geotiff",
                "path": "prepared/sedan-dem-32631.tif",
                "role": "elevation",
                "verticalDatum": "NGF-IGN69",
                "label": "IGN RGE ALTI 1 m prepared EPSG:32631 Float32 DEM (1181x1181)",
                "attribution": ATTRIBUTION,
            },
            {
                "id": "sedan-ortho-32631",
                "kind": "geotiff",
                "path": "prepared/sedan-ortho-32631.tif",
                "role": "imagery",
                "label": "IGN BD ORTHO 20 cm prepared EPSG:32631 uint8 GeoTIFF (5899x5898)",
                "attribution": ATTRIBUTION,
            },
            {
                "id": "sedan-proof-buildings",
                "kind": "kml",
                "path": "prepared/sedan-proof-buildings.kml",
                "role": "vector",
                "label": "IGN BD TOPO building footprints",
                "attribution": ATTRIBUTION,
            },
            {
                "id": "sedan-proof-roads",
                "kind": "kml",
                "path": "prepared/sedan-proof-roads.kml",
                "role": "vector",
                "label": "IGN BD TOPO road troncons (WP1026 line corridor source)",
                "attribution": ATTRIBUTION,
            },
        ],
        "targetCoordinateSystem": {"hemisphere": "north", "type": "utm", "zone": 31},
        "metersPerGameUnit": 0.3048,
        "defaultMaterial": "grass",
        "extentFeatureId": "sedan-extent",
        "verticalPolicy": {"exaggeration": 1.0, "originMeters": 149.381},
        "rasterPolicy": {
            "edge": "fail",
            "imageryPixelsPerSample": 10,
            "noData": "nearest-valid",
            "noDataSearchRadiusMeters": 3.048,
        },
        "tolerances": {"verticalMeters": 1.0},
        "controlPoints": [],
        "featureMappings": [
            {
                "sourceId": "sedan-proof-buildings",
                "layerName": "buildings",
                "geometry": "polygon",
                "target": "passability",
                "passable": False,
            },
            {
                "sourceId": "sedan-proof-buildings",
                "layerName": "buildings",
                "geometry": "polygon",
                "target": "buildability",
                "buildable": False,
            },
        ],
        # Palette introduction for the corridor material: the attribute matches no
        # polygon feature of any source, so this rule paints nothing by itself.
        "materialRules": [
            {"material": "dirt", "attribute": "paletteEntry"},
        ],
        "lineMaterialRules": [
            {
                "sourceId": "sedan-proof-roads",
                "material": "dirt",
                "widthAttribute": "largeur_de_chaussee",
                "lanesAttribute": "nombre_de_voies",
                "metersPerLane": 3.5,
                "classAttribute": "nature",
                "classWidthsMeters": CLASS_WIDTHS_METERS,
                "defaultWidthMeters": DEFAULT_WIDTH_METERS,
            },
        ],
        "maxScaleError": 0.001,
    }

    manifest_path = os.path.join(out_root, "manifest.json")
    with open(manifest_path, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, ensure_ascii=False, indent=2)
        handle.write("\n")
    print(f"manifest written: {manifest_path}")


def write_receipt(out_root: str, acquisition: dict, prepared: list[dict], roads: dict) -> None:
    receipt = {
        "proof": "r3-g6-roads-v1 (WP1026 line corridor materials)",
        "acquiredAt": now_iso(),
        "provider": {
            "id": "ign-fr",
            "wfsUrl": WFS_URL,
            "typeNames": TYPE_NAMES,
            "outputFormat": OUTPUT_FORMAT,
            "srsName": SRS_NAME,
            "bboxWgs84": list(SEDAN_BBOX_WGS84),
            "stableIdField": STABLE_ID_FIELD,
            "keptAttributes": KEPT_ATTRIBUTES,
            "licence": LICENCE,
            "attribution": ATTRIBUTION,
            "userAgent": USER_AGENT,
        },
        "acquisition": {
            "featureCount": acquisition["featureCount"],
            "numberMatched": acquisition["numberMatched"],
            "pageCount": len(acquisition["pages"]),
            "pages": acquisition["pages"],
        },
        "prepared": prepared,
        "roadsKml": roads,
        "widthLadder": {
            "widthAttribute": "largeur_de_chaussee",
            "lanesAttribute": "nombre_de_voies",
            "metersPerLane": 3.5,
            "classAttribute": "nature",
            "classWidthsMeters": CLASS_WIDTHS_METERS,
            "defaultWidthMeters": DEFAULT_WIDTH_METERS,
        },
    }

    receipt_path = os.path.join(out_root, "provenance", "roads-receipt.json")
    os.makedirs(os.path.dirname(receipt_path), exist_ok=True)
    with open(receipt_path, "w", encoding="utf-8") as handle:
        json.dump(receipt, handle, ensure_ascii=False, indent=2)
        handle.write("\n")
    print(f"receipt written: {receipt_path}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--out",
        default="/mnt/pit2/panopticon/geo-proof/r3-g6-roads-v1",
        help="proof root; refuses any existing non-empty directory")
    args = parser.parse_args()

    out_root = os.path.abspath(args.out)
    check_root(out_root)
    os.makedirs(out_root, exist_ok=True)

    acquisition = fetch_roads(out_root)
    features = []
    for page in acquisition["pages"]:
        # Feature objects are re-read from the persisted raw pages for full provenance.
        with open(os.path.join(out_root, "provenance", "pages", f"page-{page['pageIndex']:03d}.json"),
                  encoding="utf-8") as handle:
            features.extend(json.load(handle)["features"])

    prepared = copy_prepared_sources(out_root)
    roads = write_roads_kml(out_root, features)
    prepared.append(roads)
    compose_manifest(out_root)
    write_receipt(out_root, acquisition, prepared, roads)

    natures: dict[str, int] = {}
    widths_present = 0
    for feature in features:
        nature = feature["properties"].get("nature")
        natures[nature] = natures.get(nature, 0) + 1
        if feature["properties"].get("largeur_de_chaussee") is not None:
            widths_present += 1
    print(json.dumps({
        "featureCount": acquisition["featureCount"],
        "natures": natures,
        "featuresWithExplicitWidth": widths_present,
        "classWidthsMeters": CLASS_WIDTHS_METERS,
        "defaultWidthMeters": DEFAULT_WIDTH_METERS,
    }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
