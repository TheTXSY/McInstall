import {Img, staticFile} from 'remotion';
import {enter, smooth, theme} from '../theme';
import {KineticText, travel} from './Motion';

export const PitchScene = ({frame}: {frame: number}) => {
  const connect = smooth(frame, 8, 40);
  const dot = travel(frame, [10, 70], [0, 1]);
  return <div style={{position: 'absolute', inset: 0, textAlign: 'center'}}>
    <svg width={1920} height={1080} style={{position: 'absolute', inset: 0}}>
      <defs><linearGradient id="pitch-line"><stop stopColor="#5B8CFF"/><stop offset="1" stopColor="#8B5CF6"/></linearGradient></defs>
      <path d="M 790 345 C 890 345 1030 345 1130 345" fill="none" stroke={theme.border} strokeWidth={2}/>
      <path d="M 790 345 C 890 345 1030 345 1130 345" fill="none" stroke="url(#pitch-line)" strokeWidth={2} pathLength={1} strokeDasharray={1} strokeDashoffset={1 - connect}/>
      <circle cx={790 + 340 * dot} cy={345} r={4} fill={theme.blue} opacity={Math.sin(Math.PI * dot)} />
    </svg>
    <div style={{position: 'absolute', left: 570, top: 282, width: 220, height: 126, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 15, border: `1px solid ${theme.border}`, borderRadius: 20, background: theme.surface, boxShadow: theme.cardShadow, transform: `translateX(${-75 * (1 - connect)}px) rotate(${-3 * (1 - connect)}deg)`}}>
      <Img src={staticFile('icon.png')} style={{width: 57, height: 57}}/><span style={{fontSize: 25, fontWeight: 600}}>McInstall</span>
    </div>
    <div style={{position: 'absolute', left: 1130, top: 282, width: 220, height: 126, display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 48, fontWeight: 600, border: '1px solid #5B8CFF66', borderRadius: 20, background: theme.surface, boxShadow: theme.cardShadow, transform: `translateX(${75 * (1 - connect)}px) rotate(${3 * (1 - connect)}deg)`}}>PCL2</div>
    <div style={{position: 'absolute', left: 0, right: 0, top: 480, fontSize: 78, lineHeight: 1.3, fontWeight: 700, letterSpacing: -2}}><KineticText text="一键适配PCL2" frame={frame} delay={9}/></div>
    <div style={{position: 'absolute', left: 0, right: 0, top: 615, fontSize: 40, color: theme.secondary, ...enter(frame, 23)}}>无需复杂上手，简单高效</div>
    <div style={{position: 'absolute', left: 840, top: 745, width: 240 * smooth(frame, 28, 65), height: 2, background: 'linear-gradient(90deg,#5B8CFF,#8B5CF6)'}}/>
  </div>;
};
