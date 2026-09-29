import writeXlsxFile, { type CellObject, type SheetData } from 'write-excel-file/browser';
import type { Project, Task } from '../../domain/models';

type Value = string | number | boolean;
type Table = { name: string; columns: string[]; data: Value[][] };

export async function exportProjectWorkbook(project: Project, tasks: Task[]) {
  const tables: Table[] = [];
  const current = new Map<string, Table>();
  const table = (name: string, columns: string[]) => {
    const part = tables.filter((t) => t.name === name || t.name.startsWith(name + ' (')).length + 1;
    const result = { name: part === 1 ? name : `${name} (${part})`, columns, data: [columns] };
    tables.push(result);
    current.set(name, result);
    return result;
  };
  const row = (name: string, values: Value[]) => {
    let target = current.get(name)!;
    if (target.data.length >= 1048576) target = table(name, target.columns);
    const cells = values.map((value, column) => {
      if (typeof value !== 'string' || value.length <= 32767) return value;
      if (!current.has('Long text')) table('Long text', ['sheet', 'row', 'column', 'part', 'text']);
      let first = '';
      let part = 0;
      for (let offset = 0; offset < value.length;) {
        let length = Math.min(32767, value.length - offset);
        const last = value.charCodeAt(offset + length - 1);
        if (offset + length < value.length && last >= 0xd800 && last <= 0xdbff) length--;
        const text = value.slice(offset, offset + length);
        if (!part) first = text;
        row('Long text', [
          target.name,
          target.data.length + 1,
          target.columns[column],
          ++part,
          text,
        ]);
        offset += length;
      }
      return first;
    });
    target.data.push(cells);
  };
  const object = (name: string, value: object, prefix: Value[] = []) => {
    const fields = new Map<string, unknown>(Object.entries(value));
    row(name, [
      ...prefix,
      ...current
        .get(name)!
        .columns.slice(prefix.length)
        .map((key) => {
          const value = fields.get(key);
          if (value == null) return '';
          if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean')
            return value;
          throw new Error('Unsupported workbook cell value.');
        }),
    ]);
  };
  table('Cards', [
    'id',
    'project',
    'title',
    'description',
    'status',
    'priority',
    'due',
    'bucket',
    'swimlane',
    'readOnly',
    'cover',
  ]);
  table('Card labels', ['cardId', 'position', 'label']);
  table('Assignees', ['cardId', 'position', 'assignee']);
  table('Comments', ['cardId', 'position', 'text']);
  table('Checklist', ['cardId', 'position', 'text', 'done']);
  table('History', ['cardId', 'id', 'at', 'actor']);
  table('History changes', ['cardId', 'historyId', 'position', 'text']);
  table('Attachments', ['cardId', 'id', 'name', 'size', 'type', 'addedAt']);
  table('Projects', ['id', 'name', 'description', 'color', 'archived', 'system']);
  object('Projects', project);
  for (const card of tasks) {
    object('Cards', card);
    card.labels.forEach((label, i) => row('Card labels', [card.id, i + 1, label]));
    card.assignees.forEach((assignee, i) => row('Assignees', [card.id, i + 1, assignee]));
    card.comments.forEach((text, i) => row('Comments', [card.id, i + 1, text]));
    card.checklist.forEach((item, i) => object('Checklist', item, [card.id, i + 1]));
    for (const entry of card.history ?? []) {
      object('History', entry, [card.id]);
      entry.changes.forEach((text, i) => row('History changes', [card.id, entry.id, i + 1, text]));
    }
    for (const attachment of card.attachments ?? []) object('Attachments', attachment, [card.id]);
  }
  const sheets = tables.map(({ name, columns, data }) => ({
    sheet: name,
    columns: columns.map((column) => ({
      width: ['title', 'description', 'text'].includes(column) ? 55 : 24,
    })),
    data: data.map((row, i): CellObject[] =>
      row.map((value) => ({
        value,
        type: typeof value === 'number' ? Number : typeof value === 'boolean' ? Boolean : String,
        ...(i === 0 ? { fontWeight: 'bold' as const } : {}),
      })),
    ) satisfies SheetData,
  }));
  await writeXlsxFile(sheets).toFile(project.id + '.xlsx');
}
