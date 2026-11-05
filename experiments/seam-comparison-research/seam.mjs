// Research diagnostic only. Declared intervals are not certified by this module.
const roles = { frontWaist: ['frontUpper', 'frontLower'], backWaist: ['backUpper', 'backLower'], upperSide: ['frontUpper', 'backUpper'], lowerSide: ['frontLower', 'backLower'] };
const factor = unit => unit === 'mm' ? 1 : unit === 'cm' ? 10 : null;
const finite = x => typeof x === 'number' && Number.isFinite(x);
const id = x => typeof x === 'string' && x.trim().length > 0;
function freeze(x) { if (x && typeof x === 'object') { Object.values(x).forEach(freeze); Object.freeze(x); } return x; }

export function diagnose(input) {
  // Snapshot before validation: returned data never aliases caller-owned objects.
  const v = structuredClone(input);
  if (!v || typeof v !== 'object') throw new Error('invalid input');
  const scale = factor(v.unit);
  if (!scale) throw new Error('unit must be mm or cm');
  const measurements = v.measurements;
  if (!measurements || !['frontArc','backArc','total','frontEase','backEase'].every(k => finite(measurements[k]))) throw new Error('explicit measurements required');
  if (measurements.frontArc <= 0 || measurements.backArc <= 0 || measurements.total <= 0 || measurements.frontEase < 0 || measurements.backEase < 0) throw new Error('invalid measurements');
  const m = Object.fromEntries(Object.entries(measurements).map(([k,x]) => [k, x * scale]));
  if (!Object.values(m).every(finite)) throw new Error('measurement overflow');
  // Floating-point equality is not used as a physical measurement tolerance.
  const discrepancy = m.frontArc + m.backArc - m.total;
  if (!finite(discrepancy)) throw new Error('measurement overflow');
  const widths = [(m.frontArc+m.frontEase)/2,(m.backArc+m.backEase)/2];
  if (!widths.every(finite)) throw new Error('measurement overflow');
  const reasons = [];
  if (discrepancy !== 0) reasons.push('arc-total-discrepancy');
  if (!id(v.source?.version) || !id(v.source?.hash) || !id(v.source?.draft)) reasons.push('missing-source');
  if (!id(v.role) || !roles[v.role]) reasons.push('missing-role');
  if (!Array.isArray(v.sides) || v.sides.length !== 2) reasons.push('missing-sides');
  const sides = Array.isArray(v.sides) ? v.sides : [];
  const seen = new Set();
  const sums = [];
  for (let i = 0; i < sides.length; i++) {
    const s = sides[i];
    if (!s || typeof s !== 'object') { reasons.push('missing-side'); continue; }
    if (s.draft && v.source?.draft && s.draft !== v.source.draft) throw new Error('mixed drafts');
    if (roles[v.role] && s.piece && s.piece !== roles[v.role][i]) throw new Error('mixed roles');
    if (!id(s.piece) || !id(s.draft)) reasons.push('missing-piece-or-draft');
    if (s.halfPiece !== true) reasons.push('half-piece-not-declared');
    if (!['forward','reverse'].includes(s.direction)) reasons.push('missing-direction');
    if (!['not-applicable','closed'].includes(s.dartTransform)) reasons.push('unresolved-dart');
    if (!Array.isArray(s.edges) || s.edges.length === 0) { reasons.push('missing-edges'); continue; }
    let lo = 0, hi = 0;
    for (const e of s.edges) {
      if (!e || !id(e.id)) { reasons.push('missing-edge-id'); continue; }
      const key = `${s.piece}\u0000${e.id}`;
      if (seen.has(key)) throw new Error('duplicate edge');
      seen.add(key);
      if (e.draft && v.source?.draft && e.draft !== v.source.draft) throw new Error('mixed drafts');
      if (e.piece && s.piece && e.piece !== s.piece) throw new Error('mixed roles');
      if ((e.sourceVersion && v.source?.version && e.sourceVersion !== v.source.version) || (e.sourceHash && v.source?.hash && e.sourceHash !== v.source.hash)) throw new Error('mixed sources');
      if (!id(e.draft) || !id(e.piece) || !id(e.sourceVersion) || !id(e.sourceHash)) reasons.push('missing-edge-source');
      if (!Array.isArray(e.length) || e.length.length !== 2 || !e.length.every(finite) || e.length[0] < 0 || e.length[1] < e.length[0]) { reasons.push('invalid-length-interval'); continue; }
      lo += e.length[0] * scale; hi += e.length[1] * scale;
    }
    if (!finite(lo) || !finite(hi)) throw new Error('length overflow');
    sums.push([lo,hi]);
  }
  if (!finite(v.tolerance) || v.tolerance < 0 || !finite(v.tolerance * scale)) reasons.push('missing-tolerance');
  let comparison = null;
  if (reasons.length === 0 && sums.length === 2) {
    const [a,b] = sums;
    const difference = [a[0]-b[1], a[1]-b[0]];
    if (!difference.every(finite)) throw new Error('difference overflow');
    const t = v.tolerance * scale;
    comparison = { firstMm:a, secondMm:b, differenceMm:difference, disjointBeyondDeclaredTolerance: difference[0] > t || difference[1] < -t };
  }
  return freeze({ status: reasons.length ? 'unresolved' : 'diagnostic-only', reasons, measurementsMm:m, halfWaistWidthsMm:widths, arcTotalDifferenceMm:discrepancy, source:v.source ?? null, role:v.role ?? null, sides, toleranceMm:finite(v.tolerance) ? v.tolerance*scale : null, comparison, intervalEvidence:'caller-declared; not independently certified; floating-point arithmetic', productionApproved:false });
}
