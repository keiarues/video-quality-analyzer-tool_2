"""品質評価の計測結果を測定実行ごとに集計する。入力動画のパスは出力しない。"""
import argparse
import csv
import json
from pathlib import Path
from statistics import median

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('directory', type=Path)
arguments = parser.parse_args()
rows = []
for path in sorted(arguments.directory.glob('*/results.json')):
    data = json.loads(path.read_text(encoding='utf-8-sig'))
    metadata = data['Metadata']
    groups = {}
    for row in data['Results']:
        if row['Repeat'] > 0:
            groups.setdefault(row['Case'], []).append(row)
    for case, values in groups.items():
        seconds = [value['WallSeconds'] for value in values]
        mismatch_values = [value['Mismatches'] for value in values if value.get('Mismatches') is not None]
        rows.append(dict(Run=path.parent.name, Codec=metadata['Candidate']['Codec'], Phase=metadata['Phase'],
            Metric=metadata['Metric'], Case=case, Count=len(values), MedianSeconds=median(seconds),
            MinSeconds=min(seconds), MaxSeconds=max(seconds),
            MedianFfmpegSeconds=median(value['FfmpegSeconds'] for value in values),
            MedianAggregationSeconds=median(value['AggregationSeconds'] for value in values),
            MedianCpuSeconds=median(value['CpuSeconds'] for value in values),
            Frames=values[-1]['Frames'], MaxMismatches=max(mismatch_values) if mismatch_values else None))
if rows:
    target = arguments.directory / 'summary.csv'
    with target.open('w', encoding='utf-8-sig', newline='') as file:
        writer = csv.DictWriter(file, fieldnames=rows[0].keys())
        writer.writeheader()
        writer.writerows(rows)
    for row in rows:
        print(f"{row['Codec']} {row['Phase']} {row['Metric']} {row['Case']}: {row['MedianSeconds']:.3f}s ({row['MinSeconds']:.3f}..{row['MaxSeconds']:.3f}), n={row['Count']}, frames={row['Frames']}, mismatch={row['MaxMismatches'] if row['MaxMismatches'] is not None else '--'}")
else:
    raise SystemExit('集計できる測定結果がありません。')
