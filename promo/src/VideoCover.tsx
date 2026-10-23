import {AbsoluteFill, Img, staticFile, useVideoConfig} from 'remotion';
import {useFonts} from './fonts';
import {theme} from './theme';

export const VideoCover = () => {
  useFonts();
  const {width, height} = useVideoConfig();
  const compact = width / height < 1.5;
  const iconSize = compact ? 340 : 440;
  const screenshotWidth = compact ? 450 : 590;
  const screenshotHeight = screenshotWidth * 0.6875;
  return <AbsoluteFill style={{fontFamily: theme.sans, color: '#FFFFFF', overflow: 'hidden', background: 'linear-gradient(120deg,#183790 0%,#122663 48%,#081635 100%)'}}>
    <div style={{position: 'absolute', width: 920, height: 920, left: -200, top: 70, borderRadius: '50%', background: 'radial-gradient(circle,#467BFF66,transparent 68%)'}}/>
    <div style={{position: 'absolute', width: 900, height: 900, right: -390, top: -480, border: '90px solid #5486FF12', borderRadius: '50%'}}/>
    <div style={{position: 'absolute', left: compact ? 85 : 122, top: 96, display: 'flex', alignItems: 'center', gap: 13, fontSize: 29, fontWeight: 600, letterSpacing: 2}}>
      <div style={{width: 8, height: 31, background: '#FFE36B', borderRadius: 4}}/>PCL2 下载解决方案
    </div>
    <div style={{position: 'absolute', left: compact ? 85 : 110, top: compact ? 320 : 300, width: iconSize, height: iconSize, transform: 'rotate(-7deg)', filter: 'drop-shadow(0 32px 40px #00000066)'}}>
      <div style={{position: 'absolute', inset: -8, borderRadius: compact ? 82 : 102, border: '5px solid #FFFFFFBB'}}/>
      <Img src={staticFile('icon.png')} style={{width: iconSize, height: iconSize}}/>
    </div>
    <div style={{position: 'absolute', left: compact ? 500 : 675, top: compact ? 165 : 125, fontSize: 62, lineHeight: 1.1, fontWeight: 600, letterSpacing: -2, color: '#C8D8FF'}}>McInstall</div>
    <div style={{position: 'absolute', left: compact ? 495 : 670, top: compact ? 295 : 255, fontSize: compact ? 100 : 116, lineHeight: 1.18, fontWeight: 700, letterSpacing: -3, whiteSpace: 'nowrap', textShadow: '0 5px 0 #071C4B88'}}>PCL2 下载没了？</div>
    <div style={{position: 'absolute', left: compact ? 490 : 665, top: compact ? 445 : 420, fontSize: compact ? 128 : 150, lineHeight: 1.18, fontWeight: 700, letterSpacing: -5, color: '#FFE36B', whiteSpace: 'nowrap', textShadow: '0 7px 0 #071C4B88'}}>用它继续装！</div>
    <div style={{position: 'absolute', left: compact ? 500 : 683, top: compact ? 685 : 663, fontSize: compact ? 33 : 41, fontWeight: 600, color: '#E3EBFF', lineHeight: 1.6}}>
      <div>开源 · 免费</div>
      <div style={{marginTop: 8}}>5 步安装 MC 版本</div>
    </div>
    <svg width={width} height={height} style={{position: 'absolute', inset: 0, pointerEvents: 'none'}}>
      <path d={compact ? 'M 250 738 C 292 830 411 853 503 819' : 'M 398 800 C 505 873 636 867 721 812'} fill="none" stroke="#FFFFFF" strokeWidth={14} strokeLinecap="round"/>
      <path d={compact ? 'M 455 809 L 511 815 L 487 861' : 'M 676 805 L 730 807 L 712 856'} fill="none" stroke="#FFFFFF" strokeWidth={14} strokeLinecap="round" strokeLinejoin="round"/>
    </svg>
    <div style={{position: 'absolute', left: compact ? 890 : 1190, top: compact ? 685 : 638, width: screenshotWidth, height: screenshotHeight, border: '5px solid #7397EA', borderRadius: 18, overflow: 'hidden', transform: 'rotate(-5deg)', boxShadow: '0 24px 70px #00000088'}}>
      <Img src={staticFile('screens/dark-step5.png')} style={{width: screenshotWidth, height: screenshotHeight}}/>
    </div>
    <div style={{position: 'absolute', left: compact ? 85 : 125, top: 947, fontSize: compact ? 27 : 30, lineHeight: 1.5, color: '#C8D8F5', whiteSpace: 'nowrap'}}>专为还原、延续PCL2下载功能打造</div>
    <div style={{position: 'absolute', left: compact ? 85 : 125, bottom: 40, fontSize: 20, color: '#8DA4D0'}}>非官方工具 · 与 PCL 作者无关</div>
  </AbsoluteFill>;
};
