/** Local multi-sheet .xlsx builder — avoids stale Vite caches of @lunarq/frontend-shared excelExport. */

export type ExcelColumn = {
  header: string;
  key: string;
  format?: (value: unknown) => string;
};

export type ExcelSheetSpec = {
  sheetName: string;
  tableName?: string;
  columns: ExcelColumn[];
  data: Array<Record<string, unknown>>;
};

const textEncoder = new TextEncoder();

function crc32(data: Uint8Array): number {
  let crc = 0xffffffff;
  for (let index = 0; index < data.length; index += 1) {
    crc ^= data[index]!;
    for (let bit = 0; bit < 8; bit += 1) {
      const mask = -(crc & 1);
      crc = (crc >>> 1) ^ (0xedb88320 & mask);
    }
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function u16(value: number): Uint8Array {
  const bytes = new Uint8Array(2);
  bytes[0] = value & 0xff;
  bytes[1] = (value >>> 8) & 0xff;
  return bytes;
}

function u32(value: number): Uint8Array {
  const bytes = new Uint8Array(4);
  bytes[0] = value & 0xff;
  bytes[1] = (value >>> 8) & 0xff;
  bytes[2] = (value >>> 16) & 0xff;
  bytes[3] = (value >>> 24) & 0xff;
  return bytes;
}

function concat(chunks: Uint8Array[]): Uint8Array {
  const total = chunks.reduce((sum, chunk) => sum + chunk.length, 0);
  const output = new Uint8Array(total);
  let offset = 0;
  for (const chunk of chunks) {
    output.set(chunk, offset);
    offset += chunk.length;
  }
  return output;
}

function createZipBlob(entries: Array<{ name: string; data: string }>): Blob {
  const localChunks: Uint8Array[] = [];
  const centralChunks: Uint8Array[] = [];
  let offset = 0;

  for (const entry of entries) {
    const nameBytes = textEncoder.encode(entry.name);
    const data = textEncoder.encode(entry.data);
    const checksum = crc32(data);
    const localHeader = concat([
      u32(0x04034b50),
      u16(20),
      u16(0),
      u16(0),
      u16(0),
      u16(0),
      u32(checksum),
      u32(data.length),
      u32(data.length),
      u16(nameBytes.length),
      u16(0),
      nameBytes,
    ]);
    localChunks.push(localHeader, data);

    const centralHeader = concat([
      u32(0x02014b50),
      u16(20),
      u16(20),
      u16(0),
      u16(0),
      u16(0),
      u16(0),
      u32(checksum),
      u32(data.length),
      u32(data.length),
      u16(nameBytes.length),
      u16(0),
      u16(0),
      u16(0),
      u16(0),
      u32(0),
      u32(offset),
      nameBytes,
    ]);
    centralChunks.push(centralHeader);
    offset += localHeader.length + data.length;
  }

  const centralDirectory = concat(centralChunks);
  const endRecord = concat([
    u32(0x06054b50),
    u16(0),
    u16(0),
    u16(entries.length),
    u16(entries.length),
    u32(centralDirectory.length),
    u32(offset),
    u16(0),
  ]);

  return new Blob([Uint8Array.from(concat([...localChunks, centralDirectory, endRecord]))], {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  });
}

function escapeXml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&apos;');
}

function columnLetter(index: number): string {
  let value = index + 1;
  let result = '';
  while (value > 0) {
    const remainder = (value - 1) % 26;
    result = String.fromCharCode(65 + remainder) + result;
    value = Math.floor((value - 1) / 26);
  }
  return result;
}

function sanitizeSheetName(name: string): string {
  const cleaned = name.replace(/[\\/?*[\]:]/g, ' ').trim() || 'Sheet1';
  return cleaned.slice(0, 31);
}

function sanitizeTableName(name: string): string {
  const cleaned = name
    .replace(/[^A-Za-z0-9_]/g, '_')
    .replace(/^_+/, '')
    .replace(/_+/g, '_');
  const withPrefix = /^[A-Za-z_]/.test(cleaned) ? cleaned : `Table_${cleaned}`;
  return (withPrefix || 'ExportTable').slice(0, 255);
}

function uniqueHeaders(columns: ExcelColumn[]): string[] {
  const seen = new Map<string, number>();
  return columns.map((column, index) => {
    const base = column.header.trim() || `Column${index + 1}`;
    const count = seen.get(base) ?? 0;
    seen.set(base, count + 1);
    return count === 0 ? base : `${base}_${count + 1}`;
  });
}

function uniqueNames(names: string[], sanitize: (name: string) => string): string[] {
  const seen = new Map<string, number>();
  return names.map((name, index) => {
    const base = sanitize(name) || `Sheet${index + 1}`;
    const count = seen.get(base) ?? 0;
    seen.set(base, count + 1);
    if (count === 0) return base;
    const suffix = `_${count + 1}`;
    return `${base.slice(0, Math.max(1, 31 - suffix.length))}${suffix}`;
  });
}

function formatCellDisplay(column: ExcelColumn, row: Record<string, unknown>): string {
  const raw = row[column.key];
  if (column.format) return column.format(raw);
  if (typeof raw === 'number' && Number.isFinite(raw)) {
    return (Math.round(raw * 100) / 100).toFixed(2);
  }
  if (raw == null) return '';
  return String(raw);
}

function toExcelNumber(value: number): number {
  return Math.round(value * 100) / 100;
}

function renderCellXml(cellRef: string, column: ExcelColumn, row: Record<string, unknown>): string {
  const raw = row[column.key];
  if (column.format) {
    return `<c r="${cellRef}" t="inlineStr"><is><t>${escapeXml(column.format(raw))}</t></is></c>`;
  }
  if (typeof raw === 'number' && Number.isFinite(raw)) {
    return `<c r="${cellRef}" s="1"><v>${toExcelNumber(raw).toFixed(2)}</v></c>`;
  }
  if (raw == null || raw === '') {
    return `<c r="${cellRef}" t="inlineStr"><is><t/></is></c>`;
  }
  return `<c r="${cellRef}" t="inlineStr"><is><t>${escapeXml(String(raw))}</t></is></c>`;
}

function columnIsSummable(column: ExcelColumn, data: Array<Record<string, unknown>>): boolean {
  if (column.format) {
    return false;
  }
  let sawNumber = false;
  for (const row of data) {
    const raw = row[column.key];
    if (raw == null || raw === '') {
      continue;
    }
    if (typeof raw === 'number' && Number.isFinite(raw)) {
      sawNumber = true;
      continue;
    }
    return false;
  }
  return sawNumber;
}

function buildTotalsFlags(columns: ExcelColumn[], data: Array<Record<string, unknown>>): {
  summable: boolean[];
  hasTotals: boolean;
  labelIndex: number;
} {
  const summable = columns.map((column) => columnIsSummable(column, data));
  const hasTotals = summable.some(Boolean);
  const firstText = summable.findIndex((isSum) => !isSum);
  return {
    summable,
    hasTotals,
    labelIndex: firstText >= 0 ? firstText : 0,
  };
}

function sumColumnValues(column: ExcelColumn, data: Array<Record<string, unknown>>): number {
  let sum = 0;
  for (const row of data) {
    const raw = row[column.key];
    if (typeof raw === 'number' && Number.isFinite(raw)) {
      sum += raw;
    }
  }
  return toExcelNumber(sum);
}

/** Excel column width units ≈ character count of Calibri 11; pad and clamp for readability. */
function autoFitColumnWidths(
  columns: ExcelColumn[],
  headers: string[],
  data: Array<Record<string, unknown>>,
  hasTotals: boolean,
): number[] {
  const minWidth = 8;
  const maxWidth = 60;
  const padding = 2;

  return columns.map((column, index) => {
    let maxLen = headers[index]?.length ?? 0;
    if (hasTotals) {
      maxLen = Math.max(maxLen, 5); // "Total"
    }
    for (const row of data) {
      const cell = formatCellDisplay(column, row);
      if (cell.length > maxLen) {
        maxLen = cell.length;
      }
    }
    return Math.min(maxWidth, Math.max(minWidth, maxLen + padding));
  });
}

function buildColsXml(widths: number[]): string {
  if (widths.length === 0) return '';
  const cols = widths
    .map((width, index) => {
      const n = index + 1;
      // Keep one decimal for Excel; customWidth marks explicit sizing.
      return `<col min="${n}" max="${n}" width="${width.toFixed(1)}" customWidth="1"/>`;
    })
    .join('');
  return `<cols>${cols}</cols>`;
}

function buildTotalsRowXml(
  columns: ExcelColumn[],
  data: Array<Record<string, unknown>>,
  summable: boolean[],
  labelIndex: number,
): string {
  const totalsRow = data.length + 2;
  const firstDataRow = 2;
  const lastDataRow = data.length + 1;
  const cells = columns
    .map((column, columnIndex) => {
      const cellRef = `${columnLetter(columnIndex)}${totalsRow}`;
      if (columnIndex === labelIndex) {
        return `<c r="${cellRef}" t="inlineStr"><is><t>Total</t></is></c>`;
      }
      if (summable[columnIndex] && data.length > 0) {
        const col = columnLetter(columnIndex);
        // A1 SUM below the table — avoids Excel repairing Table totals metadata.
        const formula = `SUM(${col}${firstDataRow}:${col}${lastDataRow})`;
        const total = sumColumnValues(column, data);
        return `<c r="${cellRef}" s="1"><f>${escapeXml(formula)}</f><v>${total.toFixed(2)}</v></c>`;
      }
      return '';
    })
    .join('');
  return `<row r="${totalsRow}">${cells}</row>`;
}

function buildSheetXml(
  columns: ExcelColumn[],
  headers: string[],
  data: Array<Record<string, unknown>>,
  dimensionRef: string,
  summable: boolean[],
  hasTotals: boolean,
  labelIndex: number,
): string {
  const headerRow = headers
    .map((header, index) => {
      const cellRef = `${columnLetter(index)}1`;
      return `<c r="${cellRef}" t="inlineStr"><is><t>${escapeXml(header)}</t></is></c>`;
    })
    .join('');

  const bodyRows = data
    .map((row, rowIndex) => {
      const excelRow = rowIndex + 2;
      const cells = columns
        .map((column, columnIndex) =>
          renderCellXml(`${columnLetter(columnIndex)}${excelRow}`, column, row),
        )
        .join('');
      return `<row r="${excelRow}">${cells}</row>`;
    })
    .join('');

  const totalsRowXml = hasTotals
    ? buildTotalsRowXml(columns, data, summable, labelIndex)
    : '';

  const colsXml = buildColsXml(autoFitColumnWidths(columns, headers, data, hasTotals));

  return `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <dimension ref="${dimensionRef}"/>
  <sheetViews>
    <sheetView workbookViewId="0"/>
  </sheetViews>
  <sheetFormatPr defaultRowHeight="15"/>
  ${colsXml}
  <sheetData>
    <row r="1">${headerRow}</row>
    ${bodyRows}
    ${totalsRowXml}
  </sheetData>
  <tableParts count="1">
    <tablePart r:id="rId1"/>
  </tableParts>
</worksheet>`;
}

function buildTableXml(
  tableId: number,
  tableName: string,
  headers: string[],
  tableRef: string,
): string {
  const columnsXml = headers
    .map(
      (header, index) =>
        `<tableColumn id="${index + 1}" name="${escapeXml(header)}"/>`,
    )
    .join('');

  return `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" id="${tableId}" name="${escapeXml(tableName)}" displayName="${escapeXml(tableName)}" ref="${escapeXml(tableRef)}" headerRowCount="1" totalsRowCount="0" totalsRowShown="0">
  <autoFilter ref="${escapeXml(tableRef)}"/>
  <tableColumns count="${headers.length}">
    ${columnsXml}
  </tableColumns>
  <tableStyleInfo name="TableStyleMedium2" showFirstColumn="0" showLastColumn="0" showRowStripes="1" showColumnStripes="0"/>
</table>`;
}

export function buildMultiSheetExcelWorkbook(sheets: ExcelSheetSpec[]): Blob {
  if (sheets.length === 0) {
    throw new Error('Excel export requires at least one sheet.');
  }
  if (sheets.some((sheet) => sheet.columns.length === 0)) {
    throw new Error('Excel export requires at least one column.');
  }

  const sheetNames = uniqueNames(
    sheets.map((sheet) => sheet.sheetName),
    sanitizeSheetName,
  );
  const tableNames = uniqueNames(
    sheets.map((sheet) => sheet.tableName ?? sheet.sheetName),
    sanitizeTableName,
  );

  const contentTypeOverrides = [
    `<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>`,
    `<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>`,
    ...sheets.flatMap((_, index) => {
      const n = index + 1;
      return [
        `<Override PartName="/xl/worksheets/sheet${n}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>`,
        `<Override PartName="/xl/tables/table${n}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml"/>`,
      ];
    }),
  ].join('\n  ');

  const contentTypes = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  ${contentTypeOverrides}
</Types>`;

  const rootRels = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>`;

  const workbookSheets = sheetNames
    .map(
      (name, index) =>
        `<sheet name="${escapeXml(name)}" sheetId="${index + 1}" r:id="rId${index + 1}"/>`,
    )
    .join('\n    ');

  const workbook = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <bookViews>
    <workbookView xWindow="0" yWindow="0" windowWidth="24000" windowHeight="15000"/>
  </bookViews>
  <sheets>
    ${workbookSheets}
  </sheets>
</workbook>`;

  const workbookRels = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  ${sheets
    .map(
      (_, index) =>
        `<Relationship Id="rId${index + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet${index + 1}.xml"/>`,
    )
    .join('\n  ')}
  <Relationship Id="rId${sheets.length + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>`;

  const styles = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <fonts count="1">
    <font>
      <sz val="11"/>
      <color theme="1"/>
      <name val="Calibri"/>
      <family val="2"/>
      <scheme val="minor"/>
    </font>
  </fonts>
  <fills count="2">
    <fill>
      <patternFill patternType="none"/>
    </fill>
    <fill>
      <patternFill patternType="gray125"/>
    </fill>
  </fills>
  <borders count="1">
    <border>
      <left/>
      <right/>
      <top/>
      <bottom/>
      <diagonal/>
    </border>
  </borders>
  <cellStyleXfs count="1">
    <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
  </cellStyleXfs>
  <cellXfs count="2">
    <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
    <xf numFmtId="2" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
  </cellXfs>
  <cellStyles count="1">
    <cellStyle name="Normal" xfId="0" builtinId="0"/>
  </cellStyles>
  <dxfs count="0"/>
  <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
</styleSheet>`;

  const zipEntries: Array<{ name: string; data: string }> = [
    { name: '[Content_Types].xml', data: contentTypes },
    { name: '_rels/.rels', data: rootRels },
    { name: 'xl/workbook.xml', data: workbook },
    { name: 'xl/_rels/workbook.xml.rels', data: workbookRels },
    { name: 'xl/styles.xml', data: styles },
  ];

  sheets.forEach((sheet, index) => {
    const n = index + 1;
    const headers = uniqueHeaders(sheet.columns);
    // Excel Tables require ≥1 data row; header-only refs cause repair dialogs.
    const data =
      sheet.data.length > 0
        ? sheet.data
        : [Object.fromEntries(sheet.columns.map((column) => [column.key, '']))];
    const { summable, hasTotals, labelIndex } = buildTotalsFlags(sheet.columns, data);
    const dataRowCount = data.length;
    const lastColumn = columnLetter(sheet.columns.length - 1);
    // Keep the Excel Table on header+data only; SUM formulas sit on the next row so
    // Excel does not repair invalid table totals metadata.
    const tableEndRow = dataRowCount + 1;
    const sheetEndRow = hasTotals ? dataRowCount + 2 : dataRowCount + 1;
    const tableRef = `A1:${lastColumn}${tableEndRow}`;
    const dimensionRef = `A1:${lastColumn}${sheetEndRow}`;
    const sheetRels = `<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/table" Target="../tables/table${n}.xml"/>
</Relationships>`;

    zipEntries.push(
      {
        name: `xl/worksheets/sheet${n}.xml`,
        data: buildSheetXml(
          sheet.columns,
          headers,
          data,
          dimensionRef,
          summable,
          hasTotals,
          labelIndex,
        ),
      },
      { name: `xl/worksheets/_rels/sheet${n}.xml.rels`, data: sheetRels },
      {
        name: `xl/tables/table${n}.xml`,
        data: buildTableXml(n, tableNames[index]!, headers, tableRef),
      },
    );
  });

  return createZipBlob(zipEntries);
}

export function downloadMultiSheetExcel(args: {
  filename: string;
  timestamp: string;
  sheets: ExcelSheetSpec[];
}): void {
  const workbook = buildMultiSheetExcelWorkbook(args.sheets);
  const safeName = args.filename.replace(/[\\/:*?"<>|]+/g, '-').trim() || 'export';
  const url = URL.createObjectURL(workbook);
  const link = document.createElement('a');
  link.href = url;
  link.download = `${safeName}-${args.timestamp}.xlsx`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
