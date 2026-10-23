import {useEffect, useState} from 'react';
import {continueRender, delayRender, staticFile} from 'remotion';
import {loadFontFromInfo} from '@remotion/google-fonts/from-info';
import fontManifest from './font-manifest.json';

export const useFonts = () => {
  const [handle] = useState(() => delayRender('Local Google fonts ready'));
  useEffect(() => {
    const fonts = fontManifest.map(info => {
      const local = structuredClone(info) as unknown as Parameters<typeof loadFontFromInfo>[0];
      for (const weights of Object.values(local.fonts)) for (const subsets of Object.values(weights)) {
        for (const key of Object.keys(subsets)) subsets[key] = staticFile(subsets[key]);
      }
      return loadFontFromInfo(local, 'normal', {ignoreTooManyRequestsWarning: true}).waitUntilDone();
    });
    Promise.all(fonts).then(() => document.fonts.ready).then(() => continueRender(handle)).catch(error => {
      console.warn('Local font unavailable. Using Microsoft YaHei / Segoe UI / Consolas.', error);
      continueRender(handle);
    });
  }, [handle]);
};
