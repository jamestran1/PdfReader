"""Count analyzer diagnostics from SARIF logs, de-duplicating the WPF _wpftmp double compile."""
import collections
import glob
import json
import re
import sys

root = sys.argv[1]  # .../sarif/<tag>
top = int(sys.argv[2]) if len(sys.argv) > 2 else 25

seen = set()
for path in glob.glob(f"{root}/*.sarif"):
    with open(path, encoding="utf-8-sig") as f:
        log = json.load(f)
    for run in log["runs"]:
        for r in run.get("results", []):
            rule = r["ruleId"]
            if rule.startswith("NU"):
                continue
            locs = r.get("locations") or []
            if not locs:
                continue
            pl = locs[0]["physicalLocation"]
            uri = pl["artifactLocation"]["uri"].replace("\\", "/")
            if "/obj/" in uri:
                continue
            reg = pl.get("region", {})
            seen.add((rule, uri, reg.get("startLine"), reg.get("startColumn"), r.get("level", "warning")))


def family(rule):
    m = re.match(r"[A-Z]+", rule)
    p = m.group(0) if m else rule
    return {"CA": "SDK CA", "IDE": "SDK IDE", "CS": "compiler", "RCS": "Roslynator", "MA": "Meziantou", "S": "Sonar", "SA": "StyleCop", "MVVMTK": "MVVM Toolkit"}.get(p, p)


def area(uri):
    if "/tests/" in uri:
        return "tests"
    if "/src/" in uri:
        return "app"
    return "other"


by_fam = collections.Counter()
by_rule = collections.Counter()
by_area_fam = collections.Counter()
by_level = collections.Counter()
files = collections.Counter()
for rule, uri, *_rest, level in seen:
    by_fam[family(rule)] += 1
    by_rule[(rule, area(uri))] += 1
    by_area_fam[(area(uri), family(rule))] += 1
    by_level[level] += 1
    files[uri.split("/PdfReader")[-1] if False else uri.split("/measure/")[-1]] += 1

print("TOTAL", len(seen), dict(by_level))
print("\nBY FAMILY x AREA")
for fam, _ in by_fam.most_common():
    print(f"  {fam:14} total={by_fam[fam]:5}  app={by_area_fam[('app', fam)]:5}  tests={by_area_fam[('tests', fam)]:5}")
print(f"\nTOP {top} RULES (rule, area)")
for (rule, a), n in by_rule.most_common(top):
    print(f"  {rule:10} {a:6} {n}")
print("\nCOMPLEXITY / LENGTH RULES")
for (rule, a), n in sorted(by_rule.items()):
    if rule in ("CA1501", "CA1502", "CA1505", "CA1506", "MA0051", "S3776", "S138", "S104", "S1541", "S107"):
        print(f"  {rule:10} {a:6} {n}")
print("\nTOP 10 FILES")
for fpath, n in files.most_common(10):
    print(f"  {n:5} {fpath}")
