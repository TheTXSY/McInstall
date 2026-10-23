import {Easing, interpolate, spring} from 'remotion';
export const theme = {
  bg: '#F8FAFC', bgEnd: '#F1F4F8', surface: '#FFFFFF', border: '#D7DFE9',
  text: '#343B46', secondary: '#626D7C', muted: '#7C8796',
  blue: '#547CE0', violet: '#7C60CC',
  backdrop: 'linear-gradient(180deg,#FCFDFE,#F1F4F8)',
  cardShadow: '0 24px 65px #26334A1A',
  sans: 'Inter, "Noto Sans SC", "Segoe UI", "Microsoft YaHei", sans-serif',
  mono: '"JetBrains Mono", Consolas, monospace',
};
export const clamp = {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'} as const;
export const smooth = (f: number, start: number, end: number) => interpolate(f, [start, end], [0, 1], {...clamp, easing: Easing.bezier(0.22, 1, 0.36, 1)});
export const TRANSITION = 22;
export const enter = (f: number, delay = 0) => ({
  opacity: smooth(f - delay, 0, 18),
  transform: `translateY(${24 * (1 - smooth(f - delay, 0, 25))}px)`,
});
export const cardSpring = (frame: number) => spring({frame: Math.max(0, frame), fps: 30, config: {damping: 18, stiffness: 100, mass: 0.8}});
// Set a real Releases URL if one is provided later. Empty means no URL is displayed.
export const RELEASES_URL = '';
// Optional licensed local audio in public/. The delivered film is silent.
export const AUDIO_FILE: string | null = null;
