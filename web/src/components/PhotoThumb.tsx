import { ImageOff } from "lucide-react";
import { useEffect, useState } from "react";

/** A photo fetched with the signed-in person's token (an img tag cannot send it) and shown from memory. */
export function PhotoThumb({ load, alt, flagged = false }: { load: () => Promise<Blob>; alt: string; flagged?: boolean }) {
  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let current: string | null = null;
    let live = true;
    load().then((blob) => {
      if (!live) return;
      current = URL.createObjectURL(blob);
      setUrl(current);
    }, () => live && setFailed(true));
    return () => {
      live = false;
      if (current) URL.revokeObjectURL(current);
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <figure className={`photo${flagged ? " flagged" : ""}`}>
      {url ? <a href={url} target="_blank" rel="noreferrer"><img src={url} alt={alt} /></a> : <div className="photo-void">{failed ? <ImageOff size={20} /> : <span className="skeleton" />}</div>}
      {alt && <figcaption className="small muted ellipsis">{alt}</figcaption>}
    </figure>
  );
}
