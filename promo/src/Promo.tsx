import {AbsoluteFill, Audio, Img, interpolate, staticFile, useCurrentFrame, useVideoConfig} from 'remotion';
import {useFonts} from './fonts';
import timing from './timing.json';
import {AUDIO_FILE, clamp, smooth, theme, TRANSITION} from './theme';
import {BrandScene} from './scenes/BrandScene';
import {StepScene, steps} from './scenes/StepScene';
import {TrustScene} from './scenes/TrustScene';
import {PitchScene} from './scenes/PitchScene';

type Scene = {kind: 'brand' | 'pitch' | 'step' | 'trust' | 'end'; start: number; end: number; step?: number; sourceOffset?: number};
const full = timing.scenes as Scene[];
// Four 3-second moments: brand → PCL2 promise → product detail → trust.
const preview: Scene[] = [
  {kind: 'brand', start: 0, end: 90},
  {kind: 'pitch', start: 90, end: 180},
  {kind: 'step', start: 180, end: 270, step: 0},
  {kind: 'trust', start: 270, end: 360},
];

const Backdrop = ({frame: f}: {frame: number}) => <>
  <AbsoluteFill style={{background: theme.backdrop}} />
  <AbsoluteFill style={{backgroundImage: 'linear-gradient(#71829A0B 1px,transparent 1px),linear-gradient(90deg,#71829A0B 1px,transparent 1px)', backgroundSize: '80px 80px', maskImage: 'linear-gradient(transparent,#0008,transparent)'}} />
  <AbsoluteFill style={{opacity: 0.012, backgroundImage: `url(${staticFile('noise.png')})`, backgroundRepeat: 'repeat'}} />
  <div style={{position: 'absolute', width: 1250, height: 1100, left: 650 + 70 * Math.sin(f / 350), top: -200 + 40 * Math.sin(f / 270), background: 'radial-gradient(ellipse,#5B8CFF0D,transparent 68%)'}} />
</>;

export const Promo = ({short = false}: {short?: boolean}) => {
  useFonts();
  const frame = useCurrentFrame();
  const {width, fps, durationInFrames} = useVideoConfig();
  const f = frame * 30 / fps;
  const timeline = short ? preview : full;
  const end = durationInFrames * 30 / fps;
  const globalFade = interpolate(f, [0, 9, end - 30, end - 1], [0, 1, 1, 0], clamp);
  return <AbsoluteFill style={{background: theme.bg}}>
    <div style={{width: 1920, height: 1080, transform: `scale(${width / 1920})`, transformOrigin: 'top left', position: 'absolute', color: theme.text, fontFamily: theme.sans, opacity: globalFade, overflow: 'hidden', background: theme.backdrop}}>
      <Backdrop frame={f} />
      <div style={{position: 'absolute', zIndex: 20, top: 55, left: 80, display: 'flex', alignItems: 'center', gap: 13}}>
        <Img src={staticFile('icon.png')} style={{width: 36, height: 36}} />
        <span style={{fontWeight: 600, fontSize: 23, letterSpacing: -0.5}}>McInstall</span>
        <div style={{width: 1, height: 18, background: theme.border, margin: '0 3px'}} />
        <span style={{fontSize: 16, color: theme.secondary}}>MC 版本安装器</span>
      </div>
      <div style={{position: 'absolute', zIndex: 20, right: 80, top: 64, color: theme.secondary, fontSize: 16, fontFamily: theme.mono}}>v0.5.0</div>
      {timeline.map((scene, i) => {
        // A tracked diagonal reveal overtakes the outgoing camera move.
        if (f < scene.start || f >= scene.end + TRANSITION) return null;
        const local = f - scene.start + (scene.sourceOffset ?? 0);
        const incoming = i === 0 ? 1 : smooth(local, 0, TRANSITION);
        const outgoing = smooth(f, scene.end, scene.end + TRANSITION);
        const edge = 2050 - 2250 * incoming;
        return <AbsoluteFill key={i} style={{
          clipPath: incoming < 1 ? `polygon(${edge}px 0, 1920px 0, 1920px 1080px, ${edge - 170}px 1080px)` : undefined,
          opacity: 1 - .35 * outgoing,
          transform: `perspective(2400px) translateX(${90 * (1 - incoming) - 105 * outgoing}px) translateY(${-16 * outgoing}px) scale(${1 + .035 * (1 - incoming) - .045 * outgoing}) rotateY(${-3 * outgoing}deg)`,
          transformOrigin: '70% 50%', filter: `blur(${3 * (1 - incoming) + 3.5 * outgoing}px)`,
        }}>
          <Backdrop frame={f} />
          {scene.kind === 'brand' ? <BrandScene frame={local} /> : scene.kind === 'pitch' ? <PitchScene frame={local} /> : scene.kind === 'end' ? <BrandScene frame={local} ending /> : scene.kind === 'trust' ? <TrustScene frame={local} /> : <StepScene frame={local} data={{...steps[scene.step!], duration: scene.end - scene.start}} />}
        </AbsoluteFill>;
      })}
      {timeline.slice(1).map((scene, i) => {
        if (f < scene.start || f >= scene.start + TRANSITION) return null;
        const q = smooth(f - scene.start, 0, TRANSITION);
        return <div key={`edge-${i}`} style={{position: 'absolute', left: 2050 - 2250 * q - 80, top: 0, width: 90, height: 1080, transform: 'skewX(-9deg)', transformOrigin: 'top', opacity: Math.sin(Math.PI * q) * .6, background: 'linear-gradient(90deg,transparent,#5B8CFF0D,#8B5CF62A,transparent)', pointerEvents: 'none'}} />;
      })}
      <div style={{position: 'absolute', zIndex: 20, left: 0, bottom: 0, height: 3, width: `${100 * frame / (durationInFrames - 1)}%`, background: 'linear-gradient(90deg,#5B8CFF,#8B5CF6)'}} />
    </div>
    {AUDIO_FILE && <Audio src={staticFile(AUDIO_FILE)} volume={0.18} />}
  </AbsoluteFill>;
};
