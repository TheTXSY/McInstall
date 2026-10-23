import {enter, smooth, theme} from '../theme';
import {KineticText} from './Motion';
export const TrustScene = ({frame}: {frame: number}) => <div style={{position: 'absolute', inset: 0, textAlign: 'center'}}>
  <div style={{position: 'absolute', left: 900, top: 245, width: 120, height: 3, background: 'linear-gradient(90deg,#5B8CFF,#8B5CF6)', transform: `scaleX(${smooth(frame, 0, 30)})`}} />
  <div style={{position: 'absolute', top: 290, width: '100%', ...enter(frame, 3)}}>
    <div style={{fontSize: 17, color: theme.secondary, letterSpacing: 6, marginBottom: 28}}>OPEN SOURCE. OPEN CHOICE.</div>
    <div style={{fontSize: 82, fontWeight: 700, letterSpacing: 3}}><KineticText text="开源" frame={frame} delay={4}/> <span style={{color: theme.blue}}>·</span> <KineticText text="免费" frame={frame} delay={12}/> <span style={{color: theme.violet}}>·</span> <KineticText text="好用" frame={frame} delay={20}/></div>
  </div>
  <div style={{position: 'absolute', top: 505, width: '100%', fontSize: 30, lineHeight: 1.95, ...enter(frame, 6)}}>
    <div><KineticText text="代码公开可审查，不用害怕盗版和病毒" frame={frame} delay={18} stagger={.55}/></div>
    <div style={{color: theme.secondary}}><KineticText text="不放心？也可以自己从源码构建" frame={frame} delay={28} stagger={.55}/></div>
  </div>
  <div style={{position: 'absolute', top: 705, width: '100%', color: theme.secondary, fontSize: 24, ...enter(frame, 9)}}>软件后续会根据反馈持续增加功能</div>
  <div style={{position: 'absolute', top: 810, width: '100%', color: theme.muted, fontFamily: theme.mono, fontSize: 15, letterSpacing: 3, ...enter(frame, 12)}}>PUBLIC CODE / BUILD IT YOURSELF / KEEP IMPROVING</div>
</div>;
