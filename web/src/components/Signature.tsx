import { Eraser } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { thin } from "../tech/local";

/** Draws a signature from its strokes (points between 0 and 1). */
export function SignatureView({ strokes, className = "" }: { strokes: number[][][]; className?: string }) {
  return (
    <svg className={`signature ${className}`} viewBox="0 0 300 120" role="img" aria-label="Customer's signature">
      {strokes.map((s, i) => (
        <polyline key={i} fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round"
          points={s.map(([x, y]) => `${(x! * 300).toFixed(1)},${(y! * 120).toFixed(1)}`).join(" ")} />
      ))}
    </svg>
  );
}

/** A pad to sign on with a finger or a mouse. Reports strokes scaled to the pad, so any screen size gives the same result. */
export function SignaturePad({ onChange }: { onChange: (strokes: number[][][]) => void }) {
  const canvas = useRef<HTMLCanvasElement>(null);
  const strokes = useRef<number[][][]>([]);
  const current = useRef<number[][] | null>(null);
  const [empty, setEmpty] = useState(true);

  const draw = () => {
    const c = canvas.current;
    if (!c) return;
    const ctx = c.getContext("2d")!;
    const ratio = window.devicePixelRatio || 1;
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
    ctx.clearRect(0, 0, c.width, c.height);
    ctx.strokeStyle = "#1A1A1A";
    ctx.lineWidth = 2;
    ctx.lineCap = "round";
    ctx.lineJoin = "round";
    const w = c.clientWidth;
    const h = c.clientHeight;
    for (const s of [...strokes.current, ...(current.current ? [current.current] : [])]) {
      ctx.beginPath();
      s.forEach(([x, y], i) => (i === 0 ? ctx.moveTo(x! * w, y! * h) : ctx.lineTo(x! * w, y! * h)));
      ctx.stroke();
    }
  };

  useEffect(() => {
    const c = canvas.current!;
    const resize = () => {
      const ratio = window.devicePixelRatio || 1;
      c.width = c.clientWidth * ratio;
      c.height = c.clientHeight * ratio;
      draw();
    };
    resize();
    window.addEventListener("resize", resize);
    return () => window.removeEventListener("resize", resize);
  }, []);

  const point = (e: React.PointerEvent) => {
    const rect = canvas.current!.getBoundingClientRect();
    return [Math.min(1, Math.max(0, (e.clientX - rect.left) / rect.width)), Math.min(1, Math.max(0, (e.clientY - rect.top) / rect.height))];
  };

  const finish = () => {
    if (!current.current) return;
    const stroke = thin(current.current);
    current.current = null;
    if (stroke.length >= 2) strokes.current = [...strokes.current, stroke];
    setEmpty(strokes.current.length === 0);
    onChange(strokes.current);
    draw();
  };

  const clear = () => {
    strokes.current = [];
    setEmpty(true);
    onChange([]);
    draw();
  };

  return (
    <div className="pad-wrap">
      <canvas
        ref={canvas}
        className="pad"
        aria-label="Signature pad: sign with a finger or the mouse"
        onPointerDown={(e) => { e.currentTarget.setPointerCapture(e.pointerId); current.current = [point(e)]; }}
        onPointerMove={(e) => { if (current.current) { current.current.push(point(e)); draw(); } }}
        onPointerUp={finish}
        onPointerCancel={finish}
      />
      {empty ? <span className="pad-hint">Sign here</span> : <button type="button" className="button small pad-clear" onClick={clear}><Eraser size={14} />Clear</button>}
    </div>
  );
}
