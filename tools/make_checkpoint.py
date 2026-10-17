#!/usr/bin/env python3
"""Build a reproducible, hash-inventoried local checkpoint. Upload via Library separately."""
import hashlib, json, pathlib, zipfile
ROOT = pathlib.Path(__file__).resolve().parents[1]
EXCLUDED = {'.git', 'artifacts', 'python-deps', '__pycache__', 'Library', 'Temp', 'Logs'}
files = sorted(p for p in ROOT.rglob('*') if p.is_file() and not EXCLUDED.intersection(p.relative_to(ROOT).parts) and p.suffix not in {'.blend1', '.blend2', '.zip', '.log'})
manifest = {'schema': 1, 'verification': 'Unity/Udon/VRChat runtime unverified', 'files': []}
out = ROOT / 'artifacts'; out.mkdir(exist_ok=True)
zip_path = out / 'drive-roadster-checkpoint.zip'
with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
    for p in files:
        data = p.read_bytes(); name = str(p.relative_to(ROOT))
        manifest['files'].append({'path': name, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()})
        info = zipfile.ZipInfo(name, (2026, 10, 17, 0, 0, 0)); info.compress_type = zipfile.ZIP_DEFLATED
        z.writestr(info, data)
    z.writestr('CHECKPOINT_MANIFEST.json', json.dumps(manifest, ensure_ascii=False, indent=2))
with zipfile.ZipFile(zip_path) as z:
    assert z.testzip() is None
    for item in manifest['files']:
        assert hashlib.sha256(z.read(item['path'])).hexdigest() == item['sha256']
print(json.dumps({'path': str(zip_path), 'bytes': zip_path.stat().st_size, 'files': len(files), 'sha256': hashlib.sha256(zip_path.read_bytes()).hexdigest()}))
