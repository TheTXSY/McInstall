// Reproducible fallback if Remotion's bundled encoder cannot run on Windows.
// First render image sequences with the commands in README.md.
const {spawnSync} = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const timing = require('../src/timing.json');
process.chdir(root);
function findBinary(dir, name) {
  if (!fs.existsSync(dir)) return null;
  for (const entry of fs.readdirSync(dir, {withFileTypes: true})) {
    const p = path.join(dir, entry.name);
    if (entry.isFile() && entry.name === name) return p;
    if (entry.isDirectory()) {const found = findBinary(p, name); if (found) return found;}
  }
  return null;
}
const ffmpeg = process.env.PROMO_FFMPEG || findBinary(path.join(root, 'out/ffmpeg'), 'ffmpeg.exe') || 'ffmpeg';
const run = args => {
  const r = spawnSync(ffmpeg, ['-hide_banner', '-y', ...args], {stdio: 'inherit'});
  if (r.status !== 0) throw new Error(`FFmpeg failed: ${r.status}, ${r.error || ''}`);
};
const mode = process.argv[2] || 'all';
const frames = name => {
  const dir = `out/${name}-frames`;
  const sample = fs.readdirSync(dir).find(f => /\d+\.jpeg$/.test(f));
  if (!sample) throw new Error(`No JPEG sequence in ${dir}`);
  return path.join(dir, sample.replace(/\d+(?=\.jpeg$)/, n => `%0${n.length}d`));
};
if (mode === 'all' || mode === 'mp4') run(['-framerate', String(timing.fps), '-i', frames('mp4'), '-frames:v', String(timing.durationInFrames), '-vf', 'scale=in_range=pc:out_range=tv:out_color_matrix=bt709', '-c:v', 'libx264', '-preset', 'slow', '-crf', '18', '-pix_fmt', 'yuv420p', '-color_range', 'tv', '-colorspace', 'bt709', '-color_trc', 'bt709', '-color_primaries', 'bt709', '-movflags', '+faststart', '-an', '../docs/media/mcinstall-promo.mp4']);
if (mode === 'all' || mode === 'gif') run(['-framerate', '15', '-i', frames('gif'), '-t', '12', '-filter_complex', '[0:v]split[a][b];[a]palettegen=max_colors=96:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle', '-loop', '0', '../docs/media/mcinstall-promo.gif']);
