#!/usr/bin/env python3
"""McInstall promo video: offscreen screenshots -> 1080p MP4 + GIF.

No app window is opened and no network is used.
Usage: python tools/video/make_promo.py [--poster-only]
"""

from __future__ import annotations

import argparse
import math
import subprocess
import sys
import wave
from pathlib import Path

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
SHOTS = ROOT / "docs" / "screenshots"
OUT = ROOT / "docs" / "media"
WORK = ROOT / "tools" / "video" / "work"
W, H, FPS, DURATION, FADE = 1920, 1080, 30, 35.0, 0.65

INK, INK_SOFT, MUTED, FAINT = (242, 244, 248), (180, 190, 205), (138, 148, 166), (90, 100, 116)
ACCENT, ACCENT_2 = (91, 140, 255), (139, 92, 246)
LINE, CARD_LINE, CARD_LINE_LIGHT = (35, 42, 54), (48, 57, 73), (211, 221, 235)
BG_TOP, BG_BOTTOM = (7, 9, 14), (12, 18, 30)

CN_BOLD = Path(r"C:\Windows\Fonts\msyhbd.ttc")
CN = Path(r"C:\Windows\Fonts\msyh.ttc")
EN_BOLD = Path(r"C:\Windows\Fonts\segoeuib.ttf")
MONO = Path(r"C:\Windows\Fonts\consola.ttf")


def font(p, size):
    return ImageFont.truetype(str(p), size=size)


F_TITLE = font(CN_BOLD, 78)
F_BODY = font(CN, 25)
F_CHIP = font(CN, 16)
F_TINY = font(CN, 14)
F_MONO = font(MONO, 15)
F_BRAND = font(EN_BOLD, 19)
F_INTRO = font(EN_BOLD, 104)
F_INTRO_SUB = font(CN, 30)
F_INTRO_FEATURE = font(CN, 20)
F_KICKER = font(EN_BOLD, 17)
F_OUTRO = font(EN_BOLD, 86)

BG = None


def clamp(x, lo=0.0, hi=1.0):
    return max(lo, min(hi, x))


def ease(x):
    x = clamp(x)
    return x * x * (3 - 2 * x)


def tracked_len(d, text, f, tracking):
    if not text:
        return 0
    return sum(d.textlength(ch, font=f) for ch in text) + tracking * (len(text) - 1)


def tracked(d, xy, text, f, fill, tracking=2):
    x, y = xy
    for ch in text:
        d.text((x, y), ch, font=f, fill=fill)
        x += d.textlength(ch, font=f) + tracking


def rounded_mask(size, radius, ss=4):
    w, h = size
    mask = Image.new("L", (w * ss, h * ss), 0)
    d = ImageDraw.Draw(mask)
    d.rounded_rectangle([0, 0, w * ss - 1, h * ss - 1], radius=radius * ss, fill=255)
    return mask.resize((w, h), Image.Resampling.LANCZOS)


def make_background():
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    t = (yy / (H - 1))[:, :, None]
    grad = np.zeros((H, W, 3), dtype=np.float32)
    for c in range(3):
        grad[:, :, c] = BG_TOP[c] + (BG_BOTTOM[c] - BG_TOP[c]) * (t[:, :, 0] ** 1.15)

    def glow(cx, cy, radius, color, strength):
        d2 = (xx - cx) ** 2 + (yy - cy) ** 2
        return np.exp(-d2 / (2 * radius * radius))[:, :, None] * np.array(color, dtype=np.float32) * strength

    grad += glow(280, 120, 620, (22, 44, 92), 0.55)
    grad += glow(1700, 980, 760, (48, 26, 85), 0.50)
    grad += glow(1650, 100, 520, (18, 30, 60), 0.35)
    nx, ny = (xx - W / 2) / (W / 2), (yy - H / 2) / (H / 2)
    r = np.sqrt(nx * nx + ny * ny)
    grad *= np.clip(1 - 0.35 * np.clip(r - 0.45, 0, 1.6) ** 1.4, 0.45, 1.0)[:, :, None]
    img = Image.fromarray(np.clip(grad, 0, 255).astype(np.uint8), "RGB").convert("RGBA")

    grid = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    gd = ImageDraw.Draw(grid)
    for x in range(0, W, 80):
        gd.line([(x, 0), (x, H)], fill=(255, 255, 255, 5))
    for y in range(0, H, 80):
        gd.line([(0, y), (W, y)], fill=(255, 255, 255, 5))
    img.alpha_composite(grid)

    noise_path = ROOT / "src" / "McInstall.App" / "noise.png"
    if noise_path.exists():
        tile = Image.open(noise_path).convert("RGBA")
        noise = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        for y in range(0, H, tile.height):
            for x in range(0, W, tile.width):
                noise.alpha_composite(tile, (x, y))
        noise.putalpha(noise.getchannel("A").point(lambda a: int(a * 0.45)))
        img.alpha_composite(noise)
    return img


def make_card(shot_path, width, height, radius=18, light=False):
    shot = Image.open(shot_path).convert("RGBA").resize((width, height), Image.Resampling.LANCZOS)
    shot.putalpha(rounded_mask((width, height), radius))
    pad = 64
    card = Image.new("RGBA", (width + pad * 2, height + pad * 2), (0, 0, 0, 0))
    shadow = Image.new("RGBA", card.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    sd.rounded_rectangle([pad, pad + 16, pad + width, pad + height + 16],
                         radius=radius + 6, fill=(0, 0, 0, 175))
    card.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(32)))
    card.alpha_composite(shot, (pad, pad))
    d = ImageDraw.Draw(card)
    border = CARD_LINE_LIGHT if light else CARD_LINE
    d.rounded_rectangle([pad, pad, pad + width - 1, pad + height - 1],
                        radius=radius, outline=border, width=2)
    d.line([pad + radius, pad + 2, pad + width - radius, pad + 2],
           fill=(255, 255, 255, 20))
    return card, pad


def brand(im):
    d = ImageDraw.Draw(im)
    icon = Image.open(ROOT / "src" / "McInstall.App" / "app.ico").convert("RGBA")
    im.alpha_composite(icon.resize((30, 30), Image.Resampling.LANCZOS), (120, 52))
    d.text((160, 56), "McInstall", font=F_BRAND, fill=INK)
    d.line([(253, 60), (253, 76)], fill=LINE)
    d.text((266, 59), "MC 版本安装器", font=F_TINY, fill=MUTED)
    d.text((1800, 58), "v0.5.0", font=F_MONO, fill=FAINT, anchor="ra")


def layout(shot, step, title, lines, chips, light=False):
    im = BG.copy()
    brand(im)
    d = ImageDraw.Draw(im)
    d.rectangle([112, 244, 115, 600], fill=ACCENT)
    tracked(d, (136, 242), step, F_KICKER, ACCENT)
    d.text((132, 274), title, font=F_TITLE, fill=INK)
    y = 404
    for line in lines:
        d.text((136, y), line, font=F_BODY, fill=INK_SOFT)
        y += 39
    x, cy = 136, 548
    for chip in chips:
        w = int(d.textlength(chip, font=F_CHIP) + 30)
        d.rounded_rectangle([x, cy, x + w, cy + 36], radius=18,
                            fill=(20, 28, 44, 210), outline=(50, 66, 96), width=1)
        d.text((x + 15, cy + 8), chip, font=F_CHIP, fill=INK_SOFT)
        x += w + 10
    card, pad = make_card(shot, 1120, 770, light=light)
    im.alpha_composite(card, (740 - pad, 112 - pad))
    d = ImageDraw.Draw(im)
    d.line([(136, 918), (660, 918)], fill=LINE)
    d.text((136, 936), "McInstall · 给 PCL 的伴生安装器", font=F_TINY, fill=FAINT)
    return im.convert("RGB")


def intro():
    im = BG.copy()
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(glow).ellipse([W // 2 - 240, 80, W // 2 + 240, 520], fill=(91, 140, 255, 32))
    im.alpha_composite(glow.filter(ImageFilter.GaussianBlur(90)))
    icon = Image.open(ROOT / "src" / "McInstall.App" / "app.ico").convert("RGBA")
    im.alpha_composite(icon.resize((150, 150), Image.Resampling.LANCZOS), (W // 2 - 75, 215))
    d = ImageDraw.Draw(im)
    tw = tracked_len(d, "McInstall", F_INTRO, 2)
    tracked(d, (W // 2 - tw / 2, 408), "McInstall", F_INTRO, INK)
    d.text((W // 2, 552), "给 PCL 用的 MC 版本安装器", font=F_INTRO_SUB, fill=INK_SOFT, anchor="ma")
    d.line([(W // 2 - 60, 618), (W // 2 + 60, 618)], fill=ACCENT, width=2)
    d.text((W // 2, 652), "5 步装好 · 原版 / Fabric / Forge / NeoForge",
           font=F_INTRO_FEATURE, fill=MUTED, anchor="ma")
    d.text((W // 2, 712), "v0.5.0  ·  MIT License", font=F_MONO, fill=FAINT, anchor="ma")
    return im.convert("RGB")


def outro():
    im = BG.copy()
    icon = Image.open(ROOT / "src" / "McInstall.App" / "app.ico").convert("RGBA")
    im.alpha_composite(icon.resize((104, 104), Image.Resampling.LANCZOS), (W // 2 - 52, 250))
    d = ImageDraw.Draw(im)
    tw = tracked_len(d, "McInstall", F_OUTRO, 2)
    tracked(d, (W // 2 - tw / 2, 390), "McInstall", F_OUTRO, INK)
    d.text((W // 2, 510), "给 PCL 用的 MC 版本安装器", font=F_INTRO_SUB, fill=INK_SOFT, anchor="ma")
    d.line([(W // 2 - 60, 570), (W // 2 + 60, 570)], fill=ACCENT, width=2)
    d.text((W // 2, 604), "开源 · MIT License", font=F_INTRO_FEATURE, fill=MUTED, anchor="ma")
    d.text((W // 2, 648), "在 GitHub Releases 下载", font=F_INTRO_FEATURE, fill=ACCENT, anchor="ma")
    d.text((W // 2, 708), "v0.5.0", font=F_MONO, fill=FAINT, anchor="ma")
    return im.convert("RGB")


def build_scenes():
    global BG
    BG = make_background()
    return {
        "intro": intro(),
        "step1": layout(SHOTS / "dark-step1.png", "STEP 01 / 05", "选版本",
                        ["正式版、快照，或已安装版本", "搜索、校验、修复都在这一步"],
                        ["官方清单", "已安装修复"]),
        "step2": layout(SHOTS / "dark-step2.png", "STEP 02 / 05", "选加载器",
                        ["原版 / Fabric / Forge / NeoForge", "OptiFine 作为 Forge 附加项"],
                        ["自动选版本", "OptiFine"]),
        "step3": layout(SHOTS / "dark-step3.png", "STEP 03 / 05", "调环境",
                        ["线程、全量校验、Java 路径", "安装位置可以直接改"],
                        ["1–32 线程", "SHA1 校验"]),
        "step4": layout(SHOTS / "dark-step4.png", "STEP 04 / 05", "起名字",
                        ["默认名自动去重", "Forge / NeoForge 会提示命名规则"],
                        ["默认去重", "不覆盖旧版本"]),
        "step5": layout(SHOTS / "dark-step5.png", "STEP 05 / 05", "下载安装",
                        ["实时速度、当前文件、完整日志", "断点续传 · SHA1 · 复用已有文件"],
                        ["实时进度", "可取消"]),
        "theme": layout(SHOTS / "light-step1.png", "THEME", "深浅色",
                        ["默认跟随 Windows 系统", "也可以固定为浅色或深色"],
                        ["跟随系统", "浅色", "深色"], light=True),
        "outro": outro(),
    }


SCENES = [
    ("intro", 0.0, 3.4), ("step1", 3.4, 8.6), ("step2", 8.6, 13.4),
    ("step3", 13.4, 17.8), ("step4", 17.8, 22.2), ("step5", 22.2, 27.2),
    ("theme", 27.2, 31.4), ("outro", 31.4, 35.0),
]


def weights_at(t):
    out = []
    for i, (_, start, end) in enumerate(SCENES):
        a = 1.0
        if i > 0:
            a *= clamp((t - start) / FADE)
        if i < len(SCENES) - 1:
            a *= clamp((end - t) / FADE)
        if a > 0.001:
            out.append((i, a))
    return out


def vignette():
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    nx, ny = (xx - W / 2) / (W / 2), (yy - H / 2) / (H / 2)
    r = np.sqrt(nx * nx + ny * ny)
    v = np.clip(1 - 0.18 * np.clip(r - 0.5, 0, 1.4) ** 1.3, 0.72, 1.0)
    arr = (v * 255).astype(np.uint8)
    return Image.fromarray(np.dstack([arr, arr, arr]), "RGB")


def lerp(a, b, p):
    return a + (b - a) * p


def cursor(im, x, y, click=0.0):
    d = ImageDraw.Draw(im)
    pts = [(x, y), (x, y + 23), (x + 6, y + 17), (x + 11, y + 27),
           (x + 16, y + 25), (x + 11, y + 15), (x + 20, y + 15)]
    d.polygon([(px + 2, py + 3) for px, py in pts], fill=(8, 10, 16))
    d.polygon(pts, fill=(255, 255, 255), outline=(18, 24, 34))
    if 0 < click < 1:
        r = 12 + 30 * click
        d.ellipse([x - r, y - r, x + r, y + r], outline=ACCENT, width=3)
        d.ellipse([x - 3, y - 3, x + 3, y + 3], fill=ACCENT)


def overlay_cursor(frame, key, local):
    if key == "step1" and local >= 0.55:
        p = ease((local - 0.55) / 1.8)
        cursor(frame, lerp(1510, 1150, p), lerp(790, 355, p), clamp((local - 2.45) / 0.5))
    elif key == "step2" and local >= 0.5:
        p = ease((local - 0.5) / 1.7)
        cursor(frame, lerp(1500, 1190, p), lerp(760, 350, p), clamp((local - 2.25) / 0.5))


def make_audio(path):
    sr, n = 44100, int(44100 * DURATION)
    t = np.arange(n) / sr
    left, right = np.zeros(n), np.zeros(n)
    chords = [(110.00, 130.81, 164.81), (87.31, 110.00, 130.81),
              (130.81, 164.81, 196.00), (98.00, 123.47, 146.83)]
    seg = DURATION / 4
    for i, freqs in enumerate(chords):
        start, end = i * seg, min(DURATION, i * seg + seg + 1.2)
        pad = np.zeros(n)
        for j, f in enumerate(freqs):
            pad += np.sin(2 * np.pi * f * t + j * 0.7) * 0.55
            pad += np.sin(2 * np.pi * f * 2.003 * t + j * 1.1) * 0.14
            pad += np.sin(2 * np.pi * f * 0.501 * t) * 0.10
        window = np.zeros(n)
        idx = (t >= start) & (t < end)
        local = t[idx] - start
        window[idx] = np.minimum(1, local / 1.2) * np.minimum(1, (end - t[idx]) / 1.2)
        pad *= window
        pan = 0.5 + 0.16 * math.sin(i * 1.3)
        left += pad * (1 - pan) * 0.09
        right += pad * pan * 0.09
    bells = [440, 523.25, 659.25, 783.99, 659.25, 523.25, 440, 392]
    for k in range(9):
        start = 1 + k * 3.6
        if start >= DURATION:
            break
        env = np.where(t >= start, np.exp(-2.6 * np.maximum(0, t - start)), 0)
        f = bells[k % len(bells)]
        left += 0.03 * env * np.sin(2 * np.pi * f * (t - start))
        right += 0.03 * env * np.sin(2 * np.pi * f * 1.002 * (t - start))
    for _, start, _ in SCENES[1:]:
        env = np.where(t >= start, np.exp(-3.5 * np.maximum(0, t - start)), 0)
        left += 0.016 * env * np.sin(2 * np.pi * 1318.5 * (t - start))
        right += 0.016 * env * np.sin(2 * np.pi * 1567.98 * (t - start))
    fade = np.ones(n)
    fade[: int(1.2 * sr)] = np.linspace(0, 1, int(1.2 * sr))
    fade[-int(2 * sr):] = np.linspace(1, 0, int(2 * sr))
    left *= fade
    right *= fade
    peak = max(1e-9, np.max(np.abs(np.concatenate([left, right]))))
    pcm = np.clip(np.stack([left, right], axis=1) * 0.30 / peak * 32767, -32768, 32767).astype(np.int16)
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(2)
        wf.setsampwidth(2)
        wf.setframerate(sr)
        wf.writeframes(pcm.tobytes())


def find_ffmpeg():
    sys.path.insert(0, str(ROOT / "tools" / "video" / ".deps"))
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception:
        for c in ("ffmpeg", "ffmpeg.exe"):
            try:
                subprocess.run([c, "-version"], capture_output=True, check=True)
                return c
            except Exception:
                pass
    raise SystemExit("ffmpeg not found: pip install --target tools/video/.deps imageio-ffmpeg")


def render_video(scenes, ffmpeg, audio, output):
    output.parent.mkdir(parents=True, exist_ok=True)
    WORK.mkdir(parents=True, exist_ok=True)
    log_path = WORK / "ffmpeg-encode.log"
    cmd = [ffmpeg, "-y", "-loglevel", "error",
           "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "pipe:0",
           "-i", str(audio), "-c:v", "libx264", "-preset", "medium", "-crf", "18",
           "-pix_fmt", "yuv420p", "-profile:v", "high", "-c:a", "aac", "-b:a", "160k",
           "-ar", "44100", "-shortest", "-movflags", "+faststart", str(output)]
    total = int(DURATION * FPS)
    vig = vignette()
    imgs = [scenes[k] for k, _, _ in SCENES]
    with open(log_path, "w", encoding="utf-8") as log:
        proc = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=log)
        assert proc.stdin
        try:
            for i in range(total):
                t = i / FPS
                ws = weights_at(t)
                if not ws:
                    frame = imgs[0]
                elif len(ws) == 1:
                    frame = imgs[ws[0][0]]
                else:
                    ia, wa = ws[0]
                    ib, wb = ws[1]
                    frame = Image.blend(imgs[ia], imgs[ib], wb / (wa + wb))
                frame = ImageChops.multiply(frame, vig)
                if ws:
                    ki, _ = max(ws, key=lambda x: x[1])
                    key, start, _ = SCENES[ki]
                    overlay_cursor(frame, key, t - start)
                d = ImageDraw.Draw(frame)
                p = clamp(t / DURATION)
                d.rectangle([0, H - 4, int(W * p), H - 1], fill=ACCENT)
                d.rectangle([max(0, int(W * p) - 80), H - 4, int(W * p), H - 1], fill=ACCENT_2)
                if t < 0.8:
                    frame = Image.blend(Image.new("RGB", (W, H)), frame, ease(t / 0.8))
                if t > DURATION - 1.2:
                    frame = Image.blend(frame, Image.new("RGB", (W, H)),
                                        ease((t - DURATION + 1.2) / 1.2))
                proc.stdin.write(frame.tobytes())
                if i % 150 == 0:
                    print(f"  frame {i}/{total} ({i / total * 100:4.0f}%)", flush=True)
        finally:
            proc.stdin.close()
            code = proc.wait()
    if code != 0:
        print(log_path.read_text(encoding="utf-8", errors="ignore")[-4000:])
        raise SystemExit(f"ffmpeg exited with {code}")
    print(f"video -> {output} ({output.stat().st_size / 1024 / 1024:.1f} MB)")


def make_gif(ffmpeg, video, gif):
    vf = ("fps=12,scale=960:-1:flags=lanczos,split[s0][s1];"
          "[s0]palettegen=max_colors=160[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5")
    subprocess.run([ffmpeg, "-y", "-loglevel", "error", "-ss", "0", "-t", "12",
                    "-i", str(video), "-vf", vf, "-loop", "0", str(gif)], check=True)
    print(f"gif   -> {gif} ({gif.stat().st_size / 1024 / 1024:.1f} MB)")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--poster-only", action="store_true")
    args = ap.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    scenes = build_scenes()
    poster = scenes["step1"]
    poster.save(OUT / "promo-poster.png")
    print(f"poster -> {OUT / 'promo-poster.png'}")
    social = poster.resize((1280, 720), Image.Resampling.LANCZOS).crop((0, 40, 1280, 680))
    social.save(OUT / "social-preview.png")
    print(f"social -> {OUT / 'social-preview.png'}")
    if args.poster_only:
        return
    audio = WORK / "promo.wav"
    make_audio(audio)
    print(f"audio  -> {audio}")
    ffmpeg = find_ffmpeg()
    video = OUT / "promo.mp4"
    render_video(scenes, ffmpeg, audio, video)
    make_gif(ffmpeg, video, OUT / "promo.gif")


if __name__ == "__main__":
    main()
