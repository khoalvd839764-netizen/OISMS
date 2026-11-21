#!/usr/bin/env python3
"""
Gộp hai tệp coverage.cobertura.xml (UnitTests + IntegrationTests) theo từng dòng (union hits > 0)
và báo cáo line coverage cho 4 nhóm lớp lõi NFR-MAINT-02 (OISMS-66-FIN-F).
"""
import glob
import json
import os
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict

GROUPS = {
    "Nhom ghi so (OrderService, GoodsReceiptService, SalesReturnService)": [
        "OrderService.cs",
        "GoodsReceiptService.cs",
        "SalesReturnService.cs",
    ],
    "ReservationService": ["ReservationService.cs"],
    "WacCalculator": ["IWacCalculator.cs"],
    "RevenueProfitCalculationHelper": ["RevenueProfitCalculationHelper.cs"],
}

def load_cobertura(pattern):
    files = sorted(glob.glob(pattern, recursive=True))
    if not files:
        raise FileNotFoundError(f"Không tìm thấy coverage XML theo mẫu: {pattern}")
    # filename -> { line_num: max_hits }
    coverage = defaultdict(dict)
    for path in files:
        tree = ET.parse(path)
        root = tree.getroot()
        for cls in root.findall(".//class"):
            fname = cls.attrib.get("filename", "")
            base = os.path.basename(fname)
            lines = cls.find("lines")
            if lines is None:
                continue
            for ln in lines.findall("line"):
                num = int(ln.attrib["number"])
                hits = int(ln.attrib.get("hits", "0"))
                coverage[base][num] = max(coverage[base].get(num, 0), hits)
    return coverage

def merge(cov_a, cov_b):
    merged = defaultdict(dict)
    for cov in (cov_a, cov_b):
        for fname, lines in cov.items():
            for num, hits in lines.items():
                merged[fname][num] = max(merged[fname].get(num, 0), hits)
    return merged

def summarize(merged):
    results = []
    for group_name, files in GROUPS.items():
        total_lines = 0
        covered_lines = 0
        file_details = []
        for f in files:
            lines = merged.get(f, {})
            t = len(lines)
            c = sum(1 for h in lines.values() if h > 0)
            total_lines += t
            covered_lines += c
            pct = round((c / t) * 100, 2) if t > 0 else 0.0
            file_details.append({"file": f, "covered": c, "total": t, "lineRatePct": pct})
        pct = round((covered_lines / total_lines) * 100, 2) if total_lines > 0 else 0.0
        status = "ĐẠT" if pct >= 80.0 and total_lines > 0 else "CHƯA ĐẠT"
        results.append({
            "group": group_name,
            "covered": covered_lines,
            "total": total_lines,
            "lineRatePct": pct,
            "status": status,
            "files": file_details,
        })
    return results

if __name__ == "__main__":
    unit_cov = load_cobertura("tmp/coverage-unit/**/coverage.cobertura.xml")
    int_cov = load_cobertura("tmp/coverage-int/**/coverage.cobertura.xml")
    merged = merge(unit_cov, int_cov)
    summary = summarize(merged)
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    out_path = sys.argv[1] if len(sys.argv) > 1 else "docs/06-testing-and-qa/evidence/goi-5/NFR-MAINT-02/coverage-summary.json"
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "w", encoding="utf-8") as fp:
        json.dump(summary, fp, ensure_ascii=False, indent=2)
