import type {ReactNode} from 'react';
import {Img, staticFile} from 'remotion';
import {smooth, theme} from '../theme';
import type {StepData} from './StepScene';
import {travel} from './Motion';

type Crop = {x: number; y: number; w: number; h: number; caption: string};
const crops: Crop[] = [
  {x: 325, y: 180, w: 430, h: 180, caption: '分类与搜索'},
  {x: 790, y: 190, w: 430, h: 180, caption: '加载器，一目了然'},
  {x: 620, y: 180, w: 430, h: 180, caption: '线程与完整性校验'},
  {x: 620, y: 180, w: 430, h: 180, caption: '安装前，先确认将创建的版本'},
  {x: 330, y: 190, w: 430, h: 180, caption: '速度、进度与当前文件'},
];
const focuses = [
  [[640, 440], [590, 240], [540, 240], [620, 330]],
  [[640, 440], [750, 270], [800, 350], [700, 430]],
  [[640, 440], [750, 245], [750, 365], [740, 465]],
  [[640, 440], [780, 270], [790, 280], [740, 340]],
  [[640, 440], [560, 290], [620, 340], [630, 400]],
  [[640, 440], [660, 420], [650, 430], [640, 440]],
];

const Screen = ({data, frame}: {data: StepData; frame: number}) => data.number === 6 ? <>
  <Img src={staticFile('screens/dark-step1.png')} style={{position: 'absolute', width: 1280, height: 880}} />
  <Img src={staticFile('screens/light-step1.png')} style={{position: 'absolute', width: 1280, height: 880, clipPath: `inset(0 ${100 - 100 * smooth(frame, 24, 65)}% 0 0)`}} />
</> : <Img src={staticFile(`screens/${data.image}`)} style={{position: 'absolute', width: 1280, height: 880}} />;

// An actual camera move over unchanged screenshot pixels. No synthetic clicks,
// selected states, game files or download progress are generated.
export const CameraShot = ({data, frame, children}: {data: StepData; frame: number; children: ReactNode}) => {
  const d = data.duration;
  const times = [0, d * .29, d * .68, d];
  const target = focuses[data.number - 1];
  const fx = travel(frame, times, target.map(t => t[0]));
  const fy = travel(frame, times, target.map(t => t[1]));
  const close = data.number === 4 ? 1.065 : data.number === 6 ? 1.035 : 1.055;
  const zoom = travel(frame, times, [1, 1.025, close, 1.01]);
  const drift = travel(frame, times, [0, 9, -5, 0]);
  const lift = travel(frame, times, [14, -2, -9, 0]);
  const angle = travel(frame, times, [2.2, -.8, .7, 0]);
  const crop = crops[data.number - 1];
  const start = Math.max(25, d * .22);
  const reveal = smooth(frame, start, start + 24);
  const dismiss = smooth(frame, d - 30, d - 8);
  const visible = reveal * (1 - dismiss);
  const detailScale = crop ? 610 / crop.w : 1;
  return <>
    <div style={{position: 'absolute', left: 736, top: 186, width: 1120, height: 770, border: `1px solid ${theme.border}`, borderRadius: 18, overflow: 'hidden', boxShadow: theme.cardShadow, transform: `perspective(2200px) translate3d(${drift}px,${lift}px,0) rotateY(${angle}deg) rotateX(${-angle * .3}deg) scale(${zoom})`, transformOrigin: `${100 * fx / 1280}% ${100 * fy / 880}%`}}>
      <div style={{position: 'absolute', width: 1280, height: 880, transform: 'scale(.875)', transformOrigin: 'top left'}}>
        <Screen data={data} frame={frame}/>
        {children}
      </div>
    </div>
    {crop && visible > 0 && <div style={{position: 'absolute', left: 1205 + 5 * Math.sin(frame / 60), top: 638, width: 610, borderRadius: 16, overflow: 'hidden', background: theme.surface, border: '1px solid #A7B8D4', boxShadow: '0 24px 65px #18263D33', opacity: visible, transform: `translateY(${90 * (1 - reveal) + 24 * dismiss - 3 * Math.sin(frame / 32)}px) scale(${.89 + .11 * reveal}) rotate(${-2 * (1 - reveal)}deg)`, transformOrigin: 'bottom right'}}>
      <div style={{height: 40, display: 'flex', alignItems: 'center', gap: 11, paddingLeft: 19, borderBottom: `1px solid ${theme.border}`, color: theme.secondary, fontSize: 17, letterSpacing: 1}}>
        <div style={{width: 5, height: 5, borderRadius: 5, background: '#8EA7FF'}}/>{crop.caption}
        <div style={{marginLeft: 'auto', marginRight: 18, width: 30 * reveal, height: 1, background: '#698BDE'}}/>
      </div>
      <div style={{position: 'relative', height: crop.h * detailScale, overflow: 'hidden'}}>
        <div style={{position: 'absolute', width: 1280, height: 880, transformOrigin: 'top left', transform: `translate(${-crop.x * detailScale}px,${-crop.y * detailScale}px) scale(${detailScale})`}}><Screen data={data} frame={frame}/></div>
      </div>
    </div>}
  </>;
};
