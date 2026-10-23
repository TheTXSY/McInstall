import {enter, smooth, theme} from '../theme';
import {KineticText} from './Motion';
import {CameraShot} from './CameraShot';

type Detail = [label: string, explanation: string];
export type StepData = {
  number: number; kicker: string; title: string[]; summary: string;
  details: Detail[]; chips: string[]; result: string; image: string; duration: number;
};
export const steps: StepData[] = [
  {number: 1, kicker: '版本选择 · 新装与修复', title: ['从想玩的版本', '开始'], summary: '新装一个版本，也能修复已有版本。',
    details: [['分类查看', '正式版 / 快照 / 已安装'], ['快速搜索', '输入版本号，直接定位目标'], ['校验修复', '检查已有版本，补齐缺失文件']],
    chips: ['官方版本清单', '已安装修复'], result: '选好版本后，类型、Java 与安装状态在左侧同步显示', image: 'dark-step1.png', duration: 150},
  {number: 2, kicker: '加载器 · 决定你的玩法', title: ['原版或模组', '按需搭配'], summary: '加载器版本默认自动，也支持手动选择。',
    details: [['原版体验', '直接安装官方原版'], ['模组支持', 'Fabric / Forge / NeoForge'], ['附加选项', 'OptiFine 可作为 Forge mod 安装']],
    chips: ['自动选版本', 'OptiFine'], result: '按你的整合包或模组需求选择相应加载器', image: 'dark-step2.png', duration: 132},
  {number: 3, kicker: '安装环境 · 默认就能开始', title: ['线程、校验', '路径都可调'], summary: '保持默认即可继续，也能按需精细设置。',
    details: [['下载线程', '支持 1–32 线程，自由调整'], ['文件校验', '可开启安装后全量 SHA1 校验'], ['安装环境', 'Java 留空自动探测，目录可直接改']],
    chips: ['默认可用', '路径可改'], result: '安装位置直接指向 PCL 使用的游戏目录', image: 'dark-step3.png', duration: 120},
  {number: 4, kicker: '命名与预览 · 确认再安装', title: ['名字不撞车', '旧版本保留'], summary: '默认名称自动去重，安装前先看清配置。',
    details: [['自动去重', '避免与已有版本使用相同名称'], ['提前确认', '“将创建”卡片展示名称与目录'], ['命名规则', 'Forge / NeoForge 会提示命名限制']],
    chips: ['默认去重', '不覆盖旧版本'], result: '确认名称、目录与加载器，再进入下载安装', image: 'dark-step4.png', duration: 120},
  {number: 5, kicker: '下载与安装 · 进度看得见', title: ['下载到哪了', '一眼就知道'], summary: '速度、当前文件、安装阶段实时展示。',
    details: [['过程透明', '进度与完整日志，随时查看'], ['减少重下', '断点续传，复用已有文件'], ['完整性', 'SHA1 校验；需要时可取消']],
    chips: ['断点续传', '可取消'], result: '安装完成后，回到 PCL 刷新版本列表即可启动', image: 'dark-step5.png', duration: 150},
  {number: 6, kicker: '深浅外观 · 按你的习惯', title: ['白天或深夜', '都有好界面'], summary: '默认跟随 Windows 系统的深浅色设置。',
    details: [['跟随系统', '让外观与 Windows 保持一致'], ['手动固定', '也能选择始终浅色或始终深色'], ['记住偏好', '选择会保存，下次启动继续使用']],
    chips: ['跟随系统', '浅色', '深色'], result: '同一套功能，两种清晰一致的视觉体验', image: 'light-step1.png', duration: 114},
];

// Original, unmodified 1280 × 880 screenshot coordinates.
const Outline = ({x, y, w, h, opacity = 1}: {x: number; y: number; w: number; h: number; opacity?: number}) => <div style={{position: 'absolute', left: x, top: y, width: w, height: h, border: '1.5px solid #7796FF', borderRadius: 13, opacity, boxShadow: '0 0 20px #5B8CFF20', boxSizing: 'border-box'}} />;
const pulse = (frame: number, start: number, end: number) => smooth(frame, start, start + 12) * (1 - smooth(frame, end - 12, end));

const Focus = ({data, frame}: {data: StepData; frame: number}) => {
  const {number, duration} = data;
  const beat = (duration - 30) / 3;
  if (number === 1) return <>
    <Outline x={330} y={187} w={166} h={44} opacity={0.6 * pulse(frame, 20, 20 + beat)} />
    <Outline x={527} y={190} w={96} h={39} opacity={0.7 * pulse(frame, 20 + beat, 20 + beat * 2)} />
    <Outline x={337} y={265} w={900} h={36} opacity={0.45 * pulse(frame, 20 + beat * 2, duration)} />
  </>;
  if (number === 2) return <>
    <Outline x={337} y={190} w={441} h={78} opacity={0.22 * smooth(frame, 16, 35)} />
    {[{x: 788, y: 190, w: 451, h: 78}, {x: 337, y: 278, w: 441, h: 88}, {x: 788, y: 278, w: 451, h: 88}].map((r, i) => {
      const start = 20 + beat + i * beat / 3;
      return <Outline key={i} {...r} opacity={0.65 * pulse(frame, start, start + beat / 3 + 14)} />;
    })}
    <Outline x={337} y={465} w={902} h={42} opacity={0.5 * pulse(frame, 20 + beat * 2, duration)} />
  </>;
  if (number === 3) return <>{[284, 356, 451, 546].map((y, i) => {
    const starts = [20, 20 + beat, 20 + beat * 2, 20 + beat * 2.5];
    const ends = [30 + beat, 30 + beat * 2, 30 + beat * 2.5, duration];
    return <div key={y} style={{position: 'absolute', left: 337, top: y, width: 654 * smooth(frame, starts[i] + 3, starts[i] + 20), height: 1.5, background: '#7796FF', opacity: 0.6 * pulse(frame, starts[i], ends[i])}} />;
  })}</>;
  if (number === 4) return <Outline x={645} y={190} w={300} h={165} opacity={.5 * pulse(frame, 20 + beat, duration)}/>;
  return null;
};

export const StepScene = ({frame, data}: {frame: number; data: StepData}) => {
  const detailBeat = (data.duration - 30) / 3;
  return <div style={{position: 'absolute', inset: 0}}>
    <div style={{position: 'absolute', left: 80, top: 225, width: 610}}>
      <div style={{fontSize: 17, color: theme.secondary, letterSpacing: 3, ...enter(frame)}}>{data.kicker}</div>
      <div style={{marginTop: 24, fontSize: 74, lineHeight: 1.22, fontWeight: 700, letterSpacing: -2}}>{data.title.map((t, i) => <div key={t}><KineticText text={t} frame={frame} delay={5 + i * 6}/></div>)}</div>
      <div style={{marginTop: 25, fontSize: 24, lineHeight: 1.65, color: theme.secondary, whiteSpace: 'nowrap', ...enter(frame, 6)}}>{data.summary}</div>
      <div style={{marginTop: 24}}>{data.details.map(([label, text], i) => {
        const active = pulse(frame, 20 + i * detailBeat, 30 + (i + 1) * detailBeat);
        const delay = 20 + i * detailBeat * .45;
        const q = smooth(frame, delay, delay + 22);
        return <div key={label} style={{position: 'relative', padding: '16px 0 17px 18px', display: 'flex', alignItems: 'center', gap: 18, borderBottom: `1px solid ${theme.border}88`, opacity: q, transform: `translateX(${30 * (1 - q) + 4 * active}px)`}}>
          <div style={{position: 'absolute', inset: 0, background: 'linear-gradient(90deg,#5B8CFF16,transparent)', opacity: active, transform: `scaleX(${q})`, transformOrigin: 'left'}}/>
          <div style={{position: 'absolute', left: 0, top: 19, bottom: 19, width: 2, borderRadius: 2, background: '#6B92FF', opacity: .17 + active * .83}} />
          <div style={{fontSize: 18, color: theme.secondary, minWidth: 78, whiteSpace: 'nowrap'}}>{label}</div>
          <div style={{fontSize: 21, lineHeight: 1.5, whiteSpace: 'nowrap', color: theme.text, opacity: .85 + .15 * active}}>{text}</div>
        </div>;
      })}</div>
      <div style={{display: 'flex', gap: 12, marginTop: 28, ...enter(frame, 20)}}>{data.chips.map(c => <div key={c} style={{border: `1px solid ${theme.border}`, background: theme.surface, borderRadius: 8, padding: '9px 14px', fontSize: 15, color: theme.secondary}}>{c}</div>)}</div>
    </div>
    <CameraShot data={data} frame={frame}><Focus data={data} frame={frame}/></CameraShot>
    <div style={{position: 'absolute', left: 750, top: 985, display: 'flex', alignItems: 'center', gap: 14, ...enter(frame, 18)}}>
      <div style={{width: 25, height: 1, background: '#587BC0'}} />
      <div style={{fontSize: 18, color: theme.secondary}}>{data.result}</div>
    </div>
    <div style={{position: 'absolute', left: 80, bottom: 120, fontSize: 17, fontFamily: theme.mono, color: theme.secondary, letterSpacing: 2, ...enter(frame, 9)}}>
      {data.number <= 5 ? `STEP 0${data.number} / 05` : 'PERSONAL BY DESIGN'}
      {data.number === 5 && <span style={{marginLeft: 28, fontSize: 14, color: theme.secondary}}><span style={{display: 'inline-block', width: 7, height: 7, borderRadius: 7, marginRight: 9, background: theme.blue, opacity: 0.5 + 0.35 * Math.sin(frame / 12)}} />LIVE</span>}
    </div>
  </div>;
};
