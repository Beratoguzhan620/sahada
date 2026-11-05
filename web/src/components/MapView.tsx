import L from "leaflet";
import "leaflet/dist/leaflet.css";
import { useEffect, useRef } from "react";

export interface Pin {
  id: string;
  latitude: number;
  longitude: number;
  label: string;
  /** Shown in the marker: a short code or initials. */
  text: string;
  tone: "accent" | "danger" | "muted" | "ink";
  onClick?: () => void;
}

const colors = { accent: "#8A5A00", danger: "#9E2B25", muted: "#8A867F", ink: "#1F2A37" };

/** Samsun, where the demo company works; used when there is nothing to show yet. */
const fallback: L.LatLngTuple = [41.2867, 36.33];

/**
 * OpenStreetMap tiles with simple labelled pins. Markers are drawn with CSS, no images, and fit the view to what
 * is shown; labels are set as text, never as HTML.
 */
export function MapView({ pins, height = 420, routes = [], onPick }: {
  pins: Pin[]; height?: number; routes?: { points: [number, number][]; tone: Pin["tone"] }[]; onPick?: (latitude: number, longitude: number) => void;
}) {
  const element = useRef<HTMLDivElement>(null);
  const map = useRef<L.Map | null>(null);
  const layer = useRef<L.LayerGroup | null>(null);
  const pick = useRef(onPick);
  pick.current = onPick;

  useEffect(() => {
    if (!element.current || map.current) return;
    map.current = L.map(element.current, { zoomControl: true, attributionControl: false }).setView(fallback, 12);
    L.control.attribution({ prefix: '<a href="https://leafletjs.com">Leaflet</a>' }).addTo(map.current);
    L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
      maxZoom: 19,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
    }).addTo(map.current);
    layer.current = L.layerGroup().addTo(map.current);
    map.current.on("click", (e: L.LeafletMouseEvent) => pick.current?.(Math.round(e.latlng.lat * 1e6) / 1e6, Math.round(e.latlng.lng * 1e6) / 1e6));
    return () => {
      map.current?.stop();
      map.current?.off();
      map.current?.remove();
      map.current = null;
    };
  }, []);

  useEffect(() => {
    const group = layer.current;
    if (!group || !map.current) return;
    group.clearLayers();
    for (const route of routes) {
      L.polyline(route.points, { color: colors[route.tone], weight: 2, opacity: 0.7, dashArray: "4 6" }).addTo(group);
    }
    for (const pin of pins) {
      const html = document.createElement("span");
      html.className = "pin";
      html.style.background = colors[pin.tone];
      html.textContent = pin.text;
      const marker = L.marker([pin.latitude, pin.longitude], { icon: L.divIcon({ html, className: "pin-wrap", iconSize: undefined, iconAnchor: [14, 14] }), title: pin.label, alt: pin.label, keyboard: true });
      if (pin.onClick) marker.on("click", pin.onClick);
      marker.bindTooltip(pin.label, { direction: "top", offset: [0, -12] });
      marker.addTo(group);
    }
    const points = pins.map((p) => [p.latitude, p.longitude] as L.LatLngTuple);
    // No animation: a page that is left mid-animation would leave Leaflet moving a map that is gone.
    if (points.length === 1) map.current.setView(points[0]!, 14, { animate: false });
    else if (points.length > 1) map.current.fitBounds(L.latLngBounds(points), { padding: [36, 36], maxZoom: 15, animate: false });
  }, [pins, routes]);

  return <div ref={element} className={`map${onPick ? " picking" : ""}`} style={{ height }} role="region" aria-label="Map" />;
}
