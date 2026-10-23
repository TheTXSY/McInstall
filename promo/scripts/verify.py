"""Verify delivery parameters and extract final encoded frames for visual review.

python scripts/verify.py --ffmpeg <ffmpeg.exe> --ffprobe <ffprobe.exe>
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import subprocess
from PIL import Image, ImageDraw, ImageFont, ImageStat

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parent
TIMING = json.loads((ROOT / 'src/timing.json').read_text('utf-8'))
DURATION = TIMING['durationInFrames'] / TIMING['fps']
MEDIA = REPO / 'docs/media'
QA = ROOT / 'out/qa'
QA.mkdir(parents=True, exist_ok=True)
p = argparse.ArgumentParser()
p.add_argument('--ffmpeg', default='ffmpeg')
p.add_argument('--ffprobe', default='ffprobe')
p.add_argument('--external-change', action='append', default=[], help='Record an independently observed external edit; never restore it')
args = p.parse_args()
mp4 = MEDIA / 'mcinstall-promo.mp4'
gif = MEDIA / 'mcinstall-promo.gif'
poster = MEDIA / 'mcinstall-promo-poster.png'
brand = MEDIA / 'mcinstall-brand-promo.png'
probe = json.loads(subprocess.check_output([args.ffprobe, '-v', 'error', '-show_streams', '-show_format', '-of', 'json', str(mp4)]))
video = next(s for s in probe['streams'] if s['codec_type'] == 'video')
assert video['codec_name'] == 'h264'
assert (video['width'], video['height']) == (1920, 1080)
assert video['r_frame_rate'] == '30/1'
assert video['pix_fmt'] == 'yuv420p'
assert int(video['nb_frames']) == TIMING['durationInFrames']
assert abs(float(probe['format']['duration']) - DURATION) < .001
assert len(probe['streams']) == 1, 'Silent delivery should have no audio stream'
assert mp4.stat().st_size <= 25_000_000
assert gif.stat().st_size <= 8_000_000
with Image.open(poster) as im:
    assert im.size == (1920, 1080)
with Image.open(brand) as im:
    assert im.size == (1920, 1080)
    assert im.format == 'PNG'
with Image.open(gif) as im:
    assert im.size == (960, 540)
    gif_ms = 0
    gif_frames = im.n_frames
    for f in range(im.n_frames):
        im.seek(f)
        gif_ms += im.info.get('duration', 0)
    assert gif_frames == 180
    assert abs(gif_ms - 12000) <= 20

atoms = []
with mp4.open('rb') as f:
    while f.tell() < mp4.stat().st_size:
        pos = f.tell()
        header = f.read(8)
        if len(header) < 8:
            break
        size, tag = struct.unpack('>I4s', header)
        if size == 1:
            size = struct.unpack('>Q', f.read(8))[0]
        if size == 0:
            size = mp4.stat().st_size - pos
        atoms.append({'tag': tag.decode('ascii'), 'offset': pos, 'size': size})
        f.seek(pos + size)
tags = [a['tag'] for a in atoms]
assert tags.index('moov') < tags.index('mdat'), 'faststart missing'

times = TIMING['sampleSeconds']
canvas = Image.new('RGB', (1920, 1164), '#F8FAFC')
draw = ImageDraw.Draw(canvas)
try:
    label_font = ImageFont.truetype('C:/Windows/Fonts/consola.ttf', 18)
except OSError:
    label_font = ImageFont.load_default()
sample_checks = []
for i, t in enumerate(times):
    file = QA / f'frame-{t:04.1f}s.png'
    subprocess.run([args.ffmpeg, '-hide_banner', '-loglevel', 'error', '-y', '-ss', str(t), '-i', str(mp4), '-frames:v', '1', str(file)], check=True)
    with Image.open(file) as im:
        im = im.convert('RGB')
        assert im.size == (1920, 1080)
        assert ImageStat.Stat(im.convert('L')).stddev[0] > 5, f'Blank sample at {t}s'
        assert ImageStat.Stat(im.crop((10, 10, 40, 40)).convert('L')).mean[0] > 225, f'Background not light at {t}s'
        thumb = im.resize((640, 360), Image.Resampling.LANCZOS)
        x, y = (i % 3) * 640, (i // 3) * 388
        canvas.paste(thumb, (x, y + 28))
        draw.text((x + 15, y + 4), f'{t:.1f}s / frame {round(t*30)}', font=label_font, fill='#52657F')
        sample_checks.append({'seconds': t, 'frame': round(t * 30), 'luma_mean': round(ImageStat.Stat(im.convert('L')).mean[0], 3)})
canvas.save(QA / 'contact-sheet.png')

baseline_file = ROOT / 'out/original-hashes.json'
preserved = None
if baseline_file.exists():
    baseline = json.loads(baseline_file.read_text('utf-8'))
    changed = [name for name, digest in baseline.items() if not (REPO / name).exists() or hashlib.sha256((REPO / name).read_bytes()).hexdigest() != digest]
    unexpected = [name for name in changed if name not in args.external_change]
    assert not unexpected, f'Unexpected existing files changed: {unexpected}'
    preserved = {'original_files_checked': len(baseline), 'unchanged': len(baseline) - len(changed), 'changed': changed, 'external_changes_preserved': [name for name in changed if name in args.external_change]}

report = {
    'mp4': {'path': 'docs/media/mcinstall-promo.mp4', 'duration_seconds': DURATION, 'width': 1920, 'height': 1080, 'fps': TIMING['fps'], 'frames': TIMING['durationInFrames'], 'codec': video['codec_name'], 'pixel_format': video['pix_fmt'], 'color_space': video.get('color_space'), 'color_range': video.get('color_range'), 'audio': False, 'bytes': mp4.stat().st_size, 'faststart': True, 'top_level_atoms': atoms},
    'gif': {'path': 'docs/media/mcinstall-promo.gif', 'duration_seconds': gif_ms / 1000, 'width': 960, 'height': 540, 'fps': 15, 'frames': gif_frames, 'bytes': gif.stat().st_size},
    'poster': {'path': 'docs/media/mcinstall-promo-poster.png', 'width': 1920, 'height': 1080, 'frame': 180, 'seconds': 6, 'bytes': poster.stat().st_size},
    'brand_image': {'path': 'docs/media/mcinstall-brand-promo.png', 'width': 1920, 'height': 1080, 'format': 'PNG', 'title': 'McInstall', 'subtitle': '专为还原、延续PCL2下载功能打造', 'bytes': brand.stat().st_size},
    'samples_from_final_mp4': sample_checks,
    'preservation': preserved,
    'visual_review': 'Pending human-readable agent inspection of nine extracted frames',
    'encoding': 'Remotion 4.0.537 image sequences, FFmpeg 9.0.2 libx264 CRF18 slow BT709 limited range; GIF 96-color Bayer palette',
    'fonts': 'Local Noto Sans SC / Inter / JetBrains Mono; all loaded successfully via @remotion/google-fonts and delayRender',
}
(ROOT / 'verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), 'utf-8')
print(json.dumps({key: report[key] for key in ['mp4', 'gif', 'poster', 'brand_image', 'preservation']}, ensure_ascii=False, indent=2))
