# Promo renderer

`make_promo.py` builds `docs/media/promo.mp4` (1920x1080, 30fps, 35s), a 12s GIF
preview, a poster and a GitHub social preview from the curated screenshots in
`docs/screenshots/`.

It does **not** open the McInstall window and does **not** use the network.

```powershell
# Pillow / numpy are also required
python -m pip install pillow numpy imageio-ffmpeg
python tools\video\make_promo.py
```

`--poster-only` regenerates the poster / social image without encoding video.
Fonts are read from `C:\Windows\Fonts` (Microsoft YaHei / Segoe UI / Consolas).
