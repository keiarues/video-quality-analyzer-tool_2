"""検証専用。通常評価との色・時刻互換性とVMAFの区間境界を個別に照合する。"""
import argparse
import json
import math
from pathlib import Path
import subprocess
import sys
import tempfile
import time


def compare(expected, actual, boundaries=()):
    keys = sorted(expected.keys() | actual.keys())
    missing = [key for key in keys if key not in expected or key not in actual]
    differences = {key: abs(expected[key] - actual[key]) for key in keys if key not in missing}
    changed = [key for key, difference in differences.items() if difference > 1e-6]
    return {"ExpectedFrames": len(expected), "ActualFrames": len(actual), "Missing": missing,
            "Mismatches": len(changed), "MaximumDifference": max(differences.values(), default=0),
            "ChangedNumbers": changed, "BoundaryChanges": [key for key in changed if key in boundaries]}


def verify_natural_context(ffmpeg, args, report):
    """全フレームを保持した文脈と、区間の前後2フレームを含めた文脈を照合する。"""
    reference, candidate = report["Reference"], report["Candidate"]
    ranges = [(row["Start"], row["End"]) for row in report["Ranges"]]
    fps = format(reference["Fps"], ".17g")
    common = f"settb=AVTB,setpts=N/({fps}*TB)"
    total = min(reference["Frames"], candidate["Frames"])
    numbers = [number for start, end in ranges for number in range(start, end)]
    result = {"WarmupFrames": 2, "Timings": {}, "Conditions": "色タグunknown・共通time_base・VMAF既定モデル・n_threads=1。前後2フレームは今回の素材とモデルでの試験条件。"}
    with tempfile.TemporaryDirectory(prefix="vqa-context-") as temp:
        work = Path(temp)

        def evaluate(name, start, end, seek):
            inputs = ["-copyts"] if seek else []
            for path, metadata in ((args.candidate, candidate), (args.reference, reference)):
                if seek:
                    anchor = max(0, start / reference["Fps"] - 1)
                    inputs += ["-ss", f"{anchor:.9f}", "-t", f"{end / reference['Fps'] - anchor + 1:.9f}"]
                inputs += ["-i", str(path.resolve())]
            def prepare(metadata):
                select = f"select='between(round((t-{metadata['Start']})*{fps}),{start},{end - 1})'" if seek else f"trim=end_frame={end}"
                return "setparams=colorspace=unknown," + select + "," + common
            graph = f"[0:v:0]{prepare(candidate)}[d];[1:v:0]{prepare(reference)}[r];[d][r]libvmaf=log_fmt=json:log_path=metric.json:n_threads=1:shortest=1"
            print(f"文脈検証：{name}", flush=True)
            clock = time.perf_counter()
            process = subprocess.run([str(ffmpeg), "-hide_banner", "-loglevel", "error", *inputs, "-filter_complex", graph,
                                      "-an", "-f", "null", "-"], cwd=work, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            result["Timings"][name] = time.perf_counter() - clock
            if process.returncode:
                raise RuntimeError(f"{name}: FFmpeg終了コード{process.returncode}")
            payload = json.loads((work / "metric.json").read_text())
            result["VmafVersion"] = payload.get("version")
            values = {start + row["frameNum"]: row["metrics"]["vmaf"] for row in payload["frames"]}
            if len(values) != end - start:
                raise RuntimeError("文脈検証のフレーム数が一致しません。")
            return values

        full = evaluate("NaturalTimeline", 0, min(total, ranges[-1][1] + 2), False)
        expected = {number: full[number] for number in numbers}
        warm = {}
        for index, (start, end) in enumerate(ranges):
            values = evaluate(f"Warmup-{index}", max(0, start - 2), min(total, end + 2), True)
            warm.update({number: values[number] for number in range(start, end)})
        concatenated = {int(number): value for number, value in report["ValueSeries"]["Canonical"]["Vmaf"].items()}
        result["Natural-vs-Concatenated"] = compare(expected, concatenated)
        result["Natural-vs-Warmup"] = compare(expected, warm)
        result["Values"] = {"Natural": expected, "Warmup": warm}
        (args.output / "natural-context.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        if result["Natural-vs-Warmup"]["Mismatches"] or result["Natural-vs-Warmup"]["Missing"]:
            raise RuntimeError("前後2フレーム付きの区間計算が自然な文脈と一致しません。")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--quick", action="store_true")
    parser.add_argument("--context-only", action="store_true")
    args = parser.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    root = Path(__file__).resolve().parent.parent
    ffmpeg = root / "tools/ffmpeg-9.0.2-essentials_build/bin/ffmpeg.exe"
    ffprobe = ffmpeg.with_name("ffprobe.exe")
    dotnet = root / "tools/dotnet/dotnet.exe"
    runner = root / "tests/VideoQualityAnalyzer.Smoke/bin/Release/net10.0/VideoQualityAnalyzer.Smoke.dll"
    args.output.mkdir(parents=True, exist_ok=True)
    if args.context_only:
        verify_natural_context(ffmpeg, args, json.loads((args.output / "results.json").read_text(encoding="utf-8")))
        return
    report = {"Comparisons": {}, "Timings": {}, "Conditions": "単一ジョブ・キャッシュ消去なし・VMAF v0.6.1。検証側n_threads=1、通常経路は既存サービスの既定値。"}

    def probe(path):
        data = json.loads(subprocess.check_output([str(ffprobe), "-v", "error", "-select_streams", "v:0", "-show_streams", "-show_format", "-of", "json", str(path)], encoding="utf-8"))
        stream = data["streams"][0]
        numerator, denominator = map(int, stream["avg_frame_rate"].split("/"))
        return {"Codec": stream["codec_name"], "Width": stream["width"], "Height": stream["height"],
                "Fps": numerator / denominator, "Start": float(stream.get("start_time", 0)),
                "Frames": int(stream.get("nb_frames") or math.ceil(float(data["format"]["duration"]) * numerator / denominator - 1e-7)),
                "ColorSpace": stream.get("color_space", "unknown"), "PixelFormat": stream["pix_fmt"], "TimeBase": stream["time_base"]}

    reference, candidate = probe(args.reference), probe(args.candidate)
    if any(reference[key] != candidate[key] for key in ("Width", "Height", "Fps", "PixelFormat")):
        raise ValueError("同解像度・同fps・同画素形式の素材を指定してください。")
    report["Reference"], report["Candidate"] = reference, candidate
    total = min(reference["Frames"], candidate["Frames"])
    length = 12 if args.quick else 60
    starts = [0, total // 2, total - length] if args.quick else [0, 300, 1500]
    if starts[-1] + length > total:
        raise ValueError("実動画の検証には1560フレーム以上必要です。短い素材は--quickを指定してください。")
    ranges = [(start, start + length) for start in starts]
    numbers = [number for start, end in ranges for number in range(start, end)]
    report["Ranges"] = [{"Start": start, "End": end} for start, end in ranges]
    boundaries = {number for start, end in ranges for number in (start, start + 1, end - 2, end - 1)}
    fps = format(reference["Fps"], ".17g")

    with tempfile.TemporaryDirectory(prefix="vqa-compatibility-") as temp:
        work = Path(temp)

        def execute(name, arguments):
            print(f"開始：{name}", flush=True)
            clock = time.perf_counter()
            process = subprocess.run([str(ffmpeg), "-hide_banner", "-loglevel", "error", "-y", *map(str, arguments)], cwd=work, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            report["Timings"][name] = time.perf_counter() - clock
            if process.returncode:
                # パスを含む標準エラーは測定ログへ保存しない。
                raise RuntimeError(f"{name}: FFmpeg終了コード {process.returncode}")
            return process.stdout

        def save():
            (args.output / "results.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

        def metric(name, inputs, left, right, kind, indices):
            file = "metric.json" if kind == "Vmaf" else "metric.log"
            filter_text = {"Vmaf": "libvmaf=log_fmt=json:log_path=metric.json:n_threads=1:shortest=1",
                           "Ssim": "ssim=stats_file=metric.log:shortest=1", "Psnr": "psnr=stats_file=metric.log:shortest=1"}[kind]
            execute(name, [*inputs, "-filter_complex", f"[0:v:0]{left}[d];[1:v:0]{right}[r];[d][r]{filter_text}", "-an", "-fps_mode", "passthrough", "-f", "null", "-"])
            if kind == "Vmaf":
                rows = json.loads((work / file).read_text())["frames"]
                values = {indices[row["frameNum"]]: row["metrics"]["vmaf"] for row in rows}
                (args.output / f"{name}-features.json").write_text(json.dumps(rows, indent=2), encoding="utf-8")
                return values
            values = {}
            for line in (work / file).read_text().splitlines():
                fields = dict(field.split(":", 1) for field in line.split() if ":" in field)
                value = float(fields["All" if kind == "Ssim" else "psnr_avg"])
                if math.isfinite(value):
                    values[indices[int(fields["n"]) - 1]] = value
            return values

        plan = work / "plan.json"
        plan.write_text(json.dumps(report["Ranges"]))
        production_file = work / "production.json"
        print("通常アプリのサービスで3指標を計算", flush=True)
        clock = time.perf_counter()
        subprocess.run([str(dotnet), str(runner), str(root), "--compatibility-baseline", "--reference", str(args.reference.resolve()),
                        "--candidate", str(args.candidate.resolve()), "--plan", str(plan), "--output", str(production_file)], check=True)
        report["Timings"]["ProductionService"] = time.perf_counter() - clock
        production = {("Vmaf", "Ssim", "Psnr")[row["Metric"]]: {frame["Number"]: frame["Value"] for frame in row["Frames"]}
                      for row in json.loads(production_file.read_text())}
        selection = f"trim=end_frame={ranges[-1][1]},select='" + "+".join(f"between(n,{start},{end - 1})" for start, end in ranges) + "'"
        common = f"settb=AVTB,setpts=N/({fps}*TB)"
        original_inputs = ["-i", args.candidate.resolve(), "-i", args.reference.resolve()]
        canonical, time_only = {}, {}
        for kind in ("Ssim", "Psnr", "Vmaf"):
            for label, normalize, timebase in (("ColorOnly", True, False), ("TimeOnly", False, True), ("Canonical", True, True)):
                prepare = ("setparams=colorspace=unknown," if normalize else "") + selection + "," + (common if timebase else f"setpts=N/({fps}*TB)")
                values = metric(f"{kind}-{label}", original_inputs, prepare, prepare, kind, numbers)
                report["Comparisons"][f"Production-vs-{kind}-{label}"] = compare(production[kind], values, boundaries)
                if label == "Canonical":
                    canonical[kind] = values
                if label == "TimeOnly":
                    time_only[kind] = values
                save()

        # 元番号で抽出した先頭復号キャッシュと、PTSで抽出したシークキャッシュを画素照合する。
        cached = {"reference": [], "candidate": []}
        for side, path, metadata in (("reference", args.reference, reference), ("candidate", args.candidate, candidate)):
            native = f"{side}-native.nut"
            execute(f"Native-{side}", ["-i", path.resolve(), "-vf", "setparams=colorspace=unknown," + selection + "," + common,
                    "-an", "-c:v", "ffv1", "-threads:v", "1", "-fps_mode", "passthrough", native])
            for index, (start, end) in enumerate(ranges):
                anchor = max(0, start / reference["Fps"] - 1)
                file = f"{side}-{index}.nut"
                extract = f"setparams=colorspace=unknown,select='between(round((t-{metadata['Start']})*{fps}),{start},{end - 1})',{common}"
                execute(f"Seek-{side}-{index}", ["-copyts", "-ss", f"{anchor:.9f}", "-t", f"{end / reference['Fps'] - anchor + 1:.9f}",
                        "-i", path.resolve(), "-vf", extract, "-an", "-c:v", "ffv1", "-threads:v", "1", "-fps_mode", "passthrough", file])
                cached[side].append(file)
            (work / f"{side}.txt").write_text("".join(f"file '{file}'\n" for file in cached[side]))

            def hashes(inputs):
                data = execute(f"Hash-{side}-{len(report['Timings'])}", [*inputs, "-an", "-c:v", "rawvideo", "-threads:v", "1", "-fps_mode", "passthrough", "-f", "framehash", "-hash", "sha256", "-"])
                return [line.split(",")[-1].strip() for line in data.decode().splitlines() if line and not line.startswith("#")]
            expected = hashes(["-i", native])
            actual = hashes(["-f", "concat", "-safe", "0", "-i", f"{side}.txt"])
            matches = len(expected) == len(actual) == len(numbers) and expected == actual
            report["Comparisons"][f"Native-vs-Seek-{side}"] = {"ExpectedFrames": len(expected), "ActualFrames": len(actual), "Passed": matches}
            save()
            if not matches:
                raise RuntimeError("シークキャッシュの画素が一致しません。")

        joined_inputs = ["-f", "concat", "-safe", "0", "-i", "candidate.txt", "-f", "concat", "-safe", "0", "-i", "reference.txt"]
        for kind in ("Ssim", "Psnr", "Vmaf"):
            values = metric(f"{kind}-SeekJoined", joined_inputs, common, common, kind, numbers)
            comparison = compare(canonical[kind], values, boundaries)
            report["Comparisons"][f"Canonical-vs-{kind}-SeekJoined"] = comparison
            save()
            if comparison["Mismatches"] or comparison["Missing"]:
                raise RuntimeError(f"連結後の{kind}が一致しません。")
            # 色タグを復元し、通常経路の色変換を残した時刻補正基準とも比較する。
            values = metric(f"{kind}-SeekOriginalColor", joined_inputs,
                            f"setparams=colorspace={candidate['ColorSpace']},{common}",
                            f"setparams=colorspace={reference['ColorSpace']},{common}", kind, numbers)
            comparison = compare(time_only[kind], values, boundaries)
            report["Comparisons"][f"TimeOnly-vs-{kind}-SeekOriginalColor"] = comparison
            save()
            if comparison["Mismatches"] or comparison["Missing"]:
                raise RuntimeError(f"元の色タグを復元した{kind}が時刻補正基準と一致しません。")

        reset = {}
        for index, (start, end) in enumerate(ranges):
            values = metric(f"Vmaf-Reset-{index}", ["-i", cached["candidate"][index], "-i", cached["reference"][index]], common, common, "Vmaf", list(range(start, end)))
            reset.update(values)
        report["Comparisons"]["Canonical-vs-Vmaf-Reset"] = compare(canonical["Vmaf"], reset, boundaries)
        joined_features = json.loads((args.output / "Vmaf-SeekJoined-features.json").read_text())
        reset_features = [row for index in range(len(ranges)) for row in json.loads((args.output / f"Vmaf-Reset-{index}-features.json").read_text())]
        report["ResetFeatureChanges"] = {}
        for key in joined_features[0]["metrics"]:
            changed = [numbers[index] for index, (a, b) in enumerate(zip(joined_features, reset_features)) if abs(a["metrics"][key] - b["metrics"][key]) > 1e-6]
            if changed:
                report["ResetFeatureChanges"][key] = changed
        report["ValueSeries"] = {"Production": production, "TimeOnly": time_only, "Canonical": canonical, "ResetVmaf": reset}
        save()
        print("照合完了。", flush=True)


if __name__ == "__main__":
    main()
