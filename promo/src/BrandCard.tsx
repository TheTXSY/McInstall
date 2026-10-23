import {AbsoluteFill, Img, staticFile} from 'remotion';
import {useFonts} from './fonts';
import {theme} from './theme';

export const BrandCard = () => {
  useFonts();
  return <AbsoluteFill style={{background: 'linear-gradient(135deg,#FFFFFF,#F3F6FA)', color: theme.text, fontFamily: theme.sans, alignItems: 'center', justifyContent: 'center'}}>
    <div style={{display: 'flex', alignItems: 'center', gap: 110}}>
      <Img src={staticFile('icon.png')} style={{width: 340, height: 340, filter: 'drop-shadow(0 18px 28px #3A64AA20)'}}/>
      <div style={{whiteSpace: 'nowrap'}}>
        <div style={{fontSize: 140, lineHeight: 1.12, fontWeight: 700, letterSpacing: -6}}>McInstall</div>
        <div style={{marginTop: 30, fontSize: 36, lineHeight: 1.5, fontWeight: 400, color: theme.secondary}}>专为还原、延续PCL2下载功能打造</div>
      </div>
    </div>
  </AbsoluteFill>;
};
