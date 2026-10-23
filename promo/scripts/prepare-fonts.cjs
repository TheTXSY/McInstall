// Run once when changing typography/copy; renders use only the resulting local assets.
const fs = require('node:fs');
const path = require('node:path');
const {getInfo: notoInfo} = require('@remotion/google-fonts/NotoSansSC');
const {getInfo: interInfo} = require('@remotion/google-fonts/Inter');
const {getInfo: monoInfo} = require('@remotion/google-fonts/JetBrainsMono');
const root = path.resolve(__dirname, '..');
const target = path.join(root, 'public/fonts');
const readSource = d => fs.readdirSync(d, {withFileTypes: true}).map(x => x.isDirectory() ? readSource(path.join(d, x.name)) : /\.tsx?$/.test(x.name) ? fs.readFileSync(path.join(d, x.name), 'utf8') : '').join('\n');
const text = [...new Set([...readSource(path.join(root, 'src'))].filter(c => c.codePointAt(0) >= 0x3000))].join('');
async function download(url, file) {
  const r = await fetch(url);
  if (!r.ok) throw new Error(`${r.status}: ${url}`);
  fs.writeFileSync(path.join(target, file), Buffer.from(await r.arrayBuffer()));
  return `fonts/${file}`;
}
(async () => {
  const manifest = [];
  const meta = notoInfo();
  const css = await (await fetch(`https://fonts.googleapis.com/css2?family=Noto+Sans+SC:wght@400;600;700&text=${encodeURIComponent(text)}`, {headers: {'User-Agent': 'Mozilla/5.0'}})).text();
  const normalNoto = {};
  for (const block of css.split('@font-face')) {
    const weight = block.match(/font-weight:\s*(\d+)/)?.[1];
    const url = block.match(/url\(([^)]+)\)/)?.[1];
    if (weight && url) normalNoto[weight] = {text: await download(url, `NotoSansSC-copy-${weight}.woff2`)};
  }
  if (Object.keys(normalNoto).length !== 3) throw new Error('Missing Noto font weights');
  manifest.push({fontFamily: meta.fontFamily, importName: meta.importName, version: meta.version, url: meta.url, unicodeRanges: {text: [...new Set([...text])].map(c => `U+${c.codePointAt(0).toString(16)}`).join(',')}, subsets: ['text'], fonts: {normal: normalNoto}});
  for (const [getInfo, weights] of [[interInfo, ['400', '600', '700']], [monoInfo, ['400']]]) {
    const m = getInfo(); const normal = {};
    for (const weight of weights) normal[weight] = {latin: await download(m.fonts.normal[weight].latin, `${m.importName}-${weight}.woff2`)};
    manifest.push({fontFamily: m.fontFamily, importName: m.importName, version: m.version, url: m.url, unicodeRanges: {latin: m.unicodeRanges.latin}, subsets: ['latin'], fonts: {normal}});
  }
  fs.writeFileSync(path.join(root, 'src/font-manifest.json'), JSON.stringify(manifest, null, 2));
  for (const dir of ['notosanssc', 'inter', 'jetbrainsmono']) await download(`https://raw.githubusercontent.com/google/fonts/main/ofl/${dir}/OFL.txt`, `${dir}-OFL.txt`);
  console.log('Saved local Noto Sans SC, Inter, JetBrains Mono and OFL licenses. Chinese copy characters:', text.length);
})().catch(e => {console.error(e); process.exitCode = 1;});
