import {Img, staticFile} from 'remotion';
import {clamp, enter, RELEASES_URL, smooth, theme} from '../theme';
import {interpolate} from 'remotion';
import {KineticText, travel} from './Motion';

export const BrandScene = ({frame, ending = false}: {frame: number; ending?: boolean}) => {
  const opening = !ending && frame < 53;
  const brandFrame = ending ? frame : frame - 39;
  const iconIn = smooth(brandFrame, 0, 32);
  return <div style={{position: 'absolute', inset: 0, textAlign: 'center'}}>
    {!ending && <div style={{position: 'absolute', top: 495, left: 0, right: 0, fontSize: 36, fontWeight: 500,
      opacity: interpolate(frame, [0, 11, 37, 53], [0, 1, 1, 0], clamp), letterSpacing: 1}}>
      PCL2新版本无法下载的解决方案
    </div>}
    {!opening || frame >= 39 ? <div style={{position: 'absolute', inset: 0, ...enter(brandFrame)}}>
      <div style={{position: 'absolute', width: 540, height: 360, left: 690, top: 235, background: 'radial-gradient(ellipse, #5B8CFF14, transparent 70%)'}} />
      <div style={{display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 32, marginTop: ending ? 265 : 295, transform: `translateY(${travel(brandFrame, [0, 65, 140], [12, -3, 0])}px)`}}>
        <div style={{position: 'relative', width: 128, height: 128, transform: `perspective(800px) translateX(${-48 * (1 - iconIn)}px) rotateY(${-35 * (1 - iconIn)}deg) scale(${.84 + .16 * iconIn})`}}>
          <svg width={156} height={156} viewBox="0 0 156 156" style={{position: 'absolute', left: -14, top: -14}}><rect x={3} y={3} width={150} height={150} rx={31} fill="none" stroke="#6484DD55" strokeWidth={1} pathLength={1} strokeDasharray={1} strokeDashoffset={1 - smooth(brandFrame, 4, 48)}/></svg>
          <Img src={staticFile('icon.png')} style={{width: 128, height: 128}} />
        </div>
        <div style={{fontSize: 96, lineHeight: 1.2, fontWeight: 700, letterSpacing: -4}}><KineticText text="McInstall" frame={brandFrame} stagger={2.1}/></div>
      </div>
      <div style={{marginTop: 36, fontSize: ending ? 32 : 34, ...enter(brandFrame, 3)}}>
        {ending ? 'PCL2新版本无法下载的解决方案' : '给 PCL 用的 MC 版本安装器'}
      </div>
      <div style={{marginTop: 25, fontSize: 34, fontWeight: 600, color: theme.text, letterSpacing: 5, ...enter(brandFrame, 6)}}>开源 · 免费 · 好用</div>
      <div style={{marginTop: 31, fontSize: 25, color: theme.secondary, ...enter(brandFrame, 9)}}>
        {ending ? '在 GitHub Releases 下载' : '5 步装好 · 原版 / Fabric / Forge / NeoForge'}
      </div>
      {ending && RELEASES_URL && <div style={{marginTop: 16, fontSize: 17, fontFamily: theme.mono, color: theme.secondary}}>{RELEASES_URL}</div>}
      <div style={{marginTop: 30, fontSize: 17, color: theme.muted, fontFamily: theme.mono}}>v0.5.0</div>
    </div> : null}
    {ending && <div style={{position: 'absolute', bottom: 130, width: '100%', color: theme.secondary, fontSize: 19}}>非官方工具 · 与 PCL 作者无关</div>}
  </div>;
};
