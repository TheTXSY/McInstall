import {Composition} from 'remotion';
import {Promo} from './Promo';
import timing from './timing.json';
import {BrandCard} from './BrandCard';
import {VideoCover} from './VideoCover';
export const Root = () => <>
  <Composition id="McInstallPromo" component={Promo} width={1920} height={1080} fps={timing.fps} durationInFrames={timing.durationInFrames} defaultProps={{short: false}} />
  <Composition id="McInstallPromoGif" component={Promo} width={960} height={540} fps={15} durationInFrames={180} defaultProps={{short: true}} />
  <Composition id="McInstallBrandCard" component={BrandCard} width={1920} height={1080} fps={30} durationInFrames={1}/>
  <Composition id="McInstallVideoCover" component={VideoCover} width={1920} height={1080} fps={30} durationInFrames={1}/>
  <Composition id="McInstallVideoCover4x3" component={VideoCover} width={1440} height={1080} fps={30} durationInFrames={1}/>
</>;
