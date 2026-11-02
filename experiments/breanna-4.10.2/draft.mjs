import { Breanna } from '@freesewing/breanna';

export const measurementNames = Object.freeze(['biceps','bustFront','bustSpan','highBust','highBustFront','hpsToBust','hpsToWaistBack','hpsToWaistFront','waist','waistToHips','neck','shoulderToShoulder','shoulderSlope','shoulderToWrist','wrist']);
export function draftExperimental(measurements) {
  if (!measurements || typeof measurements !== 'object' || Array.isArray(measurements)) throw new TypeError('Ölçü nesnesi gerekli.');
  const bad = measurementNames.filter(k => typeof measurements[k] !== 'number' || !Number.isFinite(measurements[k]) || (k === 'shoulderSlope' ? measurements[k] <= -90 || measurements[k] >= 90 : measurements[k] <= 0));
  if (bad.length) throw new RangeError(`Eksik/geçersiz ölçü: ${bad.join(', ')}`);
  const clean = Object.fromEntries(measurementNames.map(k => [k, measurements[k]]));
  const pattern = new Breanna({ measurements: clean });
  pattern.draft();
  const parts = pattern.parts?.[0];
  if (!parts) throw new Error('Gövde parçaları oluşmadı.');
  let count=0;
  for (const part of Object.values(parts)) for (const point of Object.values(part.points || {})) {
    if (!Number.isFinite(point.x) || !Number.isFinite(point.y)) throw new Error('Sonlu olmayan nokta.');
    count++;
  }
  if (count === 0) throw new Error('Nokta oluşmadı.');
  const svg = pattern.render();
  if (typeof svg !== 'string' || !svg.includes('<svg') || /(?:NaN|Infinity)/.test(svg)) throw new Error('Geçersiz SVG.');
  return { status: 'DENEYSEL — üretime onaysız; uzman kontrolü ve numune dikimi gerekir', measurements: clean, partNames: Object.keys(parts), pointCount: count, svg };
}
