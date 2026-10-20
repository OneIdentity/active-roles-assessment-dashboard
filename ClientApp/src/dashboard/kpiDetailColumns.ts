/**
 * Column sizing for KPI drill-down tables. Columns are classified once per table so that short,
 * scannable values (names, domains, flags, dates, counts) stay on one line while long free-form
 * values (distinguished names, descriptions, paths) wrap. Classification is by known header name
 * first, then by inspecting the data so columns added later are handled automatically.
 */
export type ColumnKind = 'identity' | 'compact' | 'wrap';

const WRAP_HEADERS = /^(distinguished ?name|dn|description|path|canonical ?name|members?|member ?of|notes?|rule|filter|scope|managed ?scope|.*\bdn)$/i;
const COMPACT_HEADERS = /^(domain|tenant|membership|enabled|disabled|excluded|status|type|kind|count|members ?count|size|last ?logon.*|.*date|.*time|created|modified|expires?|when.*|.*days?|enabled\/disabled|true\/false|yes\/no)$/i;

const SHORT_VALUE_MAX = 32;
const BOOLEAN_LIKE = /^(true|false|yes|no|enabled|disabled|direct|indirect|n\/a|-)$/i;
const NUMBER_LIKE = /^-?[\d.,\s]+%?$/;
const DATE_LIKE = /^\d{4}-\d{2}-\d{2}([ T]\d{2}:\d{2}(:\d{2})?)?/;

export function classifyColumns(columns: string[], rows: string[][], sample = 200): ColumnKind[] {
  const sampleRows = rows.slice(0, sample);
  return columns.map((header, j) => {
    if (j === 0) return 'identity';
    const h = header.trim();
    if (WRAP_HEADERS.test(h)) return 'wrap';
    if (COMPACT_HEADERS.test(h)) return 'compact';
    const values = sampleRows.map((r) => (r[j] ?? '').trim()).filter((v) => v.length > 0);
    if (values.length === 0) return 'compact';
    const looksDn = values.some((v) => /^(CN|OU|DC)=/i.test(v) && v.includes(','));
    if (looksDn) return 'wrap';
    const allShort = values.every(
      (v) => v.length <= SHORT_VALUE_MAX || BOOLEAN_LIKE.test(v) || NUMBER_LIKE.test(v) || DATE_LIKE.test(v),
    );
    return allShort ? 'compact' : 'wrap';
  });
}

/**
 * Inserts zero-width break opportunities after DN separators so a distinguished name wraps
 * between RDN components (CN=…, / OU=…, / DC=…) instead of mid-word.
 */
export function breakableDn(value: string): string {
  return /^(CN|OU|DC|O|L)=/i.test(value) ? value.replace(/,(?=\s*(CN|OU|DC|O|L)=)/gi, ',\u200B') : value;
}
