import {Easing, interpolate} from 'remotion';
import {clamp, smooth} from '../theme';

export const travel = (frame: number, times: number[], values: number[]) => interpolate(frame, times, values, {...clamp, easing: Easing.bezier(.45, 0, .25, 1)});

export const KineticText = ({text, frame, delay = 0, stagger = 1.6}: {text: string; frame: number; delay?: number; stagger?: number}) => <span style={{display: 'inline-block', overflow: 'hidden', verticalAlign: 'bottom', paddingBottom: 5, marginBottom: -5, whiteSpace: 'pre'}}>
  {[...text].map((c, i) => {
    const q = smooth(frame, delay + i * stagger, delay + i * stagger + 22);
    return <span key={i} style={{display: 'inline-block', opacity: q, transform: `translate3d(${10 * (1 - q)}px,${110 * (1 - q)}%,0) rotate(${3 * (1 - q)}deg)`}}>{c}</span>;
  })}
</span>;
