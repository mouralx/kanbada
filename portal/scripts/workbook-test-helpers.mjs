import assert from 'node:assert/strict';
import { unzipSync, strFromU8 } from 'fflate';

export async function readWorkbook(page, download) {
  assert.match(download.suggestedFilename(), /\.xlsx$/);
  const chunks = [];
  for await (const chunk of await download.createReadStream()) chunks.push(chunk);
  const files = Object.fromEntries(
    Object.entries(unzipSync(Buffer.concat(chunks)))
      .filter(([name]) => name.endsWith('.xml') || name.endsWith('.rels'))
      .map(([name, bytes]) => [name, strFromU8(bytes)]),
  );
  return page.evaluate((files) => {
    const xml = (name) => {
      if (!files[name]) throw new Error('Missing workbook part: ' + name);
      const document = new DOMParser().parseFromString(files[name], 'application/xml');
      if (document.querySelector('parsererror')) throw new Error('Invalid workbook XML: ' + name);
      return document;
    };
    const decode = (value) =>
      value.replace(/_x([0-9a-f]{4})_/gi, (_, code) => String.fromCharCode(parseInt(code, 16)));
    const shared = files['xl/sharedStrings.xml']
      ? [...xml('xl/sharedStrings.xml').querySelectorAll('si')].map((item) => item.textContent)
      : [];
    const relationships = new Map(
      [...xml('xl/_rels/workbook.xml.rels').querySelectorAll('Relationship')].map((rel) => [
        rel.getAttribute('Id'),
        rel.getAttribute('Target'),
      ]),
    );
    return Object.fromEntries(
      [...xml('xl/workbook.xml').querySelectorAll('sheet')].map((sheet) => {
        const target = relationships.get(sheet.getAttribute('r:id'));
        const path = new URL(target, 'https://workbook.invalid/xl/workbook.xml').pathname.slice(1);
        const document = xml(path);
        if (document.querySelector('f'))
          throw new Error('User content must not become spreadsheet formulas');
        const rows = [...document.querySelectorAll('sheetData > row')].map((row) => {
          const result = [];
          for (const cell of row.querySelectorAll('c')) {
            const column = cell.getAttribute('r').match(/^[A-Z]+/)[0];
            const index =
              [...column].reduce((value, letter) => value * 26 + letter.charCodeAt(0) - 64, 0) - 1;
            const value = cell.querySelector('v')?.textContent ?? '';
            const type = cell.getAttribute('t');
            result[index] =
              type === 'b'
                ? value === '1'
                : type === 'n'
                  ? Number(value)
                  : decode(
                      type === 's'
                        ? shared[Number(value)]
                        : (cell.querySelector('is')?.textContent ?? value),
                    );
          }
          return result;
        });
        const headers = rows.shift();
        return [
          sheet.getAttribute('name'),
          rows.map((row) => Object.fromEntries(headers.map((header, i) => [header, row[i] ?? '']))),
        ];
      }),
    );
  }, files);
}
